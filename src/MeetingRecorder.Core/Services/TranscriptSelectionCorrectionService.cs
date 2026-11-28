using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal sealed record TranscriptSelectionSlice(string SegmentId, string? SpeakerId, string Text, int Start, int Length);

/// <summary>Stages one reversible attribution change across one or more projected passages.</summary>
internal static class TranscriptSelectionCorrectionService
{
    private const string OperationsKey = "speakerSelectionCorrections";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool TryApply(JsonObject document, IReadOnlyList<TranscriptSegment> source,
        IReadOnlyList<TranscriptSelectionSlice> slices, string operationId, string revision,
        SegmentAttributionOverrideTargetKind targetKind, string targetValue, out string? reason)
    {
        reason = null;
        if (slices.Count == 0 || string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(revision) ||
            string.IsNullOrWhiteSpace(targetValue) || !Enum.IsDefined(targetKind))
        { reason = "Select transcript text and choose a speaker."; return false; }
        var staged = document.DeepClone().AsObject();
        if (!IsArrayOrMissing(staged, "manualTranscriptSplits") || !IsArrayOrMissing(staged, "speakerAttributionOverrides") ||
            !IsArrayOrMissing(staged, OperationsKey))
        { reason = "Saved transcript edits need review."; return false; }
        var operations = EnsureArray(staged, OperationsKey);
        if (operations.OfType<JsonObject>().Any(item => Read(item, "id") == operationId))
        { reason = "This correction already exists."; return false; }
        var projected = TranscriptPassageSplitService.Project(source, staged["manualTranscriptSplits"] as JsonArray, out var invalid);
        if (invalid > 0) { reason = "Saved passage edits need review."; return false; }
        var segmentPositions = projected.Select((segment, index) => (segment, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.segment.SegmentId))
            .GroupBy(item => item.segment.SegmentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.index).ToArray(), StringComparer.Ordinal);
        var positions = new List<int>();
        foreach (var slice in slices)
        {
            if (!segmentPositions.TryGetValue(slice.SegmentId, out var matches) || matches.Length != 1)
            { reason = "The selected passage identity is missing or ambiguous. Reload before editing."; return false; }
            var index = matches[0];
            if ((positions.Count > 0 && index != positions[^1] + 1) ||
                projected[index].SpeakerId != slice.SpeakerId || projected[index].Text != slice.Text ||
                slice.Start < 0 || slice.Length <= 0 || slice.Start > slice.Text.Length - slice.Length ||
                !IsTextBoundary(slice.Text, slice.Start) || !IsTextBoundary(slice.Text, slice.Start + slice.Length) ||
                string.IsNullOrWhiteSpace(slice.Text.Substring(slice.Start, slice.Length)))
            { reason = "The selected text changed or does not have a safe boundary. Reload and try again."; return false; }
            positions.Add(index);
        }
        for (var i = 0; i < slices.Count; i++)
        {
            if (i > 0 && slices[i].Start != 0 || i < slices.Count - 1 && slices[i].Start + slices[i].Length != slices[i].Text.Length)
            { reason = "Select a continuous range within one speaker block."; return false; }
        }

