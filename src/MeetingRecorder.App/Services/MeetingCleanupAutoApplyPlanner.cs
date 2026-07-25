using MeetingRecorder.App;
using MeetingRecorder.Core.Domain;
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

    public static bool ShouldSuppressSuccessfulAutomaticApply(MeetingCleanupAction action)
    {
        return action is MeetingCleanupAction.RegenerateTranscript or
            MeetingCleanupAction.GenerateSpeakerLabels or
            MeetingCleanupAction.RepairSpeakerLabels;
    }

    public static bool ShouldSeedSuppressionFromPriorAttempt(
        MeetingCleanupAction action,
        MeetingSessionManifest? manifest)
    {
        if (manifest is null || manifest.State is not (SessionState.Published or SessionState.Failed))
        {
            return false;
        }

        return action switch
        {
            MeetingCleanupAction.RegenerateTranscript =>
                manifest.ProcessingOverrides?.ForceTranscription == true,
            MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels =>
                manifest.ProcessingOverrides?.SkipSpeakerLabeling == true &&
                manifest.DiarizationStatus.State is StageExecutionState.Skipped or StageExecutionState.Failed,
            _ => false,
        };
    }
}
