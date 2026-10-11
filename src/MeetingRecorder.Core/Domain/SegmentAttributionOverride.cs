using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

public enum SegmentAttributionOverrideTargetKind
{
    Speaker = 0,
    DisplayName = 1,
}

/// <summary>Immutable, revision-scoped user correction. It never changes diarization output.</summary>
public sealed record SegmentAttributionOverride(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("artifactRevision")] string ArtifactRevision,
    [property: JsonPropertyName("segmentId")] string SegmentId,
    [property: JsonPropertyName("originalSpeakerId")] string OriginalSpeakerId,
    [property: JsonPropertyName("targetKind")] SegmentAttributionOverrideTargetKind TargetKind,
    [property: JsonPropertyName("targetValue")] string TargetValue,
    [property: JsonPropertyName("actorSource")] string ActorSource,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("revertedAtUtc")] DateTimeOffset? RevertedAtUtc = null);

public enum EffectiveSegmentAttributionState
{
    Source = 0,
    Override = 1,
    ReviewNeeded = 2,
}

public sealed record EffectiveSegmentAttribution(
    string DisplayLabel,
    string? OriginalSpeakerId,
    EffectiveSegmentAttributionState State,
    string? OverrideId,
    string? ReviewReason);
