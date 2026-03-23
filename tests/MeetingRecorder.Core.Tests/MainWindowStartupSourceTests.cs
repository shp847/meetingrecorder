using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class MainWindowStartupSourceTests
{
    [Fact]
    public void OnLoaded_Queues_Startup_Warmup_Without_Awaiting_Heavy_Startup_Work()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var onLoadedStart = source.IndexOf("private async void OnLoaded", StringComparison.Ordinal);
        var onLoadedEnd = source.IndexOf("private void ScheduleStartupWarmup", StringComparison.Ordinal);
        var onLoadedBlock = source[onLoadedStart..onLoadedEnd];

        Assert.Contains("ScheduleStartupWarmup();", onLoadedBlock);
        Assert.DoesNotContain("await EnsureConfiguredModelPathResolvedAsync", onLoadedBlock);
        Assert.DoesNotContain("await RefreshMeetingListAsync(MeetingRefreshMode.Fast);", onLoadedBlock);
    }

    [Fact]
    public void OnLoaded_Waits_For_The_First_Render_Before_Starting_Startup_Warmup_And_Long_Lived_Timers()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var onLoadedStart = source.IndexOf("private async void OnLoaded", StringComparison.Ordinal);
        var onLoadedEnd = source.IndexOf("private void ScheduleStartupWarmup", StringComparison.Ordinal);
        var onLoadedBlock = source[onLoadedStart..onLoadedEnd];

        Assert.Contains("await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);", onLoadedBlock);
        Assert.DoesNotContain("_detectionTimer.Start();", onLoadedBlock);
        Assert.DoesNotContain("_updateTimer.Start();", onLoadedBlock);
    }

    [Fact]
    public void Meeting_Refresh_Supports_Fast_And_Full_Modes_With_Heavy_Work_Gated_To_Full_Mode()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("enum MeetingRefreshMode", source);
        Assert.Contains("RefreshMeetingListAsync(MeetingRefreshMode refreshMode", source);
        Assert.Contains("refreshMode == MeetingRefreshMode.Full", source);
        Assert.Contains("StartMeetingAttendeeBackfillRefresh", source);
        Assert.Contains("BuildMeetingInspectionsAsync", source);
        Assert.Contains("BuildVisibleMeetingCleanupRecommendationsAsync", source);
    }

    [Fact]
    public void Full_Refresh_Publishes_Baseline_Rows_Before_Queueing_Background_Enrichment()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var refreshStart = source.IndexOf("private async Task RefreshMeetingListAsync", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void UpdateMeetingsRefreshStateText", refreshStart, StringComparison.Ordinal);
        var refreshBlock = source[refreshStart..refreshEnd];

        var publishIndex = refreshBlock.IndexOf("MeetingsDataGrid.ItemsSource = _allMeetingRows;", StringComparison.Ordinal);
        var cleanupIndex = refreshBlock.IndexOf("StartMeetingCleanupRecommendationRefresh(", StringComparison.Ordinal);
        var attendeeIndex = refreshBlock.IndexOf("StartMeetingAttendeeBackfillRefresh(", StringComparison.Ordinal);

        Assert.True(publishIndex >= 0, "Expected MeetingsDataGrid.ItemsSource assignment in RefreshMeetingListAsync.");
        Assert.True(cleanupIndex > publishIndex, "Cleanup refresh should start only after baseline rows are published.");
        Assert.True(attendeeIndex > publishIndex, "Attendee backfill should start only after baseline rows are published.");
    }

    [Fact]
    public void Background_Enrichment_Does_Not_Use_The_Blocking_Meeting_Action_Busy_State()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var updateStateStart = source.IndexOf("private void UpdateMeetingActionState()", StringComparison.Ordinal);
        var updateStateEnd = source.IndexOf("private bool IsMeetingActionInProgress()", updateStateStart, StringComparison.Ordinal);
        var updateStateBlock = source[updateStateStart..updateStateEnd];
        var busyStart = updateStateEnd;
        var busyEnd = source.IndexOf("private bool HasPendingSpeakerLabelChanges()", busyStart, StringComparison.Ordinal);
        var busyBlock = source[busyStart..busyEnd];

        Assert.Contains("RefreshMeetingsButton.Content = Volatile.Read(ref _meetingBaselineRefreshOperations) > 0", updateStateBlock);
        Assert.Contains("return Volatile.Read(ref _meetingBaselineRefreshOperations) > 0", busyBlock);
        Assert.DoesNotContain("_meetingCleanupRefreshOperations", busyBlock);
        Assert.DoesNotContain("_meetingAttendeeBackfillOperations", busyBlock);
    }

    [Fact]
    public void Active_AutoStarted_Session_Reclassifies_Before_Auto_Stop_Logic_Runs()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var detectionStart = source.IndexOf("var activeAutoStartedSession = _recordingCoordinator.ActiveSession", StringComparison.Ordinal);
        var detectionEnd = source.IndexOf("var activeSessionForMicPrompt = _recordingCoordinator.ActiveSession;", detectionStart, StringComparison.Ordinal);
        var detectionBlock = source[detectionStart..detectionEnd];

        var reclassifyIndex = detectionBlock.IndexOf("TryReclassifyActiveAutoStartedSessionAsync(", StringComparison.Ordinal);
        var refreshIndex = detectionBlock.IndexOf("_autoRecordingContinuityPolicy.ShouldRefreshLastPositiveSignal(", StringComparison.Ordinal);
        var stopIndex = detectionBlock.IndexOf("AppendAutoStopStatus($\"Auto-stop triggered after", StringComparison.Ordinal);

        Assert.True(reclassifyIndex >= 0, "Expected active session reclassification hook in detection loop.");
        Assert.True(refreshIndex > reclassifyIndex, "Reclassification should happen before the positive-signal continuity check.");
        Assert.True(stopIndex > reclassifyIndex, "Reclassification should happen before the auto-stop branch.");
    }

    [Fact]
    public void Startup_Warmup_Still_Uses_A_Fast_Refresh_Before_Deferring_Full_Enrichment()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var warmupStart = source.IndexOf("private async Task RunStartupWarmupAsync", StringComparison.Ordinal);
        var warmupEnd = source.IndexOf("private void ScheduleDeferredStartupMaintenance", warmupStart, StringComparison.Ordinal);
        var warmupBlock = source[warmupStart..warmupEnd];

        Assert.Contains("await EnsureConfiguredModelPathResolvedAsync(\"startup\", _lifetimeCts.Token);", warmupBlock);
        Assert.Contains("await RefreshMeetingListAsync(MeetingRefreshMode.Fast);", warmupBlock);
        Assert.DoesNotContain("ScheduleDeferredMeetingsRefresh();", warmupBlock);
        Assert.Contains("ScheduleDeferredStartupMaintenance();", warmupBlock);
    }

    [Fact]
    public void Live_Config_Changes_Request_Meeting_Refresh_Only_When_Meeting_Data_Actually_Changed()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var handlerStart = source.IndexOf("private void LiveConfig_OnChanged", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private void ApplyConfigToUi", handlerStart, StringComparison.Ordinal);
        var handlerBlock = source[handlerStart..handlerEnd];

        Assert.Contains("RequestMeetingRefreshForCurrentContext(", handlerBlock);
        Assert.DoesNotContain("_ = RefreshMeetingListAsync();", handlerBlock);
    }

    [Fact]
    public void Meeting_Refresh_Request_Is_Context_Aware_And_Only_Queues_Full_Loads_When_Meetings_Is_Active()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var helperStart = source.IndexOf("private void RequestMeetingRefreshForCurrentContext", StringComparison.Ordinal);
        var helperEnd = source.IndexOf("private void ApplyConfigToUi", helperStart, StringComparison.Ordinal);
        var helperBlock = source[helperStart..helperEnd];

        Assert.Contains("ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)", helperBlock);
        Assert.Contains("refreshMode: MeetingRefreshMode.Fast", helperBlock);
        Assert.Contains("_hasCompletedFullMeetingsRefresh = false;", helperBlock);
        Assert.Contains("ScheduleDeferredMeetingsRefresh();", helperBlock);
    }

    [Fact]
    public void Installer_Shutdown_Path_Only_Closes_When_Main_Window_Explicitly_Allows_It()
    {
        var appPath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var mainWindowPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var appSource = File.ReadAllText(appPath);
        var mainWindowSource = File.ReadAllText(mainWindowPath);

        Assert.Contains("if (!meetingRecorderWindow.TryPrepareForInstallerShutdown())", appSource);
        Assert.Contains("internal bool TryPrepareForInstallerShutdown()", mainWindowSource);
        Assert.DoesNotContain("internal void PrepareForInstallerShutdown()", mainWindowSource);
    }

    [Fact]
    public void App_Startup_Uses_The_Installer_Relaunch_Coordinator_Before_Falling_Back_To_Second_Instance_Activation()
    {
        var appPath = GetPath("src", "MeetingRecorder.App", "App.xaml.cs");
        var coordinatorPath = GetPath("src", "MeetingRecorder.App", "Services", "InstallerRelaunchCoordinator.cs");

        var appSource = File.ReadAllText(appPath);
        var coordinatorSource = File.ReadAllText(coordinatorPath);

        Assert.Contains("new InstallerRelaunchCoordinator(", appSource);
        Assert.Contains("InstallerShutdownSignal.TrySignal", appSource);
        Assert.Contains("TryRecoverPrimaryInstance(", appSource);
        Assert.Contains("AppActivationSignal.TrySignalAndWaitForAcknowledgement(", appSource);
        Assert.Contains("TryConsumeInstallerRelaunchMarker()", appSource);
        Assert.Contains("installer-relaunch.flag", coordinatorSource);
    }

    [Fact]
    public void Stop_Recording_Path_Offloads_Recorder_Shutdown_Work_From_The_Ui_Thread()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private Task<string?> StopRecordingSessionAsync(", source);
        Assert.Contains("Task.Run(() => _recordingCoordinator.StopAsync(reason, cancellationToken), cancellationToken);", source);
        Assert.Contains("var manifestPath = await StopRecordingSessionAsync(reason, cancellationToken);", source);
    }

    [Fact]
    public void Shutdown_Path_Closes_Header_Surfaces_And_Explicitly_Shuts_Down_The_Application()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var shutdownStart = source.IndexOf("private async Task ShutdownAsync()", StringComparison.Ordinal);
        var shutdownEnd = source.IndexOf("private async Task StopCurrentRecordingAsync(", shutdownStart, StringComparison.Ordinal);
        var shutdownBlock = source[shutdownStart..shutdownEnd];

        Assert.Contains("CloseHeaderSurfaces();", shutdownBlock);
        Assert.Contains("application.Shutdown();", shutdownBlock);
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
