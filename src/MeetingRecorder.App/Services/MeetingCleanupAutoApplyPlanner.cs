using MeetingRecorder.App;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class MeetingCleanupAutoApplyPlanner
{
    public const int MaxAutomaticFixesPerAppRun = 5;

    public static IReadOnlyList<MeetingCleanupRecommendation> GetEligibleRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupAutoApplyCacheService cacheService)
    {
        return MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(recommendations)
            .Where(recommendation => !cacheService.ShouldSkipAutomaticApply(recommendation.Fingerprint))
            .ToArray();
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> GetNextAutomaticBatch(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupAutoApplyCacheService cacheService,
        int automaticAttemptCount)
    {
        var availableSlots = Math.Max(0, MaxAutomaticFixesPerAppRun - automaticAttemptCount);

        return MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(recommendations)
            .Where(recommendation => !cacheService.ShouldSkipAutomaticApply(recommendation.Fingerprint))
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

}
