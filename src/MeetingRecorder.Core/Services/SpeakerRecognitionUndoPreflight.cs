using MeetingRecorder.Core.Domain;
namespace MeetingRecorder.Core.Services;
public enum SpeakerRecognitionUndoStatus { Ready, Stale, NoUndoableAttribution, UserNameProtected }
public sealed record SpeakerRecognitionUndoInput(string ExpectedArtifactRevision, string CurrentArtifactRevision, SpeakerNameSource NameSource, bool IsUserEdited, string? ProfileId);
public sealed record SpeakerRecognitionUndoResult(SpeakerRecognitionUndoStatus Status, string Explanation, bool CanUndo);
public static class SpeakerRecognitionUndoPreflight
{
    public static SpeakerRecognitionUndoResult Resolve(SpeakerRecognitionUndoInput input) =>
        !string.Equals(input.ExpectedArtifactRevision, input.CurrentArtifactRevision, StringComparison.Ordinal) ? new(SpeakerRecognitionUndoStatus.Stale, "Meeting speaker data changed. Reload before undoing recognition.", false) :
        input.IsUserEdited || input.NameSource == SpeakerNameSource.UserEdited ? new(SpeakerRecognitionUndoStatus.UserNameProtected, "This user-entered meeting display name is protected from recognition undo.", false) :
        input.NameSource is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile && !string.IsNullOrWhiteSpace(input.ProfileId) ? new(SpeakerRecognitionUndoStatus.Ready, "Undo removes this profile-derived attribution for this meeting speaker only.", true) :
        new(SpeakerRecognitionUndoStatus.NoUndoableAttribution, "There is no profile-derived attribution to undo for this speaker.", false);
}
