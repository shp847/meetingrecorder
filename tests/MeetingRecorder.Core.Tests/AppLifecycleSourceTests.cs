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
    public void Dispatcher_Unhandled_Exception_Path_Requests_Fatal_Shutdown()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var handlerStart = source.IndexOf("private void App_OnDispatcherUnhandledException", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private void CurrentDomain_OnUnhandledException", handlerStart, StringComparison.Ordinal);
        var handlerBlock = source[handlerStart..handlerEnd];

        Assert.Contains("_fatalUiShutdownRequested", source);
        Assert.Contains("will close", handlerBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequestFatalUiShutdown();", handlerBlock, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", handlerBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Activation_Monitor_Acknowledges_Only_After_A_Window_Is_Surfaced()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var monitorStart = source.IndexOf("private async Task MonitorActivationRequestsAsync", StringComparison.Ordinal);
        var monitorEnd = source.IndexOf("private async Task MonitorInstallerShutdownAsync", monitorStart, StringComparison.Ordinal);
        var monitorBlock = source[monitorStart..monitorEnd];
        var pendingIndex = monitorBlock.IndexOf("if (MainWindow is null)", StringComparison.Ordinal);
        var acknowledgeIndex = monitorBlock.IndexOf("TryAcknowledgeActivationRequest(activationSignal)", StringComparison.Ordinal);

        Assert.True(pendingIndex >= 0, "Expected activation to defer while no main window exists.");
        Assert.True(acknowledgeIndex >= 0, "Expected activation acknowledgement to use the visibility-backed helper.");
        Assert.True(pendingIndex < acknowledgeIndex, "Activation must defer before attempting acknowledgement.");
        Assert.DoesNotContain("activationSignal.TryAcknowledge();", monitorBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_Startup_Activation_Is_Acknowledged_After_Main_Window_Show()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var showIndex = source.IndexOf("mainWindow.Show();", StringComparison.Ordinal);
        var acknowledgeIndex = source.IndexOf("TryAcknowledgeActivationRequest(_activationSignal)", showIndex, StringComparison.Ordinal);
        var installerMonitorIndex = source.IndexOf("StartInstallerShutdownMonitor();", showIndex, StringComparison.Ordinal);

        Assert.True(showIndex >= 0, "Expected startup to show the main window.");
        Assert.True(acknowledgeIndex > showIndex, "Expected pending activation acknowledgement after showing the window.");
        Assert.True(
            acknowledgeIndex < installerMonitorIndex,
            "Pending activation should be acknowledged before startup continues with installer shutdown monitoring.");
    }

    [Fact]
    public void Main_Window_Fatal_Ui_Shutdown_Skips_Shutdown_Update_Check()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("internal void RequestFatalUiShutdown()", source, StringComparison.Ordinal);
        Assert.Contains("_skipShutdownUpdateCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (!_skipShutdownUpdateCheck)", source, StringComparison.Ordinal);
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

    [Fact]
    public void App_Startup_Repairs_Missing_Install_Provenance_Before_Creating_The_Live_Config()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(", source);
        Assert.Contains("Repaired missing install provenance", source);
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
