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
