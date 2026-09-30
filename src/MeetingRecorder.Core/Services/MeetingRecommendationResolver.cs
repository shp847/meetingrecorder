using System.Security.Cryptography;
using System.Text;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Resolves one truthful next step from an already-collected meeting metadata snapshot.
/// This type deliberately accepts neither paths requiring inspection nor transcript/provider content.
/// </summary>
public enum MeetingRecommendationAvailability
{
    Unknown = 0,
    Available = 1,
    Missing = 2,
    Disabled = 3,
    Stale = 4,
}

public enum MeetingRecommendationProcessingState
{
    Unknown = 0,
    Idle = 1,
    Queued = 2,
    Processing = 3,
    RushDecisionNeeded = 4,
}

public enum MeetingPrimaryRecommendationKind
{
    NoActionNeeded = 0,
    Evaluating = 1,
    Blocked = 2,
    RecoverTranscript = 3,
    RepairSpeakerLabels = 4,
    ReviewMissingTranscript = 5,
    ReviewProcessing = 6,
    ReviewCleanup = 7,
    RetrySummary = 8,
    ImproveMetadata = 9,
}

public enum MeetingRecommendationSeverity
{
    None = 0,
    Information = 1,
    Low = 2,
    Medium = 3,
    High = 4,
}

/// <summary>
/// Routes only to an already-safe surface. It never represents an implicit destructive operation.
/// </summary>
public enum MeetingRecommendationActionTarget
{
    None = 0,
    CheckAgain = 1,
    SettingsSetup = 2,
    MeetingDetails = 3,
    CleanupReview = 4,
}

/// <summary>Describes the bounded surface a recommendation may address.</summary>
public enum MeetingRecommendationScope
{
    None = 0,
    SingleMeeting = 1,
    CleanupReview = 2,
}

/// <summary>States whether the recommendation may be promoted without another refresh.</summary>
public enum MeetingRecommendationFreshness
{
    Current = 0,
    RefreshRequired = 1,
    Unknown = 2,
}

public sealed record MeetingRecommendationDismissal(
    string Fingerprint,
    int RecommendationVersion,
    DateTimeOffset DismissedAtUtc);

public sealed record MeetingRecommendationInput(
    string MeetingStem,
    int SnapshotVersion,
    DateTimeOffset? SnapshotObservedAtUtc,
    bool IsSnapshotStale,
    SessionState? SessionState,
    MeetingRecommendationAvailability RecoverableSource,
    MeetingRecommendationAvailability LocalTranscriptionSetup,
    MeetingRecommendationAvailability Transcript,
    MeetingRecommendationAvailability TranscriptArtifact,
    MeetingRecommendationAvailability SpeakerRepair,
    bool HasSuspiciousSpeakerLabels,
    MeetingRecommendationProcessingState Processing,
    bool SummaryRetryAvailable,
    bool MetadataPolishAvailable,
    IReadOnlyList<MeetingCleanupRecommendation>? CleanupRecommendations = null,
    IReadOnlyList<MeetingRecommendationDismissal>? Dismissals = null)
{
    /// <summary>
    /// Optional Sprint 2 row truth. Callers that have not derived presentation state yet
    /// retain the existing metadata-only ranking behavior.
    /// </summary>
    public MeetingPresentationState? PresentationState { get; init; }
}

public sealed record MeetingPrimaryRecommendation(
    MeetingPrimaryRecommendationKind Kind,
    string Label,
    string Reason,
    MeetingRecommendationSeverity Severity,
    MeetingRecommendationActionTarget ActionTarget,
    bool IsActionEligible,
    string? BlockReason,
    string SnapshotFingerprint,
    int RecommendationVersion,
    int SnapshotVersion,
    DateTimeOffset EvaluatedAtUtc,
    bool IsDismissed,
    string ReasonCode = "unknown",
    MeetingRecommendationScope Scope = MeetingRecommendationScope.None,
    MeetingRecommendationFreshness Freshness = MeetingRecommendationFreshness.Unknown)
{
    public bool HasPrimaryAction =>
        IsActionEligible &&
        ActionTarget != MeetingRecommendationActionTarget.None &&
        !IsDismissed;
}

public sealed class MeetingRecommendationResolver
{
    public const int CurrentRecommendationVersion = 2;

    public static readonly TimeSpan DismissalLifetime = TimeSpan.FromDays(30);

    public MeetingPrimaryRecommendation Resolve(MeetingRecommendationInput input, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);

