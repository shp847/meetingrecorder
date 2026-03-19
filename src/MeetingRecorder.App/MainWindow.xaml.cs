using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeetingRecorder.App;

public partial class MainWindow : Window
{
    private const int AudioGraphPointCount = 120;
    private const int RecentCaptureActivitySampleCount = 24;
    private static readonly Brush HealthyModelStatusBrush = CreateBrush(0x2E, 0x7D, 0x32);
    private static readonly Brush UnhealthyModelStatusBrush = CreateBrush(0xB3, 0x26, 0x1E);
    private static readonly TimeSpan ScheduledUpdateCheckCadence = TimeSpan.FromDays(1);

    private readonly LiveAppConfig _liveConfig;
    private readonly FileLogWriter _logger;
    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;
    private readonly MeetingOutputCatalogService _meetingOutputCatalogService;
    private readonly AutoStartRegistrationService _autoStartRegistrationService;
    private readonly AppUpdateService _appUpdateService;
    private readonly AppUpdateSchedulePolicy _appUpdateSchedulePolicy;
    private readonly AppUpdateInstallPolicy _appUpdateInstallPolicy;
    private readonly RecordingSessionCoordinator _recordingCoordinator;
    private readonly WindowMeetingDetector _meetingDetector;
    private readonly ProcessingQueueService _processingQueue;
    private readonly WhisperModelService _whisperModelService;
    private readonly WhisperModelCatalogService _whisperModelCatalogService;
    private readonly WhisperModelReleaseCatalogService _whisperModelReleaseCatalogService;
    private readonly DiarizationAssetCatalogService _diarizationAssetCatalogService;
    private readonly DiarizationAssetReleaseCatalogService _diarizationAssetReleaseCatalogService;
    private readonly ExternalAudioImportService _externalAudioImportService;
    private readonly AutoRecordingContinuityPolicy _autoRecordingContinuityPolicy;
    private readonly SessionTitleDraftTracker _sessionTitleDraftTracker;
    private readonly DispatcherTimer _detectionTimer;
    private readonly DispatcherTimer _audioGraphTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly SemaphoreSlim _updateOperationGate = new(1, 1);
    private readonly SemaphoreSlim _externalAudioImportGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private DateTimeOffset? _lastPositiveDetectionUtc;
    private RecentAutoStopContext? _recentAutoStopContext;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private bool _isRecordingTransitionInProgress;
    private int _meetingRefreshOperations;
    private bool _isRenamingMeeting;
    private bool _isRetryingMeeting;
    private bool _isApplyingSpeakerNames;
    private bool _isMergingMeetings;
    private bool _isSplittingMeeting;
    private bool _isSavingConfig;
    private int _updateCheckOperations;
    private bool _isPreparingUpdateInstall;
    private bool _isDownloadingUpdate;
    private bool _isRefreshingModelStatus;
    private int _remoteModelRefreshOperations;
    private bool _isActivatingModel;
    private bool _isDownloadingRemoteModel;
    private bool _isImportingModel;
    private double _modelDownloadProgressPercent;
    private bool _modelDownloadProgressIsIndeterminate = true;
    private int _remoteDiarizationRefreshOperations;
    private bool _isDownloadingRemoteDiarizationAsset;
    private bool _isImportingDiarizationAsset;
    private bool _isUpdateInstallInProgress;
    private bool _isUpdatingCurrentMeetingEditor;
    private bool _isUpdatingSplitMeetingControls;
    private bool _isUiReady;
    private int _meetingRefreshVersion;
    private string? _lastDetectionFingerprint;
    private string? _lastAutoStopFingerprint;
    private string? _splitMeetingSuggestionStem;
    private AppUpdateCheckResult? _lastUpdateCheckResult;
    private bool IsShutdownRequested => _shutdownInProgress || _lifetimeCts.IsCancellationRequested;

    public MainWindow(LiveAppConfig liveConfig, FileLogWriter logger)
    {
        InitializeComponent();
        _liveConfig = liveConfig;
        _logger = logger;

        _pathBuilder = new ArtifactPathBuilder();
        _manifestStore = new SessionManifestStore(_pathBuilder);
        _meetingOutputCatalogService = new MeetingOutputCatalogService(_pathBuilder);
        _autoStartRegistrationService = new AutoStartRegistrationService();
        _appUpdateService = new AppUpdateService();
        _appUpdateSchedulePolicy = new AppUpdateSchedulePolicy();
        _appUpdateInstallPolicy = new AppUpdateInstallPolicy();
        _recordingCoordinator = new RecordingSessionCoordinator(liveConfig, _manifestStore, _pathBuilder, logger);
        _meetingDetector = new WindowMeetingDetector(
            liveConfig,
            new MeetingDetectionEvaluator(),
            new SystemAudioActivityProbe(),
            new MeetingTitleEnricher(new OutlookCalendarMeetingTitleProvider()));
        _processingQueue = new ProcessingQueueService(liveConfig, _manifestStore, logger);
        _whisperModelService = new WhisperModelService(new WhisperNetModelDownloader());
        _whisperModelCatalogService = new WhisperModelCatalogService(_whisperModelService);
        _whisperModelReleaseCatalogService = new WhisperModelReleaseCatalogService(new HttpAppUpdateFeedClient(), _whisperModelService);
        _diarizationAssetCatalogService = new DiarizationAssetCatalogService();
        _diarizationAssetReleaseCatalogService = new DiarizationAssetReleaseCatalogService(new HttpAppUpdateFeedClient(), _diarizationAssetCatalogService);
        _externalAudioImportService = new ExternalAudioImportService(_pathBuilder);
        _autoRecordingContinuityPolicy = new AutoRecordingContinuityPolicy();
        _sessionTitleDraftTracker = new SessionTitleDraftTracker();
        _detectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _detectionTimer.Tick += DetectionTimer_OnTick;
        _audioGraphTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _audioGraphTimer.Tick += AudioGraphTimer_OnTick;
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _updateTimer.Tick += UpdateTimer_OnTick;
        _liveConfig.Changed += LiveConfig_OnChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
        RegisterConfigEditorChangeHandlers();

        Title = AppBranding.DisplayNameWithVersion;
        ProductHeadingTextBlock.Text = AppBranding.DisplayNameWithVersion;
        ApplyConfigToUi(_liveConfig.Current, "Initial config loaded.");
        UpdateCurrentMeetingEditor();
        UpdateSelectedMeetingEditor(null);
        ApplyUpdateCheckResult(null, manual: false);
        UpdateUi("Ready to record.", MainWindowInteractionLogic.BuildDetectionSummary(null));
        UpdateModelActionButtons();
        UpdateDiarizationActionButtons();
        UpdateConfigActionState();
        UpdateAudioCaptureGraph();
        UpdateDashboardReadiness();
        _isUiReady = true;
        UpdateMeetingActionState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsShutdownRequested)
            {
                return;
            }

            AppendActivity("App started.");
            TrySyncLaunchOnLoginSetting(_liveConfig.Current, "startup");
            await EnsureConfiguredModelPathResolvedAsync("startup", _lifetimeCts.Token);
            if (IsShutdownRequested)
            {
                return;
            }

            _detectionTimer.Start();
            _audioGraphTimer.Start();
            _updateTimer.Start();
            await RefreshMeetingListAsync();
            await _processingQueue.ResumePendingSessionsAsync(_lifetimeCts.Token);
            await RunExternalAudioImportCycleAsync("startup", _lifetimeCts.Token);
            await RunScheduledUpdateCycleAsync("startup", _lifetimeCts.Token);
            _ = RefreshRemoteModelCatalogAsync(manual: false, _lifetimeCts.Token);
            _ = RefreshRemoteDiarizationAssetCatalogAsync(manual: false, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendActivity("Startup processing was canceled during shutdown.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Startup error: {exception.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        _liveConfig.Changed -= LiveConfig_OnChanged;
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingTransitionInProgress)
        {
            return;
        }

        _isRecordingTransitionInProgress = true;
        UpdateUi("Starting recording...", DetectionTextBlock.Text);

        try
        {
            _recentAutoStopContext = null;
            await _recordingCoordinator.StartAsync(
                MeetingPlatform.Manual,
                $"Manual session {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
                Array.Empty<DetectionSignal>(),
                autoStarted: false);

            UpdateCurrentMeetingEditor();
            UpdateUi("Recording in progress.", "Manual recording started.");
            UpdateAudioCaptureGraph();
            AppendActivity("Manual recording started.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to start recording: {exception.Message}");
            UpdateUi("Unable to start recording.", DetectionTextBlock.Text);
        }
        finally
        {
            _isRecordingTransitionInProgress = false;
            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingTransitionInProgress)
        {
            return;
        }

        _isRecordingTransitionInProgress = true;
        UpdateUi("Stopping recording...", DetectionTextBlock.Text);

        _recentAutoStopContext = null;
        try
        {
            await StopCurrentRecordingAsync("Manual stop requested.");
        }
        finally
        {
            _isRecordingTransitionInProgress = false;
            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
        }
    }

