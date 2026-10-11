using MeetingRecorder.Core.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Services;

public static class SegmentAttributionOverrideDocumentMutator
{
    public static bool TryApply(JsonObject document, SegmentAttributionOverride value, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(value.SegmentId) || string.IsNullOrWhiteSpace(value.OriginalSpeakerId) ||
            !string.Equals(value.ActorSource, "UserOverride", StringComparison.Ordinal))
        {
            reason = "Invalid paragraph correction.";
            return false;
        }
        if (document["segments"] is not JsonArray segments) { reason = "Transcript segments are unavailable."; return false; }
        var segment = segments.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["segmentId"]?.GetValue<string>(), value.SegmentId, StringComparison.Ordinal));
        if (segment is null || !string.Equals(segment["speakerId"]?.GetValue<string>(), value.OriginalSpeakerId, StringComparison.Ordinal)) { reason = "The transcript segment changed."; return false; }
        if (string.IsNullOrWhiteSpace(value.TargetValue)) { reason = "A correction target is required."; return false; }
        var existing = document["speakerAttributionOverrides"] as JsonArray ?? new JsonArray();
        if (document["speakerAttributionOverrides"] is null) document["speakerAttributionOverrides"] = existing;
        var prior = existing.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["id"]?.GetValue<string>(), value.Id, StringComparison.Ordinal));
        if (prior is not null) return true;
        existing.Add(JsonSerializer.SerializeToNode(value));
        if (value.TargetKind == SegmentAttributionOverrideTargetKind.DisplayName) segment["speakerLabel"] = value.TargetValue.Trim();
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

        if (item["revertedAtUtc"] is not null || item["RevertedAtUtc"] is not null)
        {
            return true;
        }

        item["revertedAtUtc"] = DateTimeOffset.UtcNow;
        return true;
    }
}
