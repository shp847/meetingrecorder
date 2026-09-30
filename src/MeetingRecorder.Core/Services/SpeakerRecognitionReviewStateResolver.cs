using MeetingRecorder.Core.Domain;
namespace MeetingRecorder.Core.Services;
public enum SpeakerRecognitionReviewState { Unavailable, NoSuggestion, Suggested, AutoApplied, Rejected, Stale, Busy }
public sealed record SpeakerRecognitionReviewStateInput(bool HasArtifact, bool IsStale, bool IsBusy, SpeakerNameSource Source, string? SuggestedDisplayName, bool IsRejected);
public sealed record SpeakerRecognitionReviewStateResult(SpeakerRecognitionReviewState State, string Explanation, bool CanUseSuggestion, bool CanRejectSuggestion);
public static class SpeakerRecognitionReviewStateResolver
{
    public static SpeakerRecognitionReviewStateResult Resolve(SpeakerRecognitionReviewStateInput input) =>
        !input.HasArtifact ? new(SpeakerRecognitionReviewState.Unavailable, "Speaker review is unavailable until meeting artifacts are published.", false, false) :
        input.IsStale ? new(SpeakerRecognitionReviewState.Stale, "Speaker data changed. Reload before reviewing this suggestion.", false, false) :
        input.IsBusy ? new(SpeakerRecognitionReviewState.Busy, "Speaker changes are being applied. Wait before editing this row.", false, false) :
        input.IsRejected ? new(SpeakerRecognitionReviewState.Rejected, "This local suggestion was rejected for this meeting speaker.", false, false) :
        input.Source == SpeakerNameSource.AutoAppliedVoiceProfile ? new(SpeakerRecognitionReviewState.AutoApplied, "This display name was auto-applied from a local profile and can be undone later.", false, false) :
        input.Source == SpeakerNameSource.SuggestedVoiceProfile && !string.IsNullOrWhiteSpace(input.SuggestedDisplayName) ? new(SpeakerRecognitionReviewState.Suggested, "This local suggestion needs confirmation before it changes the meeting display name.", true, true) :
        new(SpeakerRecognitionReviewState.NoSuggestion, "No local name suggestion is available for this speaker.", false, false);
}
