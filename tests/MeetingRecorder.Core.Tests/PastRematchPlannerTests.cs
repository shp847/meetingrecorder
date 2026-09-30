using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class PastRematchPlannerTests
{
    [Fact]
    public void Build_OnlyAdmitsCurrentPublishedGenericTargets()
    {
        var plan = PastRematchPlanner.Build([Target(), Target("busy") with { IsBusy = true }, Target("user") with { HasUserEnteredName = true }, Target("stale") with { HasCurrentArtifactFingerprint = false }]);
        Assert.Equal(1, plan.EligibleCount);
        Assert.Equal(PastRematchDisposition.Eligible, plan.Items[0].Disposition);
        Assert.Equal(PastRematchDisposition.Blocked, plan.Items.Single(item => item.MeetingStableIdentity == "busy").Disposition);
        Assert.Equal(PastRematchDisposition.Skip, plan.Items.Single(item => item.MeetingStableIdentity == "user").Disposition);
    }

    [Theory]
    [InlineData(false, true, true, PastRematchDisposition.Skip)]
    [InlineData(true, false, true, PastRematchDisposition.Skip)]
    [InlineData(true, true, false, PastRematchDisposition.Skip)]
    public void Build_RequiresStructuredMetadataSamplesAndProfiles(bool transcript, bool samples, bool profiles, PastRematchDisposition expected)
    {
        var item = Assert.Single(PastRematchPlanner.Build([Target() with { IsPublishedStructuredTranscript = transcript, HasCompatibleVoiceSamples = samples, HasActiveProfiles = profiles }]).Items);
        Assert.Equal(expected, item.Disposition);
    }

    private static PastRematchTarget Target(string identity = "meeting-1") => new(identity, "revision-1", "metadata-fingerprint", true, true, true, true, false, false, false, false, true);
}
