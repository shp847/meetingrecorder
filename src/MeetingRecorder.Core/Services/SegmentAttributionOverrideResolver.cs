using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public static class SegmentAttributionOverrideResolver
{
    public static EffectiveSegmentAttribution Resolve(
        TranscriptSegment segment,
        string artifactRevision,
        IReadOnlyDictionary<string, SpeakerIdentity> speakers,
        IReadOnlyList<SegmentAttributionOverride> overrides)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(segment.SourceSpeakerLabel)
            ? string.IsNullOrWhiteSpace(segment.SpeakerLabel) ? "Speaker" : segment.SpeakerLabel.Trim()
            : segment.SourceSpeakerLabel.Trim();
        var sourceId = string.IsNullOrWhiteSpace(segment.SpeakerId) ? null : segment.SpeakerId.Trim();
        if (string.IsNullOrWhiteSpace(segment.SegmentId) || string.IsNullOrWhiteSpace(artifactRevision))
            return new(sourceLabel, sourceId, EffectiveSegmentAttributionState.Source, null, null);

        var match = overrides.LastOrDefault(item =>
            string.Equals(item.ArtifactRevision, artifactRevision, StringComparison.Ordinal) &&
            string.Equals(item.SegmentId, segment.SegmentId, StringComparison.Ordinal));
        if (match is null) return new(sourceLabel, sourceId, EffectiveSegmentAttributionState.Source, null, null);
        if (!string.Equals(match.ActorSource, "UserOverride", StringComparison.Ordinal))
            return new(sourceLabel, sourceId, EffectiveSegmentAttributionState.ReviewNeeded, match.Id, "Unknown override source.");
        if (!string.Equals(match.OriginalSpeakerId, sourceId, StringComparison.Ordinal))
            return new(sourceLabel, sourceId, EffectiveSegmentAttributionState.ReviewNeeded, match.Id, "The original speaker changed.");

        if (match.TargetKind == SegmentAttributionOverrideTargetKind.DisplayName && IsUsableName(match.TargetValue))
            return new(match.TargetValue.Trim(), sourceId, EffectiveSegmentAttributionState.Override, match.Id, null);
        if (match.TargetKind == SegmentAttributionOverrideTargetKind.Speaker && speakers.TryGetValue(match.TargetValue.Trim(), out var target))
            return new(target.DisplayName.Trim(), sourceId, EffectiveSegmentAttributionState.Override, match.Id, null);
        return new(sourceLabel, sourceId, EffectiveSegmentAttributionState.ReviewNeeded, match.Id, "Override target is unavailable.");
    }

    private static bool IsUsableName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("Speaker", StringComparison.OrdinalIgnoreCase);
}
