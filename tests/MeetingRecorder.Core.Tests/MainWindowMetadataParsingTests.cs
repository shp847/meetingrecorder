using MeetingRecorder.App;

namespace MeetingRecorder.Core.Tests;

public sealed class MainWindowMetadataParsingTests
{
    [Fact]
    public void ParseDelimitedKeyAttendeesText_Splits_Commas_And_Semicolons_And_Merges_Reasonable_Partial_Matches()
    {
        var result = MainWindow.ParseDelimitedKeyAttendeesText("Pranav, Jane Smith; Pranav Sharma ; John Doe");

        Assert.Equal(
            ["Pranav Sharma", "Jane Smith", "John Doe"],
            result);
    }
}
