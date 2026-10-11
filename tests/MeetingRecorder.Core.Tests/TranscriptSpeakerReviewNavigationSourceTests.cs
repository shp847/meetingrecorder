using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSpeakerReviewNavigationSourceTests
{
    [Fact]
    public void Transcript_Labels_Use_Stable_Speaker_Ids_To_Focus_The_Current_Review_Row()
    {
        var xaml = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml"));
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml.cs"));

        Assert.Contains("OpenSpeakerReviewFromTranscriptButton_OnClick", xaml);
        Assert.Contains("CanOpenSpeakerReview", xaml);
        Assert.Contains("SpeakerReviewAutomationName", xaml);
        Assert.Contains("string.Equals(row.SpeakerId, segment.SpeakerId", source);
        Assert.Contains("SpeakerLabelsDataGrid.ScrollIntoView(target);", source);
        Assert.Contains("SpeakerLabelsDataGrid.CurrentCell", source);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }
            .Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }
}
