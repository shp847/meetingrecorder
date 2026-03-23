using System.Reflection;

namespace AppPlatform.Tests;

public sealed class AppPlatformExtractionTests
{
    [Fact]
    public void AppPlatform_Workspace_Has_A_Dedicated_Solution_And_Product_Adapter_Project()
    {
        var repoRoot = GetRepoRoot();
        var appPlatformSolutionPath = Path.Combine(repoRoot, "AppPlatform.sln");
        var productProjectPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Product", "MeetingRecorder.Product.csproj");

        Assert.True(File.Exists(appPlatformSolutionPath), $"Expected AppPlatform workspace solution at '{appPlatformSolutionPath}'.");
        Assert.True(File.Exists(productProjectPath), $"Expected Meeting Recorder product adapter project at '{productProjectPath}'.");

        var appPlatformSolutionContents = File.ReadAllText(appPlatformSolutionPath);

        Assert.Contains("AppPlatform.Abstractions", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Configuration", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.WpfHost", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Wix", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Shell.Wpf", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.Product", appPlatformSolutionContents, StringComparison.Ordinal);
        Assert.DoesNotContain("MeetingRecorder.ProcessingWorker", appPlatformSolutionContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_Adapter_Is_Consumed_By_App_Installer_And_Setup_Wrappers()
    {
        var repoRoot = GetRepoRoot();
        var appProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MeetingRecorder.App.csproj"));
        var installerProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.Installer", "MeetingRecorder.Installer.csproj"));
        var setupProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "MeetingRecorder.Setup.wixproj"));

        Assert.Contains(@"..\MeetingRecorder.Product\MeetingRecorder.Product.csproj", appProject, StringComparison.Ordinal);
        Assert.Contains(@"..\MeetingRecorder.Product\MeetingRecorder.Product.csproj", installerProject, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.Product", setupProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_Cli_Defines_The_Canonical_Install_And_Update_Commands()
    {
        var repoRoot = GetRepoRoot();
        var cliProgramPath = Path.Combine(repoRoot, "src", "AppPlatform.Deployment.Cli", "Program.cs");

        Assert.True(File.Exists(cliProgramPath), $"Expected deployment CLI entry point at '{cliProgramPath}'.");

        var cliProgramContents = File.ReadAllText(cliProgramPath);

        Assert.Contains("install-bundle", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("install-latest", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("apply-update", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("repair-shortcuts", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("print-layout", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("emit-manual-steps", cliProgramContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_Cli_Pauses_On_NonException_Failures_When_PauseOnError_Is_Requested()
    {
        var repoRoot = GetRepoRoot();
        var cliProgramPath = Path.Combine(repoRoot, "src", "AppPlatform.Deployment.Cli", "Program.cs");

        Assert.True(File.Exists(cliProgramPath), $"Expected deployment CLI entry point at '{cliProgramPath}'.");

        var cliProgramContents = File.ReadAllText(cliProgramPath);

        Assert.Contains("if (exitCode != 0 && pauseOnError)", cliProgramContents, StringComparison.Ordinal);
        Assert.Contains("PauseOnError();", cliProgramContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_And_InApp_Update_Wrappers_Delegate_To_The_Deployment_Cli()
    {
        var repoRoot = GetRepoRoot();
        var installScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1"));
        var latestScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Install-LatestFromGitHub.ps1"));
        var updateScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Apply-DownloadedUpdate.ps1"));
        var updateLaunchBuilder = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.App", "Services", "UpdateInstallerLaunchBuilder.cs"));

        Assert.Contains("AppPlatform.Deployment.Cli", installScript, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli", latestScript, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli", updateScript, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli", updateLaunchBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("Install-MeetingRecorder.ps1", updateLaunchBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("Apply-DownloadedUpdate.ps1", updateLaunchBuilder, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_Project_Owns_Settings_And_Help_Host_Windows()
    {
        var repoRoot = GetRepoRoot();
        var shellSettingsWindowPath = Path.Combine(repoRoot, "src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml");
        var shellHelpWindowPath = Path.Combine(repoRoot, "src", "AppPlatform.Shell.Wpf", "HelpHostWindow.xaml");
        var appSettingsWindowPath = Path.Combine(repoRoot, "src", "MeetingRecorder.App", "SettingsWindow.xaml");
        var appHelpWindowPath = Path.Combine(repoRoot, "src", "MeetingRecorder.App", "HelpWindow.xaml");

        Assert.True(File.Exists(shellSettingsWindowPath), $"Expected shared settings host window at '{shellSettingsWindowPath}'.");
        Assert.True(File.Exists(shellHelpWindowPath), $"Expected shared help host window at '{shellHelpWindowPath}'.");
        Assert.False(File.Exists(appSettingsWindowPath), $"Did not expect Meeting Recorder app to keep owning settings window XAML at '{appSettingsWindowPath}'.");
        Assert.False(File.Exists(appHelpWindowPath), $"Did not expect Meeting Recorder app to keep owning help window XAML at '{appHelpWindowPath}'.");
    }

    [Fact]
    public void Update_Package_Installer_Does_Not_Wait_Indefinitely_For_The_Source_Process()
    {
        var repoRoot = GetRepoRoot();
        var installerPath = Path.Combine(repoRoot, "src", "AppPlatform.Deployment", "UpdatePackageInstaller.cs");

        Assert.True(File.Exists(installerPath), $"Expected update package installer at '{installerPath}'.");

        var contents = File.ReadAllText(installerPath);

        Assert.Contains("SourceProcessExitTimeout", contents, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(timeout)", contents, StringComparison.Ordinal);
        Assert.Contains("EnsureInstallPathReleasedAsync(installRoot, cancellationToken)", contents, StringComparison.Ordinal);
        Assert.Contains("did not exit within", contents, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
