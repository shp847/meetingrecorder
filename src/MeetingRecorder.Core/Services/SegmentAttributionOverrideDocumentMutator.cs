using MeetingRecorder.Core.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Services;

public static class SegmentAttributionOverrideDocumentMutator
{
    public static bool TryApply(JsonObject document, SegmentAttributionOverride value, out string? reason,
        IReadOnlyList<TranscriptSegment>? projectedSegments = null)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(value.SegmentId) ||
            !string.Equals(value.ActorSource, "UserOverride", StringComparison.Ordinal))
        {
            reason = "Invalid paragraph correction.";
            return false;
        }
        if (document["segments"] is not JsonArray segments) { reason = "Transcript segments are unavailable."; return false; }
        var segment = segments.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["segmentId"]?.GetValue<string>(), value.SegmentId, StringComparison.Ordinal));
        var projected = projectedSegments?.FirstOrDefault(item => string.Equals(item.SegmentId, value.SegmentId, StringComparison.Ordinal));
        var currentSpeakerId = segment?["speakerId"]?.GetValue<string>() ?? projected?.SpeakerId;
        if (segment is null && projected is null || !string.Equals(
                string.IsNullOrWhiteSpace(currentSpeakerId) ? null : currentSpeakerId.Trim(),
                string.IsNullOrWhiteSpace(value.OriginalSpeakerId) ? null : value.OriginalSpeakerId.Trim(),
                StringComparison.Ordinal)) { reason = "The transcript segment changed."; return false; }
        if (string.IsNullOrWhiteSpace(value.TargetValue)) { reason = "A correction target is required."; return false; }
        var existing = document["speakerAttributionOverrides"] as JsonArray ?? new JsonArray();
        if (document["speakerAttributionOverrides"] is null) document["speakerAttributionOverrides"] = existing;
        var prior = existing.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["id"]?.GetValue<string>(), value.Id, StringComparison.Ordinal));
        if (prior is not null) return true;
        existing.Add(JsonSerializer.SerializeToNode(value));
        if (value.TargetKind == SegmentAttributionOverrideTargetKind.DisplayName && segment is not null)
        {
            var labelKey = segment.ContainsKey("speakerLabel") ? "speakerLabel" :
                segment.ContainsKey("SpeakerLabel") ? "SpeakerLabel" : "speakerLabel";
            // Keep the source label when older transcripts lack an explicit source field.
            if (segment["displaySpeakerLabel"] is null && segment["DisplaySpeakerLabel"] is null && segment[labelKey] is { } originalLabel)
            {
                segment["displaySpeakerLabel"] = originalLabel.DeepClone();
                segment["manualSourceLabelAdded"] = true;
            }
            if (!segment.ContainsKey(labelKey))
            {
                segment["manualSpeakerLabelWasMissing"] = true;
                if (segment["displaySpeakerLabel"] is null && segment["DisplaySpeakerLabel"] is null)
                {
                    segment["displaySpeakerLabel"] = string.IsNullOrWhiteSpace(currentSpeakerId) ? "Speaker" : currentSpeakerId;
                    segment["manualSourceLabelAdded"] = true;
                }
            }
            segment[labelKey] = value.TargetValue.Trim();
        }
        return true;
    }

    public static bool TryRevert(JsonObject document, string overrideId, string artifactRevision, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(overrideId) || string.IsNullOrWhiteSpace(artifactRevision) ||
            document["speakerAttributionOverrides"] is not JsonArray existing)
        {
            reason = "Paragraph correction history is unavailable.";
            return false;
        }

        var item = existing.OfType<JsonObject>().LastOrDefault(candidate =>
        {
            try
            {
                var parsed = candidate.Deserialize<SegmentAttributionOverride>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return parsed is not null &&
                       string.Equals(parsed.Id, overrideId, StringComparison.Ordinal) &&
                       string.Equals(parsed.ArtifactRevision, artifactRevision, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        });
        if (item is null)
        {
            reason = "Paragraph correction is no longer current.";
            return false;
        }

        if (document["speakerSelectionCorrections"] is JsonArray selectionCorrections &&
            selectionCorrections.OfType<JsonObject>().Any(operation => operation["revertedAtUtc"] is null &&
                operation["overrideIds"] is JsonArray ids && ids.Any(id => id?.GetValue<string>() == overrideId)))
        {
            reason = "Undo the complete selected-text correction instead.";
            return false;
        }

        if (item["revertedAtUtc"] is not null || item["RevertedAtUtc"] is not null)
        {
            return true;
        }

        item["revertedAtUtc"] = DateTimeOffset.UtcNow;
        return true;
    }
}
