using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ArtifactPathBuilderTests
{
    [Fact]
    public void BuildFileStem_Uses_Zoom_Platform_Token()
    {
        var builder = new ArtifactPathBuilder();

        var stem = builder.BuildFileStem(
            MeetingPlatform.Zoom,
            DateTimeOffset.Parse("2026-07-21T15:00:00Z"),
            "Next Steps: Kearney | BairesDev");

        Assert.Equal("2026-07-21_150000_zoom_next-steps-kearney-bairesdev", stem);
    }

    [Fact]
    public void BuildFileStem_Sanitizes_Title_And_Uses_Platform()
    {
        var builder = new ArtifactPathBuilder();

        var stem = builder.BuildFileStem(
            MeetingPlatform.GoogleMeet,
            new DateTimeOffset(2026, 3, 15, 17, 8, 9, TimeSpan.Zero),
            "Q1 Review / Sales & Ops");

        Assert.Equal("2026-03-15_170809_gmeet_q1-review-sales-ops", stem);
    }
}