        var recommendation = ResolveUndismissed(input, evaluatedAtUtc);
        var isDismissed = CanDismiss(recommendation.Kind, recommendation.Severity) &&
            HasActiveDismissal(input.Dismissals, recommendation.SnapshotFingerprint, evaluatedAtUtc);
        return recommendation with { IsDismissed = isDismissed };
    }

    private static MeetingPrimaryRecommendation ResolveUndismissed(
        MeetingRecommendationInput input,
        DateTimeOffset evaluatedAtUtc)
    {
        // Sprint 2 owns truth about stale and archived rows. A recommendation must not
        // manufacture an action against either terminal presentation state.
        if (input.IsSnapshotStale || input.SnapshotObservedAtUtc is null ||
            input.PresentationState == MeetingPresentationState.RefreshRequired)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.Evaluating,
                "Status needs refresh",
                "Meeting status is stale, so no recommendation is promoted yet.",
                MeetingRecommendationSeverity.Information,
                MeetingRecommendationActionTarget.None,
                eligible: false,
                blockReason: "Refresh meeting status before acting on a recommendation.",
                reasonCode: "stale-status");
        }

        if (input.PresentationState == MeetingPresentationState.Archived)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.NoActionNeeded,
                "Archived",
                "This meeting is archived and has no promoted next action.",
                MeetingRecommendationSeverity.None,
                MeetingRecommendationActionTarget.None,
                eligible: false,
                reasonCode: "archived");
        }

        var transcriptNeedsRecovery = input.SessionState == SessionState.Failed ||
            input.Transcript == MeetingRecommendationAvailability.Missing;
        if (transcriptNeedsRecovery)
        {
            return input.RecoverableSource switch
            {
                MeetingRecommendationAvailability.Available => ResolveRecoverySetup(input, evaluatedAtUtc),
                MeetingRecommendationAvailability.Missing or MeetingRecommendationAvailability.Disabled => Build(
                    input,
                    evaluatedAtUtc,
                    MeetingPrimaryRecommendationKind.Blocked,
                    "Transcript recovery is blocked",
                    "A recoverable source audio file is not available for this meeting.",
                    MeetingRecommendationSeverity.High,
                    MeetingRecommendationActionTarget.CheckAgain,
                    eligible: true,
                    blockReason: "Add or restore the source audio, then check again."),
                _ => Build(
                    input,
                    evaluatedAtUtc,
                    MeetingPrimaryRecommendationKind.Blocked,
                    "Check transcript recovery",
                    "Transcript recovery cannot be offered until source availability is confirmed.",
                    MeetingRecommendationSeverity.High,
                    MeetingRecommendationActionTarget.CheckAgain,
                    eligible: true,
                    blockReason: "Refresh the meeting catalog to confirm the source audio."),
            };
        }

        if (input.HasSuspiciousSpeakerLabels)
        {
            return input.SpeakerRepair switch
            {
                MeetingRecommendationAvailability.Available => Build(
                    input,
                    evaluatedAtUtc,
                    MeetingPrimaryRecommendationKind.RepairSpeakerLabels,
                    "Review speaker label repair",
                    "Speaker labels look unusually fragmented and need review before repair.",
                    MeetingRecommendationSeverity.High,
                    MeetingRecommendationActionTarget.MeetingDetails,
                    eligible: true),
                MeetingRecommendationAvailability.Missing or MeetingRecommendationAvailability.Disabled => Build(
                    input,
                    evaluatedAtUtc,
                    MeetingPrimaryRecommendationKind.Blocked,
                    "Speaker label repair is blocked",
                    "Speaker label repair is not ready for this meeting.",
                    MeetingRecommendationSeverity.High,
                    MeetingRecommendationActionTarget.SettingsSetup,
                    eligible: true,
                    blockReason: "Set up local speaker labeling, then review this meeting again."),
                _ => Build(
                    input,
                    evaluatedAtUtc,
                    MeetingPrimaryRecommendationKind.Blocked,
                    "Check speaker label repair",
                    "Speaker-label repair availability is not confirmed yet.",
                    MeetingRecommendationSeverity.High,
                    MeetingRecommendationActionTarget.CheckAgain,
                    eligible: true,
                    blockReason: "Refresh the meeting catalog to confirm speaker-label repair availability."),
            };
        }

        if (input.TranscriptArtifact is MeetingRecommendationAvailability.Missing or MeetingRecommendationAvailability.Disabled)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.ReviewMissingTranscript,
                "Review missing transcript",
                "This meeting has no confirmed transcript artifact to open.",
                MeetingRecommendationSeverity.High,
                MeetingRecommendationActionTarget.MeetingDetails,
                eligible: true);
        }

        if (input.Processing is MeetingRecommendationProcessingState.Queued or
            MeetingRecommendationProcessingState.Processing or
            MeetingRecommendationProcessingState.RushDecisionNeeded)
        {
            var reason = input.Processing switch
            {
                MeetingRecommendationProcessingState.Queued => "This meeting is queued for processing.",
                MeetingRecommendationProcessingState.Processing => "This meeting is still being processed.",
                _ => "This meeting needs a processing-priority decision.",
            };
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.ReviewProcessing,
                "Review processing status",
                reason,
                MeetingRecommendationSeverity.Medium,
                MeetingRecommendationActionTarget.MeetingDetails,
                eligible: true);
        }

        if (input.SummaryRetryAvailable)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.RetrySummary,
                "Review summary retry",
                "A previous summary attempt can be retried from the published transcript.",
                MeetingRecommendationSeverity.Low,
                MeetingRecommendationActionTarget.MeetingDetails,
                eligible: true,
                reasonCode: "summary-retry");
        }

        var cleanup = input.CleanupRecommendations?
            .Where(item => item.Action != MeetingCleanupAction.GenerateSummary)
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Action)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal)
            .FirstOrDefault();
        if (cleanup is not null)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.ReviewCleanup,
                "Review suggested cleanup",
                cleanup.Title,
                cleanup.Confidence == MeetingCleanupConfidence.High
                    ? MeetingRecommendationSeverity.Medium
                    : MeetingRecommendationSeverity.Low,
                MeetingRecommendationActionTarget.CleanupReview,
                eligible: true,
                cleanupFingerprint: cleanup.Fingerprint,
                reasonCode: "cleanup-review");
        }

        if (input.MetadataPolishAvailable)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.ImproveMetadata,
                "Review meeting details",
                "This meeting can use a clearer title or project label.",
                MeetingRecommendationSeverity.Low,
                MeetingRecommendationActionTarget.MeetingDetails,
                eligible: true);
        }

        if (HasUnknownMaterialState(input) ||
            input.PresentationState == MeetingPresentationState.Unavailable)
        {
            return Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.Evaluating,
                "Checking meeting status",
                "Meeting status is still being checked, so completion is not confirmed yet.",
                MeetingRecommendationSeverity.Information,
                MeetingRecommendationActionTarget.CheckAgain,
                eligible: true);
        }

        return Build(
            input,
            evaluatedAtUtc,
            MeetingPrimaryRecommendationKind.NoActionNeeded,
            "Complete — no action needed",
            "Transcript and current meeting metadata are available.",
            MeetingRecommendationSeverity.None,
            MeetingRecommendationActionTarget.None,
            eligible: false);
    }

    private static MeetingPrimaryRecommendation ResolveRecoverySetup(
        MeetingRecommendationInput input,
        DateTimeOffset evaluatedAtUtc)
    {
        return input.LocalTranscriptionSetup switch
        {
            MeetingRecommendationAvailability.Available => Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.RecoverTranscript,
                "Review transcript recovery",
                "A failed or missing transcript can be recovered from the available source audio.",
                MeetingRecommendationSeverity.High,
                MeetingRecommendationActionTarget.MeetingDetails,
                eligible: true),
            MeetingRecommendationAvailability.Missing or MeetingRecommendationAvailability.Disabled => Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.Blocked,
                "Set up transcript recovery",
                "Transcript recovery needs a local transcription model before it can start.",
                MeetingRecommendationSeverity.High,
                MeetingRecommendationActionTarget.SettingsSetup,
                eligible: true,
                blockReason: "Set up a local transcription model, then return to this meeting."),
            _ => Build(
                input,
                evaluatedAtUtc,
                MeetingPrimaryRecommendationKind.Blocked,
                "Check transcript setup",
                "Transcript recovery setup has not been confirmed yet.",
                MeetingRecommendationSeverity.High,
                MeetingRecommendationActionTarget.CheckAgain,
                eligible: true,
                blockReason: "Refresh setup status before retrying transcript recovery."),
        };
    }

    private static bool HasUnknownMaterialState(MeetingRecommendationInput input)
    {
        return input.RecoverableSource is MeetingRecommendationAvailability.Unknown or MeetingRecommendationAvailability.Stale ||
            input.LocalTranscriptionSetup is MeetingRecommendationAvailability.Unknown or MeetingRecommendationAvailability.Stale ||
            input.Transcript is MeetingRecommendationAvailability.Unknown or MeetingRecommendationAvailability.Stale ||
            input.TranscriptArtifact is MeetingRecommendationAvailability.Unknown or MeetingRecommendationAvailability.Stale ||
            input.SpeakerRepair is MeetingRecommendationAvailability.Unknown or MeetingRecommendationAvailability.Stale ||
            input.Processing == MeetingRecommendationProcessingState.Unknown;
    }

    private static MeetingPrimaryRecommendation Build(
        MeetingRecommendationInput input,
        DateTimeOffset evaluatedAtUtc,
        MeetingPrimaryRecommendationKind kind,
        string label,
        string reason,
        MeetingRecommendationSeverity severity,
        MeetingRecommendationActionTarget actionTarget,
        bool eligible,
        string? blockReason = null,
        string? cleanupFingerprint = null,
        string? reasonCode = null)
    {
        var fingerprint = BuildFingerprint(input, kind, actionTarget, cleanupFingerprint);
        return new MeetingPrimaryRecommendation(
            kind,
            label,
            reason,
            severity,
            actionTarget,
            eligible,
            blockReason,
            fingerprint,
            CurrentRecommendationVersion,
            Math.Max(0, input.SnapshotVersion),
            evaluatedAtUtc,
            IsDismissed: false,
            ReasonCode: reasonCode ?? ToReasonCode(kind),
            Scope: ToScope(actionTarget),
            Freshness: ToFreshness(input));
    }

    private static string BuildFingerprint(
        MeetingRecommendationInput input,
        MeetingPrimaryRecommendationKind kind,
        MeetingRecommendationActionTarget actionTarget,
        string? cleanupFingerprint)
    {
        var fingerprintData = string.Join("|", new[]
        {
            CurrentRecommendationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            input.MeetingStem.Trim(),
            Math.Max(0, input.SnapshotVersion).ToString(System.Globalization.CultureInfo.InvariantCulture),
            kind.ToString(),
            actionTarget.ToString(),
            input.SessionState?.ToString() ?? "unknown",
            input.RecoverableSource.ToString(),
            input.LocalTranscriptionSetup.ToString(),
            input.Transcript.ToString(),
            input.TranscriptArtifact.ToString(),
            input.SpeakerRepair.ToString(),
            input.HasSuspiciousSpeakerLabels.ToString(),
            input.Processing.ToString(),
            input.SummaryRetryAvailable.ToString(),
            input.MetadataPolishAvailable.ToString(),
            input.PresentationState?.ToString() ?? "not-derived",
            cleanupFingerprint ?? string.Empty,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintData)));
    }

    private static bool CanDismiss(MeetingPrimaryRecommendationKind kind, MeetingRecommendationSeverity severity)
    {
        return severity is MeetingRecommendationSeverity.Low or MeetingRecommendationSeverity.Medium &&
            kind is MeetingPrimaryRecommendationKind.ReviewCleanup or
                MeetingPrimaryRecommendationKind.RetrySummary or
                MeetingPrimaryRecommendationKind.ImproveMetadata;
    }

    private static MeetingRecommendationScope ToScope(MeetingRecommendationActionTarget target) => target switch
    {
        MeetingRecommendationActionTarget.CleanupReview => MeetingRecommendationScope.CleanupReview,
        MeetingRecommendationActionTarget.None => MeetingRecommendationScope.None,
        _ => MeetingRecommendationScope.SingleMeeting,
    };

    private static MeetingRecommendationFreshness ToFreshness(MeetingRecommendationInput input)
    {
        if (input.IsSnapshotStale || input.SnapshotObservedAtUtc is null ||
            input.PresentationState == MeetingPresentationState.RefreshRequired)
            return MeetingRecommendationFreshness.RefreshRequired;

        return HasUnknownMaterialState(input) || input.PresentationState == MeetingPresentationState.Unavailable
            ? MeetingRecommendationFreshness.Unknown
            : MeetingRecommendationFreshness.Current;
    }

    private static string ToReasonCode(MeetingPrimaryRecommendationKind kind) => kind switch
    {
        MeetingPrimaryRecommendationKind.NoActionNeeded => "complete",
        MeetingPrimaryRecommendationKind.Evaluating => "status-evaluating",
        MeetingPrimaryRecommendationKind.Blocked => "blocked",
        MeetingPrimaryRecommendationKind.RecoverTranscript => "transcript-recovery",
        MeetingPrimaryRecommendationKind.RepairSpeakerLabels => "speaker-repair",
        MeetingPrimaryRecommendationKind.ReviewMissingTranscript => "transcript-artifact-missing",
        MeetingPrimaryRecommendationKind.ReviewProcessing => "processing-review",
        MeetingPrimaryRecommendationKind.ReviewCleanup => "cleanup-review",
        MeetingPrimaryRecommendationKind.RetrySummary => "summary-retry",
        MeetingPrimaryRecommendationKind.ImproveMetadata => "metadata-polish",
        _ => "unknown",
    };

    private static bool HasActiveDismissal(
        IReadOnlyList<MeetingRecommendationDismissal>? dismissals,
        string fingerprint,
        DateTimeOffset evaluatedAtUtc)
    {
        if (dismissals is null)
        {
            return false;
        }

        return dismissals.Any(dismissal =>
            dismissal.RecommendationVersion == CurrentRecommendationVersion &&
            string.Equals(dismissal.Fingerprint, fingerprint, StringComparison.Ordinal) &&
            dismissal.DismissedAtUtc <= evaluatedAtUtc &&
            evaluatedAtUtc - dismissal.DismissedAtUtc <= DismissalLifetime);
    }
}
