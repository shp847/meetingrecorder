using MeetingRecorder.App;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed record CleanupSchedulerStatus(
    int SafeFixCount,
    int EligibleNowCount,
    int QueuedCount,
    int ProcessingCount,
    int DisabledSpeakerLabelCount,
    int BlockedSummaryCount,
    int DisabledCleanupCount,
    int ManualReviewCount,
    int ContainmentHeldCount = 0,
    int RetryableFailedCount = 0)
{
    public string PrimaryBlocker => DisabledSpeakerLabelCount > 0
        ? $"{DisabledSpeakerLabelCount} speaker-label fix(es) are disabled by the work plan."
        : BlockedSummaryCount > 0
            ? $"{BlockedSummaryCount} summary fix(es) need a current provider health check in Settings."
            : DisabledCleanupCount > 0
                ? $"{DisabledCleanupCount} safe cleanup fix(es) are disabled by the work plan."
                : string.Empty;
}

internal static class MeetingCleanupAutoApplyPlanner
{
    public const int MaxAutomaticFixesPerBatch = 5;
    public static readonly TimeSpan AutomaticBatchCooldown = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<MeetingCleanupRecommendation> GetEligibleRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupAutoApplyCacheService cacheService,
        MeetingCleanupWorkLedgerService? ledger = null)
    {
        return MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(recommendations)
            .Where(recommendation => ledger is null
                ? !cacheService.ShouldSkipAutomaticApply(recommendation.Fingerprint)
                : ledger.IsEligibleForAutomaticApply(recommendation.Fingerprint))
            .ToArray();
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> GetEligibleScheduledRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupWorkLedgerService ledger,
        Func<MeetingCleanupRecommendation, bool> isScheduled)
    {
        ArgumentNullException.ThrowIfNull(isScheduled);
        return recommendations
            .Where(isScheduled)
            .Where(recommendation => ledger.IsEligibleForAutomaticApply(recommendation.Fingerprint))
            .ToArray();
    }

    public static CleanupSchedulerStatus BuildSchedulerStatus(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupWorkLedgerService ledger,
        Func<MeetingCleanupRecommendation, bool> isScheduled,
        Func<MeetingCleanupRecommendation, bool> isSummaryBlocked,
        IReadOnlySet<string>? activeManifestPaths = null)
    {
        ArgumentNullException.ThrowIfNull(isScheduled);
        ArgumentNullException.ThrowIfNull(isSummaryBlocked);

        var safeFixes = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(recommendations);
        var pendingSafeFixes = safeFixes
            .Where(recommendation => ledger.IsEligibleForAutomaticApply(recommendation.Fingerprint))
            .ToArray();
        var ledgerEntries = ledger.GetEntries()
            .ToArray();
        var activePaths = activeManifestPaths ?? new HashSet<string>(StringComparer.Ordinal);
        var processingEntries = ledgerEntries
            .Where(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing)
            .ToArray();
        var activeEntries = processingEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ManifestPath) && activePaths.Contains(entry.ManifestPath))
            .ToArray();
        var containmentHeld = ledgerEntries.Count(entry =>
            entry.State == CleanupWorkState.ManualReview &&
            (entry.Action is MeetingCleanupAction.Archive or MeetingCleanupAction.Merge or MeetingCleanupAction.RegenerateTranscript));
        var retryableFailed = ledgerEntries.Count(entry =>
            entry.State == CleanupWorkState.Failed &&
            (entry.Action is MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels or MeetingCleanupAction.GenerateSummary));

        return new CleanupSchedulerStatus(
            safeFixes.Count,
            GetEligibleScheduledRecommendations(recommendations, ledger, isScheduled).Count,
            processingEntries.Count(entry => entry.State == CleanupWorkState.Queued && !activeEntries.Contains(entry)),
            processingEntries.Count(entry => entry.State == CleanupWorkState.Processing || activeEntries.Contains(entry)),
            pendingSafeFixes.Count(recommendation => IsSpeakerLabelAction(recommendation.Action) && !isScheduled(recommendation)),
            recommendations.Count(recommendation =>
                recommendation.Action == MeetingCleanupAction.GenerateSummary &&
                ledger.IsEligibleForAutomaticApply(recommendation.Fingerprint) &&
                isSummaryBlocked(recommendation)),
            pendingSafeFixes.Count(recommendation =>
                !IsSpeakerLabelAction(recommendation.Action) && !isScheduled(recommendation)),
            ledgerEntries.Count(entry => entry.State is CleanupWorkState.ManualReview or CleanupWorkState.Failed),
            containmentHeld,
            retryableFailed);
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> GetNextAutomaticBatch(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupAutoApplyCacheService cacheService,
        int automaticAttemptCount,
        MeetingCleanupWorkLedgerService? ledger = null,
        int maximumBatchSize = MaxAutomaticFixesPerBatch)
    {
        var availableSlots = Math.Max(0, maximumBatchSize - automaticAttemptCount);

        return MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(recommendations)
            .Where(recommendation => ledger is null
                ? !cacheService.ShouldSkipAutomaticApply(recommendation.Fingerprint)
                : ledger.IsEligibleForAutomaticApply(recommendation.Fingerprint))
            .Take(availableSlots)
            .ToArray();
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> GetNextScheduledBatch(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupWorkLedgerService ledger,
        Func<MeetingCleanupRecommendation, bool> isScheduled,
        int maximumBatchSize)
    {
        ArgumentNullException.ThrowIfNull(isScheduled);
        return GetEligibleScheduledRecommendations(recommendations, ledger, isScheduled)
            .Take(Math.Max(0, maximumBatchSize))
            .ToArray();
    }

    public static bool ShouldStartAutomaticApply(
        MeetingRefreshMode refreshMode,
        bool isRefreshCurrent,
        bool isShutdownRequested,
        bool isMeetingActionInProgress,
        bool isAutoApplyInProgress,
        int eligibleRecommendationCount)
    {
        return refreshMode == MeetingRefreshMode.Full &&
               isRefreshCurrent &&
               !isShutdownRequested &&
               !isMeetingActionInProgress &&
               !isAutoApplyInProgress &&
               eligibleRecommendationCount > 0;
    }

    public static bool ShouldSuppressSuccessfulAutomaticApply(MeetingCleanupAction action)
    {
        return action is MeetingCleanupAction.RegenerateTranscript or
            MeetingCleanupAction.GenerateSpeakerLabels or
            MeetingCleanupAction.RepairSpeakerLabels;
    }

    public static bool IsAutomaticBatchRefillDue(
        int automaticAttemptCount,
        DateTimeOffset? lastBatchStartedUtc,
        DateTimeOffset nowUtc,
        bool isProcessingQueueIdle)
    {
        return automaticAttemptCount > 0 &&
               lastBatchStartedUtc.HasValue &&
               nowUtc - lastBatchStartedUtc.Value >= AutomaticBatchCooldown &&
               isProcessingQueueIdle;
    }

    private static bool IsSpeakerLabelAction(MeetingCleanupAction action)
    {
        return action is MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels;
    }

}
