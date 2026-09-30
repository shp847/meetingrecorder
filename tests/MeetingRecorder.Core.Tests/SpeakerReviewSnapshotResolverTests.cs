using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerReviewSnapshotResolverTests
{
    [Fact]
    public void Resolve_ExplainsEachSupportedSource_WithoutExposingVoiceData()
    {
        var snapshot = SpeakerReviewSnapshotResolver.Resolve(Input(
            speakers:
            [
                new SpeakerIdentity("speaker_00", "Speaker 1", false),
                new SpeakerIdentity("speaker_01", "Pranav", true, "voice-pranav", SpeakerNameSource.AutoAppliedVoiceProfile, .98d),
                new SpeakerIdentity("speaker_02", "Speaker 3", false, "voice-alex", SpeakerNameSource.SuggestedVoiceProfile, .81d, "Alex"),
                new SpeakerIdentity("speaker_03", "Mina", false, "voice-mina", SpeakerNameSource.AutoAppliedVoiceProfile, .91d),
            ],
            evidenceIds: new HashSet<string>(["speaker_00", "speaker_01"], StringComparer.Ordinal)));

        Assert.Equal(
            [SpeakerReviewNameSource.Generic, SpeakerReviewNameSource.UserEntered, SpeakerReviewNameSource.Suggested, SpeakerReviewNameSource.AutoApplied],
            snapshot.Rows.Select(row => row.NameSource));
        Assert.Equal("Speaker 3", snapshot.Rows[2].AnonymousLabel);
        Assert.Equal("Alex", snapshot.Rows[2].ProposedDisplayName);
        Assert.Contains(SpeakerReviewAction.UseSuggestion, snapshot.Rows[2].PermittedActions);
        Assert.Contains(SpeakerReviewAction.RejectSuggestion, snapshot.Rows[2].PermittedActions);
        Assert.Contains("takes precedence", snapshot.Rows[1].Explanation, StringComparison.Ordinal);
        Assert.Equal(SpeakerReviewEvidenceAvailability.Unavailable, snapshot.Rows[2].EvidenceAvailability);
        Assert.DoesNotContain("embedding", string.Join(' ', snapshot.Rows.Select(row => row.Explanation)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UserNameWinsOverConflictingProfileSource()
    {
        var row = Assert.Single(SpeakerReviewSnapshotResolver.Resolve(Input(
            speakers: [new SpeakerIdentity("speaker_00", "Pranav", true, "voice-pranav", SpeakerNameSource.SuggestedVoiceProfile, .75d, "Other name")])).Rows);

        Assert.Equal(SpeakerReviewNameSource.UserEntered, row.NameSource);
        Assert.Null(row.ProposedDisplayName);
        Assert.DoesNotContain(SpeakerReviewAction.UseSuggestion, row.PermittedActions);
    }

    [Fact]
    public void Resolve_StaleOrMissingRevision_BlocksMutationAndRequiresReload()
    {
        var stale = SpeakerReviewSnapshotResolver.Resolve(Input(isStale: true));
        var missingRevision = SpeakerReviewSnapshotResolver.Resolve(Input(revision: string.Empty));

        Assert.All(stale.Rows, row => Assert.Equal([SpeakerReviewAction.Reload], row.PermittedActions));
        Assert.All(missingRevision.Rows, row => Assert.Equal(SpeakerReviewFreshness.Stale, row.Freshness));
    }

    [Fact]
    public void Resolve_LearningReadinessAndRepairWarning_AreExplicit()
    {
        var disabled = Assert.Single(SpeakerReviewSnapshotResolver.Resolve(Input(learningMode: SpeakerNameLearningMode.Disabled, repairWarning: true)).Rows);
        var unavailable = Assert.Single(SpeakerReviewSnapshotResolver.Resolve(Input(profileStoreAvailable: false)).Rows);
        var noEvidence = Assert.Single(SpeakerReviewSnapshotResolver.Resolve(Input(evidenceIds: new HashSet<string>(StringComparer.Ordinal))).Rows);

        Assert.Equal(SpeakerReviewLearningEligibility.Disabled, disabled.LearningEligibility);
        Assert.True(disabled.HasRepairWarning);
        Assert.Contains(SpeakerReviewAction.RepairSpeakerLabels, disabled.PermittedActions);
        Assert.Equal(SpeakerReviewLearningEligibility.ProfilesUnavailable, unavailable.LearningEligibility);
        Assert.Equal(SpeakerReviewLearningEligibility.InsufficientEvidence, noEvidence.LearningEligibility);
    }

    [Fact]
    public void Resolve_UnknownFutureSource_IsSafeReviewNeededState()
    {
        var row = Assert.Single(SpeakerReviewSnapshotResolver.Resolve(Input(
            speakers: [new SpeakerIdentity("speaker_00", "Unknown", false, NameSource: (SpeakerNameSource)999)])).Rows);

        Assert.Equal(SpeakerReviewNameSource.ReviewRequired, row.NameSource);
        Assert.Contains("unknown source", row.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SpeakerReviewConfidence.Unavailable, row.Confidence);
    }

    private static SpeakerReviewSnapshotInput Input(
        IReadOnlyList<SpeakerIdentity>? speakers = null,
        IReadOnlySet<string>? evidenceIds = null,
        SpeakerNameLearningMode learningMode = SpeakerNameLearningMode.LocalAutoLearn,
        bool profileStoreAvailable = true,
        bool isStale = false,
        bool repairWarning = false,
        string revision = "revision-1") =>
        new(
            "meeting-1",
            revision,
            speakers ?? [new SpeakerIdentity("speaker_00", "Speaker 1", false)],
            evidenceIds ?? new HashSet<string>(["speaker_00"], StringComparer.Ordinal),
            learningMode,
            profileStoreAvailable,
            isStale,
            repairWarning);
}
