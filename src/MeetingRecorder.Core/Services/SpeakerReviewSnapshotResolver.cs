using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Projects one immutable diarization artifact revision into safe speaker-review rows.
/// The projection intentionally carries attribution metadata only; it never exposes
/// voice samples, embeddings, audio locations, or profile payloads.
/// </summary>
public enum SpeakerReviewNameSource
{
    Generic = 0,
    UserEntered = 1,
    Suggested = 2,
    AutoApplied = 3,
    ReviewRequired = 4,
}

public enum SpeakerReviewConfidence
{
    Unavailable = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum SpeakerReviewEvidenceAvailability
{
    Available = 0,
    Unavailable = 1,
}

public enum SpeakerReviewLearningEligibility
{
    Eligible = 0,
    Disabled = 1,
    InsufficientEvidence = 2,
    ProfilesUnavailable = 3,
}

public enum SpeakerReviewFreshness
{
    Current = 0,
    Stale = 1,
}

public enum SpeakerReviewAction
{
    EditName = 0,
    UseSuggestion = 1,
    RejectSuggestion = 2,
    Reload = 3,
    RepairSpeakerLabels = 4,
}

public sealed record SpeakerReviewSnapshotInput(
    string MeetingStableIdentity,
    string ArtifactRevision,
    IReadOnlyList<SpeakerIdentity>? Speakers,
    IReadOnlySet<string>? SpeakerIdsWithEvidence,
    SpeakerNameLearningMode LearningMode,
    bool IsLocalProfileStoreAvailable,
    bool IsStale,
    bool HasRepairWarning);

public sealed record SpeakerReviewRow(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    string AnonymousLabel,
    string DisplayName,
    string? ProposedDisplayName,
    SpeakerReviewNameSource NameSource,
    SpeakerReviewConfidence Confidence,
    string Explanation,
    string? ProfileId,
    bool IsUserEdited,
    SpeakerReviewEvidenceAvailability EvidenceAvailability,
    SpeakerReviewLearningEligibility LearningEligibility,
    bool HasRepairWarning,
    SpeakerReviewFreshness Freshness,
    IReadOnlyList<SpeakerReviewAction> PermittedActions);

public sealed record SpeakerReviewSnapshot(
    string MeetingStableIdentity,
    string ArtifactRevision,
    SpeakerReviewFreshness Freshness,
    IReadOnlyList<SpeakerReviewRow> Rows);

public static class SpeakerReviewSnapshotResolver
{
    public static SpeakerReviewSnapshot Resolve(SpeakerReviewSnapshotInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var meetingIdentity = input.MeetingStableIdentity?.Trim() ?? string.Empty;
        var artifactRevision = input.ArtifactRevision?.Trim() ?? string.Empty;
        var freshness = input.IsStale || string.IsNullOrWhiteSpace(meetingIdentity) || string.IsNullOrWhiteSpace(artifactRevision)
            ? SpeakerReviewFreshness.Stale
            : SpeakerReviewFreshness.Current;
        var evidenceIds = input.SpeakerIdsWithEvidence ?? new HashSet<string>(StringComparer.Ordinal);
        var speakers = input.Speakers ?? Array.Empty<SpeakerIdentity>();

        var rows = speakers
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Id))
            .GroupBy(speaker => speaker.Id.Trim(), StringComparer.Ordinal)
            .Select(group => group.First())
            .Select((speaker, index) => BuildRow(input, meetingIdentity, artifactRevision, freshness, evidenceIds, speaker, index))
            .ToArray();

