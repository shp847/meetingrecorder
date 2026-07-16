using MeetingRecorder.App;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class MeetingCleanupAutoApplyPlanner
{
    public static IReadOnlyList<MeetingCleanupRecommendation> GetEligibleRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingCleanupAutoApplyCacheService cacheService)
    {
        return MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(recommendations)
            .Where(recommendation => !cacheService.ShouldSkipAutomaticApply(recommendation.Fingerprint))
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
}
