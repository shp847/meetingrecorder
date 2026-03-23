using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerWindowXamlTests
{
    [Fact]
    public void Installer_App_Merges_The_Shared_Shell_Theme()
    {
        var appXamlPath = GetPath("src", "MeetingRecorder.Installer", "App.xaml");

        var appXaml = File.ReadAllText(appXamlPath);

        Assert.Contains("/AppPlatform.Shell.Wpf;component/ShellTheme.xaml", appXaml);
    }

    [Fact]
    public void Installer_Window_Uses_Shared_Brushes_Cards_And_Action_Styles()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Background=\"{StaticResource AppCanvasBrush}\"", xaml);
        Assert.Contains("Style=\"{StaticResource DialogCardStyle}\"", xaml);
        Assert.Contains("BasedOn=\"{StaticResource PrimaryActionButtonStyle}\"", xaml);
        Assert.Contains("BasedOn=\"{StaticResource SecondaryActionButtonStyle}\"", xaml);
        Assert.Contains("Style=\"{StaticResource InstallerPrimaryButtonStyle}\"", xaml);
        Assert.Contains("Style=\"{StaticResource InstallerSecondaryButtonStyle}\"", xaml);
    }

    [Fact]
    public void Installer_Window_Uses_A_Clear_Branded_Information_Hierarchy()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"InstallerHeroPanel\"", xaml);
        Assert.Contains("x:Name=\"InstallerStatusCard\"", xaml);
        Assert.Contains("x:Name=\"InstallerProgressCard\"", xaml);
        Assert.Contains("x:Name=\"InstallerActivityCard\"", xaml);
        Assert.Contains("x:Name=\"InstallerFallbackCard\"", xaml);
        Assert.Contains("x:Name=\"InstallerActionsCard\"", xaml);
        Assert.Contains("x:Name=\"InstallerToneBadge\"", xaml);
        Assert.Contains("x:Name=\"PrimaryActionsPanel\"", xaml);
        Assert.Contains("x:Name=\"SecondaryActionsPanel\"", xaml);
    }

    [Fact]
    public void Installer_Window_Uses_A_Scrollable_Shell_And_Installer_Specific_Action_Styles()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"InstallerShellScrollViewer\"", xaml);
        Assert.Contains("Style=\"{StaticResource InstallerPrimaryButtonStyle}\"", xaml);
        Assert.Contains("Style=\"{StaticResource InstallerSecondaryButtonStyle}\"", xaml);
    }

    [Fact]
    public void Installer_Window_Describes_The_Exe_As_A_Launcher_And_Omits_Shortcut_Action_Buttons()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("command bootstrapper", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continue in the command window", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add desktop shortcut", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add Start Menu shortcut", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_Window_Explains_The_Split_Between_Installed_App_Files_And_Local_AppData_Runtime_Data()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Documents\\MeetingRecorder", xaml, StringComparison.Ordinal);
        Assert.Contains("%LOCALAPPDATA%\\MeetingRecorder", xaml, StringComparison.Ordinal);
        Assert.Contains("config, logs, models, and work files", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_Window_CodeBehind_Keeps_The_Backup_Cmd_Path_Available_After_Handoff()
    {
        var codeBehindPath = GetPath("src", "MeetingRecorder.Installer", "MainWindow.xaml.cs");

        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Contains("public bool CanLaunchBackupInstaller => !_isBusy;", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool CanLaunchBackupInstaller => !_isBusy && !_installSucceeded;", codeBehind, StringComparison.Ordinal);
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
