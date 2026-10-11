using MeetingRecorder.Core.Domain; using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class SpeakerRecognitionReviewStateResolverTests
{
    [Fact] public void Suggested_EnablesDraftActionsOnly() { var r = SpeakerRecognitionReviewStateResolver.Resolve(new(true,false,false,SpeakerNameSource.SuggestedVoiceProfile,"Alex",false)); Assert.Equal(SpeakerRecognitionReviewState.Suggested,r.State); Assert.True(r.CanUseSuggestion); Assert.True(r.CanRejectSuggestion); }
    [Fact] public void UnavailableStaleAndBusy_BlockReviewActions() { foreach (var i in new[] { new SpeakerRecognitionReviewStateInput(false,false,false,SpeakerNameSource.None,null,false), new(true,true,false,SpeakerNameSource.None,null,false), new(true,false,true,SpeakerNameSource.None,null,false) }) { var r=SpeakerRecognitionReviewStateResolver.Resolve(i); Assert.False(r.CanUseSuggestion); Assert.False(r.CanRejectSuggestion); } }
}
