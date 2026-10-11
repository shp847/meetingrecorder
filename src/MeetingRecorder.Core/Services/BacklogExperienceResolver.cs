using System.Globalization;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// The user-facing interpretation of already-observed queue metadata. This
/// deliberately does not inspect manifests, worker output, paths, or logs.
/// </summary>
internal enum BacklogExperienceKind
{
    Idle = 0,
    Processing = 1,
    Paused = 2,
    NeedsSetup = 3,
    NeedsDecision = 4,
    Failed = 5,
    RefreshRequired = 6,
}

internal enum BacklogEtaConfidence
{
    Unavailable = 0,
    Measured = 1,
    StatusStale = 2,
}

/// <summary>
/// A recovery intent, rather than a command. Presentation code must route the
/// intent to its existing review surface and must not perform work implicitly.
/// </summary>
internal enum BacklogRecoveryAction
{
    None = 0,
    RefreshStatus = 1,
    OpenSetup = 2,
    PublishTranscriptFirst = 3,
    RunSpeakerLabelsLater = 4,
    RetryTranscript = 5,
    ProcessThisFirst = 6,
    InspectSource = 7,
    ExplainUnrecoverableSource = 8,
}

internal sealed record BacklogRecoveryMetadata(
    string MeetingStem,
    string? MeetingTitle,
    bool HasFailedWork,
    MeetingPrimaryRecommendationKind RecommendationKind,
    MeetingRecommendationActionTarget RecommendationTarget,
    string? Reason,
    bool CanRetry);

internal sealed record BacklogExperienceInput(
    ProcessingQueueStatusSnapshot LiveSnapshot,
    PersistedProcessingBacklogState? PersistedBacklog,
    BacklogRecoveryMetadata? Recovery,
    DateTimeOffset NowUtc);

internal sealed record BacklogExperienceState(
    BacklogExperienceKind Kind,
    bool IsVisible,
    string Headline,
    string Detail,
    int RemainingCount,
    string? CurrentMeetingTitle,
    string? StageText,
    TimeSpan? Elapsed,
    string CurrentEtaText,
    string OverallEtaText,
    BacklogEtaConfidence EtaConfidence,
    BacklogRecoveryAction RecoveryAction,
    MeetingRecommendationActionTarget ActionTarget,
    string? ActionLabel,
    string? FailureReason,
    bool RetryEligible)
{
    public bool HasAction => RecoveryAction != BacklogRecoveryAction.None &&
        ActionTarget != MeetingRecommendationActionTarget.None &&
        !string.IsNullOrWhiteSpace(ActionLabel);
}

/// <summary>
/// Converts the live queue and the last known meeting catalog state into one
/// conservative user-facing backlog state. A stale live snapshot never claims
/// an empty queue or a countdown, and saved manifests never overwrite live
/// queue truth.
/// </summary>
internal sealed class BacklogExperienceResolver
{
    internal static readonly TimeSpan MaximumFreshStatusAge = TimeSpan.FromMinutes(2);

    public BacklogExperienceState Resolve(BacklogExperienceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var snapshot = input.LiveSnapshot ?? throw new ArgumentNullException(nameof(input.LiveSnapshot));
        if (IsSnapshotStale(snapshot.LastUpdatedAtUtc, input.NowUtc))
        {
            return BuildRefreshRequired(snapshot, input.PersistedBacklog);
        }

        if (HasLiveQueueSignal(snapshot))
        {
            return ResolveLiveQueue(snapshot, input.Recovery, input.NowUtc);
        }

        // A catalog can tell us work existed, but it cannot prove the queue is
        // still idle or running. Keep that distinction explicit.
        if (input.PersistedBacklog is { HasBacklog: true })
        {
            return new BacklogExperienceState(
                BacklogExperienceKind.RefreshRequired,
                IsVisible: true,
                "STATUS NEEDS REFRESH",
                "Saved meeting records still show unfinished work. Refresh status before acting on the backlog.",
                input.PersistedBacklog.TotalRemainingCount,
                null,
                null,
                null,
                "Current ETA unavailable until live status refreshes",
                "Overall ETA unavailable until live status refreshes",
                BacklogEtaConfidence.Unavailable,
                BacklogRecoveryAction.RefreshStatus,
                MeetingRecommendationActionTarget.CheckAgain,
                "Refresh status",
                null,
                RetryEligible: false);
        }

        if (input.Recovery is { HasFailedWork: true } recovery)
        {
            return ResolveRecovery(recovery);
        }

        return new BacklogExperienceState(
            BacklogExperienceKind.Idle,
            IsVisible: false,
            "IDLE",
            "No background meeting work is reported.",
            0,
            null,
            null,
            null,
            "Current ETA unavailable",
            "Overall ETA unavailable",
            BacklogEtaConfidence.Unavailable,
            BacklogRecoveryAction.None,
            MeetingRecommendationActionTarget.None,
            null,
            null,
            RetryEligible: false);
    }

