using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class UpdateInstallSourceTests
{
    [Fact]
    public void LaunchDownloadedUpdateInstaller_Uses_Installed_Process_Path_Root_For_The_Updater_Helper()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void LaunchDownloadedUpdateInstaller", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task PersistLastUpdateCheckUtcAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("UpdateInstallerLaunchBuilder.ResolveInstalledAppRoot(Environment.ProcessPath, AppContext.BaseDirectory)", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, UpdateInstallerLaunchBuilder.DeploymentCliExecutableName)", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_Same_Version_Update_Is_Not_Cleared_Based_On_Version_Alone()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task<bool> TryInstallPendingDownloadedUpdateIfIdleAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task InstallAvailableUpdateAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("MainWindowInteractionLogic.IsPendingUpdateAlreadyInstalled(config, AppBranding.Version)", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(config.PendingUpdateVersion, AppBranding.Version", methodBlock, StringComparison.Ordinal);
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
