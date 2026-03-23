using MeetingRecorder.Installer;
using MeetingRecorder.Core.Services;
using System.Diagnostics;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerBootstrapperTests
{
    [Fact]
    public async Task InstallLatestAsync_Prefers_Colocated_Local_Package_Assets_Over_GitHub_Downloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "InstallerBootstrapperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var commandPath = Path.Combine(root, "Install-LatestFromGitHub.cmd");
        var powerShellPath = Path.Combine(root, "Install-LatestFromGitHub.ps1");
        var zipPath = Path.Combine(root, "MeetingRecorder-v0.3-win-x64.zip");
        File.WriteAllText(commandPath, "@echo off");
        File.WriteAllText(powerShellPath, "Write-Host 'bootstrap'");
        File.WriteAllText(zipPath, "zip");

        try
        {
            var processLauncher = new FakeInstallerProcessLauncher();
            var bootstrapper = new InstallerBootstrapper(
                new GitHubReleaseBootstrapService(new ThrowingFeedClient()),
                new HttpFileDownloader(),
                processLauncher,
                root);

            var result = await bootstrapper.InstallLatestAsync(
                Array.Empty<string>(),
                progress: null,
                CancellationToken.None);

            Assert.NotNull(processLauncher.StartInfo);
            Assert.Equal(commandPath, processLauncher.StartInfo!.FileName);
            Assert.Equal(root, processLauncher.StartInfo.WorkingDirectory);
            Assert.Contains("\"-PackageZipPath\"", processLauncher.StartInfo.Arguments, StringComparison.Ordinal);
            Assert.Contains($"\"{zipPath}\"", processLauncher.StartInfo.Arguments, StringComparison.Ordinal);
            Assert.Equal(commandPath, result.BootstrapCommandPath);
            Assert.Equal(Path.GetFileName(zipPath), result.ReleaseInfo?.AppZipAsset.Name);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public void BuildBootstrapHandoffStartInfo_Launches_The_Command_Bootstrapper_With_Forwarded_Arguments()
    {
        var startInfo = InstallerBootstrapper.BuildBootstrapHandoffStartInfo(
            commandPath: @"C:\Temp\Bootstrap\Install-LatestFromGitHub.cmd",
            workingDirectory: @"C:\Temp\Bootstrap",
            forwardedArguments:
            [
                "-InstallChannel",
                "ExecutableBootstrap",
                "-NoLaunch",
            ]);

        Assert.Equal(@"C:\Temp\Bootstrap\Install-LatestFromGitHub.cmd", startInfo.FileName);
        Assert.Equal(@"C:\Temp\Bootstrap", startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(
            "\"-InstallChannel\" \"ExecutableBootstrap\" \"-NoLaunch\"",
            startInfo.Arguments);
    }

    [Fact]
    public void BuildLaunchStartInfo_Uses_ShellExecute_For_Command_Launchers()
    {
        var startInfo = InstallerBootstrapper.BuildLaunchStartInfo(
            executablePath: @"C:\Portable Apps\MeetingRecorder\Run-MeetingRecorder.cmd",
            workingDirectory: @"C:\Portable Apps\MeetingRecorder");

        Assert.Equal(@"C:\Portable Apps\MeetingRecorder\Run-MeetingRecorder.cmd", startInfo.FileName);
        Assert.Equal(@"C:\Portable Apps\MeetingRecorder", startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
    }

    private sealed class FakeInstallerProcessLauncher : IInstallerProcessLauncher
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Launch(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
        }
    }

    private sealed class ThrowingFeedClient : IAppUpdateFeedClient
    {
        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The bootstrapper should not query the feed when local assets are present.");
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The bootstrapper should not download assets when local assets are present.");
        }
    }
}
