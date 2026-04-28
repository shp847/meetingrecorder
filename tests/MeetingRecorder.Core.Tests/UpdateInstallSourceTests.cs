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
    public void LaunchOnLogin_Registration_Uses_The_Installed_App_Executable_Instead_Of_The_Temporary_AppContext_Copy()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void TrySyncLaunchOnLoginSetting", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task RunScheduledUpdateCycleAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("UpdateInstallerLaunchBuilder.ResolveInstalledAppExecutablePath(", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, \"MeetingRecorder.App.exe\")", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_Same_Version_Update_Is_Not_Cleared_Based_On_Version_Alone()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task<bool> TryInstallPendingDownloadedUpdateIfIdleAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task InstallAvailableUpdateAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("var localUpdateState = BuildLocalUpdateState(config);", methodBlock, StringComparison.Ordinal);
        Assert.Contains("MainWindowInteractionLogic.IsPendingUpdateAlreadyInstalled(config, localUpdateState)", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Equals(config.PendingUpdateVersion, AppBranding.Version", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_Update_Handlers_Distinguish_Queueing_From_Processing_Override()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private async void InstallQueuedUpdateNowButton_OnClick", source, StringComparison.Ordinal);
        Assert.Contains("await InstallAvailableUpdateAsync(\"manual install\", manual: true, allowProcessingOverride: false, queueWhenProcessingBlocked: true, _lifetimeCts.Token);", source, StringComparison.Ordinal);
        Assert.Contains("await InstallAvailableUpdateAsync(\"manual install override\", manual: true, allowProcessingOverride: true, queueWhenProcessingBlocked: false, _lifetimeCts.Token);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Processing_Queue_Status_Updates_Can_Immediately_Retry_A_Queued_Install_When_Background_Work_Drains()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void ApplyProcessingQueueStatusSnapshot", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void ProcessingQueueStatusTimer_OnTick", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("TryInstallAvailableUpdateIfIdleAsync(\"processing idle\", _lifetimeCts.Token)", methodBlock, StringComparison.Ordinal);
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
