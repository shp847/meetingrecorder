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
}
