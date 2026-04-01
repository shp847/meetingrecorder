using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class LiveMicrophoneToggleSourceTests
{
    [Fact]
    public void Home_Quick_Settings_Apply_Microphone_Changes_To_The_Current_Recording()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task SaveHomeQuickSettingAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<bool> ApplyLiveMicCapturePreferenceIfNeededAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("applyMicCaptureLiveChange = false", methodBlock, StringComparison.Ordinal);
        Assert.Contains("ApplyLiveMicCapturePreferenceIfNeededAsync(", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_Save_Applies_A_Microphone_Change_To_The_Current_Recording()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async void SaveConfigButton_OnClick", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void AudioFolderLink_OnClick", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("currentConfig.MicCaptureEnabled != nextConfig.MicCaptureEnabled", methodBlock, StringComparison.Ordinal);
        Assert.Contains("ApplyLiveMicCapturePreferenceIfNeededAsync(", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Microphone_Activity_Prompt_Enables_Capture_For_The_Current_Recording_From_Now_On()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task TryPromptToEnableMicCaptureAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<IReadOnlyList<MeetingInspectionRecord>> BuildMeetingInspectionsAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("ApplyLiveMicCapturePreferenceIfNeededAsync(", methodBlock, StringComparison.Ordinal);
        Assert.Contains("from now on for this recording", methodBlock, StringComparison.Ordinal);
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
