using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class AppLifecycleSourceTests
{
    [Fact]
    public void Update_Handoff_Requests_A_Full_Application_Shutdown_Instead_Of_Closing_Only_The_Main_Window()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var handoffStart = source.IndexOf("private async Task InstallDownloadedUpdateCoreAsync", StringComparison.Ordinal);
        var handoffEnd = source.IndexOf("private void LaunchDownloadedUpdateInstaller", handoffStart, StringComparison.Ordinal);
        var handoffBlock = source[handoffStart..handoffEnd];

        Assert.Contains("RequestApplicationShutdownForInstallerHandoff();", handoffBlock);
        Assert.DoesNotContain("_allowClose = true;", handoffBlock);
        Assert.DoesNotContain("Close();", handoffBlock);
    }

    [Fact]
    public void App_OnExit_Does_Not_Block_The_Ui_Thread_Waiting_For_Signal_Monitor_Tasks()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var onExitStart = source.IndexOf("protected override void OnExit", StringComparison.Ordinal);
        var onExitEnd = source.IndexOf("private void StartInstallerShutdownMonitor", onExitStart, StringComparison.Ordinal);
        var onExitBlock = source[onExitStart..onExitEnd];

        Assert.Contains("ObserveCompletedMonitorTask(_activationMonitorTask);", onExitBlock);
        Assert.Contains("ObserveCompletedMonitorTask(_installerShutdownMonitorTask);", onExitBlock);
        Assert.DoesNotContain("_activationMonitorTask?.GetAwaiter().GetResult();", onExitBlock);
        Assert.DoesNotContain("_installerShutdownMonitorTask?.GetAwaiter().GetResult();", onExitBlock);
    }

    [Fact]
    public void App_Startup_Runs_The_Versioned_Published_Meeting_Repair_Against_The_App_Data_Root()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("PublishedMeetingRepairService.RepairKnownIssuesAsync(", source);
        Assert.Contains("AppDataPaths.GetAppRoot()", source);
        Assert.Contains("Published meeting repair completed:", source);
        Assert.Contains("Published meeting repair failed:", source);
    }

    [Fact]
    public void App_Startup_Shows_The_Main_Window_Before_Running_Long_Post_Window_Repair_Work()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        var showIndex = source.IndexOf("mainWindow.Show();", StringComparison.Ordinal);
        var transcriptMigrationIndex = source.IndexOf("TranscriptSidecarLayoutMigrationService.Migrate(", StringComparison.Ordinal);
        var publishedRepairIndex = source.IndexOf("PublishedMeetingRepairService.RepairKnownIssuesAsync(", StringComparison.Ordinal);

        Assert.True(showIndex >= 0, "Expected App startup to show the main window.");
        Assert.True(transcriptMigrationIndex >= 0, "Expected App startup to run transcript sidecar migration.");
        Assert.True(publishedRepairIndex >= 0, "Expected App startup to run published meeting repair.");
        Assert.True(
            showIndex < transcriptMigrationIndex,
            "The main window should be shown before transcript sidecar migration runs so startup cannot strand a headless primary instance.");
        Assert.True(
            showIndex < publishedRepairIndex,
            "The main window should be shown before published meeting repair runs so startup cannot strand a headless primary instance.");
    }

    [Fact]
    public void App_Startup_Offloads_Post_Window_Published_Meeting_Repair_Work_From_The_Dispatcher()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task RunPostWindowStartupMaintenanceAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("protected override void OnExit", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("publishedMeetingRepairResult = await Task.Run(", methodBlock);
        Assert.DoesNotContain("publishedMeetingRepairResult = await PublishedMeetingRepairService.RepairKnownIssuesAsync(", methodBlock);
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
