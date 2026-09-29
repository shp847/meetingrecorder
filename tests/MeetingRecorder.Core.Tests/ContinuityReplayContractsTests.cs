using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ContinuityReplayContractsTests
{
    [Fact]
    public void Public_Synthetic_Corpus_Is_Opaque_Valid_And_Replays_To_Its_Baseline()
    {
        var corpus = ContinuityReplayCorpus.CreatePublicSyntheticFixtures();
        var contract = new ContinuityReplayDecisionContract();

        Assert.Equal(5, corpus.Count);
        Assert.All(corpus, fixture => fixture.ValidateForPublicReplay());
        Assert.Equal(corpus.Count, corpus.Select(contract.Evaluate).Count(result => result.Decision is not ContinuityReplayDecision.SameMeeting || !result.SupportsAutomaticMerge));

        var metrics = ContinuityReplayMetrics.Calculate(corpus, contract);
        Assert.Equal(5, metrics.ExactDecisionCount);
        Assert.Equal(0, metrics.FalseMergeCount);
        Assert.Equal(0, metrics.FalseSplitCount);
        Assert.Equal(1, metrics.UnknownGraceCount);
        Assert.Equal(1, metrics.ManualReviewCount);
    }

    [Fact]
    public void Contract_Prioritizes_Manual_Stop_And_Contradictory_Strong_Identity()
    {
        var baseline = ContinuityReplayCorpus.CreatePublicSyntheticFixtures().First();
        var contract = new ContinuityReplayDecisionContract();

        var manualStop = contract.Evaluate((baseline with { WasManuallyStopped = true }).WithComputedIntegrityHash());
        var conflict = contract.Evaluate((baseline with { HasContradictoryStableIdentity = true }).WithComputedIntegrityHash());

        Assert.Equal(ContinuityReplayDecision.ManualReview, manualStop.Decision);
        Assert.Equal("manual-stop-authority", manualStop.ReasonCode);
        Assert.Equal(ContinuityReplayDecision.DifferentMeeting, conflict.Decision);
        Assert.Equal("contradictory-strong-identity", conflict.ReasonCode);
    }

    [Fact]
    public void Contract_Never_Uses_Weak_Evidence_For_Automatic_Merge_And_Bounds_Grace()
    {
        var baseline = ContinuityReplayCorpus.CreatePublicSyntheticFixtures().First();
        var contract = new ContinuityReplayDecisionContract();
        var weak = (baseline with
        {
            EvidenceTier = ContinuityEvidenceTier.Weak,
            HasStableIdentity = false,
            HasRecentCaptureActivity = true,
            ElapsedSinceLastPositiveSignal = ContinuityReplayDecisionContract.MaximumGrace + TimeSpan.FromSeconds(1),
            ExpectedDecision = ContinuityReplayDecision.ManualReview,
            ExpectedReasonCode = "insufficient-identity-evidence",
        }).WithComputedIntegrityHash();

        var result = contract.Evaluate(weak);

        Assert.Equal(ContinuityReplayDecision.ManualReview, result.Decision);
        Assert.False(result.EntersGrace);
        Assert.False(result.SupportsAutomaticMerge);
    }

    [Fact]
    public void Validation_Rejects_Restricted_Or_Tampered_Fixture()
    {
        var fixture = ContinuityReplayCorpus.CreatePublicSyntheticFixtures().First();

        Assert.Throws<InvalidOperationException>(() => (fixture with { Consent = ContinuityFixtureConsent.RestrictedLocal }).ValidateForPublicReplay());
        Assert.Throws<InvalidOperationException>(() => (fixture with { IntegrityHash = "tampered" }).ValidateForPublicReplay());
    }

    [Fact]
    public void Validation_Rejects_NonOpaque_Identifiers_And_Unknown_Platform()
    {
        var fixture = ContinuityReplayCorpus.CreatePublicSyntheticFixtures().First();

        Assert.Throws<InvalidOperationException>(() => (fixture with { ScenarioId = "Private Scenario" }).WithComputedIntegrityHash().ValidateForPublicReplay());
        Assert.Throws<InvalidOperationException>(() => (fixture with { Platform = MeetingPlatform.Unknown }).WithComputedIntegrityHash().ValidateForPublicReplay());
    }
}
