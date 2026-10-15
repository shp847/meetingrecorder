using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingTitleNormalizerTests
{
    [Theory]
    [InlineData("Meet - Project planning and 17 more pages - Work - Microsoft Edge", "Project planning")]
    [InlineData("Meet - Project planning - Microsoft Edge", "Project planning")]
    [InlineData("Meet - Project planning", "Meet - Project planning")]
    [InlineData("Project planning - Work - Microsoft Edge", "Project planning")]
    public void NormalizeGoogleMeetWindowTitle_Removes_Only_Verified_Edge_Decoration(string title, string expected)
    {
        Assert.Equal(expected, MeetingTitleNormalizer.NormalizeGoogleMeetWindowTitle(title));
    }

    [Theory]
    [InlineData("Meet - dbh-eecx-utm and 11 more pages - Work - Microsoft Edge")]
    [InlineData("Meet - dbh-eecx-utm and 1 more page - Work - Microsoft Edge")]
    [InlineData("Meet - dbh-eecx-utm - Camera and microphone recording - Memory usage - 437 MB")]
    [InlineData("Meet - dbh-eecx-utm - Work - Microsoft Edge")]
    public void NormalizeForComparison_Returns_A_Stable_Value_For_Google_Meet_Title_Variants(string title)
    {
        var normalized = MeetingTitleNormalizer.NormalizeForComparison(title);

        Assert.Equal("dbh eecx utm", normalized);
    }

    [Theory]
    [InlineData("Meet - Commercial Model Discussion with Kearney and 29 more pages - Work - Microsoft Edge")]
    [InlineData("Meet - Commercial Model Discussion with Kearney and 28 more pages - Work - Microsoft Edge")]
    [InlineData("Meet - Commercial Model Discussion with Kearney - Work - Microsoft Edge")]
    public void NormalizeForComparison_Ignores_Named_Meet_Browser_Decoration(string title)
    {
        var normalized = MeetingTitleNormalizer.NormalizeForComparison(title);

        Assert.Equal("meet commercial model discussion with kearney", normalized);
    }

    [Theory]
    [InlineData("Google Meet and 19 more pages - Work - Microsoft Edge")]
    [InlineData("Google Meet and 1 more page - Work - Microsoft Edge")]
    public void NormalizeForComparison_Treats_Generic_Google_Meet_Browser_Shell_As_Generic(string title)
    {
        var normalized = MeetingTitleNormalizer.NormalizeForComparison(title);

        Assert.Equal("google meet", normalized);
    }
}
