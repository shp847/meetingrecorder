using MeetingRecorder.Core.Domain; using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class SpeakerRecognitionUndoPreflightTests
{
    [Fact] public void CurrentProfileAttribution_IsUndoableOnlyForThatSpeaker() { var r=SpeakerRecognitionUndoPreflight.Resolve(new("r1","r1",SpeakerNameSource.AutoAppliedVoiceProfile,false,"voice-1")); Assert.Equal(SpeakerRecognitionUndoStatus.Ready,r.Status); Assert.True(r.CanUndo); }
    [Fact] public void StaleAndUserEnteredNames_AreProtected() { Assert.Equal(SpeakerRecognitionUndoStatus.Stale,SpeakerRecognitionUndoPreflight.Resolve(new("old","new",SpeakerNameSource.AutoAppliedVoiceProfile,false,"voice")).Status); Assert.Equal(SpeakerRecognitionUndoStatus.UserNameProtected,SpeakerRecognitionUndoPreflight.Resolve(new("r","r",SpeakerNameSource.UserEdited,true,null)).Status); }
}