    private void CurrentMeetingTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCurrentMeetingEditor)
        {
            return;
        }

        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is not null)
        {
            _sessionTitleDraftTracker.UpdateDraft(
                activeSession.Manifest.SessionId,
                activeSession.Manifest.DetectedTitle,
                CurrentMeetingTitleTextBox.Text);
        }

        UpdateCurrentMeetingTitleStatus();
    }

    private void DashboardPrimaryActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DashboardPrimaryActionButton.Tag is not DashboardPrimaryActionTarget target)
        {
            return;
        }

        switch (target)
        {
            case DashboardPrimaryActionTarget.Models:
                MainTabControl.SelectedItem = ModelsTabItem;
                break;
            case DashboardPrimaryActionTarget.Updates:
                MainTabControl.SelectedItem = UpdatesTabItem;
                break;
            case DashboardPrimaryActionTarget.Config:
                MainTabControl.SelectedItem = ConfigTabItem;
                break;
            case DashboardPrimaryActionTarget.None:
            default:
                break;
        }
    }

    private void OpenUpdatesTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedItem = UpdatesTabItem;
    }

    private async void DetectionTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (IsShutdownRequested)
            {
                return;
            }

            await TryReloadConfigAsync();
            await EnsureConfiguredModelPathResolvedAsync("runtime check", _lifetimeCts.Token);
            if (IsShutdownRequested)
            {
                return;
            }

            var decision = _meetingDetector.DetectBestCandidate();
            var nowUtc = DateTimeOffset.UtcNow;
            LogDetectionChange(decision);
            DetectionTextBlock.Text = MainWindowInteractionLogic.BuildDetectionSummary(decision);

            if (!_recordingCoordinator.IsRecording &&
                _liveConfig.Current.AutoDetectEnabled &&
                decision is not null)
            {
                var shouldRecoverFromRecentAutoStop = _autoRecordingContinuityPolicy.ShouldRecoverFromRecentAutoStop(
                    decision,
                    _recentAutoStopContext,
                    nowUtc);
                if (decision.ShouldStart || shouldRecoverFromRecentAutoStop)
                {
                    await _recordingCoordinator.StartAsync(
                        decision.Platform,
                        decision.SessionTitle,
                        decision.Signals,
                        autoStarted: true);
                    _lastPositiveDetectionUtc = nowUtc;
                    _recentAutoStopContext = null;
                    _lastAutoStopFingerprint = null;
                    UpdateCurrentMeetingEditor();
                    UpdateUi("Recording in progress.", DetectionTextBlock.Text);
                    AppendActivity(
                        shouldRecoverFromRecentAutoStop && !decision.ShouldStart
                            ? $"Resumed recording for '{decision.SessionTitle}' after a recent auto-stop."
                            : $"Auto-started recording for '{decision.SessionTitle}'.");
                }
            }

            var activeAutoStartedSession = _recordingCoordinator.ActiveSession is { AutoStarted: true } activeSession
                ? activeSession
                : null;
            if (activeAutoStartedSession is not null &&
                _autoRecordingContinuityPolicy.ShouldRefreshLastPositiveSignal(
                    decision,
                    activeAutoStartedSession.Manifest.Platform,
                    activeAutoStartedSession.Manifest.DetectedTitle,
                    HasRecentLoopbackActivity(activeAutoStartedSession),
                    HasRecentMicrophoneActivity(activeAutoStartedSession)))
            {
                if (decision is { ShouldKeepRecording: true })
                {
                    _lastAutoStopFingerprint = null;
                }
                else
                {
                    AppendAutoStopStatus("Auto-stop deferred because recent captured audio activity was still detected.");
                }

                _lastPositiveDetectionUtc = nowUtc;
            }
            else if (_recordingCoordinator.IsRecording &&
                     activeAutoStartedSession is not null)
            {
                var activePlatform = activeAutoStartedSession.Manifest.Platform;
                var elapsedSincePositive = _lastPositiveDetectionUtc.HasValue
                    ? nowUtc - _lastPositiveDetectionUtc.Value
                    : (TimeSpan?)null;
                var lastPositiveUtc = _lastPositiveDetectionUtc;

                if (elapsedSincePositive.HasValue && lastPositiveUtc.HasValue)
                {
                    var stopTimeout = _autoRecordingContinuityPolicy.GetAutoStopTimeout(
                        decision,
                        activePlatform,
                        TimeSpan.FromSeconds(_liveConfig.Current.MeetingStopTimeoutSeconds));
                    var remaining = stopTimeout - elapsedSincePositive.Value;
                    if (remaining <= TimeSpan.Zero)
                    {
                        _recentAutoStopContext = new RecentAutoStopContext(activePlatform, nowUtc);
                        AppendAutoStopStatus($"Auto-stop triggered after {Math.Ceiling(stopTimeout.TotalSeconds)} seconds without a strong meeting signal.");
                        await StopCurrentRecordingAsync("Meeting signals expired after the configured timeout.");
                        _lastPositiveDetectionUtc = null;
                        _lastAutoStopFingerprint = null;
                    }
                    else
                    {
                        AppendAutoStopStatus(
                            $"Auto-stop countdown active: {Math.Ceiling(remaining.TotalSeconds)} seconds remaining. Last strong meeting signal was at {lastPositiveUtc.Value:O}.");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppendActivity($"Detection error: {exception.Message}");
        }
    }

    private async void UpdateTimer_OnTick(object? sender, EventArgs e)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        UpdateUpdateActionButtons();
        await RunExternalAudioImportCycleAsync("background timer", _lifetimeCts.Token);
        await RunScheduledUpdateCycleAsync("background timer", _lifetimeCts.Token);
    }

    private void AudioGraphTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateAudioCaptureGraph();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        if (!_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        _ = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            AppendActivity("Application shutdown requested.");

            if (_recordingCoordinator.IsRecording)
            {
                _recentAutoStopContext = null;
                await StopCurrentRecordingAsync("Application closing.", enqueueForProcessing: false, CancellationToken.None);
                if (_recordingCoordinator.IsRecording)
                {
                    var manifestPath = await _recordingCoordinator.StopAsync(
                        "Application closing after a forced recorder cleanup.",
                        CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(manifestPath))
                    {
                        AppendActivity($"Forced recording cleanup completed during shutdown. Deferred processing until next launch: {manifestPath}");
                    }
                }
            }

            await _processingQueue.StopAsync(CancellationToken.None);
            AppendActivity("Application shutdown completed.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Shutdown error: {exception.Message}");
        }
        finally
        {
            _allowClose = true;
            await Dispatcher.InvokeAsync(Close);
        }
    }

    private async Task StopCurrentRecordingAsync(
        string reason,
        bool enqueueForProcessing = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await ApplyPendingCurrentTitleAsync(cancellationToken);
            var manifestPath = await _recordingCoordinator.StopAsync(reason, cancellationToken);
            var processingQueued = false;
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                if (enqueueForProcessing)
                {
                    await _processingQueue.EnqueueAsync(manifestPath, cancellationToken);
                    processingQueued = true;
                    AppendActivity($"Queued session for processing: {manifestPath}");
                }
                else
                {
                    AppendActivity($"Deferred processing until next launch: {manifestPath}");
                }
            }

            await RefreshMeetingListAsync();
            UpdateCurrentMeetingEditor();
            UpdateUi(
                MainWindowInteractionLogic.BuildRecordingStoppedMessage(processingQueued),
                "No meeting detected.");
            UpdateAudioCaptureGraph();
            _ = TryInstallAvailableUpdateIfIdleAsync("recording stop", _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendActivity($"Stop operation canceled. Reason: {reason}");
            UpdateUi("Stopping canceled.", DetectionTextBlock.Text);
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to stop recording: {exception.Message}");
            UpdateUi("Unable to stop recording cleanly.", DetectionTextBlock.Text);
        }
    }

    private async void RefreshMeetingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshMeetingListAsync();
        AppendActivity("Refreshed published meeting list.");
    }

    private async void RenameSelectedMeetingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow selectedMeeting)
        {
            AppendActivity("Select a published meeting before renaming it.");
            return;
        }

        _isRenamingMeeting = true;
        UpdateMeetingActionState();
        SelectedMeetingStatusTextBlock.Text = $"Renaming '{selectedMeeting.Title}'...";

        try
        {
            var renamed = await _meetingOutputCatalogService.RenameMeetingAsync(
                _liveConfig.Current.AudioOutputDir,
                _liveConfig.Current.TranscriptOutputDir,
                selectedMeeting.Source.Stem,
                SelectedMeetingTitleTextBox.Text,
                _liveConfig.Current.WorkDir,
                _lifetimeCts.Token);

            await RefreshMeetingListAsync(renamed.Stem);
            AppendActivity($"Renamed published meeting '{selectedMeeting.Title}' to '{renamed.Title}'.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to rename published meeting: {exception.Message}");
        }
        finally
        {
            _isRenamingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private void MeetingsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMeetingEditor(MeetingsDataGrid.SelectedItem as MeetingListRow);
        UpdateMergeMeetingsEditor();
    }

    private void SelectedMeetingTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        UpdateMeetingActionState();
    }

    private void MergeSelectedMeetingsTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        UpdateMeetingActionState();
    }

    private void SplitSelectedMeetingPointTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        if (_isUpdatingSplitMeetingControls)
        {
            UpdateMeetingActionState();
            return;
        }

        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 1 &&
            selectedMeetings[0].Source.Duration is { } duration)
        {
            if (MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                    SplitSelectedMeetingPointTextBox.Text,
                    duration,
                    out var splitPoint,
                    out var errorMessage))
            {
                ApplySplitMeetingSelection(duration, splitPoint);
            }
            else if (!string.IsNullOrWhiteSpace(SplitSelectedMeetingPointTextBox.Text))
            {
                SplitSelectedMeetingStatusTextBlock.Text = errorMessage;
                SplitSelectedMeetingPreviewTextBlock.Text = "Enter a valid split point to preview part lengths.";
            }
        }

        UpdateMeetingActionState();
    }

    private void SplitSelectedMeetingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiReady)
        {
            return;
        }

        if (_isUpdatingSplitMeetingControls)
        {
            return;
        }

        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1 || selectedMeetings[0].Source.Duration is not { } duration)
        {
            UpdateMeetingActionState();
            return;
        }

        var splitPoint = TimeSpan.FromSeconds(Math.Round(SplitSelectedMeetingSlider.Value));
        ApplySplitMeetingSelection(duration, splitPoint);
        UpdateMeetingActionState();
    }

    private async void ApplySpeakerNamesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow selectedMeeting)
        {
            AppendActivity("Select a meeting before applying speaker names.");
            return;
        }

        var labelRows = SpeakerLabelsEditorDataGrid.ItemsSource as IEnumerable<SpeakerLabelEditorRow>;
        if (labelRows is null)
        {
            SpeakerNamesStatusTextBlock.Text = "No speaker labels are loaded for the selected meeting.";
            return;
        }

        var labelMap = MainWindowInteractionLogic.BuildSpeakerLabelMap(
            labelRows.Select(row => new SpeakerLabelDraft(row.OriginalLabel, row.EditedLabel)));

        if (labelMap.Count == 0)
        {
            SpeakerNamesStatusTextBlock.Text = "No speaker name changes are pending.";
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        SpeakerNamesStatusTextBlock.Text = "Applying speaker name changes...";

        try
        {
            await _meetingOutputCatalogService.RenameSpeakerLabelsAsync(
                selectedMeeting.Source,
                labelMap,
                _lifetimeCts.Token);

            UpdateSelectedMeetingEditor(selectedMeeting);
            SpeakerNamesStatusTextBlock.Text = $"Updated {labelMap.Count} speaker label(s) for '{selectedMeeting.Title}'.";
            AppendActivity($"Updated speaker labels for '{selectedMeeting.Title}'.");
        }
        catch (Exception exception)
        {
            SpeakerNamesStatusTextBlock.Text = $"Failed to update speaker labels: {exception.Message}";
            AppendActivity($"Failed to update speaker labels: {exception.Message}");
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private async void RetrySelectedMeetingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow selectedMeeting)
        {
            AppendActivity("Select a meeting before re-generating its transcript.");
            return;
        }

        if (!selectedMeeting.CanRegenerateTranscript)
        {
            AppendActivity("The selected meeting does not have usable source audio for transcript re-generation.");
            return;
        }

        _isRetryingMeeting = true;
        UpdateMeetingActionState();
        SelectedMeetingStatusTextBlock.Text = $"Queueing transcript re-generation for '{selectedMeeting.Title}'...";

        try
        {
            var manifestPath = selectedMeeting.Source.ManifestPath;
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                manifestPath = await _meetingOutputCatalogService.CreateSyntheticManifestForPublishedMeetingAsync(
                    selectedMeeting.Source,
                    _liveConfig.Current.WorkDir,
                    _lifetimeCts.Token);
                AppendActivity($"Created a synthetic work manifest for '{selectedMeeting.Title}' from the published audio file.");
            }

            var manifest = await _manifestStore.LoadAsync(manifestPath, _lifetimeCts.Token);
            var now = DateTimeOffset.UtcNow;
            var queuedManifest = manifest with
            {
                State = SessionState.Queued,
                ErrorSummary = null,
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Queued, now, "Queued to re-generate the transcript."),
                DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
            };

            await _manifestStore.SaveAsync(queuedManifest, manifestPath, _lifetimeCts.Token);
            await RefreshMeetingListAsync(selectedMeeting.Source.Stem);
            await _processingQueue.EnqueueAsync(manifestPath, _lifetimeCts.Token);
            AppendActivity($"Re-generated transcript requested for '{selectedMeeting.Title}'.");
            await RefreshMeetingListAsync(selectedMeeting.Source.Stem);
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to re-generate transcript: {exception.Message}");
        }
        finally
        {
            _isRetryingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private async void MergeSelectedMeetingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length < 2)
        {
            MergeSelectedMeetingsStatusTextBlock.Text = "Select two or more meetings before trying to merge them.";
            AppendActivity("Select at least two meetings before merging.");
            return;
        }

        _isMergingMeetings = true;
        UpdateMeetingActionState();
        MergeSelectedMeetingsStatusTextBlock.Text = $"Creating a merged meeting from {selectedMeetings.Length} recordings...";

        try
        {
            var mergeResult = await _meetingOutputCatalogService.MergeMeetingsAsync(
                selectedMeetings.Select(row => row.Source).ToArray(),
                MergeSelectedMeetingsTitleTextBox.Text,
                _liveConfig.Current.WorkDir,
                _lifetimeCts.Token);

            await _processingQueue.EnqueueAsync(mergeResult.ManifestPath, _lifetimeCts.Token);
            MergeSelectedMeetingsStatusTextBlock.Text =
                $"Queued merged meeting '{mergeResult.Title}'. It will appear in the list after processing finishes.";
            AppendActivity($"Queued merged meeting '{mergeResult.Title}' from {selectedMeetings.Length} recordings.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MergeSelectedMeetingsStatusTextBlock.Text = $"Merge failed: {exception.Message}";
            AppendActivity($"Failed to merge selected meetings: {exception.Message}");
        }
        finally
        {
            _isMergingMeetings = false;
            UpdateMeetingActionState();
        }
    }

    private async void SplitSelectedMeetingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            SplitSelectedMeetingStatusTextBlock.Text = "Select exactly one meeting before trying to split it.";
            AppendActivity("Select exactly one meeting before splitting.");
            return;
        }

        var selectedMeeting = selectedMeetings[0];
        if (!MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                SplitSelectedMeetingPointTextBox.Text,
                selectedMeeting.Source.Duration,
                out var splitPoint,
                out var errorMessage))
        {
            SplitSelectedMeetingStatusTextBlock.Text = errorMessage;
            return;
        }

        _isSplittingMeeting = true;
        UpdateMeetingActionState();
        SplitSelectedMeetingStatusTextBlock.Text = $"Splitting '{selectedMeeting.Title}' at {MainWindowInteractionLogic.FormatMeetingSplitPoint(splitPoint)}...";

        try
        {
            var splitResult = await _meetingOutputCatalogService.SplitMeetingAsync(
                selectedMeeting.Source,
                splitPoint,
                _liveConfig.Current.WorkDir,
                _lifetimeCts.Token);

            await _processingQueue.EnqueueAsync(splitResult.FirstManifestPath, _lifetimeCts.Token);
            await _processingQueue.EnqueueAsync(splitResult.SecondManifestPath, _lifetimeCts.Token);
            SplitSelectedMeetingStatusTextBlock.Text =
                $"Queued '{splitResult.FirstTitle}' and '{splitResult.SecondTitle}'. They will appear after processing finishes.";
            AppendActivity(
                $"Queued split meetings '{splitResult.FirstTitle}' and '{splitResult.SecondTitle}' from '{selectedMeeting.Title}'.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            SplitSelectedMeetingStatusTextBlock.Text = $"Split failed: {exception.Message}";
            AppendActivity($"Failed to split selected meeting: {exception.Message}");
        }
        finally
        {
            _isSplittingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private async void SaveConfigButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSavingConfig = true;
        UpdateConfigActionState();
        ConfigSaveStatusTextBlock.Text = "Saving config...";

        try
        {
            if (!double.TryParse(ConfigAutoDetectThresholdTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
            {
                throw new InvalidOperationException("Auto-detect audio threshold must be a number.");
            }

            if (!int.TryParse(ConfigMeetingStopTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stopTimeoutSeconds))
            {
                throw new InvalidOperationException("Meeting stop timeout must be a whole number of seconds.");
            }

            await _liveConfig.SaveAsync(new AppConfig
            {
                AudioOutputDir = ConfigAudioOutputDirTextBox.Text.Trim(),
                TranscriptOutputDir = ConfigTranscriptOutputDirTextBox.Text.Trim(),
                WorkDir = ConfigWorkDirTextBox.Text.Trim(),
                ModelCacheDir = ConfigModelCacheDirTextBox.Text.Trim(),
                TranscriptionModelPath = ConfigTranscriptionModelPathTextBox.Text.Trim(),
                DiarizationAssetPath = ConfigDiarizationAssetPathTextBox.Text.Trim(),
                MicCaptureEnabled = ConfigMicCaptureCheckBox.IsChecked == true,
                LaunchOnLoginEnabled = ConfigLaunchOnLoginCheckBox.IsChecked == true,
                AutoDetectEnabled = ConfigAutoDetectCheckBox.IsChecked == true,
                CalendarTitleFallbackEnabled = ConfigCalendarTitleFallbackCheckBox.IsChecked == true,
                UpdateCheckEnabled = ConfigUpdateCheckEnabledCheckBox.IsChecked == true,
                AutoInstallUpdatesEnabled = ConfigAutoInstallUpdatesCheckBox.IsChecked == true,
                UpdateFeedUrl = ConfigUpdateFeedUrlTextBox.Text.Trim(),
                LastUpdateCheckUtc = _liveConfig.Current.LastUpdateCheckUtc,
                InstalledReleaseVersion = _liveConfig.Current.InstalledReleaseVersion,
                InstalledReleasePublishedAtUtc = _liveConfig.Current.InstalledReleasePublishedAtUtc,
                InstalledReleaseAssetSizeBytes = _liveConfig.Current.InstalledReleaseAssetSizeBytes,
                PendingUpdateZipPath = _liveConfig.Current.PendingUpdateZipPath,
                PendingUpdateVersion = _liveConfig.Current.PendingUpdateVersion,
                PendingUpdatePublishedAtUtc = _liveConfig.Current.PendingUpdatePublishedAtUtc,
                PendingUpdateAssetSizeBytes = _liveConfig.Current.PendingUpdateAssetSizeBytes,
                AutoDetectAudioPeakThreshold = threshold,
                MeetingStopTimeoutSeconds = stopTimeoutSeconds,
            }, _lifetimeCts.Token);

            ConfigSaveStatusTextBlock.Text = "Config saved and applied to the running app.";
        }
        catch (Exception exception)
        {
            ConfigSaveStatusTextBlock.Text = $"Save failed: {exception.Message}";
            AppendActivity($"Config save failed: {exception.Message}");
        }
        finally
        {
            _isSavingConfig = false;
            UpdateConfigActionState();
        }
    }

    private void AudioFolderLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.Current.AudioOutputDir);
    }

    private void TranscriptFolderLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.Current.TranscriptOutputDir);
    }

    private void ConfigPathLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.ConfigPath);
    }

    private void ModelPathLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenContainingFolder(_liveConfig.Current.TranscriptionModelPath);
    }

    private void DiarizationAssetPathLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.Current.DiarizationAssetPath);
    }

    private async void CheckForUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync("manual check", manual: true, _lifetimeCts.Token);
    }

    private async void InstallLatestUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        await InstallAvailableUpdateAsync("manual install", manual: true, _lifetimeCts.Token);
    }

    private async void DownloadLatestUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var result = await EnsureUpdateAvailableAsync("manual download", _lifetimeCts.Token);
        if (result is null ||
            string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            UpdateCheckStatusTextBlock.Text = "No downloadable update is currently available.";
            return;
        }

        _isDownloadingUpdate = true;
        UpdateUpdateActionButtons();
        UpdateCheckStatusTextBlock.Text = $"Downloading update {FormatVersionLabel(result.LatestVersion)}...";

        try
        {
            var downloadedPath = await _appUpdateService.DownloadUpdateAsync(
                result.DownloadUrl,
                result.LatestVersion,
                _lifetimeCts.Token);
            UpdateCheckStatusTextBlock.Text = $"Downloaded {FormatVersionLabel(result.LatestVersion)} to '{downloadedPath}'.";
            AppendActivity($"Downloaded update {FormatVersionLabel(result.LatestVersion)} to '{downloadedPath}'.");
            OpenContainingFolder(downloadedPath);
        }
        catch (Exception exception)
        {
            UpdateCheckStatusTextBlock.Text = $"Update download failed: {exception.Message}";
            AppendActivity($"Update download failed: {exception.Message}");
        }
        finally
        {
            _isDownloadingUpdate = false;
            ApplyUpdateCheckResult(_lastUpdateCheckResult, manual: true);
        }
    }

    private void OpenLatestReleasePageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var releasePageUrl = _lastUpdateCheckResult?.ReleasePageUrl;
        if (string.IsNullOrWhiteSpace(releasePageUrl))
        {
            releasePageUrl = AppBranding.DefaultReleasePageUrl;
        }

        if (string.IsNullOrWhiteSpace(releasePageUrl))
        {
            UpdateCheckStatusTextBlock.Text = "No release page URL is available for the current update source.";
            return;
        }

        OpenExternalUrl(releasePageUrl);
    }

    private void WhisperCppRepoLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl("https://huggingface.co/ggerganov/whisper.cpp/tree/main");
    }

    private void WhisperBaseModelLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true");
    }

    private void WhisperSmallModelLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin?download=true");
    }

    private async void RefreshModelStatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isRefreshingModelStatus = true;
        UpdateModelActionButtons();
        ModelActionStatusTextBlock.Text = "Refreshing model status and GitHub model list...";
        DiarizationActionStatusTextBlock.Text = "Refreshing diarization status and GitHub asset list...";

        try
        {
            await EnsureConfiguredModelPathResolvedAsync("model refresh", _lifetimeCts.Token);
            RefreshWhisperModelStatus();
            await RefreshRemoteModelCatalogAsync(manual: true, _lifetimeCts.Token);
            RefreshDiarizationAssetStatus();
            await RefreshRemoteDiarizationAssetCatalogAsync(manual: true, _lifetimeCts.Token);
            ModelActionStatusTextBlock.Text = "Model status, local models, and GitHub model list refreshed.";
            DiarizationActionStatusTextBlock.Text = "Diarization status and GitHub asset list refreshed.";
        }
        finally
        {
            _isRefreshingModelStatus = false;
            UpdateModelActionButtons();
            UpdateDiarizationActionButtons();
        }
    }

    private void AvailableModelsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedModelEditor(AvailableModelsComboBox.SelectedItem as WhisperModelListRow);
    }

    private void AvailableRemoteModelsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedRemoteModelEditor(AvailableRemoteModelsComboBox.SelectedItem as WhisperRemoteModelListRow);
    }

    private void AvailableRemoteDiarizationAssetsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedRemoteDiarizationAssetEditor(AvailableRemoteDiarizationAssetsComboBox.SelectedItem as DiarizationRemoteAssetListRow);
    }

    private async void ActivateSelectedModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AvailableModelsComboBox.SelectedItem is not WhisperModelListRow selectedModel)
        {
            ModelActionStatusTextBlock.Text = "Select a model before trying to activate it.";
            return;
        }

        if (selectedModel.Source.IsConfigured)
        {
            ModelActionStatusTextBlock.Text = $"'{selectedModel.Source.FileName}' is already the active model.";
            return;
        }

        if (selectedModel.Source.Status.Kind != WhisperModelStatusKind.Valid)
        {
            ModelActionStatusTextBlock.Text = $"'{selectedModel.Source.FileName}' is not valid yet and cannot be activated.";
            return;
        }

        _isActivatingModel = true;
        UpdateModelActionButtons();
        ModelActionStatusTextBlock.Text = $"Switching to '{selectedModel.Source.FileName}'...";

        try
        {
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                TranscriptionModelPath = selectedModel.Source.ModelPath,
            }, _lifetimeCts.Token);

            ModelActionStatusTextBlock.Text = $"Active model updated to '{selectedModel.Source.FileName}'.";
            AppendActivity($"Active Whisper model changed to '{selectedModel.Source.ModelPath}'.");
        }
        catch (Exception exception)
        {
            ModelActionStatusTextBlock.Text = $"Failed to activate model: {exception.Message}";
            AppendActivity($"Failed to activate Whisper model: {exception.Message}");
        }
        finally
        {
            _isActivatingModel = false;
            UpdateModelActionButtons();
        }
    }

    private async void RefreshRemoteModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ModelActionStatusTextBlock.Text = "Refreshing GitHub model list...";
        await RefreshRemoteModelCatalogAsync(manual: true, _lifetimeCts.Token);
    }

    private async void RefreshRemoteDiarizationAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        DiarizationActionStatusTextBlock.Text = "Refreshing GitHub diarization assets...";
        await RefreshRemoteDiarizationAssetCatalogAsync(manual: true, _lifetimeCts.Token);
    }

    private async void DownloadSelectedRemoteModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AvailableRemoteModelsComboBox.SelectedItem is not WhisperRemoteModelListRow selectedRemoteModel)
        {
            ModelActionStatusTextBlock.Text = "Select a downloadable GitHub model before starting the download.";
            return;
        }

        _isDownloadingRemoteModel = true;
        ResetModelDownloadProgress();
        UpdateModelActionButtons();
        ModelActionStatusTextBlock.Text = $"Downloading '{selectedRemoteModel.Source.FileName}' from GitHub...";

        try
        {
            var progress = new Progress<FileDownloadProgress>(update =>
                ReportRemoteModelDownloadProgress(selectedRemoteModel.Source, update));
            var imported = await _whisperModelReleaseCatalogService.DownloadRemoteModelIntoManagedDirectoryAsync(
                selectedRemoteModel.Source,
                _liveConfig.Current.ModelCacheDir,
                progress,
                _lifetimeCts.Token);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                TranscriptionModelPath = imported.ModelPath,
            }, _lifetimeCts.Token);
            RefreshWhisperModelStatus();
            await RefreshRemoteModelCatalogAsync(manual: false, _lifetimeCts.Token);
            ModelActionStatusTextBlock.Text =
                $"Downloaded '{selectedRemoteModel.Source.FileName}' ({FormatBytes(imported.Status.FileSizeBytes)}) from GitHub and set it as the active model.";
            AppendActivity(
                $"Downloaded Whisper model '{selectedRemoteModel.Source.FileName}' from GitHub to '{imported.ModelPath}' and set it as active.");
        }
        catch (Exception exception)
        {
            ModelActionStatusTextBlock.Text = $"Download failed: {exception.Message}";
            AppendActivity($"Whisper model download failed: {exception.Message}");
        }
        finally
        {
            _isDownloadingRemoteModel = false;
            ResetModelDownloadProgress();
            UpdateModelActionButtons();
        }
    }

    private async void ImportWhisperModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a Whisper ggml model file",
                Filter = "Whisper model (*.bin)|*.bin|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _isImportingModel = true;
            UpdateModelActionButtons();
            ModelActionStatusTextBlock.Text = $"Importing '{Path.GetFileName(dialog.FileName)}'...";
            var imported = await _whisperModelCatalogService.ImportModelIntoManagedDirectoryAsync(
                dialog.FileName,
                _liveConfig.Current.ModelCacheDir,
                _lifetimeCts.Token);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                TranscriptionModelPath = imported.ModelPath,
            }, _lifetimeCts.Token);
            RefreshWhisperModelStatus();
            ModelActionStatusTextBlock.Text = $"Imported '{imported.FileName}' ({FormatBytes(imported.Status.FileSizeBytes)}). Active model set to '{imported.FileName}'.";
            AppendActivity($"Imported Whisper model from '{dialog.FileName}' to '{imported.ModelPath}' and set it as active.");
        }
        catch (Exception exception)
        {
            ModelActionStatusTextBlock.Text = $"Import failed: {exception.Message}";
            AppendActivity($"Whisper model import failed: {exception.Message}");
        }
        finally
        {
            _isImportingModel = false;
            UpdateModelActionButtons();
        }
    }

    private async void DownloadSelectedRemoteDiarizationAssetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AvailableRemoteDiarizationAssetsComboBox.SelectedItem is not DiarizationRemoteAssetListRow selectedAsset)
        {
            DiarizationActionStatusTextBlock.Text = "Select a downloadable diarization asset before starting the download.";
            return;
        }

        _isDownloadingRemoteDiarizationAsset = true;
        UpdateDiarizationActionButtons();
        DiarizationActionStatusTextBlock.Text = $"Downloading '{selectedAsset.Source.FileName}' from GitHub...";

        try
        {
            var installed = await _diarizationAssetReleaseCatalogService.DownloadRemoteAssetIntoManagedDirectoryAsync(
                selectedAsset.Source,
                _liveConfig.Current.ModelCacheDir,
                _lifetimeCts.Token);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                DiarizationAssetPath = installed.AssetRootPath,
            }, _lifetimeCts.Token);
            RefreshDiarizationAssetStatus();
            await RefreshRemoteDiarizationAssetCatalogAsync(manual: false, _lifetimeCts.Token);
            DiarizationActionStatusTextBlock.Text =
                $"Installed '{selectedAsset.Source.FileName}' into '{installed.AssetRootPath}'. Speaker labeling is now {GetDiarizationAvailabilityText(installed)}.";
            AppendActivity($"Installed diarization asset '{selectedAsset.Source.FileName}' into '{installed.AssetRootPath}'.");
        }
        catch (Exception exception)
        {
            DiarizationActionStatusTextBlock.Text = $"Diarization asset download failed: {exception.Message}";
            AppendActivity($"Diarization asset download failed: {exception.Message}");
        }
        finally
        {
            _isDownloadingRemoteDiarizationAsset = false;
            UpdateDiarizationActionButtons();
        }
    }

    private async void ImportDiarizationAssetButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a diarization sidecar bundle or supporting file",
                Filter = "Diarization assets (*.zip;*.exe;*.onnx;*.bin;*.json;*.yaml;*.yml)|*.zip;*.exe;*.onnx;*.bin;*.json;*.yaml;*.yml|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _isImportingDiarizationAsset = true;
            UpdateDiarizationActionButtons();
            DiarizationActionStatusTextBlock.Text = $"Importing '{Path.GetFileName(dialog.FileName)}'...";

            var installed = await _diarizationAssetCatalogService.ImportAssetIntoManagedDirectoryAsync(
                dialog.FileName,
                _liveConfig.Current.ModelCacheDir,
                _lifetimeCts.Token);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                DiarizationAssetPath = installed.AssetRootPath,
            }, _lifetimeCts.Token);
            RefreshDiarizationAssetStatus();
            DiarizationActionStatusTextBlock.Text =
                $"Imported '{Path.GetFileName(dialog.FileName)}' into '{installed.AssetRootPath}'. Speaker labeling is now {GetDiarizationAvailabilityText(installed)}.";
            AppendActivity($"Imported diarization asset from '{dialog.FileName}' into '{installed.AssetRootPath}'.");
        }
        catch (Exception exception)
        {
            DiarizationActionStatusTextBlock.Text = $"Diarization asset import failed: {exception.Message}";
            AppendActivity($"Diarization asset import failed: {exception.Message}");
        }
        finally
        {
            _isImportingDiarizationAsset = false;
            UpdateDiarizationActionButtons();
        }
    }

    private void OpenDiarizationFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.Current.DiarizationAssetPath);
    }

    private void OpenModelFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenContainingFolder(_liveConfig.Current.TranscriptionModelPath);
    }

    private void UpdateUi(string status, string detection)
    {
        StatusTextBlock.Text = status;
        DetectionTextBlock.Text = detection;
        StartButton.Content = _isRecordingTransitionInProgress && !_recordingCoordinator.IsRecording
            ? "Starting..."
            : "Start Recording";
        StopButton.Content = _isRecordingTransitionInProgress
            ? "Stopping..."
            : "Stop Recording";
        StartButton.IsEnabled = !_recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        StopButton.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        CurrentMeetingTitleTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        RecordingControlsHintTextBlock.Text = MainWindowInteractionLogic.BuildRecordingControlsHint(
            _recordingCoordinator.IsRecording,
            _isRecordingTransitionInProgress,
            _isUpdateInstallInProgress);
        UpdateCurrentMeetingTitleStatus();
        UpdateUpdateActionButtons();
        UpdateDashboardReadiness();
    }

    private void UpdateCurrentMeetingEditor()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            _sessionTitleDraftTracker.Clear();
        }

        var nextText = activeSession is null
            ? string.Empty
            : _sessionTitleDraftTracker.GetDisplayTitle(
                activeSession.Manifest.SessionId,
                activeSession.Manifest.DetectedTitle);

        _isUpdatingCurrentMeetingEditor = true;
        try
        {
            if (!string.Equals(CurrentMeetingTitleTextBox.Text, nextText, StringComparison.Ordinal))
            {
                CurrentMeetingTitleTextBox.Text = nextText;
            }

            CurrentMeetingTitleTextBox.IsEnabled = activeSession is not null && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        }
        finally
        {
            _isUpdatingCurrentMeetingEditor = false;
        }

        UpdateCurrentMeetingTitleStatus();
    }

    private async Task RefreshMeetingListAsync(string? selectedStem = null)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        Interlocked.Increment(ref _meetingRefreshOperations);
        UpdateMeetingActionState();

        var refreshVersion = Interlocked.Increment(ref _meetingRefreshVersion);
        var config = _liveConfig.Current;
        SelectedMeetingStatusTextBlock.Text = "Loading published meetings...";
        _logger.Log(
            $"Meeting list refresh {refreshVersion} started. " +
            $"audioDir='{config.AudioOutputDir}', transcriptDir='{config.TranscriptOutputDir}', workDir='{config.WorkDir}', selectedStem='{selectedStem ?? string.Empty}'.");

        try
        {
            var records = await Task.Run(
                () => _meetingOutputCatalogService.ListMeetings(
                    config.AudioOutputDir,
                    config.TranscriptOutputDir,
                    config.WorkDir),
                _lifetimeCts.Token);
            if (refreshVersion != Volatile.Read(ref _meetingRefreshVersion) || _lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            var rows = records
                .Select(record => new MeetingListRow(record))
                .ToArray();
            _logger.Log(
                $"Meeting list refresh {refreshVersion} completed. " +
                $"rows={rows.Length}, audio={rows.Count(row => !string.IsNullOrWhiteSpace(row.Source.AudioPath))}, " +
                $"transcripts={rows.Count(row => !string.IsNullOrWhiteSpace(row.Source.MarkdownPath) || !string.IsNullOrWhiteSpace(row.Source.JsonPath))}, " +
                $"manifests={rows.Count(row => !string.IsNullOrWhiteSpace(row.Source.ManifestPath))}.");

            MeetingsDataGrid.ItemsSource = rows;

            MeetingListRow? selectedRow = null;
            if (!string.IsNullOrWhiteSpace(selectedStem))
            {
                selectedRow = rows.SingleOrDefault(row => string.Equals(row.Source.Stem, selectedStem, StringComparison.OrdinalIgnoreCase));
            }

            MeetingsDataGrid.SelectedItem = selectedRow;
            UpdateSelectedMeetingEditor(selectedRow);
        }
        catch (OperationCanceledException)
        {
            _logger.Log($"Meeting list refresh {refreshVersion} was canceled.");
            // Ignore refresh cancellation during shutdown or superseded refresh requests.
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting list refresh {refreshVersion} failed: {exception}");
            AppendActivity($"Failed to load published meetings: {exception.Message}");
            UpdateSelectedMeetingEditor(null);
        }
        finally
        {
            Interlocked.Decrement(ref _meetingRefreshOperations);
            UpdateMeetingActionState();
        }
    }

    private void UpdateSelectedMeetingEditor(MeetingListRow? row)
    {
        SelectedMeetingTitleTextBox.Text = row?.Title ?? string.Empty;
        SelectedMeetingStatusTextBlock.Text = row is null
            ? "Select a meeting to see its current processing status and available actions."
            : $"Status: {row.Status}. {row.RegenerationStatusText}";
        UpdateSpeakerLabelEditor(row);
        UpdateSplitMeetingEditor();
        UpdateMergeMeetingsEditor();
        UpdateMeetingActionState();
    }

    private void UpdateSplitMeetingEditor()
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            _splitMeetingSuggestionStem = null;
            _isUpdatingSplitMeetingControls = true;
            try
            {
                SplitSelectedMeetingPointTextBox.Text = string.Empty;
                SplitSelectedMeetingSlider.Minimum = 1d;
                SplitSelectedMeetingSlider.Maximum = 2d;
                SplitSelectedMeetingSlider.Value = 1d;
                SplitSelectedMeetingSliderMinTextBlock.Text = "00:01";
                SplitSelectedMeetingSliderMaxTextBlock.Text = "00:02";
            }
            finally
            {
                _isUpdatingSplitMeetingControls = false;
            }
            SplitSelectedMeetingPreviewTextBlock.Text = "Part-length preview will appear here once a valid meeting is selected.";
            SplitSelectedMeetingStatusTextBlock.Text = selectedMeetings.Length == 0
                ? "Select one meeting to split it into part 1 and part 2."
                : "Select exactly one meeting to split it.";
            UpdateMeetingActionState();
            return;
        }

        var selectedMeeting = selectedMeetings[0];
        if (selectedMeeting.Source.Duration is not { } duration || duration <= TimeSpan.FromSeconds(2))
        {
            _splitMeetingSuggestionStem = selectedMeeting.Source.Stem;
            _isUpdatingSplitMeetingControls = true;
            try
            {
                SplitSelectedMeetingPointTextBox.Text = string.Empty;
                SplitSelectedMeetingSlider.Minimum = 1d;
                SplitSelectedMeetingSlider.Maximum = 2d;
                SplitSelectedMeetingSlider.Value = 1d;
                SplitSelectedMeetingSliderMinTextBlock.Text = "00:01";
                SplitSelectedMeetingSliderMaxTextBlock.Text = "00:02";
            }
            finally
            {
                _isUpdatingSplitMeetingControls = false;
            }
            SplitSelectedMeetingPreviewTextBlock.Text = "This meeting needs more than two seconds of audio before it can be split.";
            SplitSelectedMeetingStatusTextBlock.Text =
                "This meeting does not have enough readable audio duration to split yet.";
            UpdateMeetingActionState();
            return;
        }

        if (!string.Equals(_splitMeetingSuggestionStem, selectedMeeting.Source.Stem, StringComparison.OrdinalIgnoreCase))
        {
            var suggestedSplitPoint = MainWindowInteractionLogic.GetSuggestedMeetingSplitPoint(duration);
            ApplySplitMeetingSelection(duration, suggestedSplitPoint);
            _splitMeetingSuggestionStem = selectedMeeting.Source.Stem;
        }
        else if (MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                     SplitSelectedMeetingPointTextBox.Text,
                     duration,
                     out var currentSplitPoint,
                     out _))
        {
            ApplySplitMeetingSelection(duration, currentSplitPoint);
        }
        else
        {
            SplitSelectedMeetingPreviewTextBlock.Text = "Enter a valid split point to preview part lengths.";
            SplitSelectedMeetingStatusTextBlock.Text =
                $"Choose a split point for this {MainWindowInteractionLogic.FormatMeetingSplitPoint(duration)} meeting.";
        }
        UpdateMeetingActionState();
    }

    private void ApplySplitMeetingSelection(TimeSpan duration, TimeSpan splitPoint)
    {
        var minimumSeconds = 1d;
        var maximumSeconds = Math.Max(minimumSeconds, Math.Floor(duration.TotalSeconds) - 1d);
        var clampedSeconds = Math.Clamp(Math.Floor(splitPoint.TotalSeconds), minimumSeconds, maximumSeconds);
        var clampedSplitPoint = TimeSpan.FromSeconds(clampedSeconds);

        _isUpdatingSplitMeetingControls = true;
        try
        {
            SplitSelectedMeetingSlider.Minimum = minimumSeconds;
            SplitSelectedMeetingSlider.Maximum = maximumSeconds;
            SplitSelectedMeetingSlider.Value = clampedSeconds;
            SplitSelectedMeetingSliderMinTextBlock.Text = MainWindowInteractionLogic.FormatMeetingSplitPoint(TimeSpan.FromSeconds(minimumSeconds));
            SplitSelectedMeetingSliderMaxTextBlock.Text = MainWindowInteractionLogic.FormatMeetingSplitPoint(TimeSpan.FromSeconds(maximumSeconds));

            var text = MainWindowInteractionLogic.FormatMeetingSplitPoint(clampedSplitPoint);
            if (!string.Equals(SplitSelectedMeetingPointTextBox.Text, text, StringComparison.Ordinal))
            {
                SplitSelectedMeetingPointTextBox.Text = text;
            }
        }
        finally
        {
            _isUpdatingSplitMeetingControls = false;
        }

        SplitSelectedMeetingStatusTextBlock.Text =
            $"Split at {MainWindowInteractionLogic.FormatMeetingSplitPoint(clampedSplitPoint)} of {MainWindowInteractionLogic.FormatMeetingSplitPoint(duration)} total duration. The original meeting will stay in place.";
        SplitSelectedMeetingPreviewTextBlock.Text = MainWindowInteractionLogic.BuildMeetingSplitPreview(duration, clampedSplitPoint);
    }

    private void UpdateMergeMeetingsEditor()
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length < 2)
        {
            MergeSelectedMeetingsTitleTextBox.Text = string.Empty;
            MergeSelectedMeetingsStatusTextBlock.Text =
                "Select two or more meetings to combine them into one new merged recording.";
            UpdateMeetingActionState();
            return;
        }

        var suggestedTitle = BuildDefaultMergedMeetingTitle(selectedMeetings);
        if (!string.Equals(MergeSelectedMeetingsTitleTextBox.Text, suggestedTitle, StringComparison.Ordinal))
        {
            MergeSelectedMeetingsTitleTextBox.Text = suggestedTitle;
        }

        MergeSelectedMeetingsStatusTextBlock.Text =
            $"Selected {selectedMeetings.Length} meetings. The originals will stay in place after the merged session is queued.";
        UpdateMeetingActionState();
    }

    private void UpdateSpeakerLabelEditor(MeetingListRow? row)
    {
        if (row is null)
        {
            SpeakerLabelsEditorDataGrid.ItemsSource = Array.Empty<SpeakerLabelEditorRow>();
            SpeakerNamesStatusTextBlock.Text = "Select a meeting to review or rename diarized speaker labels.";
            UpdateMeetingActionState();
            return;
        }

        var labels = _meetingOutputCatalogService.ListSpeakerLabels(row.Source);
        var labelRows = labels
            .Select(label => new SpeakerLabelEditorRow(label, UpdateMeetingActionState))
            .ToArray();

        SpeakerLabelsEditorDataGrid.ItemsSource = labelRows;
        SpeakerNamesStatusTextBlock.Text = labelRows.Length == 0
            ? "This transcript does not currently contain diarized speaker labels."
            : "Edit the display names below, then click Apply Speaker Names to update the selected transcript.";
        UpdateMeetingActionState();
    }

    private void UpdateMeetingActionState()
    {
        if (!_isUiReady)
        {
            return;
        }

        var selectedMeeting = MeetingsDataGrid.SelectedItem as MeetingListRow;
        var selectedMeetings = GetSelectedMeetingRows();
        var splitMeeting = selectedMeetings.Length == 1 ? selectedMeetings[0] : null;
        var isMeetingActionInProgress =
            Volatile.Read(ref _meetingRefreshOperations) > 0 ||
            _isRenamingMeeting ||
            _isRetryingMeeting ||
            _isApplyingSpeakerNames ||
            _isMergingMeetings ||
            _isSplittingMeeting;
        var canEditSplitPoint = splitMeeting?.Source.Duration is { } splitDuration &&
            splitDuration > TimeSpan.FromSeconds(2) &&
            !isMeetingActionInProgress;

        RefreshMeetingsButton.Content = Volatile.Read(ref _meetingRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh List";
        RenameSelectedMeetingButton.Content = _isRenamingMeeting ? "Renaming..." : "Rename Meeting";
        RetrySelectedMeetingButton.Content = _isRetryingMeeting ? "Queueing..." : "Re-Generate Transcript";
        ApplySpeakerNamesButton.Content = _isApplyingSpeakerNames ? "Applying..." : "Apply Speaker Names";
        SplitSelectedMeetingButton.Content = _isSplittingMeeting ? "Splitting..." : "Split Into Two";
        MergeSelectedMeetingsButton.Content = _isMergingMeetings ? "Merging..." : "Merge Selected Meetings";

        RefreshMeetingsButton.IsEnabled = !isMeetingActionInProgress;
        SelectedMeetingTitleTextBox.IsEnabled = selectedMeeting is not null && !isMeetingActionInProgress;
        RenameSelectedMeetingButton.IsEnabled = selectedMeeting is not null &&
            !isMeetingActionInProgress &&
            MainWindowInteractionLogic.HasPendingMeetingRename(selectedMeeting.Title, SelectedMeetingTitleTextBox.Text);
        RetrySelectedMeetingButton.IsEnabled = selectedMeeting?.CanRegenerateTranscript == true && !isMeetingActionInProgress;
        ApplySpeakerNamesButton.IsEnabled = selectedMeeting is not null &&
            !isMeetingActionInProgress &&
            HasPendingSpeakerLabelChanges();
        SplitSelectedMeetingPointTextBox.IsEnabled = canEditSplitPoint;
        SplitSelectedMeetingSlider.IsEnabled = canEditSplitPoint;
        SplitSelectedMeetingButton.IsEnabled = splitMeeting is not null &&
            canEditSplitPoint &&
            MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                SplitSelectedMeetingPointTextBox.Text,
                splitMeeting.Source.Duration,
                out _,
                out _);
        MergeSelectedMeetingsTitleTextBox.IsEnabled = selectedMeetings.Length >= 2 && !isMeetingActionInProgress;
        MergeSelectedMeetingsButton.IsEnabled = selectedMeetings.Length >= 2 &&
            !isMeetingActionInProgress &&
            !string.IsNullOrWhiteSpace(MergeSelectedMeetingsTitleTextBox.Text);
    }

    private bool HasPendingSpeakerLabelChanges()
    {
        if (SpeakerLabelsEditorDataGrid.ItemsSource is not IEnumerable<SpeakerLabelEditorRow> labelRows)
        {
            return false;
        }

        return MainWindowInteractionLogic.BuildSpeakerLabelMap(
            labelRows.Select(row => new SpeakerLabelDraft(row.OriginalLabel, row.EditedLabel))).Count > 0;
    }

    private MeetingListRow[] GetSelectedMeetingRows()
    {
        return MeetingsDataGrid.SelectedItems
            .OfType<MeetingListRow>()
            .ToArray();
    }

    private static string BuildDefaultMergedMeetingTitle(IReadOnlyList<MeetingListRow> selectedMeetings)
    {
        var distinctTitles = selectedMeetings
            .Select(row => row.Title.Trim())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctTitles.Length == 1)
        {
            return distinctTitles[0];
        }

        return $"{selectedMeetings[0].Title} merged";
    }

    private void LoadConfigEditorValues(AppConfig config)
    {
        ConfigAudioOutputDirTextBox.Text = config.AudioOutputDir;
        ConfigTranscriptOutputDirTextBox.Text = config.TranscriptOutputDir;
        ConfigWorkDirTextBox.Text = config.WorkDir;
        ConfigModelCacheDirTextBox.Text = config.ModelCacheDir;
        ConfigTranscriptionModelPathTextBox.Text = config.TranscriptionModelPath;
        ConfigDiarizationAssetPathTextBox.Text = config.DiarizationAssetPath;
        ConfigAutoDetectThresholdTextBox.Text = config.AutoDetectAudioPeakThreshold.ToString("0.###", CultureInfo.InvariantCulture);
        ConfigMeetingStopTimeoutTextBox.Text = config.MeetingStopTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ConfigMicCaptureCheckBox.IsChecked = config.MicCaptureEnabled;
        ConfigLaunchOnLoginCheckBox.IsChecked = config.LaunchOnLoginEnabled;
        ConfigAutoDetectCheckBox.IsChecked = config.AutoDetectEnabled;
        ConfigCalendarTitleFallbackCheckBox.IsChecked = config.CalendarTitleFallbackEnabled;
        ConfigUpdateCheckEnabledCheckBox.IsChecked = config.UpdateCheckEnabled;
        ConfigAutoInstallUpdatesCheckBox.IsChecked = config.AutoInstallUpdatesEnabled;
        ConfigUpdateFeedUrlTextBox.Text = config.UpdateFeedUrl;
    }

    private void RegisterConfigEditorChangeHandlers()
    {
        foreach (var textBox in new[]
                 {
                     ConfigAudioOutputDirTextBox,
                     ConfigTranscriptOutputDirTextBox,
                     ConfigWorkDirTextBox,
                     ConfigModelCacheDirTextBox,
                     ConfigTranscriptionModelPathTextBox,
                     ConfigDiarizationAssetPathTextBox,
                     ConfigAutoDetectThresholdTextBox,
                     ConfigMeetingStopTimeoutTextBox,
                     ConfigUpdateFeedUrlTextBox,
                 })
        {
            textBox.TextChanged += ConfigEditorValueChanged;
        }

        foreach (var checkBox in new[]
                 {
                     ConfigMicCaptureCheckBox,
                     ConfigLaunchOnLoginCheckBox,
                     ConfigAutoDetectCheckBox,
                     ConfigCalendarTitleFallbackCheckBox,
                     ConfigUpdateCheckEnabledCheckBox,
                     ConfigAutoInstallUpdatesCheckBox,
                 })
        {
            checkBox.Checked += ConfigEditorValueChanged;
            checkBox.Unchecked += ConfigEditorValueChanged;
        }
    }

    private void ConfigEditorValueChanged(object sender, RoutedEventArgs e)
    {
        UpdateConfigActionState();
    }

    private void ConfigEditorValueChanged(object sender, TextChangedEventArgs e)
    {
        UpdateConfigActionState();
    }

    private void UpdateConfigActionState()
    {
        UpdateConfigDependencyState();
        SaveConfigButton.Content = _isSavingConfig ? "Saving..." : "Save Config";
        SaveConfigButton.IsEnabled = !_isSavingConfig &&
            MainWindowInteractionLogic.HasPendingConfigChanges(
                _liveConfig.Current,
                ReadConfigEditorSnapshot());
    }

    private ConfigEditorSnapshot ReadConfigEditorSnapshot()
    {
        return new ConfigEditorSnapshot(
            ConfigAudioOutputDirTextBox.Text,
            ConfigTranscriptOutputDirTextBox.Text,
            ConfigWorkDirTextBox.Text,
            ConfigModelCacheDirTextBox.Text,
            ConfigTranscriptionModelPathTextBox.Text,
            ConfigDiarizationAssetPathTextBox.Text,
            ConfigAutoDetectThresholdTextBox.Text,
            ConfigMeetingStopTimeoutTextBox.Text,
            ConfigMicCaptureCheckBox.IsChecked == true,
            ConfigLaunchOnLoginCheckBox.IsChecked == true,
            ConfigAutoDetectCheckBox.IsChecked == true,
            ConfigCalendarTitleFallbackCheckBox.IsChecked == true,
            ConfigUpdateCheckEnabledCheckBox.IsChecked == true,
            ConfigAutoInstallUpdatesCheckBox.IsChecked == true,
            ConfigUpdateFeedUrlTextBox.Text);
    }

    private async Task TryReloadConfigAsync()
    {
        var reloaded = await _liveConfig.ReloadIfChangedAsync(_lifetimeCts.Token);
        if (reloaded is not null)
        {
            ConfigSaveStatusTextBlock.Text = "Config file changed on disk and was reloaded.";
        }
    }

    private void LiveConfig_OnChanged(object? sender, LiveAppConfigChangedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (IsShutdownRequested)
            {
                return;
            }

            ApplyConfigToUi(e.CurrentConfig, $"Config {e.Source.ToString().ToLowerInvariant()} applied without restart.");
            TrySyncLaunchOnLoginSetting(e.CurrentConfig, $"config {e.Source.ToString().ToLowerInvariant()}");
            var updateSourceChanged =
                !string.Equals(e.PreviousConfig.UpdateFeedUrl, e.CurrentConfig.UpdateFeedUrl, StringComparison.OrdinalIgnoreCase) ||
                e.PreviousConfig.UpdateCheckEnabled != e.CurrentConfig.UpdateCheckEnabled;

            if (updateSourceChanged)
            {
                _ = CheckForUpdatesAsync($"config {e.Source.ToString().ToLowerInvariant()}", manual: false, _lifetimeCts.Token);
                _ = RefreshRemoteModelCatalogAsync(manual: false, _lifetimeCts.Token);
                _ = RefreshRemoteDiarizationAssetCatalogAsync(manual: false, _lifetimeCts.Token);
            }

            if (!e.PreviousConfig.AutoInstallUpdatesEnabled && e.CurrentConfig.AutoInstallUpdatesEnabled)
            {
                _ = TryInstallAvailableUpdateIfIdleAsync($"config {e.Source.ToString().ToLowerInvariant()}", _lifetimeCts.Token);
            }

            _ = EnsureConfiguredModelPathResolvedAsync($"config {e.Source.ToString().ToLowerInvariant()}", _lifetimeCts.Token);
            _ = RefreshMeetingListAsync();
        });
    }

    private void ApplyConfigToUi(AppConfig config, string? statusMessage)
    {
        ModelPathRun.Text = config.TranscriptionModelPath;
        DiarizationAssetPathRun.Text = config.DiarizationAssetPath;
        LoadConfigEditorValues(config);
        RefreshWhisperModelStatus();
        RefreshDiarizationAssetStatus();
        RefreshUpdateMetadataDisplay(config, _lastUpdateCheckResult);
        UpdateConfigActionState();
        UpdateDashboardReadiness();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            AppendActivity(statusMessage);
        }
    }

    private void TrySyncLaunchOnLoginSetting(AppConfig config, string source)
    {
        try
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "MeetingRecorder.App.exe");
            var changed = _autoStartRegistrationService.SyncRegistration(config.LaunchOnLoginEnabled, executablePath);
            if (changed)
            {
                AppendActivity(
                    $"Launch-on-login registration {(config.LaunchOnLoginEnabled ? "enabled" : "disabled")} from {source}.");
            }
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to apply launch-on-login setting from {source}: {exception.Message}");
        }
    }

    private async Task RunScheduledUpdateCycleAsync(string source, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        try
        {
            if (ShouldRunScheduledUpdateCheck(DateTimeOffset.UtcNow))
            {
                await CheckForUpdatesAsync(source, manual: false, cancellationToken);
            }

            await TryInstallAvailableUpdateIfIdleAsync(source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            AppendActivity($"Scheduled update cycle failed from {source}: {exception.Message}");
        }
    }

    private async Task RunExternalAudioImportCycleAsync(string source, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        if (!await _externalAudioImportGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var imported = await _externalAudioImportService.ImportPendingAudioFilesAsync(
                _liveConfig.Current,
                DateTimeOffset.UtcNow,
                cancellationToken);

            if (imported.Count == 0)
            {
                return;
            }

            foreach (var item in imported)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AppendActivity($"Queued dropped audio '{item.Title}' for automatic transcription.");
                await _processingQueue.EnqueueAsync(item.ManifestPath, cancellationToken);

                var manifest = await _manifestStore.LoadAsync(item.ManifestPath, cancellationToken);
                if (manifest.State == SessionState.Failed)
                {
                    var errorSummary = string.IsNullOrWhiteSpace(manifest.ErrorSummary)
                        ? manifest.TranscriptionStatus.Message ?? "See app.log for details."
                        : manifest.ErrorSummary;
                    AppendActivity($"Dropped audio '{item.Title}' failed to transcribe: {errorSummary}");
                }
                else if (manifest.State == SessionState.Published)
                {
                    AppendActivity($"Finished transcript build for dropped audio '{item.Title}'.");
                }
            }

            await RefreshMeetingListAsync();
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            AppendActivity($"External audio import from {source} failed: {exception.Message}");
        }
        finally
        {
            _externalAudioImportGate.Release();
        }
    }

    private bool ShouldRunScheduledUpdateCheck(DateTimeOffset nowUtc)
    {
        var config = _liveConfig.Current;
        if (!config.UpdateCheckEnabled)
        {
            return false;
        }

        return _appUpdateSchedulePolicy.ShouldRunScheduledCheck(
            config.LastUpdateCheckUtc,
            nowUtc,
            ScheduledUpdateCheckCadence);
    }

    private async Task CheckForUpdatesAsync(string source, bool manual, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        if (manual)
        {
            await _updateOperationGate.WaitAsync(cancellationToken);
        }
        else if (!await _updateOperationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        Interlocked.Increment(ref _updateCheckOperations);
        UpdateUpdateActionButtons();
        if (manual)
        {
            UpdateCheckStatusTextBlock.Text = "Checking GitHub for updates...";
        }

        try
        {
            await CheckForUpdatesCoreAsync(source, manual, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            UpdateCheckStatusTextBlock.Text = $"Update check failed: {exception.Message}";
            AppendActivity($"Update check failed from {source}: {exception.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _updateCheckOperations);
            UpdateUpdateActionButtons();
            _updateOperationGate.Release();
        }
    }

    private async Task<AppUpdateCheckResult> CheckForUpdatesCoreAsync(
        string source,
        bool manual,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        var checkedAtUtc = DateTimeOffset.UtcNow;
        var result = await _appUpdateService.CheckForUpdateAsync(
            BuildLocalUpdateState(currentConfig),
            currentConfig.UpdateFeedUrl,
            manual || currentConfig.UpdateCheckEnabled,
            cancellationToken);
        await PersistLastUpdateCheckUtcAsync(checkedAtUtc, cancellationToken);
        _lastUpdateCheckResult = result;
        ApplyUpdateCheckResult(result, manual);

        if (result.Status == AppUpdateStatusKind.UpdateAvailable)
        {
            AppendActivity($"Update check from {source}: version {result.LatestVersion} is available.");
        }
        else if (manual || result.Status == AppUpdateStatusKind.Error)
        {
            AppendActivity($"Update check from {source}: {result.Message}");
        }

        return result;
    }

    private async Task<AppUpdateCheckResult?> EnsureUpdateAvailableAsync(string source, CancellationToken cancellationToken)
    {
        if (_lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "" } available)
        {
            return available;
        }

        await CheckForUpdatesAsync(source, manual: true, cancellationToken);
        return _lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "" } refreshed
            ? refreshed
            : null;
    }

    private async Task TryInstallAvailableUpdateIfIdleAsync(string source, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        if (await TryInstallPendingDownloadedUpdateIfIdleAsync(source, cancellationToken))
        {
            return;
        }

        var config = _liveConfig.Current;
        var shouldAutoInstall = _appUpdateInstallPolicy.ShouldAutoInstall(
            _lastUpdateCheckResult,
            config.UpdateCheckEnabled && config.AutoInstallUpdatesEnabled,
            _recordingCoordinator.IsRecording,
            _processingQueue.IsProcessingInProgress,
            _isUpdateInstallInProgress);
        if (!shouldAutoInstall)
        {
            return;
        }

        await InstallAvailableUpdateAsync(source, manual: false, cancellationToken);
    }

    private async Task<bool> TryInstallPendingDownloadedUpdateIfIdleAsync(string source, CancellationToken cancellationToken)
    {
        var config = _liveConfig.Current;
        if (!_appUpdateInstallPolicy.ShouldRetryPendingInstall(
                config.PendingUpdateZipPath,
                _recordingCoordinator.IsRecording,
                _processingQueue.IsProcessingInProgress,
                _isUpdateInstallInProgress))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(config.PendingUpdateVersion) &&
            string.Equals(config.PendingUpdateVersion, AppBranding.Version, StringComparison.OrdinalIgnoreCase))
        {
            AppendActivity($"Clearing pending update {FormatVersionLabel(config.PendingUpdateVersion)} because this app version is already installed.");
            await ClearPendingDownloadedUpdateAsync(cancellationToken);
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.PendingUpdateZipPath) || !File.Exists(config.PendingUpdateZipPath))
        {
            AppendActivity("Clearing stale pending update because the downloaded ZIP is no longer available.");
            await ClearPendingDownloadedUpdateAsync(cancellationToken);
            return false;
        }

        var pendingResult = BuildPendingDownloadedUpdateResult(config);
        await InstallDownloadedUpdateAsync($"{source} restart retry", config.PendingUpdateZipPath, pendingResult, cancellationToken);
        return true;
    }

    private async Task InstallAvailableUpdateAsync(string source, bool manual, CancellationToken cancellationToken)
    {
        if (manual)
        {
            await _updateOperationGate.WaitAsync(cancellationToken);
        }
        else if (!await _updateOperationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        _isPreparingUpdateInstall = true;
        UpdateUpdateActionButtons();
        if (manual)
        {
            UpdateCheckStatusTextBlock.Text = "Preparing the latest update...";
        }

        try
        {
            var result = _lastUpdateCheckResult;
            if (manual && result is not { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "" })
            {
                result = await CheckForUpdatesCoreAsync($"{source} check", manual: true, cancellationToken);
            }

            if (result is not { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "" } available)
            {
                if (manual)
                {
                    UpdateCheckStatusTextBlock.Text = "No installable update is currently available.";
                }

                return;
            }

            if (!TryGetUpdateInstallBlockReason(out var blockReason))
            {
                if (manual)
                {
                    UpdateCheckStatusTextBlock.Text = blockReason;
                }

                return;
            }

            _isUpdateInstallInProgress = true;
            UpdateUpdateActionButtons();
            _detectionTimer.Stop();
            _updateTimer.Stop();
            UpdateUi($"Installing update {FormatVersionLabel(available.LatestVersion)}...", DetectionTextBlock.Text);
            UpdateCheckStatusTextBlock.Text = $"Downloading update {FormatVersionLabel(available.LatestVersion)} and preparing the installer handoff...";
            AppendActivity($"Starting {(manual ? "manual" : "automatic")} update install for {FormatVersionLabel(available.LatestVersion)} from {source}.");

            var downloadedPath = await _appUpdateService.DownloadUpdateAsync(
                available.DownloadUrl,
                available.LatestVersion,
                cancellationToken);

            await InstallDownloadedUpdateCoreAsync(source, downloadedPath, available, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            UpdateCheckStatusTextBlock.Text = $"Update install failed: {exception.Message}";
            AppendActivity($"Update install failed from {source}: {exception.Message}");
        }
        finally
        {
            _isPreparingUpdateInstall = false;
            if (!_allowClose)
            {
                _isUpdateInstallInProgress = false;
                if (!_detectionTimer.IsEnabled)
                {
                    _detectionTimer.Start();
                }

                if (!_updateTimer.IsEnabled)
                {
                    _updateTimer.Start();
                }

                UpdateUi(
                    _recordingCoordinator.IsRecording ? "Recording in progress." : "Ready to record.",
                    DetectionTextBlock.Text);
                ApplyUpdateCheckResult(_lastUpdateCheckResult, manual: false);
            }

            UpdateUpdateActionButtons();
            _updateOperationGate.Release();
        }
    }

    private async Task InstallDownloadedUpdateAsync(
        string source,
        string downloadedPath,
        AppUpdateCheckResult updateResult,
        CancellationToken cancellationToken)
    {
        if (!await _updateOperationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        _isPreparingUpdateInstall = true;
        _isUpdateInstallInProgress = true;
        UpdateUpdateActionButtons();
        _detectionTimer.Stop();
        _updateTimer.Stop();
        UpdateUi($"Installing update {FormatVersionLabel(updateResult.LatestVersion)}...", DetectionTextBlock.Text);
        UpdateCheckStatusTextBlock.Text = $"Retrying previously downloaded update {FormatVersionLabel(updateResult.LatestVersion)} after restart...";
        AppendActivity($"Retrying pending downloaded update {FormatVersionLabel(updateResult.LatestVersion)} from {source}.");

        try
        {
            await InstallDownloadedUpdateCoreAsync(source, downloadedPath, updateResult, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            UpdateCheckStatusTextBlock.Text = $"Update install failed: {exception.Message}";
            AppendActivity($"Pending update install failed from {source}: {exception.Message}");
        }
        finally
        {
            _isPreparingUpdateInstall = false;
            if (!_allowClose)
            {
                _isUpdateInstallInProgress = false;
                if (!_detectionTimer.IsEnabled)
                {
                    _detectionTimer.Start();
                }

                if (!_updateTimer.IsEnabled)
                {
                    _updateTimer.Start();
                }

                UpdateUi(
                    _recordingCoordinator.IsRecording ? "Recording in progress." : "Ready to record.",
                    DetectionTextBlock.Text);
                ApplyUpdateCheckResult(_lastUpdateCheckResult, manual: false);
            }

            UpdateUpdateActionButtons();
            _updateOperationGate.Release();
        }
    }

    private async Task InstallDownloadedUpdateCoreAsync(
        string source,
        string downloadedPath,
        AppUpdateCheckResult updateResult,
        CancellationToken cancellationToken)
    {
        if (!TryGetUpdateInstallBlockReason(out var blockReason, allowCurrentInstallInProgress: true))
        {
            await PersistPendingDownloadedUpdateAsync(downloadedPath, updateResult, cancellationToken);
            UpdateCheckStatusTextBlock.Text = MainWindowInteractionLogic.BuildDeferredUpdateInstallMessage(
                blockReason,
                downloadedPath);
            AppendActivity($"Deferred update install for {FormatVersionLabel(updateResult.LatestVersion)} because the app became busy after download.");
            OpenContainingFolder(downloadedPath);
            return;
        }

        await PersistPendingDownloadedUpdateAsync(downloadedPath, updateResult, cancellationToken);
        LaunchDownloadedUpdateInstaller(downloadedPath, updateResult);
        UpdateCheckStatusTextBlock.Text = $"Installing update {FormatVersionLabel(updateResult.LatestVersion)}. The app will close and relaunch automatically.";
        AppendActivity($"Handed off update {FormatVersionLabel(updateResult.LatestVersion)} to the external installer from {source}.");

        _updateTimer.Stop();
        _allowClose = true;
        Close();
    }

    private void LaunchDownloadedUpdateInstaller(string downloadedPath, AppUpdateCheckResult updateResult)
    {
        var updaterScriptPath = Path.Combine(AppContext.BaseDirectory, "Apply-DownloadedUpdate.ps1");
        if (!File.Exists(updaterScriptPath))
        {
            throw new InvalidOperationException($"The updater helper '{updaterScriptPath}' is missing from the installed app folder.");
        }

        var installRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = installRoot,
        };

        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(updaterScriptPath);
        startInfo.ArgumentList.Add("-ZipPath");
        startInfo.ArgumentList.Add(downloadedPath);
        startInfo.ArgumentList.Add("-InstallRoot");
        startInfo.ArgumentList.Add(installRoot);
        startInfo.ArgumentList.Add("-SourceProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(updateResult.LatestVersion))
        {
            startInfo.ArgumentList.Add("-ReleaseVersion");
            startInfo.ArgumentList.Add(updateResult.LatestVersion);
        }

        if (updateResult.LatestPublishedAtUtc.HasValue)
        {
            startInfo.ArgumentList.Add("-ReleasePublishedAtUtc");
            startInfo.ArgumentList.Add(updateResult.LatestPublishedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (updateResult.LatestAssetSizeBytes is > 0)
        {
            startInfo.ArgumentList.Add("-ReleaseAssetSizeBytes");
            startInfo.ArgumentList.Add(updateResult.LatestAssetSizeBytes.Value.ToString(CultureInfo.InvariantCulture));
        }

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the downloaded update installer.");
    }

    private async Task PersistLastUpdateCheckUtcAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (currentConfig.LastUpdateCheckUtc.HasValue &&
            currentConfig.LastUpdateCheckUtc.Value >= checkedAtUtc)
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            LastUpdateCheckUtc = checkedAtUtc,
        }, cancellationToken);
    }

    private async Task PersistPendingDownloadedUpdateAsync(
        string downloadedPath,
        AppUpdateCheckResult updateResult,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (string.Equals(currentConfig.PendingUpdateZipPath, downloadedPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentConfig.PendingUpdateVersion, updateResult.LatestVersion, StringComparison.Ordinal))
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            PendingUpdateZipPath = downloadedPath,
            PendingUpdateVersion = updateResult.LatestVersion,
            PendingUpdatePublishedAtUtc = updateResult.LatestPublishedAtUtc,
            PendingUpdateAssetSizeBytes = updateResult.LatestAssetSizeBytes,
        }, cancellationToken);
    }

    private async Task ClearPendingDownloadedUpdateAsync(CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (string.IsNullOrWhiteSpace(currentConfig.PendingUpdateZipPath) &&
            string.IsNullOrWhiteSpace(currentConfig.PendingUpdateVersion) &&
            !currentConfig.PendingUpdatePublishedAtUtc.HasValue &&
            !currentConfig.PendingUpdateAssetSizeBytes.HasValue)
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
        }, cancellationToken);
    }

    private AppUpdateCheckResult BuildPendingDownloadedUpdateResult(AppConfig config)
    {
        var version = string.IsNullOrWhiteSpace(config.PendingUpdateVersion)
            ? AppBranding.Version
            : config.PendingUpdateVersion;

        return new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            AppBranding.Version,
            version,
            config.PendingUpdateZipPath,
            _lastUpdateCheckResult?.ReleasePageUrl,
            config.PendingUpdatePublishedAtUtc,
            config.PendingUpdateAssetSizeBytes,
            false,
            false,
            false,
            $"A previously downloaded update {FormatVersionLabel(version)} is ready to install.");
    }

    private AppUpdateLocalState BuildLocalUpdateState(AppConfig config)
    {
        return new AppUpdateLocalState(
            AppBranding.Version,
            string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
                ? AppBranding.Version
                : config.InstalledReleaseVersion,
            config.InstalledReleasePublishedAtUtc,
            config.InstalledReleaseAssetSizeBytes);
    }

    private bool TryGetUpdateInstallBlockReason(out string reason, bool allowCurrentInstallInProgress = false)
    {
        reason = _appUpdateInstallPolicy.GetInstallBlockReason(
            _recordingCoordinator.IsRecording,
            _processingQueue.IsProcessingInProgress,
            _isUpdateInstallInProgress,
            allowCurrentInstallInProgress) ?? string.Empty;
        return string.IsNullOrEmpty(reason);
    }

    private void ApplyUpdateCheckResult(AppUpdateCheckResult? result, bool manual)
    {
        RefreshUpdateMetadataDisplay(_liveConfig.Current, result);

        if (result is null)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateBannerTextBlock.Text = string.Empty;
            UpdateCheckStatusTextBlock.Text = $"Current version: {FormatVersionLabel(AppBranding.Version)}. No update check has run yet.";
            UpdateUpdateActionButtons();
            return;
        }

        UpdateCheckStatusTextBlock.Text = result.Message;

        if (result.Status == AppUpdateStatusKind.UpdateAvailable)
        {
            var installMessage = _liveConfig.Current.AutoInstallUpdatesEnabled
                ? "If auto-install is enabled, the app will install it automatically the next time no recording or background processing is active."
                : "Use the Updates tab to install it manually when the app is idle.";
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerTextBlock.Text =
                $"A newer GitHub release is available: {FormatVersionLabel(result.LatestVersion)}. " +
                installMessage;
        }
        else
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateBannerTextBlock.Text = string.Empty;

            if (manual && result.Status == AppUpdateStatusKind.UpToDate)
            {
                UpdateCheckStatusTextBlock.Text = $"You are up to date on {FormatVersionLabel(result.CurrentVersion)}.";
            }
        }

        UpdateUpdateActionButtons();
        UpdateDashboardReadiness();
    }

    private void RefreshUpdateMetadataDisplay(AppConfig config, AppUpdateCheckResult? result)
    {
        InstalledAppVersionTextBlock.Text = FormatVersionLabel(AppBranding.Version);
        InstalledReleaseVersionTextBlock.Text = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
            ? FormatVersionLabel(AppBranding.Version)
            : FormatVersionLabel(config.InstalledReleaseVersion);
        InstalledReleasePublishedAtTextBlock.Text = FormatUpdateTimestamp(config.InstalledReleasePublishedAtUtc);
        InstalledReleaseAssetSizeTextBlock.Text = FormatUpdateSize(config.InstalledReleaseAssetSizeBytes);
        LastUpdateCheckTextBlock.Text = config.LastUpdateCheckUtc.HasValue
            ? $"Last checked: {FormatUpdateTimestamp(config.LastUpdateCheckUtc)}"
            : "Last checked: the app has not queried GitHub yet.";

        LatestGitHubVersionTextBlock.Text = result is null
            ? "Not checked yet"
            : FormatVersionLabel(result.LatestVersion);
        LatestGitHubPublishedAtTextBlock.Text = FormatUpdateTimestamp(result?.LatestPublishedAtUtc);
        LatestGitHubAssetSizeTextBlock.Text = FormatUpdateSize(result?.LatestAssetSizeBytes);
        LatestGitHubComparisonTextBlock.Text = BuildUpdateComparisonText(result);
        UpdateAutomationSummaryTextBlock.Text = BuildUpdateAutomationSummary(config);
    }

    private string BuildUpdateAutomationSummary(AppConfig config)
    {
        if (!config.UpdateCheckEnabled)
        {
            return "Automatic GitHub checks are disabled. Use Check Now to query the release feed manually.";
        }

        var nextCheckUtc = _appUpdateSchedulePolicy.GetNextCheckUtc(config.LastUpdateCheckUtc, ScheduledUpdateCheckCadence);
        var cadenceText = config.LastUpdateCheckUtc.HasValue
            ? $"The next automatic GitHub check is due around {FormatUpdateTimestamp(nextCheckUtc)}."
            : "No automatic check has run yet. The app will check GitHub within the next minute.";
        var installText = config.AutoInstallUpdatesEnabled
            ? "When a newer release is found, the app will download and install it automatically once recording and background processing are idle."
            : "Newer releases will be detected automatically, but installs must be triggered manually from this tab.";
        return $"{cadenceText} {installText}";
    }

    private static string BuildUpdateComparisonText(AppUpdateCheckResult? result)
    {
        if (result is null)
        {
            return "Run Check Now to compare the installed app with the latest GitHub release.";
        }

        if (result.Status == AppUpdateStatusKind.UpdateAvailable)
        {
            var reasons = new List<string>();
            if (result.IsNewerByVersion)
            {
                reasons.Add("newer version number");
            }

            if (result.IsNewerByPublishedAt)
            {
                reasons.Add("newer publish date");
            }

            if (result.IsNewerByAssetSize)
            {
                reasons.Add("different installer asset size");
            }

            return reasons.Count == 0
                ? result.Message
                : $"Update available because GitHub reports a {string.Join(", ", reasons)}.";
        }

        return result.Message;
    }

    private static string FormatVersionLabel(string? versionLabel)
    {
        if (string.IsNullOrWhiteSpace(versionLabel))
        {
            return "Unknown";
        }

        return ReleaseVersionParsing.TryNormalizeVersionLabel(versionLabel, out var normalizedLabel, out _)
            ? $"v{normalizedLabel}"
            : versionLabel.Trim();
    }

    private static string FormatUpdateTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp.HasValue
            ? timestamp.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : "Unknown";
    }

    private static string FormatUpdateSize(long? bytes)
    {
        return bytes is > 0
            ? FormatBytes(bytes.Value)
            : "Unknown";
    }

    private void UpdateUpdateActionButtons()
    {
        var isCheckingForUpdates = Volatile.Read(ref _updateCheckOperations) > 0;
        var isAnyUpdateActionBusy = isCheckingForUpdates || _isDownloadingUpdate || _isPreparingUpdateInstall || _isUpdateInstallInProgress;
        var hasDownloadableUpdate =
            _lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpdateAvailable } available &&
            !string.IsNullOrWhiteSpace(available.DownloadUrl);
        var canInstallUpdate = hasDownloadableUpdate && !isAnyUpdateActionBusy && TryGetUpdateInstallBlockReason(out _);
        var canDownloadUpdate = hasDownloadableUpdate && !isAnyUpdateActionBusy;

        CheckForUpdatesButton.Content = isCheckingForUpdates ? "Checking..." : "Check for Updates";
        InstallLatestUpdateButton.Content = _isUpdateInstallInProgress
            ? "Installing..."
            : _isPreparingUpdateInstall
                ? "Preparing..."
                : "Install Available Update";
        DownloadLatestUpdateButton.Content = _isDownloadingUpdate ? "Downloading..." : "Download Latest ZIP";

        CheckForUpdatesButton.IsEnabled = !isAnyUpdateActionBusy;
        InstallLatestUpdateButton.IsEnabled = canInstallUpdate;
        DownloadLatestUpdateButton.IsEnabled = canDownloadUpdate;
        OpenLatestReleasePageButton.IsEnabled = !_isUpdateInstallInProgress;
        UpdateOperationProgressBar.Visibility = isAnyUpdateActionBusy ? Visibility.Visible : Visibility.Collapsed;

        if (isAnyUpdateActionBusy)
        {
            UpdateInstallAvailabilityTextBlock.Text = "Working on the current update task. The install controls will unlock again when it finishes.";
        }
        else if (_lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "" })
        {
            UpdateInstallAvailabilityTextBlock.Text = TryGetUpdateInstallBlockReason(out var reason)
                ? "The app is idle and can install this update now."
                : reason;
        }
        else if (_lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpToDate })
        {
            UpdateInstallAvailabilityTextBlock.Text = "No newer GitHub release is currently available.";
        }
        else
        {
            UpdateInstallAvailabilityTextBlock.Text = "Run Check Now to compare this installation with the latest GitHub release.";
        }
    }

    private void UpdateConfigDependencyState()
    {
        var updateChecksEnabled = ConfigUpdateCheckEnabledCheckBox.IsChecked == true;
        var autoDetectEnabled = ConfigAutoDetectCheckBox.IsChecked == true;

        ConfigAutoInstallUpdatesCheckBox.IsEnabled = updateChecksEnabled;
        ConfigAutoInstallDependencyTextBlock.Text =
            MainWindowInteractionLogic.BuildAutoInstallUpdatesHint(updateChecksEnabled);

        ConfigAutoDetectThresholdTextBox.IsEnabled = autoDetectEnabled;
        ConfigMeetingStopTimeoutTextBox.IsEnabled = autoDetectEnabled;
        ConfigAutoDetectSettingsHintTextBlock.Text =
            MainWindowInteractionLogic.BuildAutoDetectSettingsHint(autoDetectEnabled);
    }

    private void UpdateDashboardReadiness()
    {
        var hasValidModel = _whisperModelService.Inspect(_liveConfig.Current.TranscriptionModelPath).Kind == WhisperModelStatusKind.Valid;
        var diarizationReady = false;
        try
        {
            diarizationReady = _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady;
        }
        catch
        {
            diarizationReady = false;
        }

        DashboardRecordingReadinessTextBlock.Text = _recordingCoordinator.IsRecording
            ? "Recording: active. The current session is capturing audio right now."
            : "Recording: idle. You can start manually here or wait for supported auto-detection.";
        DashboardModelReadinessTextBlock.Text = hasValidModel
            ? "Transcription model: ready."
            : "Transcription model: action needed. Download or import a valid Whisper model before relying on transcript output.";
        DashboardDiarizationReadinessTextBlock.Text = diarizationReady
            ? "Speaker labeling: optional diarization sidecar is installed."
            : "Speaker labeling: optional add-on not installed. Transcripts still work without it.";

        DashboardUpdatesReadinessTextBlock.Text = _lastUpdateCheckResult?.Status switch
        {
            AppUpdateStatusKind.UpdateAvailable => "Updates: a newer GitHub release is available.",
            AppUpdateStatusKind.Disabled => "Updates: daily GitHub checks are off.",
            _ when _liveConfig.Current.UpdateCheckEnabled && _liveConfig.Current.AutoInstallUpdatesEnabled
                => "Updates: daily checks are on and idle auto-install is enabled.",
            _ when _liveConfig.Current.UpdateCheckEnabled
                => "Updates: daily checks are on; installs stay manual until you trigger them.",
            _ => "Updates: daily GitHub checks are off.",
        };

        var recommendation = MainWindowInteractionLogic.BuildDashboardPrimaryAction(
            hasValidModel,
            _recordingCoordinator.IsRecording,
            _liveConfig.Current.UpdateCheckEnabled,
            _liveConfig.Current.AutoInstallUpdatesEnabled,
            _lastUpdateCheckResult);

        DashboardPrimaryActionHeadlineTextBlock.Text = recommendation.Headline;
        DashboardPrimaryActionBodyTextBlock.Text = recommendation.Body;
        DashboardPrimaryActionButton.Tag = recommendation.Target;
        DashboardPrimaryActionButton.Content = recommendation.ActionLabel ?? string.Empty;
        DashboardPrimaryActionButton.Visibility = string.IsNullOrWhiteSpace(recommendation.ActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private bool HasRecentLoopbackActivity(ActiveRecordingSession activeSession)
    {
        var threshold = Math.Clamp(_liveConfig.Current.AutoDetectAudioPeakThreshold, 0.01d, 1d);
        return activeSession.LoopbackRecorder.LevelHistory.HasRecentActivity(RecentCaptureActivitySampleCount, threshold);
    }

    private bool HasRecentMicrophoneActivity(ActiveRecordingSession activeSession)
    {
        var threshold = Math.Clamp(_liveConfig.Current.AutoDetectAudioPeakThreshold, 0.01d, 1d);
        return activeSession.MicrophoneRecorder?.LevelHistory.HasRecentActivity(RecentCaptureActivitySampleCount, threshold) == true;
    }

    private async Task ApplyPendingCurrentTitleAsync(CancellationToken cancellationToken)
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        var pendingTitle = CurrentMeetingTitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pendingTitle) ||
            string.Equals(pendingTitle, activeSession.Manifest.DetectedTitle, StringComparison.Ordinal))
        {
            return;
        }

        var renamed = await _recordingCoordinator.RenameActiveSessionAsync(pendingTitle, cancellationToken);
        if (!renamed)
        {
            return;
        }

        _sessionTitleDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, pendingTitle);

        UpdateCurrentMeetingEditor();
        AppendActivity($"Applied current meeting title '{pendingTitle}' before publishing.");
    }

    private void UpdateCurrentMeetingTitleStatus()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            CurrentMeetingTitleStatusTextBlock.Text = "Start a recording to set the meeting title that will drive the published filenames.";
            return;
        }

        var pendingTitle = CurrentMeetingTitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pendingTitle))
        {
            CurrentMeetingTitleStatusTextBlock.Text = "Leave the field blank to keep the detected meeting title for publishing.";
            return;
        }

        var stem = _pathBuilder.BuildFileStem(activeSession.Manifest.Platform, activeSession.Manifest.StartedAtUtc, pendingTitle);
        if (string.Equals(pendingTitle, activeSession.Manifest.DetectedTitle, StringComparison.Ordinal))
        {
            CurrentMeetingTitleStatusTextBlock.Text = $"Current publish stem: {stem}";
            return;
        }

        CurrentMeetingTitleStatusTextBlock.Text = $"Pending publish stem: {stem}. The title will be applied automatically when recording stops.";
    }

    private void RefreshWhisperModelStatus()
    {
        try
        {
            var models = _whisperModelCatalogService.ListAvailableModels(
                _liveConfig.Current.ModelCacheDir,
                _liveConfig.Current.TranscriptionModelPath);
            UpdateModelCatalog(models);
            var status = models.FirstOrDefault(model => model.IsConfigured)?.Status
                ?? _whisperModelService.Inspect(_liveConfig.Current.TranscriptionModelPath);
            ApplyWhisperModelStatusDisplayState(WhisperModelStatusDisplayStateFactory.Create(status));
        }
        catch (Exception exception)
        {
            UpdateModelCatalog(Array.Empty<WhisperModelCatalogItem>());
            ApplyWhisperModelStatusDisplayState(WhisperModelStatusDisplayStateFactory.CreateError(exception.Message));
        }
    }

    private void RefreshDiarizationAssetStatus()
    {
        try
        {
            var status = _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath);
            ApplyDiarizationAssetStatus(status);
        }
        catch (Exception exception)
        {
            DiarizationAssetStatusTextBlock.Text = "Unable to inspect diarization assets.";
            DiarizationAssetStatusTextBlock.Foreground = UnhealthyModelStatusBrush;
            DiarizationAssetDetailsTextBlock.Text = exception.Message;
        }
    }

    private async Task RefreshRemoteModelCatalogAsync(bool manual, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        Interlocked.Increment(ref _remoteModelRefreshOperations);
        UpdateModelActionButtons();

        try
        {
            var remoteModels = await _whisperModelReleaseCatalogService.ListAvailableRemoteModelsAsync(
                _liveConfig.Current.UpdateFeedUrl,
                cancellationToken);
            UpdateRemoteModelCatalog(remoteModels);

            if (remoteModels.Count == 0)
            {
                if (manual)
                {
                    ModelActionStatusTextBlock.Text = "No downloadable model assets were found in the current GitHub release.";
                }

                return;
            }

            var configuredStatus = _whisperModelService.Inspect(_liveConfig.Current.TranscriptionModelPath);
            if (configuredStatus.Kind != WhisperModelStatusKind.Valid)
            {
                ModelActionStatusTextBlock.Text =
                    "No valid local model is active yet. Choose a downloadable GitHub model below or import your own file.";
            }
            else if (manual)
            {
                ModelActionStatusTextBlock.Text = "GitHub model list refreshed.";
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            UpdateRemoteModelCatalog(Array.Empty<WhisperRemoteModelAsset>());
            if (manual)
            {
                ModelActionStatusTextBlock.Text = $"Failed to load GitHub models: {exception.Message}";
            }

            AppendActivity($"Failed to load downloadable Whisper models: {exception.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _remoteModelRefreshOperations);
            UpdateModelActionButtons();
        }
    }

    private async Task RefreshRemoteDiarizationAssetCatalogAsync(bool manual, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        Interlocked.Increment(ref _remoteDiarizationRefreshOperations);
        UpdateDiarizationActionButtons();

        try
        {
            var remoteAssets = await _diarizationAssetReleaseCatalogService.ListAvailableRemoteAssetsAsync(
                _liveConfig.Current.UpdateFeedUrl,
                cancellationToken);
            UpdateRemoteDiarizationAssetCatalog(remoteAssets);

            if (remoteAssets.Count == 0)
            {
                if (manual)
                {
                    DiarizationActionStatusTextBlock.Text = "No downloadable diarization assets were found in the current GitHub release.";
                }

                return;
            }

            if (!_diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady)
            {
                DiarizationActionStatusTextBlock.Text =
                    "No diarization sidecar is installed yet. Choose the recommended bundle or import approved assets manually.";
            }
            else if (manual)
            {
                DiarizationActionStatusTextBlock.Text = "GitHub diarization asset list refreshed.";
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            UpdateRemoteDiarizationAssetCatalog(Array.Empty<DiarizationRemoteAsset>());
            if (manual)
            {
                DiarizationActionStatusTextBlock.Text = $"Failed to load GitHub diarization assets: {exception.Message}";
            }

            AppendActivity($"Failed to load downloadable diarization assets: {exception.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _remoteDiarizationRefreshOperations);
            UpdateDiarizationActionButtons();
        }
    }

    private async Task EnsureConfiguredModelPathResolvedAsync(string source, CancellationToken cancellationToken)
    {
        var resolution = _whisperModelCatalogService.ResolveConfiguredOrFallbackModel(
            _liveConfig.Current.ModelCacheDir,
            _liveConfig.Current.TranscriptionModelPath);
        if (!resolution.UsedFallbackModel || resolution.ActiveModel is null)
        {
            return;
        }

        if (string.Equals(
                resolution.ActiveModel.ModelPath,
                _liveConfig.Current.TranscriptionModelPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _liveConfig.SaveAsync(_liveConfig.Current with
        {
            TranscriptionModelPath = resolution.ActiveModel.ModelPath,
        }, cancellationToken);

        ModelActionStatusTextBlock.Text =
            $"Configured model was unavailable. Switched to '{resolution.ActiveModel.FileName}'.";
        AppendActivity(
            $"Configured Whisper model '{resolution.RequestedModelPath}' was unavailable. " +
            $"Auto-switched to '{resolution.ActiveModel.ModelPath}' during {source}.");
    }

    private void ApplyWhisperModelStatusDisplayState(WhisperModelStatusDisplayState state)
    {
        WhisperModelStatusTextBlock.Text = state.StatusText;
        WhisperModelStatusTextBlock.Foreground = state.IsHealthy ? HealthyModelStatusBrush : UnhealthyModelStatusBrush;
        WhisperModelDetailsTextBlock.Text = state.DetailsText;
        ModelHealthBannerTextBlock.Text = state.DashboardBannerText ?? string.Empty;
        ModelHealthBanner.Visibility = string.IsNullOrWhiteSpace(state.DashboardBannerText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateDashboardReadiness();
    }

    private void ApplyDiarizationAssetStatus(DiarizationAssetInstallStatus status)
    {
        DiarizationAssetStatusTextBlock.Text = status.StatusText;
        DiarizationAssetStatusTextBlock.Foreground = status.IsReady ? HealthyModelStatusBrush : UnhealthyModelStatusBrush;
        DiarizationAssetDetailsTextBlock.Text = status.DetailsText;
        UpdateDiarizationActionButtons();
        UpdateDashboardReadiness();
    }

    private void UpdateModelActionButtons()
    {
        var isModelActionInProgress =
            _isRefreshingModelStatus ||
            Volatile.Read(ref _remoteModelRefreshOperations) > 0 ||
            _isActivatingModel ||
            _isDownloadingRemoteModel ||
            _isImportingModel;

        RefreshModelStatusButton.Content = _isRefreshingModelStatus ? "Refreshing..." : "Refresh Status";
        RefreshRemoteModelsButton.Content = Volatile.Read(ref _remoteModelRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh GitHub Models";
        DownloadSelectedRemoteModelButton.Content = _isDownloadingRemoteModel
            ? "Downloading..."
            : "Download Selected Model";
        ImportWhisperModelButton.Content = _isImportingModel
            ? "Importing..."
            : "Import Existing File";
        ActivateSelectedModelButton.Content = _isActivatingModel
            ? "Switching..."
            : "Use Selected Model";

        RefreshModelStatusButton.IsEnabled = !isModelActionInProgress;
        RefreshRemoteModelsButton.IsEnabled = !isModelActionInProgress;
        DownloadSelectedRemoteModelButton.IsEnabled = !isModelActionInProgress &&
            AvailableRemoteModelsComboBox.SelectedItem is WhisperRemoteModelListRow;
        ImportWhisperModelButton.IsEnabled = !isModelActionInProgress;
        OpenModelFolderButton.IsEnabled = true;
        ActivateSelectedModelButton.IsEnabled = !isModelActionInProgress &&
            AvailableModelsComboBox.SelectedItem is WhisperModelListRow selectedRow &&
            !selectedRow.Source.IsConfigured &&
            selectedRow.Source.Status.Kind == WhisperModelStatusKind.Valid;
        ModelOperationProgressBar.Visibility = isModelActionInProgress ? Visibility.Visible : Visibility.Collapsed;
        ModelOperationProgressBar.IsIndeterminate = _isDownloadingRemoteModel
            ? _modelDownloadProgressIsIndeterminate
            : true;
        ModelOperationProgressBar.Value = _isDownloadingRemoteModel && !_modelDownloadProgressIsIndeterminate
            ? _modelDownloadProgressPercent
            : 0;
    }

    private void ResetModelDownloadProgress()
    {
        _modelDownloadProgressPercent = 0;
        _modelDownloadProgressIsIndeterminate = true;
    }

    private void ReportRemoteModelDownloadProgress(WhisperRemoteModelAsset model, FileDownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            _modelDownloadProgressIsIndeterminate = false;
            _modelDownloadProgressPercent = Math.Clamp(
                progress.BytesDownloaded / (double)progress.TotalBytes.Value * 100d,
                0d,
                100d);
            ModelActionStatusTextBlock.Text =
                $"Downloading '{model.FileName}' from GitHub... {_modelDownloadProgressPercent:0}% " +
                $"({FormatBytes(progress.BytesDownloaded)} of {FormatBytes(progress.TotalBytes.Value)})";
        }
        else
        {
            _modelDownloadProgressIsIndeterminate = true;
            ModelActionStatusTextBlock.Text =
                $"Downloading '{model.FileName}' from GitHub... {FormatBytes(progress.BytesDownloaded)} downloaded";
        }

        ModelOperationProgressBar.IsIndeterminate = _modelDownloadProgressIsIndeterminate;
        ModelOperationProgressBar.Value = _modelDownloadProgressIsIndeterminate
            ? 0
            : _modelDownloadProgressPercent;
    }

    private void UpdateDiarizationActionButtons()
    {
        var isBusy =
            _isRefreshingModelStatus ||
            Volatile.Read(ref _remoteDiarizationRefreshOperations) > 0 ||
            _isDownloadingRemoteDiarizationAsset ||
            _isImportingDiarizationAsset;

        RefreshRemoteDiarizationAssetsButton.Content = Volatile.Read(ref _remoteDiarizationRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh Diarization Assets";
        DownloadSelectedRemoteDiarizationAssetButton.Content = _isDownloadingRemoteDiarizationAsset
            ? "Downloading..."
            : "Download Selected Asset";
        ImportDiarizationAssetButton.Content = _isImportingDiarizationAsset
            ? "Importing..."
            : "Import Existing File";

        RefreshRemoteDiarizationAssetsButton.IsEnabled = !isBusy;
        DownloadSelectedRemoteDiarizationAssetButton.IsEnabled = !isBusy &&
            AvailableRemoteDiarizationAssetsComboBox.SelectedItem is DiarizationRemoteAssetListRow;
        ImportDiarizationAssetButton.IsEnabled = !isBusy;
        OpenDiarizationFolderButton.IsEnabled = true;
        DiarizationOperationProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetDiarizationAvailabilityText(DiarizationAssetInstallStatus status)
    {
        return status.IsReady ? "available" : "still unavailable";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000d:0.##} GB";
        }

        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000d:0.##} MB";
        }

        if (bytes >= 1_000)
        {
            return $"{bytes / 1_000d:0.##} KB";
        }

        return $"{bytes} bytes";
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void AppendActivity(string message)
    {
        _logger.Log(message);
        ActivityTextBox.AppendText($"{DateTimeOffset.Now:t} {message}{Environment.NewLine}");
        ActivityTextBox.ScrollToEnd();
    }

    private void LogDetectionChange(DetectionDecision? decision)
    {
        var fingerprint = decision is null
            ? "none"
            : $"{decision.Platform}|{decision.ShouldStart}|{decision.ShouldKeepRecording}|{decision.SessionTitle}|{decision.Reason}";

        if (string.Equals(fingerprint, _lastDetectionFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastDetectionFingerprint = fingerprint;

        if (decision is null)
        {
            AppendActivity("Detection scan found no meeting candidates.");
            return;
        }

        var signals = string.Join(
            "; ",
            decision.Signals.Select(signal => $"{signal.Source}='{signal.Value}' w={signal.Weight:0.00}"));
        AppendActivity(
            $"Detection candidate: platform={decision.Platform}; title='{decision.SessionTitle}'; confidence={decision.Confidence:P0}; shouldStart={decision.ShouldStart}; shouldKeepRecording={decision.ShouldKeepRecording}; reason='{decision.Reason}'; signals={signals}");
    }

    private void AppendAutoStopStatus(string message)
    {
        if (string.Equals(_lastAutoStopFingerprint, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastAutoStopFingerprint = message;
        AppendActivity(message);
    }

    private void AudioArtifactLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetMeetingRowFromSender(sender) is { } row)
        {
            OpenPath(row.Source.AudioPath ?? string.Empty);
        }
    }

    private void AudioArtifactFolderLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetMeetingRowFromSender(sender) is { } row)
        {
            OpenContainingFolder(row.Source.AudioPath ?? string.Empty);
        }
    }

    private void TranscriptArtifactLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetMeetingRowFromSender(sender) is { } row)
        {
            OpenPath(row.PrimaryTranscriptPath ?? string.Empty);
        }
    }

    private void TranscriptArtifactFolderLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetMeetingRowFromSender(sender) is { } row)
        {
            OpenContainingFolder(row.PrimaryTranscriptPath ?? string.Empty);
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            var target = File.Exists(path)
                ? path
                : Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path);

            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException($"The path '{path}' could not be resolved.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to open path '{path}': {exception.Message}");
        }
    }

    private void OpenContainingFolder(string path)
    {
        try
        {
            var target = File.Exists(path)
                ? Path.GetDirectoryName(path)
                : Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path);

            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException($"The containing folder for '{path}' could not be resolved.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to open containing folder for '{path}': {exception.Message}");
        }
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to open URL '{url}': {exception.Message}");
        }
    }

    private static MeetingListRow? TryGetMeetingRowFromSender(object sender)
    {
        return sender switch
        {
            FrameworkContentElement contentElement => contentElement.DataContext as MeetingListRow,
            FrameworkElement element => element.DataContext as MeetingListRow,
            _ => null,
        };
    }

    private void AudioCaptureGraphCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAudioCaptureGraph();
    }

    private void UpdateModelCatalog(IReadOnlyList<WhisperModelCatalogItem> models)
    {
        var rows = models
            .Select(model => new WhisperModelListRow(model))
            .ToArray();

        var selectedPath = AvailableModelsComboBox.SelectedItem is WhisperModelListRow selectedRow
            ? selectedRow.Source.ModelPath
            : _liveConfig.Current.TranscriptionModelPath;

        AvailableModelsComboBox.ItemsSource = rows;

        var configuredRow = rows.FirstOrDefault(row => row.Source.IsConfigured);
        var nextSelectedRow =
            rows.FirstOrDefault(row => string.Equals(row.Source.ModelPath, selectedPath, StringComparison.OrdinalIgnoreCase)) ??
            configuredRow ??
            rows.FirstOrDefault();

        AvailableModelsComboBox.SelectedItem = nextSelectedRow;
        UpdateSelectedModelEditor(nextSelectedRow);
        UpdateModelActionButtons();
    }

    private void UpdateRemoteModelCatalog(IReadOnlyList<WhisperRemoteModelAsset> remoteModels)
    {
        var rows = remoteModels
            .Select(model => new WhisperRemoteModelListRow(model))
            .ToArray();

        AvailableRemoteModelsComboBox.ItemsSource = rows;
        AvailableRemoteModelsComboBox.SelectedItem = rows.FirstOrDefault(row => row.Source.IsRecommended) ?? rows.FirstOrDefault();
        UpdateSelectedRemoteModelEditor(AvailableRemoteModelsComboBox.SelectedItem as WhisperRemoteModelListRow);
        UpdateModelActionButtons();
    }

    private void UpdateRemoteDiarizationAssetCatalog(IReadOnlyList<DiarizationRemoteAsset> remoteAssets)
    {
        var rows = remoteAssets
            .Select(asset => new DiarizationRemoteAssetListRow(asset))
            .ToArray();

        AvailableRemoteDiarizationAssetsComboBox.ItemsSource = rows;
        AvailableRemoteDiarizationAssetsComboBox.SelectedItem = rows.FirstOrDefault(row => row.Source.IsRecommended) ?? rows.FirstOrDefault();
        UpdateSelectedRemoteDiarizationAssetEditor(AvailableRemoteDiarizationAssetsComboBox.SelectedItem as DiarizationRemoteAssetListRow);
        UpdateDiarizationActionButtons();
    }

    private void UpdateAudioCaptureGraph()
    {
        var width = AudioCaptureGraphCanvas.ActualWidth;
        var height = AudioCaptureGraphCanvas.ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var (levels, currentPeak, statusText) = BuildAudioGraphSnapshot();
        AudioCaptureGraphPolyline.Points = BuildAudioGraphPoints(levels, width, height);
        AudioCaptureGraphStatusTextBlock.Text = statusText + $" Current peak: {currentPeak:0.000}.";
    }

    private (double[] Levels, double CurrentPeak, string StatusText) BuildAudioGraphSnapshot()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return (
                Enumerable.Repeat(0d, AudioGraphPointCount).ToArray(),
                0d,
                "Start a recording to see live capture activity.");
        }

        var loopbackLevels = activeSession.LoopbackRecorder.LevelHistory.Snapshot(AudioGraphPointCount);
        var microphoneLevels = activeSession.MicrophoneRecorder?.LevelHistory.Snapshot(AudioGraphPointCount);
        var combined = new double[AudioGraphPointCount];
        for (var index = 0; index < combined.Length; index++)
        {
            combined[index] = Math.Max(loopbackLevels[index], microphoneLevels?[index] ?? 0d);
        }

        var currentPeak = combined.Length == 0 ? 0d : combined[^1];
        var statusText = activeSession.MicrophoneRecorder is null
            ? "Showing recent loopback capture levels during the active recording."
            : "Showing recent combined loopback and microphone capture levels during the active recording.";
        return (combined, currentPeak, statusText);
    }

    private static PointCollection BuildAudioGraphPoints(IReadOnlyList<double> levels, double width, double height)
    {
        var points = new PointCollection(levels.Count);
        if (levels.Count == 0)
        {
            points.Add(new Point(0d, height - 2d));
            points.Add(new Point(width, height - 2d));
            return points;
        }

        var usableHeight = Math.Max(1d, height - 4d);
        var denominator = Math.Max(1, levels.Count - 1);
        for (var index = 0; index < levels.Count; index++)
        {
            var x = (width * index) / denominator;
            var level = Math.Clamp(levels[index], 0d, 1d);
            var y = height - 2d - (usableHeight * level);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private void UpdateSelectedModelEditor(WhisperModelListRow? row)
    {
        if (row is null)
        {
            SelectedModelSummaryTextBlock.Text = "No local Whisper models are currently available. Download or import a model to make one selectable here.";
            UpdateModelActionButtons();
            return;
        }

        var sourceText = row.Source.IsManaged ? "managed model folder" : "custom external path";
        var activeText = row.Source.IsConfigured ? "This model is currently active." : "This model is available but not active.";
        SelectedModelSummaryTextBlock.Text = $"{row.StatusText}. Source: {sourceText}. Path: {row.Source.ModelPath}. {activeText}";
        UpdateModelActionButtons();
    }

    private void UpdateSelectedRemoteModelEditor(WhisperRemoteModelListRow? row)
    {
        if (row is null)
        {
            SelectedRemoteModelSummaryTextBlock.Text =
                "No downloadable GitHub model assets were found in the current release. Upload one or more ggml *.bin files to the release to make them appear here.";
            UpdateModelActionButtons();
            return;
        }

        var sizeText = row.Source.FileSizeBytes.HasValue
            ? FormatBytes(row.Source.FileSizeBytes.Value)
            : "unknown size";
        SelectedRemoteModelSummaryTextBlock.Text =
            $"{row.Source.Description} Download size: {sizeText}. " +
            (row.Source.IsRecommended
                ? "This is the recommended default."
                : "You can download this model and switch to it immediately after install.");
        UpdateModelActionButtons();
    }

    private void UpdateSelectedRemoteDiarizationAssetEditor(DiarizationRemoteAssetListRow? row)
    {
        if (row is null)
        {
            SelectedRemoteDiarizationAssetSummaryTextBlock.Text =
                "No downloadable diarization sidecar assets were found in the current release. Upload a sidecar bundle or supporting model assets to make them appear here.";
            UpdateDiarizationActionButtons();
            return;
        }

        var sizeText = row.Source.FileSizeBytes.HasValue
            ? FormatBytes(row.Source.FileSizeBytes.Value)
            : "unknown size";
        SelectedRemoteDiarizationAssetSummaryTextBlock.Text =
            $"{row.Source.Description} Download size: {sizeText}. " +
            (row.Source.IsRecommended
                ? "This is the recommended starting point."
                : "Download this after the bundle if your sidecar setup needs extra supporting files.");
        UpdateDiarizationActionButtons();
    }

    private sealed class MeetingListRow
    {
        public MeetingListRow(MeetingOutputRecord source)
        {
            Source = source;
            Title = source.Title;
            StartedAtUtc = source.StartedAtUtc == DateTimeOffset.MinValue
                ? "Unknown"
                : source.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Duration = FormatDuration(source.Duration);
            Platform = source.Platform.ToString();
            Status = source.ManifestState?.ToString() ?? (
                !string.IsNullOrWhiteSpace(source.ReadyMarkerPath) ? SessionState.Published.ToString() :
                !string.IsNullOrWhiteSpace(source.MarkdownPath) || !string.IsNullOrWhiteSpace(source.JsonPath) ? "Transcript files present" :
                !string.IsNullOrWhiteSpace(source.AudioPath) ? "Audio only" :
                "Unknown");
            CanRegenerateTranscript =
                !string.IsNullOrWhiteSpace(source.ManifestPath) ||
                !string.IsNullOrWhiteSpace(source.AudioPath);
            RegenerationStatusText =
                !string.IsNullOrWhiteSpace(source.ManifestPath)
                    ? "Transcript re-generation is available for this session."
                    : !string.IsNullOrWhiteSpace(source.AudioPath)
                        ? "Transcript re-generation is available by rebuilding a work session from the published audio file."
                        : "Transcript re-generation is unavailable because no source audio is available.";
            AudioFileName = Path.GetFileName(source.AudioPath) ?? "Missing";
            TranscriptFileName = Path.GetFileName(source.MarkdownPath ?? source.JsonPath) ?? "Missing";
            PrimaryTranscriptPath = source.MarkdownPath ?? source.JsonPath;
        }

        public MeetingOutputRecord Source { get; }

        public string Title { get; set; }

        public string StartedAtUtc { get; set; }

        public string Duration { get; set; }

        public string Platform { get; set; }

        public string Status { get; set; }

        public bool CanRegenerateTranscript { get; set; }

        public string RegenerationStatusText { get; set; }

        public string AudioFileName { get; set; }

        public string TranscriptFileName { get; set; }

        public string? PrimaryTranscriptPath { get; set; }

        private static string FormatDuration(TimeSpan? duration)
        {
            if (duration is null)
            {
                return "Unknown";
            }

            var value = duration.Value;
            if (value.TotalHours >= 1d)
            {
                return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            }

            return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private sealed class WhisperModelListRow
    {
        public WhisperModelListRow(WhisperModelCatalogItem source)
        {
            Source = source;
        }

        public WhisperModelCatalogItem Source { get; }

        public string DisplayText =>
            $"{Source.FileName} | {StatusText}" +
            (Source.IsConfigured ? " | active" : string.Empty) +
            (Source.IsManaged ? string.Empty : " | external");

        public string StatusText => Source.Status.Kind switch
        {
            WhisperModelStatusKind.Valid => "ready",
            WhisperModelStatusKind.Missing => "missing",
            WhisperModelStatusKind.Invalid => "invalid",
            _ => "unknown",
        };
    }

    private sealed class WhisperRemoteModelListRow
    {
        public WhisperRemoteModelListRow(WhisperRemoteModelAsset source)
        {
            Source = source;
        }

        public WhisperRemoteModelAsset Source { get; }

        public string DisplayText =>
            $"{Source.FileName}" +
            (Source.IsRecommended ? " | recommended" : string.Empty) +
            (Source.FileSizeBytes.HasValue ? $" | {FormatBytes(Source.FileSizeBytes.Value)}" : string.Empty);
    }

    private sealed class DiarizationRemoteAssetListRow
    {
        public DiarizationRemoteAssetListRow(DiarizationRemoteAsset source)
        {
            Source = source;
        }

        public DiarizationRemoteAsset Source { get; }

        public string DisplayText =>
            $"{Source.FileName}" +
            (Source.IsRecommended ? " | recommended" : string.Empty) +
            $" | {Source.Kind}" +
            (Source.FileSizeBytes.HasValue ? $" | {FormatBytes(Source.FileSizeBytes.Value)}" : string.Empty);
    }

    private sealed class SpeakerLabelEditorRow
    {
        private readonly Action _onEditedLabelChanged;
        private string _editedLabel;

        public SpeakerLabelEditorRow(string originalLabel, Action onEditedLabelChanged)
        {
            OriginalLabel = originalLabel;
            _editedLabel = originalLabel;
            _onEditedLabelChanged = onEditedLabelChanged;
        }

        public string OriginalLabel { get; }

        public string EditedLabel
        {
            get => _editedLabel;
            set
            {
                if (string.Equals(_editedLabel, value, StringComparison.Ordinal))
                {
                    return;
                }

                _editedLabel = value;
                _onEditedLabelChanged();
            }
        }
    }
}
