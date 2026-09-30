using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Temporary production-safety fence for automatic meeting maintenance. Manual
/// review routes remain available; only actions that can change or relocate an
/// existing meeting are held until the recovery roadmap supplies leases and
/// promotion receipts.
/// </summary>
public static class AutomaticMutationContainmentPolicy
{
    public static bool CanDispatch(MeetingCleanupAction action) => action is not
        (MeetingCleanupAction.Archive or
         MeetingCleanupAction.Merge or
         MeetingCleanupAction.RegenerateTranscript);

    public static string GetReason(MeetingCleanupAction action) => CanDispatch(action)
        ? "This automatic action is outside the containment hold."
        : "Automatic archive, merge, and transcript regeneration are paused while recovery evidence is established. Review and apply this action manually if appropriate.";
}
