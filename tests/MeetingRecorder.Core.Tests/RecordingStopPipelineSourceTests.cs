using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class RecordingStopPipelineSourceTests
{
    [Fact]
    public void Auto_Stop_Timeout_Branch_Enters_A_Visible_Stopping_State_Before_Awaiting_Stop()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var branchStart = source.IndexOf("if (remaining <= TimeSpan.Zero)", StringComparison.Ordinal);
        var branchEnd = source.IndexOf("else", branchStart, StringComparison.Ordinal);
        var branchBlock = source[branchStart..branchEnd];

        var transitionIndex = branchBlock.IndexOf("_isRecordingTransitionInProgress = true;", StringComparison.Ordinal);
        var uiIndex = branchBlock.IndexOf("UpdateUi(\"Auto-stopping recording.\"", StringComparison.Ordinal);
        var stopIndex = branchBlock.IndexOf("await StopCurrentRecordingAsync(\"Meeting signals expired after the configured timeout.\");", StringComparison.Ordinal);

        Assert.True(transitionIndex >= 0, "Expected auto-stop timeout branch to enter a recording transition state.");
        Assert.True(uiIndex > transitionIndex, "Expected auto-stop timeout branch to update the UI after entering the transition state.");
        Assert.True(stopIndex > uiIndex, "Expected auto-stop timeout branch to await the stop path only after the visible UI transition.");
    }

    [Fact]
    public void Recording_Stop_Path_Offloads_Attendee_Enrichment_To_The_Background_Processing_Queue()
    {
        var coordinatorSourcePath = GetPath("src", "MeetingRecorder.App", "Services", "RecordingSessionCoordinator.cs");
        var coordinatorSource = File.ReadAllText(coordinatorSourcePath);
        var stopStart = coordinatorSource.IndexOf("public async Task<string?> StopAsync", StringComparison.Ordinal);
        var stopEnd = coordinatorSource.IndexOf("public async Task<bool> RenameActiveSessionAsync", stopStart, StringComparison.Ordinal);
        var stopBlock = coordinatorSource[stopStart..stopEnd];

        var queueSourcePath = GetPath("src", "MeetingRecorder.App", "Services", "ProcessingQueueService.cs");
        var queueSource = File.ReadAllText(queueSourcePath);

        Assert.DoesNotContain("TryEnrichAsync", stopBlock, StringComparison.Ordinal);
        Assert.Contains("TryEnrichAsync", queueSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_Stop_Countdown_Branch_Updates_Home_Visual_State_Before_Timeout_Expires()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        Assert.Contains("SetAutoStopCountdown(remaining);", source, StringComparison.Ordinal);
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
