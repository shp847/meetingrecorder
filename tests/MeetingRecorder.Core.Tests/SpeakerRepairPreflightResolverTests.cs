using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerRepairPreflightResolverTests
{
    [Fact]
    public void Resolve_Blocks_Stale_And_Busy_Meetings()
    {
        Assert.False(SpeakerRepairPreflightResolver.Resolve(Input() with { HasCurrentStructuredArtifact = false }).CanQueue);
        Assert.False(SpeakerRepairPreflightResolver.Resolve(Input() with { IsBusy = true }).CanQueue);
    }

    [Fact]
    public void Resolve_Explains_Name_And_Profile_Boundaries()
    {
        var result = SpeakerRepairPreflightResolver.Resolve(Input() with { UserEnteredNameCount = 2, ProfileAttributionCount = 1 });

        Assert.True(result.CanQueue);
        Assert.Contains("not be copied", result.Consequence);
        Assert.Contains("need review", result.Consequence);
        Assert.Contains("not retranscribed", result.Consequence);
    }

    private static SpeakerRepairPreflightInput Input() => new("meeting-1", "revision-1", true, false, true, 0, 0);
}