    private static BacklogExperienceState ResolveLiveQueue(
        ProcessingQueueStatusSnapshot snapshot,
        BacklogRecoveryMetadata? recovery,
        DateTimeOffset nowUtc)
    {
        if (snapshot.CurrentStageState == StageExecutionState.Failed)
        {
            return ResolveFailedStage(snapshot, nowUtc);
        }

        if (snapshot.RunState == ProcessingQueueRunState.Paused)
        {
            return new BacklogExperienceState(
                BacklogExperienceKind.Paused,
                IsVisible: true,
                "PAUSED",
                snapshot.PauseReason is ProcessingQueuePauseReason.LiveRecordingResponsiveMode or ProcessingQueuePauseReason.LiveRecordingOvernightAcceleration
                    ? "Background work is paused while a live recording is protected."
                    : "Background work is paused and needs review before it can continue.",
                snapshot.TotalRemainingCount,
                snapshot.CurrentTitle,
                null,
                null,
                "Current ETA unavailable while paused",
                "Overall ETA unavailable while paused",
                BacklogEtaConfidence.Unavailable,
                BacklogRecoveryAction.None,
                MeetingRecommendationActionTarget.None,
                null,
                null,
                RetryEligible: false);
        }

        var stageText = BuildStageText(snapshot.CurrentStageName, snapshot.RunState);
        var elapsed = snapshot.CurrentItemStartedAtUtc is { } startedAtUtc
            ? ClampElapsed(nowUtc - startedAtUtc)
            : (TimeSpan?)null;
        var currentEta = FormatMeasuredEta(snapshot.CurrentItemEstimatedRemaining);
        var overallEta = FormatMeasuredEta(snapshot.OverallEstimatedRemaining);
        var etaConfidence = snapshot.CurrentItemEstimatedRemaining is not null || snapshot.OverallEstimatedRemaining is not null
            ? BacklogEtaConfidence.Measured
            : BacklogEtaConfidence.Unavailable;
        var action = recovery is { HasFailedWork: true }
            ? ResolveRecovery(recovery)
            : null;
        var detail = BuildLiveDetail(snapshot.CurrentTitle, stageText, elapsed, currentEta);

        return new BacklogExperienceState(
            BacklogExperienceKind.Processing,
            IsVisible: true,
            snapshot.RunState == ProcessingQueueRunState.Queued ? "QUEUED" : "PROCESSING",
            detail,
            snapshot.TotalRemainingCount,
            snapshot.CurrentTitle,
            stageText,
            elapsed,
            currentEta,
            overallEta,
            etaConfidence,
            action?.RecoveryAction ?? BacklogRecoveryAction.None,
            action?.ActionTarget ?? MeetingRecommendationActionTarget.None,
            action?.ActionLabel,
            action?.FailureReason,
            action?.RetryEligible ?? false);
    }

