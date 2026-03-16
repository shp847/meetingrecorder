using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ArtifactPathBuilderTests
{
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
