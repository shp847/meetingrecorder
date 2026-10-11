using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerCorrectionReceiptResolverTests
{
    [Fact]
    public void Confirm_UsesCanonicalSpeakerIdentity_NotDisplayText_AndLearnsOnlyWithEvidence()
    {
        var decision = SpeakerCorrectionReceiptResolver.Resolve(Request("Alex") with { CanonicalSpeakerId = "speaker_00_merged" }, Current() with { CanonicalSpeakerId = "speaker_00_merged", HasEligibleEvidence = true });

        Assert.Equal(SpeakerCorrectionDisposition.Accepted, decision.Disposition);
        Assert.Equal("speaker_00_merged", decision.EffectiveSpeakerId);
        Assert.True(decision.CanWriteArtifact);
        Assert.True(decision.CanLearn);
    }

    [Fact]
    public void StaleFingerprintOrRevision_RequiresReload()
    {
        var fingerprint = SpeakerCorrectionReceiptResolver.Resolve(Request("Alex") with { PriorFingerprint = "old" }, Current());
        var revision = SpeakerCorrectionReceiptResolver.Resolve(Request("Alex") with { ArtifactRevision = "revision-0" }, Current());

        Assert.Equal(SpeakerCorrectionDisposition.Stale, fingerprint.Disposition);
        Assert.Equal(SpeakerCorrectionDisposition.Stale, revision.Disposition);
        Assert.False(fingerprint.CanWriteArtifact);
    }

    [Fact]
    public void DuplicateAndNoOpRequests_AreNotWritten()
    {
        var duplicate = SpeakerCorrectionReceiptResolver.Resolve(Request("Alex"), Current(), new HashSet<string>(["operation-1"], StringComparer.Ordinal));
        var noOp = SpeakerCorrectionReceiptResolver.Resolve(Request("Speaker 1"), Current());

        Assert.Equal(SpeakerCorrectionDisposition.Duplicate, duplicate.Disposition);
        Assert.Equal(SpeakerCorrectionDisposition.NoOp, noOp.Disposition);
        Assert.False(noOp.CanWriteArtifact);
    }

    [Fact]
    public void RejectionAndUndo_OnlyAllowRecognitionDerivedAttribution()
    {
        var reject = SpeakerCorrectionReceiptResolver.Resolve(Request(action: SpeakerCorrectionAction.RejectSuggestion), Current() with { NameSource = SpeakerNameSource.SuggestedVoiceProfile, ProfileId = "voice-1" });
        var undo = SpeakerCorrectionReceiptResolver.Resolve(Request(action: SpeakerCorrectionAction.UndoRecognition), Current() with { NameSource = SpeakerNameSource.UserEdited });

        Assert.Equal(SpeakerCorrectionDisposition.Accepted, reject.Disposition);
        Assert.Equal(SpeakerCorrectionDisposition.NoOp, undo.Disposition);
    }

    private static SpeakerCorrectionRequest Request(string? updatedName = null, SpeakerCorrectionAction action = SpeakerCorrectionAction.ConfirmName) =>
        new("operation-1", "meeting-1", "revision-1", "speaker_00", null, "fingerprint-1", action, updatedName, LearningEnabled: true);

    private static SpeakerCorrectionCurrentState Current() =>
        new("revision-1", "speaker_00", null, "fingerprint-1", "Speaker 1", SpeakerNameSource.None, null, HasEligibleEvidence: false);
}
