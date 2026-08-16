using MeetingRecorder.App;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

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

}
