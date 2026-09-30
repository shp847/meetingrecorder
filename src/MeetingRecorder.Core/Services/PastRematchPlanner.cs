using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum PastRematchDisposition { Eligible, Blocked, Skip }
public enum PastRematchOutcome { AutoApplied, Suggested, Unchanged, Skipped, Failed, Cancelled }

public sealed record PastRematchTarget(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string MetadataFingerprint,
    bool IsPublishedStructuredTranscript,
    bool HasCompatibleVoiceSamples,
    bool HasActiveProfiles,
    bool HasGenericOrSuggestedSpeaker,
    bool HasUserEnteredName,
    bool HasSegmentOverride,
    bool IsSuppressed,
    bool IsBusy,
    bool HasCurrentArtifactFingerprint);

public sealed record PastRematchPlanItem(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string MetadataFingerprint,
    PastRematchDisposition Disposition,
    string Reason);

public sealed record PastRematchPlan(IReadOnlyList<PastRematchPlanItem> Items)
{
    public int EligibleCount => Items.Count(item => item.Disposition == PastRematchDisposition.Eligible);
}

/// <summary>Plans a manual metadata-only rematch; it neither opens audio nor invokes processing.</summary>
public static class PastRematchPlanner
{
    public static PastRematchPlan Build(IReadOnlyList<PastRematchTarget>? targets)
    {
        var items = (targets ?? Array.Empty<PastRematchTarget>())
            .Where(target => !string.IsNullOrWhiteSpace(target.MeetingStableIdentity))
            .GroupBy(target => target.MeetingStableIdentity.Trim(), StringComparer.Ordinal)
            .Select(group => Plan(group.First()))
            .ToArray();
        return new PastRematchPlan(items);
    }

    private static PastRematchPlanItem Plan(PastRematchTarget target)
    {
        var (disposition, reason) = string.IsNullOrWhiteSpace(target.ArtifactRevision) || !target.HasCurrentArtifactFingerprint
            ? (PastRematchDisposition.Blocked, "Meeting metadata changed. Reload before rematching names.")
            : target.IsBusy ? (PastRematchDisposition.Blocked, "This meeting is changing now. Wait for it to finish before rematching names.")
            : !target.IsPublishedStructuredTranscript ? (PastRematchDisposition.Skip, "This meeting has no current structured transcript to rematch.")
            : !target.HasCompatibleVoiceSamples ? (PastRematchDisposition.Skip, "This meeting has no compatible local speaker evidence.")
            : !target.HasActiveProfiles ? (PastRematchDisposition.Skip, "No active local voice profiles are available.")
            : target.HasUserEnteredName || target.HasSegmentOverride ? (PastRematchDisposition.Skip, "User-confirmed speaker attribution is preserved and will not be rematched.")
            : target.IsSuppressed ? (PastRematchDisposition.Skip, "A local profile was rejected for this meeting speaker.")
            : !target.HasGenericOrSuggestedSpeaker ? (PastRematchDisposition.Skip, "No generic or suggested speaker attribution needs rematching.")
            : (PastRematchDisposition.Eligible, "Eligible for a local metadata-only speaker-name rematch.");
        return new PastRematchPlanItem(target.MeetingStableIdentity.Trim(), target.ArtifactRevision, target.MetadataFingerprint, disposition, reason);
    }
}
