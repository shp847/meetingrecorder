using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class OngoingMeetingHealEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_Strong_Adjacent_Sealed_Pair_Is_Eligible()
    {
        var identity = Identity("a");
        var result = new OngoingMeetingHealEligibilityResolver().Evaluate(Candidate(identity, identity), Now);
        Assert.Equal(OngoingMeetingHealEligibility.Eligible, result);
    }

    [Theory]
    [InlineData(true, false, OngoingMeetingHealEligibility.ActiveOrIncomplete)]
    [InlineData(false, true, OngoingMeetingHealEligibility.UserMetadataConflict)]
    public void Lease_And_User_Conflict_Block_AutoHeal(bool lease, bool conflict, OngoingMeetingHealEligibility expected)
    {
        var identity = Identity("a");
        Assert.Equal(expected, new OngoingMeetingHealEligibilityResolver().Evaluate(Candidate(identity, identity, lease, conflict), Now));
    }

    [Fact]
    public void Unknown_And_Different_Identity_Never_AutoHeal()
    {
        var resolver = new OngoingMeetingHealEligibilityResolver();
        Assert.Equal(OngoingMeetingHealEligibility.UnknownIdentity, resolver.Evaluate(Candidate(Identity("a"), null), Now));
        Assert.Equal(OngoingMeetingHealEligibility.DifferentMeeting, resolver.Evaluate(Candidate(Identity("a"), Identity("b")), Now));
    }

    [Fact]
    public void Admitted_Pair_Is_Not_Admitted_Twice()
    {
        var identity = Identity("a");
        var candidate = Candidate(identity, identity);
        var service = new OngoingMeetingHealService();

        Assert.True(service.TryAdmit(candidate, Now));
        Assert.False(service.TryAdmit(candidate, Now));
    }

    private static OngoingMeetingHealCandidate Candidate(MeetingIdentitySnapshot? first, MeetingIdentitySnapshot? second, bool lease = false, bool conflict = false)
    {
        var predecessor = new MeetingSessionManifest { SessionId = "first", Platform = MeetingPlatform.Teams, StartedAtUtc = Now.AddMinutes(-10), EndedAtUtc = Now.AddMinutes(-1), State = SessionState.Published, MergedAudioPath = "first.wav" };
        var successor = predecessor with { SessionId = "second", StartedAtUtc = Now, EndedAtUtc = Now.AddMinutes(1), MergedAudioPath = "second.wav" };
        return new(predecessor, successor, first, second, lease, conflict);
    }

    private static MeetingIdentitySnapshot Identity(string token) => new(1, MeetingPlatform.Teams, MeetingIdentityEvidenceTier.Strong, null, token.PadRight(64, '0'), true, true, false, Now, Now.AddHours(1), [MeetingIdentityEvidenceSource.ManifestTitle], new string('c', 64), new string('d', 64));
}