        return new SpeakerReviewSnapshot(meetingIdentity, artifactRevision, freshness, rows);
    }

    private static SpeakerReviewRow BuildRow(
        SpeakerReviewSnapshotInput input,
        string meetingIdentity,
        string artifactRevision,
        SpeakerReviewFreshness freshness,
        IReadOnlySet<string> evidenceIds,
        SpeakerIdentity speaker,
        int index)
    {
        var source = ResolveSource(speaker);
        var hasEvidence = evidenceIds.Contains(speaker.Id);
        var evidence = hasEvidence
            ? SpeakerReviewEvidenceAvailability.Available
            : SpeakerReviewEvidenceAvailability.Unavailable;
        var learning = input.LearningMode == SpeakerNameLearningMode.Disabled
            ? SpeakerReviewLearningEligibility.Disabled
            : !input.IsLocalProfileStoreAvailable
                ? SpeakerReviewLearningEligibility.ProfilesUnavailable
                : !hasEvidence
                    ? SpeakerReviewLearningEligibility.InsufficientEvidence
                    : SpeakerReviewLearningEligibility.Eligible;
        var actions = ResolveActions(source, freshness, input.HasRepairWarning);

        return new SpeakerReviewRow(
            meetingIdentity,
            artifactRevision,
            speaker.Id.Trim(),
            $"Speaker {index + 1}",
            NormalizeDisplayName(speaker.DisplayName, index),
            source == SpeakerReviewNameSource.Suggested ? NormalizeOptionalName(speaker.SuggestedDisplayName) : null,
            source,
            BucketConfidence(speaker.Confidence),
            Explain(source, freshness, input.HasRepairWarning),
            NormalizeOptionalName(speaker.ProfileId),
            speaker.IsUserEdited || speaker.NameSource == SpeakerNameSource.UserEdited,
            evidence,
            learning,
            input.HasRepairWarning,
            freshness,
            actions);
    }

    private static SpeakerReviewNameSource ResolveSource(SpeakerIdentity speaker)
    {
        if (speaker.IsUserEdited || speaker.NameSource == SpeakerNameSource.UserEdited)
        {
            return SpeakerReviewNameSource.UserEntered;
        }

        return speaker.NameSource switch
        {
            SpeakerNameSource.None => SpeakerReviewNameSource.Generic,
            SpeakerNameSource.AutoAppliedVoiceProfile => SpeakerReviewNameSource.AutoApplied,
            SpeakerNameSource.SuggestedVoiceProfile => SpeakerReviewNameSource.Suggested,
            _ => SpeakerReviewNameSource.ReviewRequired,
        };
    }

    private static SpeakerReviewConfidence BucketConfidence(double? confidence) => confidence switch
    {
        null => SpeakerReviewConfidence.Unavailable,
        >= 0.90d => SpeakerReviewConfidence.High,
        >= 0.70d => SpeakerReviewConfidence.Medium,
        _ => SpeakerReviewConfidence.Low,
    };

    private static IReadOnlyList<SpeakerReviewAction> ResolveActions(
        SpeakerReviewNameSource source,
        SpeakerReviewFreshness freshness,
        bool hasRepairWarning)
    {
        if (freshness == SpeakerReviewFreshness.Stale)
        {
            return [SpeakerReviewAction.Reload];
        }

        var actions = new List<SpeakerReviewAction> { SpeakerReviewAction.EditName };
        if (source == SpeakerReviewNameSource.Suggested)
        {
            actions.Add(SpeakerReviewAction.UseSuggestion);
            actions.Add(SpeakerReviewAction.RejectSuggestion);
        }

        if (hasRepairWarning)
        {
            actions.Add(SpeakerReviewAction.RepairSpeakerLabels);
        }

        return actions;
    }

    private static string Explain(SpeakerReviewNameSource source, SpeakerReviewFreshness freshness, bool hasRepairWarning)
    {
        if (freshness == SpeakerReviewFreshness.Stale)
        {
            return "This speaker data changed. Reload before changing the meeting display name.";
        }

        if (hasRepairWarning)
        {
            return "Speaker labels may need repair. Review the cluster before trusting a name suggestion.";
        }

        return source switch
        {
            SpeakerReviewNameSource.Generic => "This is an anonymous diarization label. Add a meeting display name only after review.",
            SpeakerReviewNameSource.UserEntered => "This meeting display name was confirmed by a user and takes precedence over local profile matches.",
            SpeakerReviewNameSource.Suggested => "This local voice-profile suggestion is not a confirmed identity. Accept or reject it before applying a meeting display name.",
            SpeakerReviewNameSource.AutoApplied => "This meeting display name was auto-applied from a local voice profile and can be changed or undone later.",
            _ => "This speaker attribution uses an unknown source. Review the name before changing it.",
        };
    }

    private static string NormalizeDisplayName(string? displayName, int index) =>
        string.IsNullOrWhiteSpace(displayName) ? $"Speaker {index + 1}" : displayName.Trim();

    private static string? NormalizeOptionalName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
