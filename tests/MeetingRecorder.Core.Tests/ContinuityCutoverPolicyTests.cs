using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ContinuityCutoverPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Legacy_Mode_Preserves_Legacy_Verdict()
    {
        var decision = new ContinuityCutoverPolicy().Evaluate(Input(ContinuityCutoverMode.Legacy, MeetingIdentityVerdict.DifferentMeeting, null, null));

        Assert.Equal(ContinuityLifecycleAction.DifferentMeeting, decision.Action);
        Assert.False(decision.UsedMatcher);
    }

    [Fact]
    public void Matcher_Mode_Continues_Only_Compatible_Strong_Identity()
    {
        var identity = Snapshot("a");
        var decision = new ContinuityCutoverPolicy().Evaluate(Input(ContinuityCutoverMode.Matcher, MeetingIdentityVerdict.DifferentMeeting, identity, identity));

        Assert.Equal(ContinuityLifecycleAction.Continue, decision.Action);
        Assert.Equal(MeetingIdentityVerdict.SameMeeting, decision.Verdict);
        Assert.True(decision.UsedMatcher);
    }

    [Fact]
    public void Unknown_Evidence_Uses_One_Bounded_Idempotent_Grace_Receipt()
    {
        var policy = new ContinuityCutoverPolicy();
        var first = policy.Evaluate(Input(ContinuityCutoverMode.Matcher, MeetingIdentityVerdict.SameMeeting, Snapshot("a"), null));
        var second = policy.Evaluate(Input(ContinuityCutoverMode.Matcher, MeetingIdentityVerdict.SameMeeting, Snapshot("a"), null, first.GraceReceipt, Now.AddMinutes(1)));
        var expired = policy.Evaluate(Input(ContinuityCutoverMode.Matcher, MeetingIdentityVerdict.SameMeeting, Snapshot("a"), null, first.GraceReceipt, Now.AddMinutes(6)));

        Assert.Equal(ContinuityLifecycleAction.Grace, first.Action);
        Assert.Equal(first.GraceReceipt?.DeadlineUtc, second.GraceReceipt?.DeadlineUtc);
        Assert.Equal(2, second.GraceReceipt?.ObservationCount);
        Assert.NotEqual(first.GraceReceipt?.DeadlineUtc, expired.GraceReceipt?.DeadlineUtc);
    }

    [Fact]
    public void Manual_Stop_Wins_Over_All_Identity_And_Grace_State()
    {
        var decision = new ContinuityCutoverPolicy().Evaluate(Input(ContinuityCutoverMode.Matcher, MeetingIdentityVerdict.SameMeeting, Snapshot("a"), Snapshot("a")) with { IsManualStop = true });

        Assert.Equal(ContinuityLifecycleAction.Stop, decision.Action);
        Assert.Null(decision.GraceReceipt);
        Assert.False(decision.UsedMatcher);
    }

    private static ContinuityCutoverInput Input(
        ContinuityCutoverMode mode,
        MeetingIdentityVerdict legacy,
        MeetingIdentitySnapshot? existing,
        MeetingIdentitySnapshot? observed,
        ContinuityGraceReceipt? grace = null,
        DateTimeOffset? nowUtc = null) =>
        new(mode, 7, existing, observed, legacy, false, grace, nowUtc ?? Now);

    private static MeetingIdentitySnapshot Snapshot(string token) => new(
        MeetingIdentitySnapshot.CurrentSchemaVersion,
        MeetingPlatform.Teams,
        MeetingIdentityEvidenceTier.Strong,
        null,
        token.PadRight(64, '0'),
        true,
        true,
        false,
        Now,
        Now.AddHours(1),
        [MeetingIdentityEvidenceSource.ManifestTitle],
        new string('b', 64),
        new string('c', 64));
}
