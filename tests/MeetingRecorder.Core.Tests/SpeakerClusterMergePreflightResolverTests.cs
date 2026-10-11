using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerClusterMergePreflightResolverTests
{
    [Fact]
    public void Resolve_Uses_Generic_Canonical_And_Explains_No_Learning()
    {
        var result = SpeakerClusterMergePreflightResolver.Resolve(Input(
        [
            Candidate("speaker-a", "Speaker 1", SpeakerNameSource.None, turns: 4),
            Candidate("speaker-b", "Alex", SpeakerNameSource.SuggestedVoiceProfile, confidence: 0.72d, turns: 3),
        ]));

        Assert.True(result.CanMerge);
        Assert.Equal("speaker-a", result.RecommendedCanonicalSpeakerId);
        Assert.Equal(7, result.AffectedTurnCount);
        Assert.Contains("does not teach local voice profiles", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Blocks_Conflicting_User_Or_HighConfidence_Profile_Names()
    {
        var userConflict = SpeakerClusterMergePreflightResolver.Resolve(Input(
        [
            Candidate("speaker-a", "Alex", SpeakerNameSource.UserEdited),
            Candidate("speaker-b", "Morgan", SpeakerNameSource.UserEdited),
        ]));
        var profileConflict = SpeakerClusterMergePreflightResolver.Resolve(Input(
        [
            Candidate("speaker-a", "Alex", SpeakerNameSource.AutoAppliedVoiceProfile, 0.95d),
            Candidate("speaker-b", "Morgan", SpeakerNameSource.AutoAppliedVoiceProfile, 0.94d),
        ]));

        Assert.Equal(SpeakerClusterMergePreflightDisposition.ResolveNameConflict, userConflict.Disposition);
        Assert.Equal(SpeakerClusterMergePreflightDisposition.ResolveNameConflict, profileConflict.Disposition);
    }

    [Fact]
    public void Resolve_Routes_Fragmented_Or_Unreviewable_Selections_Away_From_Merge()
    {
        var repair = SpeakerClusterMergePreflightResolver.Resolve(Input([Candidate("speaker-a"), Candidate("speaker-b")], repair: true));
        var missingEvidence = SpeakerClusterMergePreflightResolver.Resolve(Input([Candidate("speaker-a"), Candidate("speaker-b", evidence: false)]));

        Assert.Equal(SpeakerClusterMergePreflightDisposition.RepairRecommended, repair.Disposition);
        Assert.Equal(SpeakerClusterMergePreflightDisposition.Blocked, missingEvidence.Disposition);
    }

    private static SpeakerClusterMergePreflightInput Input(IReadOnlyList<SpeakerClusterMergeCandidate> speakers, bool repair = false) =>
        new("revision-1", speakers, IsBusy: false, IsRepairRecommended: repair);

    private static SpeakerClusterMergeCandidate Candidate(
        string id,
        string name = "Speaker",
        SpeakerNameSource source = SpeakerNameSource.None,
        double? confidence = null,
        bool evidence = true,
        int turns = 2) =>
        new(id, name, source, confidence, evidence, turns);
}
