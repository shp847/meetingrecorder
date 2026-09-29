using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class AccessibilityContractTests
{
    [Fact]
    public void Critical_Controls_Expose_Accessible_Names_And_Polite_Status()
    {
        var mainWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml"));
        var settingsWindow = File.ReadAllText(GetPath("src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml"));
        var detailWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml"));

        Assert.Contains("AutomationProperties.Name=\"Start recording\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Stop recording\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Open Settings\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Open Help\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Search meetings\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Meeting view preset\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Meetings list\"", mainWindow);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Save Settings\"", settingsWindow);
        Assert.Contains("AutomationProperties.Name=\"Close Settings\"", settingsWindow);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", settingsWindow);
        Assert.Contains("AutomationProperties.Name=\"Close meeting details\"", detailWindow);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", detailWindow);
    }

    [Fact]
    public void Settings_Deep_Links_Return_Keyboard_Focus_To_Target_Or_Section_Default()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));

        Assert.Contains("private void FocusSettingsTarget", source);
        Assert.Contains("requestedControl.BringIntoView()", source);
        Assert.Contains("requestedControl.Focus()", source);
        Assert.Contains("TranscriptionOverviewPrimaryButton.Focus()", source);
        Assert.Contains("ConfigSummaryGenerationEnabledCheckBox.Focus()", source);
        Assert.Contains("ConfigAudioOutputDirTextBox.Focus()", source);
    }

    [Fact]
    public void ExternalAudioImport_Uses_Accessible_Bounded_Review_And_Returns_Focus_To_It()
    {
        var mainWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml"));
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));

        Assert.Contains("AutomationProperties.Name=\"Add audio files to import review\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Queue valid imported audio\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Remove only the selected row from import review\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Open setup for a blocked import\"", mainWindow);
        Assert.Contains("AutomationProperties.Name=\"Import privacy and consent notice\"", mainWindow);
        Assert.Contains("AutomationProperties.HelpText=\"Select a row to edit its meeting details. The original file stays in place.\"", mainWindow);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Local\"", mainWindow);
        Assert.DoesNotContain("Binding=\"{Binding SourcePath", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ExternalAudioImportDataGrid.Focus();", source);
    }

    [Fact]
    public void Technical_Studio_Surfaces_Avoid_Drop_Shadows_And_Use_Contained_Keyboard_Navigation()
    {
        var theme = File.ReadAllText(GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml"));
        var settingsWindow = File.ReadAllText(GetPath("src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml"));

        Assert.DoesNotContain("DropShadowEffect", theme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KeyboardNavigation.DirectionalNavigation=\"Contained\"", theme);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Local\"", theme);
        Assert.Contains("MinWidth=\"780\"", settingsWindow);
        Assert.Contains("MinHeight=\"620\"", settingsWindow);
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
