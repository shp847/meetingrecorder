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
    public void Active_Session_Reclassifies_Before_Auto_Stop_Logic_Runs_While_Auto_Stop_Uses_Meeting_Lifecycle_Management()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var detectionStart = source.IndexOf("var activeSession = _recordingCoordinator.ActiveSession;", StringComparison.Ordinal);
        var detectionEnd = source.IndexOf("var activeSessionForMicPrompt = _recordingCoordinator.ActiveSession;", detectionStart, StringComparison.Ordinal);
        var detectionBlock = source[detectionStart..detectionEnd];

        var reclassifyIndex = detectionBlock.IndexOf("TryReclassifyActiveSessionAsync(", StringComparison.Ordinal);
        var refreshIndex = detectionBlock.IndexOf("_autoRecordingContinuityPolicy.ShouldRefreshLastPositiveSignal(", StringComparison.Ordinal);
        var lifecycleManagedIndex = detectionBlock.IndexOf("var activeMeetingManagedSession = _recordingCoordinator.ActiveSession is { MeetingLifecycleManaged: true } reclassifiedSession", StringComparison.Ordinal);
        var stopIndex = detectionBlock.IndexOf("AppendAutoStopStatus($\"Auto-stop triggered after", StringComparison.Ordinal);

        Assert.True(reclassifyIndex >= 0, "Expected active session reclassification hook in detection loop.");
        Assert.True(refreshIndex > reclassifyIndex, "Reclassification should happen before the positive-signal continuity check.");
        Assert.True(lifecycleManagedIndex > reclassifyIndex, "Meeting lifecycle gating should be evaluated only after any reclassification completes.");
        Assert.True(stopIndex > reclassifyIndex, "Reclassification should happen before the auto-stop branch.");
    }

    [Fact]
    public void Active_Session_Reclassification_Uses_The_Current_Title_And_Resets_The_Title_Draft_To_The_New_Meeting()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task<bool> TryReclassifyActiveSessionAsync(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async void UpdateTimer_OnTick", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("activeSession.Manifest.DetectedTitle", methodBlock);
        Assert.Contains("_sessionTitleDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, decision.SessionTitle);", methodBlock);
    }

    [Fact]
    public void Audio_Graph_Timer_Also_Refreshes_The_Live_Recording_Elapsed_Time_Readout()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var tickStart = source.IndexOf("private void AudioGraphTimer_OnTick", StringComparison.Ordinal);
        var tickEnd = source.IndexOf("protected override void OnClosing", tickStart, StringComparison.Ordinal);
        var tickBlock = source[tickStart..tickEnd];

        Assert.Contains("UpdateCurrentRecordingElapsedText();", tickBlock);
        Assert.Contains("private void UpdateCurrentRecordingElapsedText()", source);
        Assert.Contains("activeSession.Manifest.StartedAtUtc", source);
        Assert.Contains("DateTimeOffset.UtcNow", source);
    }

    [Fact]
    public void Startup_Warmup_Requests_A_Deferred_Fast_Refresh_Instead_Of_Loading_The_Meetings_Catalog_Inline()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var warmupStart = source.IndexOf("private async Task RunStartupWarmupAsync", StringComparison.Ordinal);
        var warmupEnd = source.IndexOf("private void ScheduleDeferredStartupMaintenance", warmupStart, StringComparison.Ordinal);
        var warmupBlock = source[warmupStart..warmupEnd];

        Assert.Contains("await EnsureConfiguredModelPathResolvedAsync(\"startup\", _lifetimeCts.Token);", warmupBlock);
        Assert.Contains("RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, \"startup warmup\");", warmupBlock);
        Assert.DoesNotContain("await RefreshMeetingListAsync(MeetingRefreshMode.Fast);", warmupBlock);
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
        Assert.Contains("ShouldRefreshMeetingCatalogForConfigChange(", handlerBlock);
    }

    [Fact]
    public void Config_Save_And_Snapshot_Include_Background_Processing_Selections()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("InitializeConfigEditorSelectionControls()", source);
        Assert.Contains("ConfigBackgroundProcessingModeComboBox.SelectedValue", source);
        Assert.Contains("ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValue", source);
        Assert.Contains("BackgroundProcessingMode =", source);
        Assert.Contains("BackgroundSpeakerLabelingMode =", source);
    }

    [Fact]
    public void Meeting_Refresh_Request_Is_Context_Aware_And_Only_Queues_Full_Loads_When_Meetings_Is_Active()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var helperStart = source.IndexOf("private void RequestMeetingRefreshForCurrentContext", StringComparison.Ordinal);
        var helperEnd = source.IndexOf("private void ApplyConfigToUi", helperStart, StringComparison.Ordinal);
        var helperBlock = source[helperStart..helperEnd];

        Assert.Contains("_recordingCoordinator.IsRecording", helperBlock);
        Assert.Contains("MainWindowInteractionLogic.ShouldDeferMeetingRefresh(", helperBlock);
        Assert.Contains("ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)", helperBlock);
        Assert.Contains("_hasPendingMeetingsRefreshRequest = true;", helperBlock);
        Assert.Contains("SchedulePendingMeetingsRefreshIfReady();", helperBlock);
        Assert.Contains("_hasCompletedFullMeetingsRefresh = false;", helperBlock);
        Assert.Contains("private void SchedulePendingMeetingsRefreshIfReady()", source);
    }

    [Fact]
    public void Detection_Timer_Offloads_Window_Scanning_And_Skips_Overlapping_Detection_Cycles()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async void DetectionTimer_OnTick", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<bool> TryReclassifyActiveSessionAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("if (!TryBeginDetectionCycle())", methodBlock);
        Assert.Contains("DetectBestCandidateAsync(_lifetimeCts.Token)", methodBlock);
        Assert.DoesNotContain("? _meetingDetector.DetectBestCandidate()", methodBlock);
        Assert.Contains("Skipped overlapping detection scan because the previous scan is still running.", source);
        Assert.Contains("private bool TryBeginDetectionCycle()", source);
    }

    [Fact]
    public void Detection_Timer_Can_AutoStart_A_Sustained_Quiet_Teams_Meeting_Without_Waiting_For_Late_Audio()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async void DetectionTimer_OnTick", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<bool> TryReclassifyActiveSessionAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("var shouldAutoStartQuietTeamsMeeting = ShouldAutoStartQuietTeamsMeeting(decision, nowUtc);", methodBlock);
        Assert.Contains("decision.ShouldStart || shouldAutoStartQuietTeamsMeeting || shouldRecoverFromRecentAutoStop", methodBlock);
        Assert.Contains("private bool ShouldAutoStartQuietTeamsMeeting(", source);
    }

    [Fact]
    public void Detection_Timer_Uses_Manual_Stop_Suppression_Before_Auto_Starting_A_Detected_Meeting()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async void DetectionTimer_OnTick", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<bool> TryReclassifyActiveSessionAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("GetManualStopSuppressionDisposition(", methodBlock);
        Assert.Contains("var shouldSuppressManualRestart = manualStopSuppressionDisposition == ManualStopSuppressionDisposition.SuppressAutoStart;", methodBlock);
        Assert.Contains("if (!shouldSuppressManualRestart &&", methodBlock);
    }

    [Fact]
    public void Active_Session_Transition_Uses_The_Transition_Helper_And_Has_A_Managed_Rollover_Path()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task<bool> TryReclassifyActiveSessionAsync(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void UpdateCaptureStatusSurface()", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("MainWindowInteractionLogic.GetEligibleActiveSessionTransition(", methodBlock);
        Assert.Contains("ActiveSessionTransitionKind.RollOver", methodBlock);
        Assert.Contains("TryRollOverManagedSessionAsync(", methodBlock);
        Assert.Contains("private async Task<bool> TryRollOverManagedSessionAsync(", source);
    }

    [Fact]
    public void Main_Tab_Selection_Changes_Are_Ignored_Until_The_Window_Is_Fully_Initialized()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void MainTabControl_OnSelectionChanged", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void RequestMeetingRefreshForCurrentContext", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("if (!_isUiReady)", methodBlock);
        Assert.Contains("return;", methodBlock);
        Assert.Contains("UpdateAudioGraphTimerState();", methodBlock);
    }

    [Fact]
    public void Stop_Current_Recording_Queues_A_Deferred_Meetings_Refresh_Instead_Of_Awaiting_It_On_The_Foreground_Path()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task StopCurrentRecordingAsync(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private Task<string?> StopRecordingSessionAsync(", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.DoesNotContain("await RefreshMeetingListAsync();", methodBlock);
        Assert.Contains("RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, \"recording stop\");", methodBlock);
    }

    [Fact]
    public void Audio_Graph_Updates_Reuse_Buffers_And_Only_Run_While_Home_Is_Visible_Or_Recording_State_Is_Visible()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("_audioGraphCombinedLevels = new double[AudioGraphPointCount];", source);
        Assert.DoesNotContain("Enumerable.Repeat(0d, AudioGraphPointCount).ToArray()", source);
        Assert.Contains("private void UpdateAudioGraphTimerState()", source);
        Assert.DoesNotContain("_audioGraphTimer.Start();", source[source.IndexOf("private async void OnLoaded", StringComparison.Ordinal)..source.IndexOf("private void ScheduleStartupWarmup", StringComparison.Ordinal)]);
        Assert.Contains("!ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)", source);
    }

    [Fact]
    public void Main_Window_Subscribes_To_Processing_Queue_Status_Changes_And_Uses_A_Dedicated_Timer_For_Relative_Queue_Status_Refreshes()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private readonly DispatcherTimer _processingQueueStatusTimer;", source);
        Assert.Contains("Interval = TimeSpan.FromSeconds(1)", source);
        Assert.Contains("_processingQueue.StatusChanged += ProcessingQueue_OnStatusChanged;", source);
        Assert.Contains("private void ProcessingQueue_OnStatusChanged(", source);
        Assert.Contains("private void ProcessingQueueStatusTimer_OnTick(", source);
        Assert.Contains("private void UpdateProcessingQueueStatusTimerState()", source);
    }

    [Fact]
    public void Main_Window_Refreshes_The_Header_Chip_And_Meetings_Processing_Strip_From_The_Latest_Queue_Snapshot_Without_Hitting_Disk_On_Every_Tick()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("_latestProcessingQueueStatusSnapshot", source);
        Assert.Contains("UpdateProcessingQueueStatusUi();", source);
        Assert.Contains("BuildProcessingQueueHeaderState(", source);
        Assert.Contains("BuildMeetingsProcessingStripState(", source);
        Assert.DoesNotContain("LoadAsync(", source[source.IndexOf("private void ProcessingQueueStatusTimer_OnTick", StringComparison.Ordinal)..source.IndexOf("protected override void OnClosing", StringComparison.Ordinal)]);
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

    [Fact]
    public void Header_Shell_Status_Action_Slot_Uses_Hidden_Instead_Of_Collapsed_To_Avoid_Layout_Shifts()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var updateStart = source.IndexOf("var shellStatus = _shellStatusOverride ?? MainWindowInteractionLogic.BuildShellStatus(", StringComparison.Ordinal);
        var updateEnd = source.IndexOf("private static string BuildMicCaptureReadinessText(", updateStart, StringComparison.Ordinal);
        var updateBlock = source[updateStart..updateEnd];

        Assert.Contains("Visibility.Hidden", updateBlock);
        Assert.DoesNotContain("Visibility.Collapsed", updateBlock);
    }

    [Fact]
    public void Whisper_Model_Status_Changes_Recompute_Recording_Control_State()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var updateUiStart = source.IndexOf("private void UpdateUi(string status, string detection)", StringComparison.Ordinal);
        var updateUiEnd = source.IndexOf("private void UpdateDetectedAudioSourceSurface", updateUiStart, StringComparison.Ordinal);
        var updateUiBlock = source[updateUiStart..updateUiEnd];
        var applyStart = source.IndexOf("private void ApplyWhisperModelStatusDisplayState", StringComparison.Ordinal);
        var applyEnd = source.IndexOf("private void ApplyDiarizationAssetStatus", applyStart, StringComparison.Ordinal);
        var applyBlock = source[applyStart..applyEnd];

        Assert.Contains("UpdateRecordingControlState();", updateUiBlock);
        Assert.Contains("UpdateRecordingControlState();", applyBlock);
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
