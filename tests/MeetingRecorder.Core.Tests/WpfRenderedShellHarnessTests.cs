namespace MeetingRecorder.Core.Tests;

public sealed class WpfRenderedShellHarnessTests
{
    [Fact]
    public void Home_Shell_Renders_In_Isolated_Profile_With_Automation_And_Keyboard_Evidence()
    {
        var evidence = WpfRenderHarness.CaptureHomeShell();

        Assert.True(File.Exists(evidence.ScreenshotPath));
        Assert.True(new FileInfo(evidence.ScreenshotPath).Length > 0);
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("HomePrimaryActionButton | Button | Start recording | False", automationTrace);
        Assert.Contains("MainTabControl | Tab |", automationTrace);
        Assert.Contains("HeaderShellStatusDetailTextBlock | Text | Local transcription needs setup.", automationTrace);
        Assert.Contains("Focus: Open Settings", keyboardTrace);
        Assert.DoesNotContain("<none>", keyboardTrace);
    }

    [Fact]
    public void Processing_Shell_Renders_A_Synthetic_Manifest_In_The_Meetings_Workspace()
    {
        var evidence = WpfRenderHarness.CaptureProcessingShell();

        Assert.True(File.Exists(evidence.ScreenshotPath));
        Assert.True(new FileInfo(evidence.ScreenshotPath).Length > 0);
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("MeetingsDataGrid | List |", automationTrace);
        Assert.Contains("MeetingsProcessingStatusBorder", automationTrace);
        Assert.Contains("Focus: Open Settings", keyboardTrace);
        Assert.DoesNotContain("<none>", keyboardTrace);
    }

    [Fact]
    public void Selection_Active_Shell_Renders_The_Synthetic_Meeting_Inspector()
    {
        var evidence = WpfRenderHarness.CaptureSelectionActiveShell();

        Assert.True(File.Exists(evidence.ScreenshotPath));
        Assert.True(new FileInfo(evidence.ScreenshotPath).Length > 0);
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);

        Assert.Contains("MeetingsDataGrid | List |", automationTrace);
        Assert.Contains("SelectedMeetingInspectorTitleTextBlock | Text | Synthetic processing review", automationTrace);
    }

    [Fact]
    public void Cleanup_Recommendation_Shell_Renders_The_Contained_Synthetic_Review_Banner()
    {
        var evidence = WpfRenderHarness.CaptureCleanupRecommendationShell();

        Assert.True(File.Exists(evidence.ScreenshotPath));
        Assert.True(new FileInfo(evidence.ScreenshotPath).Length > 0);
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);

        Assert.Contains("MeetingCleanupReviewBannerBorder", automationTrace);
        Assert.Contains("MeetingCleanupReviewBannerTextBlock | Text |", automationTrace);
    }

    [Fact]
    public void Empty_Healthy_Shell_Renders_A_Ready_Synthetic_Model_State()
    {
        var evidence = WpfRenderHarness.CaptureEmptyHealthyShell();

        Assert.True(File.Exists(evidence.ScreenshotPath));
        Assert.True(new FileInfo(evidence.ScreenshotPath).Length > 0);
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);

        Assert.Contains(
            "DashboardModelReadinessTextBlock | Text | Ready. A valid Whisper model is active for transcript generation.",
            automationTrace);
    }

    [Fact]
    public void Setup_Blocked_Shell_Renders_A_1280x800_125Percent_Raster()
    {
        var evidence = WpfRenderHarness.CaptureShellAt125Dpi(
            WpfRenderHarness.SyntheticShellState.SetupBlocked);

        using var stream = File.OpenRead(evidence.ScreenshotPath);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Single();

        Assert.Equal(1280, frame.PixelWidth);
        Assert.Equal(800, frame.PixelHeight);
        Assert.Equal(120d, frame.DpiX);
        Assert.Equal(120d, frame.DpiY);
    }
}