        var createdSplits = new List<string>();
        var createdOverrides = new List<string>();
        var priorOverrideIds = new HashSet<string>(StringComparer.Ordinal);
        var affectedSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slice in slices)
        {
            var selectedId = slice.SegmentId;
            var selectedLength = slice.Text.Length;
            affectedSegmentIds.Add(selectedId);
            _ = FindActiveOverride(staged, selectedId, revision, out var invalidPrior);
            if (invalidPrior) { reason = "An earlier speaker correction needs review."; return false; }
            foreach (var activeId in GetActiveOverrideIds(staged, selectedId)) priorOverrideIds.Add(activeId);
            var end = slice.Start + slice.Length;
            if (end < selectedLength)
            {
                if (!Split(staged, source, selectedId, end, operationId, revision, createdSplits, createdOverrides, affectedSegmentIds, out var left, out _, out reason)) return false;
                selectedId = left;
            }
            if (slice.Start > 0)
            {
                if (!Split(staged, source, selectedId, slice.Start, operationId, revision, createdSplits, createdOverrides, affectedSegmentIds, out _, out var right, out reason)) return false;
                selectedId = right;
            }
            var id = $"{operationId}:override:{createdOverrides.Count}";
            var now = DateTimeOffset.UtcNow;
            var value = new SegmentAttributionOverride(id, revision, selectedId, slice.SpeakerId, targetKind,
                targetValue.Trim(), "UserOverride", now, now, Reason: "selection-correction:" + operationId);
            EnsureArray(staged, "speakerAttributionOverrides").Add(JsonSerializer.SerializeToNode(value));
            createdOverrides.Add(id);
            affectedSegmentIds.Add(selectedId);
        }

        operations.Add(new JsonObject
        {
            ["id"] = operationId, ["artifactRevision"] = revision, ["createdAtUtc"] = DateTimeOffset.UtcNow,
            ["splitIds"] = ToArray(createdSplits), ["overrideIds"] = ToArray(createdOverrides),
            ["priorOverrideIds"] = ToArray(priorOverrideIds), ["segmentIds"] = ToArray(affectedSegmentIds),
        });
        CopyChanges(document, staged);
        return true;
    }

    public static bool TryRevert(JsonObject document, string operationId, string revision, out string? reason)
    {
        reason = null;
        var staged = document.DeepClone().AsObject();
        var operation = (staged[OperationsKey] as JsonArray)?.OfType<JsonObject>().LastOrDefault(item => Read(item, "id") == operationId);
        if (operation is null || operation["revertedAtUtc"] is not null || Read(operation, "artifactRevision") != revision ||
            operation["splitIds"] is not JsonArray splitArray || operation["overrideIds"] is not JsonArray overrideArray ||
            operation["priorOverrideIds"] is not JsonArray priorArray || operation["segmentIds"] is not JsonArray segmentArray)
        { reason = "This correction is no longer available for undo."; return false; }
        var splits = splitArray.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();
        var ownOverrides = Strings(overrideArray); var prior = Strings(priorArray); var segments = Strings(segmentArray);
        var overrides = staged["speakerAttributionOverrides"] as JsonArray ?? new JsonArray();
        var active = overrides.OfType<JsonObject>().Where(item => item["revertedAtUtc"] is null && item["RevertedAtUtc"] is null).ToArray();
        var laterSplit = (staged["manualTranscriptSplits"] as JsonArray ?? []).OfType<JsonObject>().Any(item =>
            item["revertedAtUtc"] is null && segments.Contains(Read(item, "parentSegmentId") ?? string.Empty) &&
            !splits.Contains(Read(item, "id") ?? string.Empty, StringComparer.Ordinal));
        if (laterSplit || prior.Any(id => active.Count(item => Read(item, "id") == id || Read(item, "Id") == id) != 1) ||
            ownOverrides.Any(id => active.Count(item => Read(item, "id") == id || Read(item, "Id") == id) != 1) ||
            active.Any(item => segments.Contains(Read(item, "segmentId") ?? Read(item, "SegmentId") ?? string.Empty) &&
                !ownOverrides.Contains(Read(item, "id") ?? Read(item, "Id") ?? string.Empty) &&
                !prior.Contains(Read(item, "id") ?? Read(item, "Id") ?? string.Empty)))
        { reason = "Later speaker edits depend on this correction. Undo them first."; return false; }
        foreach (var item in active.Where(item => ownOverrides.Contains(Read(item, "id") ?? Read(item, "Id") ?? string.Empty)))
            item["revertedAtUtc"] = DateTimeOffset.UtcNow;
        foreach (var id in splits.Reverse())
            if (!TranscriptPassageSplitService.TryRevert(staged, id, out reason)) return false;
        operation["revertedAtUtc"] = DateTimeOffset.UtcNow;
        CopyChanges(document, staged);
        return true;
    }

    private static bool Split(JsonObject document, IReadOnlyList<TranscriptSegment> source, string parentId, int offset,
        string operationId, string revision, List<string> splits, List<string> overrides, HashSet<string> segments,
        out string left, out string right, out string? reason)
    {
        var prior = FindActiveOverride(document, parentId, revision, out var invalid);
        left = right = string.Empty;
        if (invalid) { reason = "An earlier speaker correction needs review."; return false; }
        var splitId = $"{operationId}:split:{splits.Count}";
        if (!TranscriptPassageSplitService.TryApply(document, source, parentId, offset, splitId, revision, out reason, allowActiveOverride: true)) return false;
        splits.Add(splitId); left = splitId + ":left"; right = splitId + ":right";
        segments.Add(parentId); segments.Add(left); segments.Add(right);
        if (prior is not null)
        {
            foreach (var child in new[] { left, right })
            {
                var id = $"{operationId}:override:{overrides.Count}";
                EnsureArray(document, "speakerAttributionOverrides").Add(JsonSerializer.SerializeToNode(prior with
                {
                    Id = id, SegmentId = child, Reason = "selection-inherited:" + operationId,
                }));
                overrides.Add(id);
            }
        }
        return true;
    }

    private static SegmentAttributionOverride? FindActiveOverride(JsonObject document, string segmentId, string revision, out bool invalid)
    {
        invalid = false;
        var candidates = (document["speakerAttributionOverrides"] as JsonArray)?.OfType<JsonObject>()
            .Where(item => (Read(item, "segmentId") ?? Read(item, "SegmentId")) == segmentId &&
                item["revertedAtUtc"] is null && item["RevertedAtUtc"] is null).ToArray() ?? [];
        foreach (var item in candidates.Reverse())
        {
            try
            {
                var value = item.Deserialize<SegmentAttributionOverride>(ReadOptions);
                if (value is null || string.IsNullOrWhiteSpace(value.Id) || value.ActorSource != "UserOverride") { invalid = true; return null; }
                if (value.ArtifactRevision == revision) return value;
            }
            catch (JsonException) { invalid = true; return null; }
        }
        return null;
    }

    private static bool IsTextBoundary(string text, int offset) => offset == 0 || offset == text.Length ||
        Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), offset) >= 0;
    private static IEnumerable<string> GetActiveOverrideIds(JsonObject document, string segmentId) =>
        (document["speakerAttributionOverrides"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(item => (Read(item, "segmentId") ?? Read(item, "SegmentId")) == segmentId &&
                item["revertedAtUtc"] is null && item["RevertedAtUtc"] is null)
            .Select(item => Read(item, "id") ?? Read(item, "Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!);
    private static bool IsArrayOrMissing(JsonObject root, string key) => root[key] is null or JsonArray;
    private static JsonArray EnsureArray(JsonObject root, string key) => (JsonArray)(root[key] ??= new JsonArray());
    private static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
    private static HashSet<string> Strings(JsonArray values) => values.Select(node => node?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
    private static string? Read(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static void CopyChanges(JsonObject destination, JsonObject source)
    {
        foreach (var key in new[] { "manualTranscriptSplits", "speakerAttributionOverrides", OperationsKey })
            if (source[key] is not null) destination[key] = source[key]!.DeepClone();
    }
}
