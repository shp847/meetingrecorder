using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerReviewPlaybackSourceTests
{
    [Fact]
    public void Detail_Player_Exposes_Keyboard_Accessible_Stateful_Controls_And_Releases_Leases()
    {
        var xaml = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml"));
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml.cs"));

        Assert.Contains("PlaySpeakerEvidenceCueButton_OnClick", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding DataContext.PlaybackText", xaml);
        Assert.Contains("PlaybackProgressText", xaml);
        Assert.Contains("PlaybackState.Paused", source);
        Assert.Contains("_speakerReviewProgressTimer", source);
        Assert.Contains("_speakerReviewLease?.Dispose();", source);
        Assert.Contains("protected override void OnClosed", source);
        Assert.Contains("StopSpeakerReviewPlayback();", source);
    }

    [Fact]
    public void Playback_Is_Blocked_For_Recording_And_Maintenance_And_Uses_A_Clip_Lease()
    {
        var mainWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));
        var detailWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml.cs"));

        Assert.Contains("_recordingCoordinator.IsRecording", mainWindow);
        Assert.Contains("IsMeetingActionInProgress()", mainWindow);
        Assert.Contains("_speakerReviewClipService.AcquireLease", mainWindow);
        Assert.Contains("if (isBusy)", detailWindow);
        Assert.Contains("StopSpeakerReviewPlayback();", detailWindow);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }
}