    private static BacklogExperienceState ResolveFailedStage(
        ProcessingQueueStatusSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        var (action, label) = NormalizeStage(snapshot.CurrentStageName) switch
        {
            "transcript" => (BacklogRecoveryAction.RetryTranscript, "Review transcript recovery"),
            "speaker labels" => (BacklogRecoveryAction.RunSpeakerLabelsLater, "Review speaker labels"),
            "transcript publication" => (BacklogRecoveryAction.PublishTranscriptFirst, "Review transcript publication"),
            _ => (BacklogRecoveryAction.RefreshStatus, "Refresh status"),
        };
        var stageText = BuildStageText(snapshot.CurrentStageName, snapshot.RunState);
        var elapsed = snapshot.CurrentItemStartedAtUtc is { } startedAtUtc
            ? ClampElapsed(nowUtc - startedAtUtc)
            : (TimeSpan?)null;
        return new BacklogExperienceState(
            BacklogExperienceKind.Failed,
            IsVisible: true,
            "FAILED",
            string.IsNullOrWhiteSpace(snapshot.CurrentTitle)
                ? $"{stageText} did not complete. Review the safe recovery step."
                : $"{snapshot.CurrentTitle}: {stageText} did not complete. Review the safe recovery step.",
            snapshot.TotalRemainingCount,
            snapshot.CurrentTitle,
            stageText,
            elapsed,
            "Current ETA unavailable after a failure",
            "Overall ETA unavailable until the failure is reviewed",
            BacklogEtaConfidence.Unavailable,
            action,
            action == BacklogRecoveryAction.RefreshStatus
                ? MeetingRecommendationActionTarget.CheckAgain
                : MeetingRecommendationActionTarget.MeetingDetails,
            label,
            "The latest processing stage reported a failure.",
            RetryEligible: action is BacklogRecoveryAction.RetryTranscript or BacklogRecoveryAction.PublishTranscriptFirst);
    }

    private static BacklogExperienceState ResolveRecovery(BacklogRecoveryMetadata recovery)
    {
        var (kind, action, target, label, detail) = recovery.RecommendationKind switch
        {
            MeetingPrimaryRecommendationKind.RecoverTranscript => (
                BacklogExperienceKind.Failed,
                BacklogRecoveryAction.RetryTranscript,
                MeetingRecommendationActionTarget.MeetingDetails,
                "Review transcript recovery",
                "A failed transcript can be recovered from the available source audio."),
            MeetingPrimaryRecommendationKind.ReviewMissingTranscript => (
                BacklogExperienceKind.Failed,
                BacklogRecoveryAction.InspectSource,
                MeetingRecommendationActionTarget.MeetingDetails,
                "Inspect source",
                "The transcript artifact is missing; inspect the available source before retrying."),
            MeetingPrimaryRecommendationKind.Blocked when recovery.RecommendationTarget == MeetingRecommendationActionTarget.SettingsSetup => (
                BacklogExperienceKind.NeedsSetup,
                BacklogRecoveryAction.OpenSetup,
                MeetingRecommendationActionTarget.SettingsSetup,
                "Open setup",
                "Recovery needs local setup before it can continue."),
            MeetingPrimaryRecommendationKind.Blocked when recovery.RecommendationTarget == MeetingRecommendationActionTarget.CheckAgain => (
                BacklogExperienceKind.NeedsDecision,
                BacklogRecoveryAction.ExplainUnrecoverableSource,
                MeetingRecommendationActionTarget.MeetingDetails,
                "Review recovery",
                "Recovery cannot proceed until the source and current meeting status are reviewed."),
            MeetingPrimaryRecommendationKind.RepairSpeakerLabels => (
                BacklogExperienceKind.NeedsDecision,
                BacklogRecoveryAction.RunSpeakerLabelsLater,
                MeetingRecommendationActionTarget.MeetingDetails,
                "Review speaker labels",
                "Speaker labels need review before any repair is queued."),
            MeetingPrimaryRecommendationKind.ReviewProcessing => (
                BacklogExperienceKind.NeedsDecision,
                BacklogRecoveryAction.ProcessThisFirst,
                MeetingRecommendationActionTarget.MeetingDetails,
                "Review processing priority",
                "This meeting is still waiting for a processing decision."),
            _ => (
                BacklogExperienceKind.Failed,
                BacklogRecoveryAction.RefreshStatus,
                MeetingRecommendationActionTarget.CheckAgain,
                "Refresh status",
                "The failed meeting status needs a fresh check before recovery can be offered."),
        };
        var titlePrefix = string.IsNullOrWhiteSpace(recovery.MeetingTitle)
            ? string.Empty
            : $"{recovery.MeetingTitle}: ";
        return new BacklogExperienceState(
            kind,
            IsVisible: true,
            kind switch
            {
                BacklogExperienceKind.NeedsSetup => "NEEDS SETUP",
                BacklogExperienceKind.NeedsDecision => "NEEDS DECISION",
                _ => "FAILED",
            },
            titlePrefix + detail,
            0,
            recovery.MeetingTitle,
            null,
            null,
            "Current ETA unavailable",
            "Overall ETA unavailable",
            BacklogEtaConfidence.Unavailable,
            action,
            target,
            label,
            string.IsNullOrWhiteSpace(recovery.Reason) ? detail : recovery.Reason,
            recovery.CanRetry);
    }

