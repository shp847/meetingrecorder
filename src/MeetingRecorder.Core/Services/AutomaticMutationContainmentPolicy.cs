using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Admits only high-confidence recommendations to the scheduler. The caller
/// must create and verify a recovery snapshot before dispatching protected work.
/// </summary>
public static class AutomaticMutationContainmentPolicy
{
    public static bool CanDispatch(MeetingCleanupAction action) => action is not
        (MeetingCleanupAction.Archive or
         MeetingCleanupAction.Merge or
         MeetingCleanupAction.RegenerateTranscript);

    public static bool CanDispatch(MeetingCleanupRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        return recommendation.CanApplyAutomatically &&
               recommendation.Confidence == MeetingCleanupConfidence.High &&
               recommendation.Action is MeetingCleanupAction.Archive or MeetingCleanupAction.Merge or
                   MeetingCleanupAction.RegenerateTranscript or MeetingCleanupAction.GenerateSpeakerLabels or
                   MeetingCleanupAction.RepairSpeakerLabels;
    }

    public static string GetReason(MeetingCleanupAction action) => CanDispatch(action)
        ? "This automatic action is outside the containment hold."
        : "This change requires a high-confidence recommendation and a verified recovery copy. Review and apply it manually if appropriate.";
}
