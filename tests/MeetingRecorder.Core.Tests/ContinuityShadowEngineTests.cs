using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ContinuityShadowEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_Is_Pure_And_Classifies_Protected_Divergence()
    {
        var left = Snapshot("title-a", MeetingIdentityEvidenceTier.Strong);
        var right = Snapshot("title-b", MeetingIdentityEvidenceTier.Strong);
        var input = Input(left, right, MeetingIdentityVerdict.DifferentMeeting, "legacy-different", ContinuityShadowScenarioLabel.ProtectedNegative);
        var engine = new ContinuityShadowEngine();

        var first = engine.Evaluate(input, Now);
        var second = engine.Evaluate(input, Now);

        Assert.Equal(first with { DurationMilliseconds = 0 }, second with { DurationMilliseconds = 0 });
        Assert.Equal(MeetingIdentityVerdict.DifferentMeeting, first.ShadowVerdict);
        Assert.Equal(ContinuityShadowDivergence.ReasonDrift, first.Divergence);
        Assert.DoesNotContain("title-a", first.ShadowReasonCode, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MeetingIdentityVerdict.SameMeeting, MeetingIdentityVerdict.Unknown, ContinuityShadowDivergence.ConservativeUnknown)]
    [InlineData(MeetingIdentityVerdict.SameMeeting, MeetingIdentityVerdict.DifferentMeeting, ContinuityShadowDivergence.ShadowWouldCreateSplit)]
    [InlineData(MeetingIdentityVerdict.DifferentMeeting, MeetingIdentityVerdict.SameMeeting, ContinuityShadowDivergence.PotentialFalseMerge)]
    public void Taxonomy_Is_Conservative_And_Deterministic(
        MeetingIdentityVerdict legacy,
        MeetingIdentityVerdict shadow,
        ContinuityShadowDivergence expected)
    {
        Assert.Equal(expected, ContinuityShadowEngine.Classify(legacy, "legacy-result", shadow, "shadow-result"));
    }

    [Fact]
    public void Meter_Bounds_Overload_Without_Changing_The_Receipt()
    {
        var meter = new ContinuityShadowMeter(capacity: 1);
        var receipt = new ContinuityShadowEngine().Evaluate(Input(Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), MeetingIdentityVerdict.SameMeeting, "compatible-strong-identity", ContinuityShadowScenarioLabel.ProtectedIncident, MeetingIdentityVerdict.SameMeeting), Now);

        Assert.True(meter.TryRecord(receipt, TimeSpan.FromSeconds(1)));
        Assert.True(meter.TryRecord(receipt, TimeSpan.FromSeconds(1)));
        Assert.False(meter.TryRecord(receipt with { DurationMilliseconds = 2_000 }, TimeSpan.FromMilliseconds(1)));

        var report = meter.CreateReport();
        Assert.Equal(1, report.TotalCount);
        Assert.Equal(2, report.DroppedCount);
    }

    [Fact]
    public void Report_And_Gate_Require_Labeled_Protected_And_Reviewed_Truth()
    {
        var engine = new ContinuityShadowEngine();
        var incident = engine.Evaluate(Input(Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), MeetingIdentityVerdict.SameMeeting, "compatible-strong-identity", ContinuityShadowScenarioLabel.ProtectedIncident, MeetingIdentityVerdict.SameMeeting), Now);
        var potentialFalseMerge = engine.Evaluate(Input(Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), Snapshot("title-a", MeetingIdentityEvidenceTier.Strong), MeetingIdentityVerdict.DifferentMeeting, "legacy-different", ContinuityShadowScenarioLabel.ProtectedNegative), Now);
        var report = ContinuityShadowReport.Create([incident, potentialFalseMerge]);

        Assert.Single(report.Rows);
        Assert.Equal(2, report.Rows[0].LabeledCount);
        Assert.Equal(1, report.Rows[0].PotentialFalseMergeCount);

        var blocked = ContinuityShadowCutoverGate.Evaluate([incident, potentialFalseMerge], new Dictionary<string, ContinuityShadowReviewDisposition>(), TimeSpan.FromSeconds(1));
        Assert.False(blocked.CanCutOver);
        Assert.Contains("unreviewed-potential-false-merge", blocked.BlockingReasonCodes);

        var approved = ContinuityShadowCutoverGate.Evaluate(
            [incident, potentialFalseMerge],
            new Dictionary<string, ContinuityShadowReviewDisposition> { [potentialFalseMerge.OpaqueCorrelationId] = ContinuityShadowReviewDisposition.IntentionalRiskReduction },
            TimeSpan.FromSeconds(1));
        Assert.True(approved.CanCutOver);
    }

    [Fact]
    public void Unavailable_Input_Is_Recorded_As_Metadata_Only()
    {
        var receipt = ContinuityShadowEngine.Unavailable(
            Input(null, null, MeetingIdentityVerdict.Unknown, "legacy-unknown", ContinuityShadowScenarioLabel.Unlabeled),
            4,
            "shadow-input-unavailable");

        Assert.Null(receipt.ShadowVerdict);
        Assert.Equal(ContinuityShadowDivergence.Unavailable, receipt.Divergence);
        Assert.Equal("shadow-input-unavailable", receipt.ShadowReasonCode);
    }

    private static ContinuityShadowInput Input(
        MeetingIdentitySnapshot? left,
        MeetingIdentitySnapshot? right,
        MeetingIdentityVerdict legacy,
        string legacyReason,
        ContinuityShadowScenarioLabel label,
        MeetingIdentityVerdict? expected = null) =>
        new("scenario-a", 1, ContinuityDecisionBoundary.Continuation, left, right, legacy, legacyReason, label, expected);

    private static MeetingIdentitySnapshot Snapshot(string titleToken, MeetingIdentityEvidenceTier tier) =>
        new(
            MeetingIdentitySnapshot.CurrentSchemaVersion,
            MeetingPlatform.Teams,
            tier,
            null,
            titleToken.PadRight(64, '0'),
            true,
            true,
            false,
            Now,
            Now.AddHours(1),
            [MeetingIdentityEvidenceSource.ManifestTitle],
            new string('a', 64),
            new string('b', 64));
}
