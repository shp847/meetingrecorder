using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingTitleNormalizerTests
{
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
}
