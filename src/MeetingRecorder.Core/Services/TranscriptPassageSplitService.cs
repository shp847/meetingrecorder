using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>Projects reversible passage boundaries without changing the source transcript or inventing timestamps.</summary>
internal static class TranscriptPassageSplitService
{
    public static IReadOnlyList<TranscriptSegment> Project(IReadOnlyList<TranscriptSegment> source, JsonArray? records) =>
        Project(source, records, out _);

    public static IReadOnlyList<TranscriptSegment> Project(IReadOnlyList<TranscriptSegment> source, JsonArray? records, out int invalidCount)
    {
        invalidCount = 0;
        var result = source.ToList();
        foreach (var node in records ?? [])
        {
            if (node is not JsonObject record) { invalidCount++; continue; }
            if (record["revertedAtUtc"] is not null) continue;
            var parentId = Read(record, "parentSegmentId");
            var operationId = Read(record, "id");
            var leftId = Read(record, "leftSegmentId");
            var rightId = Read(record, "rightSegmentId");
            if (string.IsNullOrWhiteSpace(parentId) || string.IsNullOrWhiteSpace(operationId) ||
                string.IsNullOrWhiteSpace(leftId) || string.IsNullOrWhiteSpace(rightId) ||
                record["offset"] is not JsonValue offsetValue || !offsetValue.TryGetValue<int>(out var offset)) { invalidCount++; continue; }
            var index = result.FindIndex(segment => string.Equals(segment.SegmentId, parentId, StringComparison.Ordinal));
            if (index < 0) { invalidCount++; continue; }
            var parent = result[index];
            if (!string.Equals(Read(record, "textHash"), Hash(parent.Text), StringComparison.Ordinal) ||
                !ValidBoundary(parent.Text, offset) || result.Any(segment => segment.SegmentId == leftId || segment.SegmentId == rightId)) { invalidCount++; continue; }
            result.RemoveAt(index);
            var sourceId = parent.SourceSegmentId ?? parent.SegmentId;
            result.Insert(index, parent with { SegmentId = rightId, Text = parent.Text[offset..], ManualSplitId = operationId, SourceSegmentId = sourceId, SourceTextOffset = parent.SourceTextOffset + offset });
            result.Insert(index, parent with { SegmentId = leftId, Text = parent.Text[..offset], ManualSplitId = operationId, SourceSegmentId = sourceId });
        }
        return result;
    }

    public static bool TryApply(JsonObject document, IReadOnlyList<TranscriptSegment> source,
        string segmentId, int offset, string operationId, string revision, out string? reason, bool allowActiveOverride = false)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(segmentId) || string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(revision))
        { reason = "Passage data is unavailable."; return false; }
        if (document["manualTranscriptSplits"] is not null && document["manualTranscriptSplits"] is not JsonArray)
        { reason = "Saved passage edits need review before adding another split."; return false; }
        var records = document["manualTranscriptSplits"] as JsonArray;
        if (records?.OfType<JsonObject>().Any(item => Read(item, "id") == operationId) == true)
        { reason = "This split already exists."; return false; }
        var projected = Project(source, records, out var invalidCount);
        if (invalidCount > 0) { reason = "Saved passage edits need review before adding another split."; return false; }
        var segment = projected.FirstOrDefault(item => string.Equals(item.SegmentId, segmentId, StringComparison.Ordinal));
        if (segment is null || !ValidBoundary(segment.Text, offset))
        { reason = "Choose a boundary inside the passage with words on both sides."; return false; }
        if (!allowActiveOverride && HasActiveOverride(document, segmentId))
        { reason = "Undo the passage correction before splitting it, then correct each part."; return false; }
        records ??= new JsonArray();
        if (document["manualTranscriptSplits"] is null) document["manualTranscriptSplits"] = records;
        records.Add(new JsonObject
        {
            ["id"] = operationId,
            ["artifactRevision"] = revision,
            ["parentSegmentId"] = segmentId,
            ["leftSegmentId"] = operationId + ":left",
            ["rightSegmentId"] = operationId + ":right",
            ["offset"] = offset,
            ["textHash"] = Hash(segment.Text),
            ["createdAtUtc"] = DateTimeOffset.UtcNow,
        });
        return true;
    }

    public static bool TryRevert(JsonObject document, string operationId, out string? reason)
    {
        reason = null;
        var records = document["manualTranscriptSplits"] as JsonArray;
        var record = records?.OfType<JsonObject>().LastOrDefault(item => Read(item, "id") == operationId && item["revertedAtUtc"] is null);
        if (record is null) { reason = "This split is no longer available."; return false; }
        var children = new[] { Read(record, "leftSegmentId"), Read(record, "rightSegmentId") };
        if (records!.OfType<JsonObject>().Any(item => item["revertedAtUtc"] is null && children.Contains(Read(item, "parentSegmentId"))) ||
            children.Any(child => child is not null && HasActiveOverride(document, child)))
        { reason = "Undo corrections and later splits in these passages first."; return false; }
        record["revertedAtUtc"] = DateTimeOffset.UtcNow;
        return true;
    }

    private static bool HasActiveOverride(JsonObject document, string segmentId) =>
        document["speakerAttributionOverrides"] is JsonArray overrides && overrides.OfType<JsonObject>().Any(item =>
            string.Equals(Read(item, "segmentId") ?? Read(item, "SegmentId"), segmentId, StringComparison.Ordinal) &&
            item["revertedAtUtc"] is null && item["RevertedAtUtc"] is null);

    private static bool ValidBoundary(string text, int offset) => offset > 0 && offset < text.Length &&
        !string.IsNullOrWhiteSpace(text[..offset]) && !string.IsNullOrWhiteSpace(text[offset..]) &&
        !(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string? Read(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
