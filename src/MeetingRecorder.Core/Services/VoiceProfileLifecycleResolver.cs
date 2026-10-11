namespace MeetingRecorder.Core.Services;
public enum VoiceProfileLifecycleAction { Disable, Enable, Delete, DeleteAll }
public enum VoiceProfileLifecycleDisposition { Ready, NoOp, Blocked }
public sealed record VoiceProfileLifecycleInput(VoiceProfileLifecycleAction Action, int SelectedCount, int ActiveCount, bool StoreAvailable, bool HasActiveLearningOrMatching, bool SelectedProfilesValid);
public sealed record VoiceProfileLifecycleDecision(VoiceProfileLifecycleDisposition Disposition, string Explanation, bool RequiresConfirmation, bool ChangesHistoricAttribution);
public static class VoiceProfileLifecycleResolver
{
    public static VoiceProfileLifecycleDecision Resolve(VoiceProfileLifecycleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.StoreAvailable) return new(VoiceProfileLifecycleDisposition.Blocked, "Local voice memory is unavailable. Meeting display names remain unchanged.", false, false);
        if (input.HasActiveLearningOrMatching) return new(VoiceProfileLifecycleDisposition.Blocked, "Wait for local voice matching to finish before changing voice profiles.", false, false);
        if (input.Action != VoiceProfileLifecycleAction.DeleteAll && (input.SelectedCount == 0 || !input.SelectedProfilesValid)) return new(VoiceProfileLifecycleDisposition.NoOp, "Select a valid local voice profile first.", false, false);
        if (input.Action == VoiceProfileLifecycleAction.DeleteAll && input.ActiveCount == 0) return new(VoiceProfileLifecycleDisposition.NoOp, "There are no local voice profiles to delete.", false, false);
        return input.Action switch
        {
            VoiceProfileLifecycleAction.Disable => new(VoiceProfileLifecycleDisposition.Ready, "Disable stops this local profile from future matching and learning. Existing meeting names stay unchanged.", false, false),
            VoiceProfileLifecycleAction.Enable => new(VoiceProfileLifecycleDisposition.Ready, "Enable allows this compatible local profile to inform future matching. Existing meeting names stay unchanged.", false, false),
            VoiceProfileLifecycleAction.Delete => new(VoiceProfileLifecycleDisposition.Ready, "Delete permanently removes this sensitive local voice-derived profile. Existing user-entered meeting names stay unchanged.", true, false),
            _ => new(VoiceProfileLifecycleDisposition.Ready, "Delete permanently removes all sensitive local voice-derived profiles. Existing meeting names stay unchanged.", true, false),
        };
    }
}