    private static BacklogExperienceState BuildRefreshRequired(
        ProcessingQueueStatusSnapshot snapshot,
        PersistedProcessingBacklogState? persistedBacklog)
    {
        var count = snapshot.TotalRemainingCount > 0
            ? snapshot.TotalRemainingCount
            : persistedBacklog?.TotalRemainingCount ?? 0;
        return new BacklogExperienceState(
            BacklogExperienceKind.RefreshRequired,
            IsVisible: true,
            "STATUS NEEDS REFRESH",
            "Live queue status is stale. Refresh status before treating work as idle, complete, or delayed.",
            count,
            null,
            null,
            null,
            "Current ETA unavailable because status is stale",
            "Overall ETA unavailable because status is stale",
            BacklogEtaConfidence.StatusStale,
            BacklogRecoveryAction.RefreshStatus,
            MeetingRecommendationActionTarget.CheckAgain,
            "Refresh status",
            null,
            RetryEligible: false);
    }

    private static bool HasLiveQueueSignal(ProcessingQueueStatusSnapshot snapshot) =>
        snapshot.TotalRemainingCount > 0 ||
        snapshot.RunState is ProcessingQueueRunState.Processing or ProcessingQueueRunState.Paused or ProcessingQueueRunState.Queued;

    private static bool IsSnapshotStale(DateTimeOffset observedAtUtc, DateTimeOffset nowUtc) =>
        observedAtUtc == default || nowUtc - observedAtUtc > MaximumFreshStatusAge;

    private static string BuildLiveDetail(
        string? currentTitle,
        string stageText,
        TimeSpan? elapsed,
        string currentEta)
    {
        if (string.IsNullOrWhiteSpace(currentTitle))
        {
            return $"Current: waiting to start processing. {currentEta}";
        }

        var elapsedText = elapsed is { } value
            ? $" {FormatElapsed(value)} elapsed."
            : string.Empty;
        return $"Current: {currentTitle}. {stageText}.{elapsedText} {currentEta}";
    }

    private static string BuildStageText(string? stageName, ProcessingQueueRunState runState) =>
        string.IsNullOrWhiteSpace(stageName)
            ? runState == ProcessingQueueRunState.Queued ? "Waiting to start" : "Preparing meeting work"
            : NormalizeStage(stageName) switch
            {
                "transcript" => "Creating transcript",
                "speaker labels" => "Adding speaker labels",
                "summary" => "Creating summary",
                "transcript publication" => "Publishing transcript",
                "cleanup" => "Applying meeting cleanup",
                _ => "Processing meeting work",
            };

    private static string NormalizeStage(string? stageName) => stageName?.Trim().ToLowerInvariant() switch
    {
        "transcription" or "transcript" => "transcript",
        "diarization" or "speaker-labeling" or "speaker labels" => "speaker labels",
        "summarization" or "summary" => "summary",
        "publish" or "publication" => "transcript publication",
        "cleanup" => "cleanup",
        _ => "unknown",
    };

    private static string FormatMeasuredEta(TimeSpan? estimate) => estimate is { } value && value > TimeSpan.Zero
        ? $"ETA ~{FormatCompactDuration(value)}"
        : "ETA unavailable";

    private static TimeSpan ClampElapsed(TimeSpan elapsed) => elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

    private static string FormatCompactDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{Math.Max(1, (int)Math.Floor(duration.TotalMinutes))}m";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}s";
    }

    private static string FormatElapsed(TimeSpan duration) =>
        duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
