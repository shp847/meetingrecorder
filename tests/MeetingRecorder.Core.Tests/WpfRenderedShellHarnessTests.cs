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
    public void Setup_Blocked_Shell_Renders_A_1280x800_Viewport_At_125Percent()
    {
        var evidence = WpfRenderHarness.CaptureShellAt125Dpi(
            WpfRenderHarness.SyntheticShellState.SetupBlocked);

        using var stream = File.OpenRead(evidence.ScreenshotPath);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Single();

        Assert.Equal(1600, frame.PixelWidth);
        Assert.Equal(1000, frame.PixelHeight);
        Assert.Equal(120d, frame.DpiX);
        Assert.Equal(120d, frame.DpiY);
    }

    [Fact]
    public void Processing_Shell_Renders_A_1024x768_Viewport_At_125Percent()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.Processing,
            logicalWidth: 1024,
            logicalHeight: 768,
            rasterScale: 1.25d);

        using var stream = File.OpenRead(evidence.ScreenshotPath);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Single();

        Assert.Equal(1280, frame.PixelWidth);
        Assert.Equal(960, frame.PixelHeight);
        Assert.Equal(120d, frame.DpiX);
        Assert.Equal(120d, frame.DpiY);
    }

    [Fact]
    public void Settings_Recording_Closes_On_Escape_And_Returns_Focus_To_Its_Opener()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.SettingsRecording,
            logicalWidth: 1280,
            logicalHeight: 800,
            rasterScale: 1.25d);

        Assert.True(File.Exists(evidence.ScreenshotPath));
        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("SettingsRecordingSectionButton | Button | Recording | True", automationTrace);
        Assert.Contains("Focus: SettingsRecordingSectionButton", keyboardTrace);
        Assert.Contains("Escape: Open Settings", keyboardTrace);
    }

    [Fact]
    public void Settings_Recording_Saves_A_Harmless_Edit_In_The_Isolated_Profile()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.SettingsRecordingSaved,
            logicalWidth: 1280,
            logicalHeight: 800,
            rasterScale: 1.25d);

        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("Edit: Use Outlook calendar as a fallback meeting title =", keyboardTrace);
        Assert.Contains("Save: Config saved and applied to the running app.", keyboardTrace);
        Assert.Contains("Escape: Open Settings", keyboardTrace);
    }

    [Fact]
    public void Permanent_Delete_Confirmation_Exposes_Named_Actions_And_Escape_Cancels()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.PermanentDeleteCancelled,
            logicalWidth: 1280,
            logicalHeight: 800,
            rasterScale: 1.25d);

        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains(" | Edit | Type DELETE to confirm permanent delete | True", automationTrace);
        Assert.Contains(" | Button | Delete permanently | False", automationTrace);
        Assert.Contains(" | Button | Cancel permanent delete | True", automationTrace);
        Assert.Contains("Focus: Type DELETE to confirm permanent delete", keyboardTrace);
        Assert.Contains("Escape:", keyboardTrace);
    }

    [Fact]
    public void Hosted_Summary_Consent_Explains_Its_Boundary_And_Escape_Cancels()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.HostedSummaryConsentCancelled,
            logicalWidth: 1280,
            logicalHeight: 800,
            rasterScale: 1.25d);

        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("published transcript text", automationTrace, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" | Button | Authorize hosted summaries | True", automationTrace);
        Assert.Contains(" | Button | Cancel hosted summary authorization | True", automationTrace);
        Assert.Contains("Focus: Authorize hosted summaries", keyboardTrace);
        Assert.Contains("Escape:", keyboardTrace);
    }

    [Fact]
    public void Voice_Profile_Delete_Explains_Its_Scope_And_Escape_Cancels()
    {
        var evidence = WpfRenderHarness.CaptureShell(
            WpfRenderHarness.SyntheticShellState.VoiceProfileDeleteCancelled,
            logicalWidth: 1280,
            logicalHeight: 800,
            rasterScale: 1.25d);

        var automationTrace = File.ReadAllText(evidence.AutomationTracePath);
        var keyboardTrace = File.ReadAllText(evidence.KeyboardTracePath);

        Assert.Contains("Existing meeting display names stay unchanged", automationTrace);
        Assert.Contains(" | Button | Delete selected Voice Profile | True", automationTrace);
        Assert.Contains(" | Button | Cancel Voice Profile deletion | True", automationTrace);
        Assert.Contains("Focus: Delete selected Voice Profile", keyboardTrace);
        Assert.Contains("Escape:", keyboardTrace);
    }
}
