using AppPlatform.Shell.Wpf;
using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using MeetingRecorder.Product;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeetingRecorder.App;

internal enum MeetingRefreshMode
{
    Fast = 0,
    Full = 1,
}

public partial class MainWindow : Window
{
    private const int AudioGraphPointCount = 120;
    private const int RecentCaptureActivitySampleCount = 24;
    private const string MeetingCleanupHistoricalReviewMarkerFileName = "meeting-cleanup-review-v1.done";
    private const string SpeakerLabelingSetupGuideFallbackUrl = "https://github.com/shp847/meetingrecorder/blob/main/SETUP.md#speaker-labeling-optional";
    private const string TeamsThirdPartyApiGuideUrl = "https://support.microsoft.com/en-au/office/connect-to-third-party-devices-in-microsoft-teams-aabca9f2-47bb-407f-9f9b-81a104a883d6";
    private static readonly TimeSpan ShutdownUpdateCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly Brush HealthyModelStatusBrush = CreateBrush(0x2E, 0x7D, 0x32);
    private static readonly Brush HealthyModelStatusChipBackgroundBrush = CreateBrush(0xE8, 0xF3, 0xE8);
    private static readonly Brush HealthyModelStatusChipBorderBrush = CreateBrush(0xA8, 0xCC, 0xAB);
    private static readonly Brush UnhealthyModelStatusBrush = CreateBrush(0xB3, 0x26, 0x1E);
    private static readonly Brush UnhealthyModelStatusChipBackgroundBrush = CreateBrush(0xFB, 0xEB, 0xE8);
    private static readonly Brush UnhealthyModelStatusChipBorderBrush = CreateBrush(0xE3, 0xB1, 0xA8);
    private static readonly TimeSpan ScheduledUpdateCheckCadence = TimeSpan.FromDays(1);

    private readonly LiveAppConfig _liveConfig;
    private readonly FileLogWriter _logger;
    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;
    private readonly MeetingOutputCatalogService _meetingOutputCatalogService;
    private readonly MeetingCleanupExecutionService _meetingCleanupExecutionService;
    private readonly AutoStartRegistrationService _autoStartRegistrationService;
    private readonly AppUpdateService _appUpdateService;
    private readonly AppUpdateSchedulePolicy _appUpdateSchedulePolicy;
    private readonly AppUpdateInstallPolicy _appUpdateInstallPolicy;
    private readonly RecordingSessionCoordinator _recordingCoordinator;
    private readonly WindowMeetingDetector _meetingDetector;
    private readonly IAudioActivityProbe _microphoneActivityProbe;
    private readonly ProcessingQueueService _processingQueue;
    private readonly WhisperModelService _whisperModelService;
    private readonly WhisperModelCatalogService _whisperModelCatalogService;
    private readonly WhisperModelReleaseCatalogService _whisperModelReleaseCatalogService;
    private readonly DiarizationAssetCatalogService _diarizationAssetCatalogService;
    private readonly DiarizationAssetReleaseCatalogService _diarizationAssetReleaseCatalogService;
    private readonly AppConfigStore _setupConfigStore;
    private readonly ModelProvisioningResultStore _modelProvisioningResultStore;
    private readonly MeetingRecorderModelCatalogService _meetingRecorderModelCatalogService;
    private readonly MeetingRecorderModelCatalog _bundledModelCatalog;
    private readonly ModelProvisioningService _modelProvisioningService;
    private readonly ExternalAudioImportService _externalAudioImportService;
    private readonly AutoRecordingContinuityPolicy _autoRecordingContinuityPolicy;
    private readonly TeamsIntegrationProbeService _teamsIntegrationProbeService;
    private readonly TeamsDetectionArbitrator _teamsDetectionArbitrator;
    private readonly SessionTitleDraftTracker _sessionTitleDraftTracker;
    private readonly SessionTitleDraftTracker _sessionProjectDraftTracker;
    private readonly SessionTitleDraftTracker _sessionKeyAttendeesDraftTracker;
    private readonly MeetingTitleSuggestionService _meetingTitleSuggestionService;
    private readonly TeamsLiveAttendeeCaptureService _teamsLiveAttendeeCaptureService;
    private readonly OutlookCalendarMeetingTitleProvider _outlookCalendarMeetingTitleProvider;
    private readonly MeetingsAttendeeBackfillService _meetingsAttendeeBackfillService;
    private readonly DispatcherTimer _detectionTimer;
    private readonly DispatcherTimer _audioGraphTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _processingQueueStatusTimer;
    private readonly DispatcherTimer _currentMeetingOptionalMetadataSaveTimer;
    private readonly SemaphoreSlim _updateOperationGate = new(1, 1);
    private readonly SemaphoreSlim _externalAudioImportGate = new(1, 1);
    private readonly SemaphoreSlim _teamsAttendeeCaptureGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly double[] _audioGraphLoopbackLevels = new double[AudioGraphPointCount];
    private readonly double[] _audioGraphMicrophoneLevels = new double[AudioGraphPointCount];
    private readonly double[] _audioGraphCombinedLevels = new double[AudioGraphPointCount];
    private readonly PointCollection _audioGraphPoints = new(AudioGraphPointCount);
    private DateTimeOffset? _lastPositiveDetectionUtc;
    private RecentAutoStopContext? _recentAutoStopContext;
    private ManualStopSuppressionContext? _manualStopSuppressionContext;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private bool _isRecordingTransitionInProgress;
    private bool _isAutoStopTransitionInProgress;
    private int? _autoStopCountdownSecondsRemaining;
    private int _meetingBaselineRefreshOperations;
    private int _meetingCleanupRefreshOperations;
    private int _meetingAttendeeBackfillOperations;
    private bool _isRenamingMeeting;
    private bool _isRetryingMeeting;
    private bool _isSuggestingMeetingTitle;
    private bool _isApplyingSuggestedMeetingTitles;
    private bool _isApplyingSpeakerNames;
    private bool _isUpdatingMeetingProject;
    private bool _isMergingMeetings;
    private bool _isSplittingMeeting;
    private bool _isApplyingMeetingCleanupRecommendations;
    private bool _isDismissingMeetingCleanupRecommendations;
    private bool _isApplyingSafeMeetingCleanupFixes;
    private bool _isDeletingMeetings;
    private bool _isArchivingMeetings;
    private bool _isUpdatingRushProcessing;
    private bool _isSavingConfig;
    private bool _isRunningTeamsIntegrationProbe;
    private int _updateCheckOperations;
    private bool _isPreparingUpdateInstall;
    private bool _isDownloadingUpdate;
    private bool _isRefreshingModelStatus;
    private bool _isStartupWarmupQueued;
    private bool _isDeferredStartupMaintenanceQueued;
    private bool _isDeferredMeetingsRefreshQueued;
    private bool _hasPendingMeetingsRefreshRequest;
    private bool _hasCompletedFullMeetingsRefresh;
    private bool _isUpdatingMeetingsWorkspaceControls;
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
    private bool _isMicCaptureEnablePromptInProgress;
    private bool _isUiReady;
    private int _meetingRefreshVersion;
    private int _detectionCycleActive;
    private int _detectionCycleGeneration;
    private CancellationTokenSource? _meetingBackgroundWorkCts;
    private HashSet<string> _meetingAttendeeBackfillAttemptedStems = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _meetingAttendeeBackfillForcedStems = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastDetectionFingerprint;
    private string? _lastAutoStopFingerprint;
    private DetectedAudioSource? _lastObservedDetectedAudioSource;
    private DetectionDecision? _lastObservedDetectionDecision;
    private string? _micCaptureEnablePromptedSessionId;
    private string? _splitMeetingSuggestionStem;
    private string? _quietTeamsAutoStartFingerprint;
    private DateTimeOffset? _quietTeamsAutoStartFirstObservedUtc;
    private string? _lastTeamsProbeBaselineSummary;
    private string? _pendingMeetingsRefreshSelectedStem;
    private AppUpdateCheckResult? _lastUpdateCheckResult;
    private WhisperModelStatusDisplayState? _currentWhisperModelDisplayState;
    private DiarizationAssetInstallStatus? _currentDiarizationAssetStatus;
    private ModelsTabSetupState? _currentTranscriptionSetupState;
    private ModelsTabSetupState? _currentSpeakerLabelingSetupState;
    private SettingsHostWindow? _settingsWindow;
    private HelpHostWindow? _helpWindow;
    private UIElement? _detachedSettingsBody;
    private ShellStatusState? _shellStatusOverride;
    private ConfigEditorSnapshot? _pendingConfigEditorSnapshotRestore;
    private bool _isSynchronizingSpeakerLabelingModeSelectors;
    private MeetingCleanupRecommendation[] _meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();
    private MeetingListRow[] _allMeetingRows = Array.Empty<MeetingListRow>();
    private Dictionary<string, bool> _meetingGroupExpansionStates = new(StringComparer.Ordinal);
    private bool _isApplyingMeetingGroupExpansionState;
    private AppShutdownMode _shutdownMode = AppShutdownMode.Deferred;
    private MeetingRefreshMode _pendingMeetingsRefreshMode = MeetingRefreshMode.Fast;
    private ProcessingQueueStatusSnapshot _latestProcessingQueueStatusSnapshot;
    private string? _currentMeetingsRefreshStateText;
    private bool IsShutdownRequested => _shutdownInProgress || _lifetimeCts.IsCancellationRequested;

    public MainWindow(LiveAppConfig liveConfig, FileLogWriter logger)
    {
        InitializeComponent();
        AttachSetupSectionsToSettingsHosts();
        _liveConfig = liveConfig;
        _logger = logger;

        _pathBuilder = new ArtifactPathBuilder();
        _manifestStore = new SessionManifestStore(_pathBuilder);
        _meetingOutputCatalogService = new MeetingOutputCatalogService(_pathBuilder);
        _meetingCleanupExecutionService = new MeetingCleanupExecutionService(_pathBuilder, _meetingOutputCatalogService);
        _autoStartRegistrationService = new AutoStartRegistrationService();
        _appUpdateService = new AppUpdateService();
        _appUpdateSchedulePolicy = new AppUpdateSchedulePolicy();
        _appUpdateInstallPolicy = new AppUpdateInstallPolicy();
        _outlookCalendarMeetingTitleProvider = new OutlookCalendarMeetingTitleProvider();
        var calendarMeetingMetadataEnricher = new CalendarMeetingMetadataEnricher(
            _outlookCalendarMeetingTitleProvider,
            _manifestStore);
        _recordingCoordinator = new RecordingSessionCoordinator(
            liveConfig,
            _manifestStore,
            _pathBuilder,
            logger);
        _meetingDetector = new WindowMeetingDetector(
            liveConfig,
            new MeetingDetectionEvaluator(),
            new SystemAudioActivityProbe(),
            new MeetingTitleEnricher(_outlookCalendarMeetingTitleProvider),
            WindowMeetingDetector.EnumerateCandidateWindows,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMinutes(2),
            logger.Log);
        _meetingTitleSuggestionService = new MeetingTitleSuggestionService(_outlookCalendarMeetingTitleProvider);
        _meetingsAttendeeBackfillService = new MeetingsAttendeeBackfillService(
            _outlookCalendarMeetingTitleProvider,
            _meetingOutputCatalogService,
            new MeetingsAttendeeBackfillCacheService());
        _microphoneActivityProbe = new SystemMicrophoneActivityProbe();
        _processingQueue = new ProcessingQueueService(
            liveConfig,
            _manifestStore,
            logger,
            calendarMeetingMetadataEnricher);
        _latestProcessingQueueStatusSnapshot = _processingQueue.GetStatusSnapshot();
        _whisperModelService = new WhisperModelService(new WhisperNetModelDownloader());
        _whisperModelCatalogService = new WhisperModelCatalogService(_whisperModelService);
        var updateFeedClient = new HttpAppUpdateFeedClient();
        _whisperModelReleaseCatalogService = new WhisperModelReleaseCatalogService(updateFeedClient, _whisperModelService);
        _diarizationAssetCatalogService = new DiarizationAssetCatalogService();
        _diarizationAssetReleaseCatalogService = new DiarizationAssetReleaseCatalogService(updateFeedClient, _diarizationAssetCatalogService);
        _setupConfigStore = new AppConfigStore(_liveConfig.ConfigPath);
        _modelProvisioningResultStore = new ModelProvisioningResultStore(_liveConfig.ConfigPath);
        _meetingRecorderModelCatalogService = new MeetingRecorderModelCatalogService();
        _bundledModelCatalog = _meetingRecorderModelCatalogService.LoadBundledCatalog();
        _modelProvisioningService = new ModelProvisioningService(
            _setupConfigStore,
            _modelProvisioningResultStore,
            _meetingRecorderModelCatalogService,
            _whisperModelService,
            _whisperModelReleaseCatalogService,
            _diarizationAssetCatalogService,
            _diarizationAssetReleaseCatalogService);
        _externalAudioImportService = new ExternalAudioImportService(_pathBuilder);
        _autoRecordingContinuityPolicy = new AutoRecordingContinuityPolicy();
        var teamsThirdPartyApiAdapter = new UnavailableTeamsThirdPartyApiAdapter();
        _teamsIntegrationProbeService = new TeamsIntegrationProbeService(
            () => _meetingDetector.DetectBestCandidateAsync(_lifetimeCts.Token),
            teamsThirdPartyApiAdapter);
        _teamsDetectionArbitrator = new TeamsDetectionArbitrator(
            teamsThirdPartyApiAdapter);
        _sessionTitleDraftTracker = new SessionTitleDraftTracker();
        _sessionProjectDraftTracker = new SessionTitleDraftTracker();
        _sessionKeyAttendeesDraftTracker = new SessionTitleDraftTracker();
        _teamsLiveAttendeeCaptureService = new TeamsLiveAttendeeCaptureService();
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
        _processingQueueStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _processingQueueStatusTimer.Tick += ProcessingQueueStatusTimer_OnTick;
        _currentMeetingOptionalMetadataSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        _currentMeetingOptionalMetadataSaveTimer.Tick += CurrentMeetingOptionalMetadataSaveTimer_OnTick;
        _liveConfig.Changed += LiveConfig_OnChanged;
        _processingQueue.StatusChanged += ProcessingQueue_OnStatusChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
        InitializeConfigEditorSelectionControls();
        RegisterConfigEditorChangeHandlers();
        InitializeMeetingsWorkspaceControls();

        Title = AppBranding.DisplayNameWithVersion;
        ProductHeadingTextBlock.Text = AppBranding.ProductName.ToUpperInvariant();
        ApplyConfigToUi(_liveConfig.Current, "Initial config loaded.", refreshSetupDiagnostics: false);
        UpdateCurrentMeetingEditor();
        UpdateSelectedMeetingEditor(null);
        UpdateSelectedMeetingInspector(null);
        ApplyUpdateCheckResult(null, manual: false);
        UpdateUi(
            "Ready to record.",
            MainWindowInteractionLogic.BuildDetectionSummary(null, _liveConfig.Current.AutoDetectEnabled));
        UpdateModelActionButtons();
        UpdateDiarizationActionButtons();
        UpdateModelsTabGuidance();
        UpdateConfigActionState();
        UpdateAudioCaptureGraph();
        UpdateCaptureStatusSurface();
        UpdateDashboardReadiness();
        UpdateMeetingsRefreshStateText();
        UpdateProcessingQueueStatusUi();
        UpdateProcessingQueueStatusTimerState();
        _isUiReady = true;
        UpdateMeetingActionState();
    }

    private void AttachSetupSectionsToSettingsHosts()
    {
        if (DetachSetupBody(SettingsSetupTranscriptionBodyHostBorder) is { } transcriptionBody)
        {
            SettingsSetupTranscriptionSectionHost.Content = transcriptionBody;
        }

        if (DetachSetupBody(SettingsSetupSpeakerLabelingBodyHostBorder) is { } speakerLabelingBody)
        {
            SettingsSetupSpeakerLabelingSectionHost.Content = speakerLabelingBody;
        }
    }

    private void InitializeMeetingsWorkspaceControls()
    {
        _isUpdatingMeetingsWorkspaceControls = true;
        try
        {
            MeetingsViewModeComboBox.DisplayMemberPath = nameof(SelectionOption<MeetingsViewMode>.Label);
            MeetingsViewModeComboBox.SelectedValuePath = nameof(SelectionOption<MeetingsViewMode>.Value);
            MeetingsViewModeComboBox.ItemsSource = new[]
            {
                new SelectionOption<MeetingsViewMode>(MeetingsViewMode.Table, "Table"),
                new SelectionOption<MeetingsViewMode>(MeetingsViewMode.Grouped, "Grouped"),
            };

            MeetingsSortKeyComboBox.DisplayMemberPath = nameof(SelectionOption<MeetingsSortKey>.Label);
            MeetingsSortKeyComboBox.SelectedValuePath = nameof(SelectionOption<MeetingsSortKey>.Value);
            MeetingsSortKeyComboBox.ItemsSource = new[]
            {
                new SelectionOption<MeetingsSortKey>(MeetingsSortKey.Started, "Started"),
                new SelectionOption<MeetingsSortKey>(MeetingsSortKey.Title, "Title"),
                new SelectionOption<MeetingsSortKey>(MeetingsSortKey.Duration, "Duration"),
                new SelectionOption<MeetingsSortKey>(MeetingsSortKey.Platform, "Platform"),
            };

            MeetingsSortDirectionComboBox.DisplayMemberPath = nameof(SelectionOption<bool>.Label);
            MeetingsSortDirectionComboBox.SelectedValuePath = nameof(SelectionOption<bool>.Value);
            MeetingsSortDirectionComboBox.ItemsSource = new[]
            {
                new SelectionOption<bool>(true, "Descending"),
                new SelectionOption<bool>(false, "Ascending"),
            };

            MeetingsGroupKeyComboBox.DisplayMemberPath = nameof(SelectionOption<MeetingsGroupKey>.Label);
            MeetingsGroupKeyComboBox.SelectedValuePath = nameof(SelectionOption<MeetingsGroupKey>.Value);
            MeetingsGroupKeyComboBox.ItemsSource = new[]
            {
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Week, "Week"),
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Month, "Month"),
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Platform, "Platform"),
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Status, "Status"),
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.ClientProject, "Client / project"),
                new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Attendee, "Attendee"),
            };
        }
        finally
        {
            _isUpdatingMeetingsWorkspaceControls = false;
        }
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
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (IsShutdownRequested)
            {
                return;
            }

            UpdateAudioCaptureGraph();
            UpdateCurrentRecordingElapsedText();
            UpdateAudioGraphTimerState();
            ScheduleStartupWarmup();
            await TrySurfaceInstallerProvisioningResultAsync();
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

    private void ScheduleStartupWarmup()
    {
        if (_isStartupWarmupQueued || IsShutdownRequested)
        {
            return;
        }

        _isStartupWarmupQueued = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunStartupWarmupAsync()),
            DispatcherPriority.Background);
    }

    private async Task RunStartupWarmupAsync()
    {
        try
        {
            TrySyncLaunchOnLoginSetting(_liveConfig.Current, "startup");
            await EnsureConfiguredModelPathResolvedAsync("startup", _lifetimeCts.Token);
            if (IsShutdownRequested)
            {
                return;
            }

            RefreshWhisperModelStatus();
            RefreshDiarizationAssetStatus();
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "startup warmup");
            ScheduleDeferredStartupMaintenance();
        }
        catch (OperationCanceledException)
        {
            AppendActivity("Startup warmup was canceled during shutdown.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Startup warmup error: {exception.Message}");
        }
        finally
        {
            if (!IsShutdownRequested)
            {
                EnsureInteractiveTimersStarted();
            }

            _isStartupWarmupQueued = false;
        }
    }

    private async Task TrySurfaceInstallerProvisioningResultAsync()
    {
        var provisioningResult = await _modelProvisioningResultStore.TryConsumeAsync(_lifetimeCts.Token);
        if (provisioningResult is null)
        {
            return;
        }

        ModelActionStatusTextBlock.Text = provisioningResult.Transcription.Detail;
        DiarizationActionStatusTextBlock.Text = provisioningResult.SpeakerLabeling.Detail;

        if (provisioningResult.RequiresFirstLaunchSetupBeforeRecording || !provisioningResult.Transcription.IsReady)
        {
            MessageBox.Show(
                "Meeting Recorder finished installing, but transcription setup still needs to finish before recording can start. Open Settings > Setup to retry the Standard download, try Higher Accuracy, or import an approved local model.",
                AppBranding.DisplayNameWithVersion,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            OpenSetupWindow(
                SetupWindowSection.Transcription,
                SettingsTranscriptionSetupSectionBorder,
                _currentTranscriptionSetupState,
                ModelActionStatusTextBlock);
            return;
        }

        if (provisioningResult.Transcription.RetryRecommended ||
            provisioningResult.SpeakerLabeling.RetryRecommended)
        {
            var retryTargets = new List<string>();
            if (provisioningResult.Transcription.RetryRecommended)
            {
                retryTargets.Add("Higher Accuracy transcription");
            }

            if (provisioningResult.SpeakerLabeling.RetryRecommended)
            {
                retryTargets.Add("Higher Accuracy speaker labeling");
            }

            MessageBox.Show(
                "Meeting Recorder finished setup and transcription is ready. " +
                $"{string.Join(" and ", retryTargets)} did not finish during install. Retry it later from Settings > Setup.",
                AppBranding.DisplayNameWithVersion,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void EnsureInteractiveTimersStarted()
    {
        if (!_detectionTimer.IsEnabled)
        {
            _detectionTimer.Start();
        }

        if (!_updateTimer.IsEnabled)
        {
            _updateTimer.Start();
        }
    }

    private void ScheduleDeferredStartupMaintenance()
    {
        if (_isDeferredStartupMaintenanceQueued || IsShutdownRequested)
        {
            return;
        }

        _isDeferredStartupMaintenanceQueued = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunDeferredStartupMaintenanceAsync()),
            DispatcherPriority.Background);
    }

    private async Task RunDeferredStartupMaintenanceAsync()
    {
        try
        {
            if (IsShutdownRequested)
            {
                return;
            }

            await _processingQueue.ResumePendingSessionsAsync(_lifetimeCts.Token);
            await RunExternalAudioImportCycleAsync("startup", _lifetimeCts.Token);
            await RunAutomaticUpdateCycleAsync("startup", AppUpdateCheckTrigger.Startup, _lifetimeCts.Token);
            _ = RefreshRemoteModelCatalogAsync(manual: false, _lifetimeCts.Token);
            _ = RefreshRemoteDiarizationAssetCatalogAsync(manual: false, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendActivity("Deferred startup maintenance was canceled during shutdown.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Deferred startup maintenance error: {exception.Message}");
        }
        finally
        {
            _isDeferredStartupMaintenanceQueued = false;
        }
    }

    private void ScheduleDeferredMeetingsRefresh()
    {
        if (_hasCompletedFullMeetingsRefresh || IsShutdownRequested)
        {
            return;
        }

        RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Full, "meetings tab selected");
    }

    private async Task RunDeferredMeetingsRefreshAsync()
    {
        try
        {
            while (_hasPendingMeetingsRefreshRequest && !IsShutdownRequested)
            {
                if (MainWindowInteractionLogic.ShouldDeferMeetingRefresh(
                        _recordingCoordinator.IsRecording,
                        ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)))
                {
                    return;
                }

                var refreshMode = _pendingMeetingsRefreshMode;
                var selectedStem = _pendingMeetingsRefreshSelectedStem;
                _hasPendingMeetingsRefreshRequest = false;
                _pendingMeetingsRefreshMode = MeetingRefreshMode.Fast;
                _pendingMeetingsRefreshSelectedStem = null;

                var stopwatch = Stopwatch.StartNew();
                await RefreshMeetingListAsync(selectedStem, refreshMode);
                _logger.Log(
                    $"Completed queued meeting list refresh. mode='{refreshMode}', " +
                    $"selectedStem='{selectedStem ?? string.Empty}', elapsedMs={stopwatch.ElapsedMilliseconds}.");
            }
        }
        finally
        {
            _isDeferredMeetingsRefreshQueued = false;
            UpdateMeetingsRefreshStateText();
            SchedulePendingMeetingsRefreshIfReady();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        _processingQueueStatusTimer.Stop();
        _liveConfig.Changed -= LiveConfig_OnChanged;
        _processingQueue.StatusChanged -= ProcessingQueue_OnStatusChanged;
        CancelMeetingBackgroundWork();
        if (!_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        if (Application.Current is { Dispatcher.HasShutdownStarted: false } application)
        {
            application.Shutdown();
        }
    }

    private void ProcessingQueue_OnStatusChanged(ProcessingQueueStatusSnapshot snapshot)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyProcessingQueueStatusSnapshot(snapshot);
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(() => ApplyProcessingQueueStatusSnapshot(snapshot)), DispatcherPriority.Background);
    }

    private void ApplyProcessingQueueStatusSnapshot(ProcessingQueueStatusSnapshot snapshot)
    {
        _latestProcessingQueueStatusSnapshot = snapshot;
        UpdateProcessingQueueStatusUi();
        UpdateProcessingQueueStatusTimerState();
    }

    private void ProcessingQueueStatusTimer_OnTick(object? sender, EventArgs e)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        UpdateProcessingQueueStatusUi();
        UpdateProcessingQueueStatusTimerState();
    }

    private void UpdateProcessingQueueStatusTimerState()
    {
        var shouldRun = _latestProcessingQueueStatusSnapshot.TotalRemainingCount > 0 ||
                        _latestProcessingQueueStatusSnapshot.RunState is ProcessingQueueRunState.Processing or ProcessingQueueRunState.Paused or ProcessingQueueRunState.Queued;
        if (shouldRun)
        {
            if (!_processingQueueStatusTimer.IsEnabled)
            {
                _processingQueueStatusTimer.Start();
            }

            return;
        }

        if (_processingQueueStatusTimer.IsEnabled)
        {
            _processingQueueStatusTimer.Stop();
        }
    }

    private void UpdateProcessingQueueStatusUi()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var persistedBacklog = BuildPersistedProcessingBacklogState();
        var headerState = MainWindowInteractionLogic.BuildProcessingQueueHeaderState(_latestProcessingQueueStatusSnapshot, persistedBacklog, nowUtc);
        HeaderQueueStatusBorder.Visibility = headerState.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        HeaderQueueStatusLabelTextBlock.Text = headerState.Label;
        HeaderQueueStatusDetailTextBlock.Text = headerState.Detail;

        var stripState = MainWindowInteractionLogic.BuildMeetingsProcessingStripState(
            _latestProcessingQueueStatusSnapshot,
            _currentMeetingsRefreshStateText,
            persistedBacklog,
            nowUtc);
        MeetingsProcessingStatusBorder.Visibility = stripState.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        MeetingsProcessingStatusLine1TextBlock.Text = stripState.Line1;
        MeetingsProcessingStatusLine1TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line1) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsProcessingStatusLine2TextBlock.Text = stripState.Line2;
        MeetingsProcessingStatusLine2TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line2) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsProcessingStatusLine3TextBlock.Text = stripState.Line3;
        MeetingsProcessingStatusLine3TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line3) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsRefreshStateTextBlock.Text = stripState.SecondaryText ?? string.Empty;
        MeetingsRefreshStateTextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
    }

    private PersistedProcessingBacklogState? BuildPersistedProcessingBacklogState()
    {
        if (_allMeetingRows.Length == 0)
        {
            return null;
        }

        var queuedCount = _allMeetingRows.Count(row => row.Source.ManifestState == SessionState.Queued);
        var processingCount = _allMeetingRows.Count(
            row => row.Source.ManifestState is SessionState.Processing or SessionState.Finalizing);

        return queuedCount + processingCount == 0
            ? null
            : new PersistedProcessingBacklogState(queuedCount, processingCount);
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingTransitionInProgress)
        {
            return;
        }

        if (!HasReadyTranscriptionModel())
        {
            ShowTranscriptionSetupRequiredMessage();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _isRecordingTransitionInProgress = true;
        UpdateUi("Starting recording...", DetectionTextBlock.Text);

        try
        {
            _recentAutoStopContext = null;
            _manualStopSuppressionContext = null;
            ClearAutoStopVisualState();
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
            _isAutoStopTransitionInProgress = false;
            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
            _logger.Log($"Start foreground path completed in {stopwatch.ElapsedMilliseconds}ms.");
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingTransitionInProgress)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _isRecordingTransitionInProgress = true;
        _isAutoStopTransitionInProgress = false;
        ClearAutoStopVisualState();
        PauseDetectionDuringStopTransition();
        UpdateUi("Stopping recording...", DetectionTextBlock.Text);

        var activeSession = _recordingCoordinator.ActiveSession;
        _manualStopSuppressionContext = activeSession is null
            ? null
            : new ManualStopSuppressionContext(
                activeSession.Manifest.Platform,
                activeSession.Manifest.DetectedTitle,
                DateTimeOffset.UtcNow);
        _recentAutoStopContext = null;
        try
        {
            await StopCurrentRecordingAsync("Manual stop requested.");
        }
        finally
        {
            _isRecordingTransitionInProgress = false;
            _isAutoStopTransitionInProgress = false;
            ResumeDetectionAfterStopTransitionIfNeeded();
            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
            _logger.Log($"Manual stop button flow completed in {stopwatch.ElapsedMilliseconds}ms.");
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

    private void CurrentMeetingProjectTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCurrentMeetingEditor)
        {
            return;
        }

        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        _sessionProjectDraftTracker.UpdateDraft(
            activeSession.Manifest.SessionId,
            activeSession.Manifest.ProjectName ?? string.Empty,
            CurrentMeetingProjectTextBox.Text);
        ScheduleCurrentMeetingOptionalMetadataSave();
    }

    private void CurrentMeetingKeyAttendeesTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingCurrentMeetingEditor)
        {
            return;
        }

        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        _sessionKeyAttendeesDraftTracker.UpdateDraft(
            activeSession.Manifest.SessionId,
            FormatKeyAttendeesForDisplay(activeSession.Manifest.KeyAttendees),
            CurrentMeetingKeyAttendeesTextBox.Text);
        ScheduleCurrentMeetingOptionalMetadataSave();
    }

    private void DashboardPrimaryActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        HeaderShellStatusActionButton_OnClick(sender, e);
    }

    private void OpenUpdatesTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.Updates);
    }

    private void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabControl))
        {
            return;
        }

        if (!_isUiReady)
        {
            return;
        }

        UpdateAudioGraphTimerState();
        SchedulePendingMeetingsRefreshIfReady();

        if (ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem))
        {
            ScheduleDeferredMeetingsRefresh();
        }
    }

    private void RequestMeetingRefreshForCurrentContext(
        MeetingRefreshMode refreshMode,
        string reason,
        string? selectedStem = null)
    {
        if (refreshMode == MeetingRefreshMode.Full)
        {
            _hasCompletedFullMeetingsRefresh = false;
        }

        _hasPendingMeetingsRefreshRequest = true;
        if (refreshMode > _pendingMeetingsRefreshMode)
        {
            _pendingMeetingsRefreshMode = refreshMode;
        }

        if (!string.IsNullOrWhiteSpace(selectedStem))
        {
            _pendingMeetingsRefreshSelectedStem = selectedStem;
        }

        var isMeetingsTabSelected = ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem);
        var shouldDefer = MainWindowInteractionLogic.ShouldDeferMeetingRefresh(
            _recordingCoordinator.IsRecording,
            isMeetingsTabSelected);
        _logger.Log(
            $"Queued meeting list refresh request. reason='{reason}', mode='{refreshMode}', " +
            $"selectedStem='{selectedStem ?? string.Empty}', deferred={shouldDefer}, " +
            $"isRecording={_recordingCoordinator.IsRecording}, meetingsTabSelected={isMeetingsTabSelected}.");
        UpdateMeetingsRefreshStateText();
        SchedulePendingMeetingsRefreshIfReady();
    }

    private void SchedulePendingMeetingsRefreshIfReady()
    {
        if (!_hasPendingMeetingsRefreshRequest || _isDeferredMeetingsRefreshQueued || IsShutdownRequested)
        {
            return;
        }

        if (MainWindowInteractionLogic.ShouldDeferMeetingRefresh(
                _recordingCoordinator.IsRecording,
                ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)))
        {
            UpdateMeetingsRefreshStateText();
            return;
        }

        _isDeferredMeetingsRefreshQueued = true;
        UpdateMeetingsRefreshStateText();
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunDeferredMeetingsRefreshAsync()),
            DispatcherPriority.Background);
    }

    private void HeaderSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.General);
    }

    private void HeaderHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowHelpWindow();
    }

    private void OpenTranscriptionSetupFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSetupSectionFromHome(
            SetupWindowSection.Transcription,
            SettingsTranscriptionSetupSectionBorder,
            _currentTranscriptionSetupState,
            ModelActionStatusTextBlock);
    }

    private void OpenSpeakerLabelingSetupFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSetupSectionFromHome(
            SetupWindowSection.SpeakerLabeling,
            SettingsSpeakerLabelingSetupSectionBorder,
            _currentSpeakerLabelingSetupState,
            DiarizationActionStatusTextBlock);
    }

    private void OpenMicCaptureSettingsFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.General);
    }

    private void OpenAutoDetectSettingsFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.General);
    }

    private void OpenMeetingFilesSettingsFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.Files);
    }

    private void HeaderShellStatusActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: { } rawTarget })
        {
            return;
        }

        switch (rawTarget)
        {
            case ShellStatusTarget shellTarget:
                OpenShellStatusTarget(shellTarget);
                break;
            case DashboardPrimaryActionTarget dashboardTarget:
                OpenShellStatusTarget(dashboardTarget switch
                {
                    DashboardPrimaryActionTarget.Setup => ShellStatusTarget.SettingsSetup,
                    DashboardPrimaryActionTarget.SettingsUpdates => ShellStatusTarget.SettingsUpdates,
                    DashboardPrimaryActionTarget.SettingsGeneral => ShellStatusTarget.SettingsGeneral,
                    _ => ShellStatusTarget.None,
                });
                break;
        }
    }

    private void OpenShellStatusTarget(ShellStatusTarget target)
    {
        switch (target)
        {
            case ShellStatusTarget.SettingsSetup:
                OpenSettingsSurface(SettingsWindowSection.Setup);
                break;
            case ShellStatusTarget.SettingsUpdates:
                OpenSettingsSurface(SettingsWindowSection.Updates);
                break;
            case ShellStatusTarget.SettingsGeneral:
                OpenSettingsSurface(SettingsWindowSection.General);
                break;
            case ShellStatusTarget.None:
            default:
                break;
        }
    }

    private async void HomeMicCaptureEnabledButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveHomeQuickSettingAsync(
            enabled: true,
            configUpdater: config => config with { MicCaptureEnabled = true },
            snapshotUpdater: snapshot => snapshot with { MicCaptureEnabled = true },
            currentValueSelector: config => config.MicCaptureEnabled,
            settingName: "Microphone capture",
            applyMicCaptureLiveChange: true);
    }

    private async void HomeMicCaptureDisabledButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveHomeQuickSettingAsync(
            enabled: false,
            configUpdater: config => config with { MicCaptureEnabled = false },
            snapshotUpdater: snapshot => snapshot with { MicCaptureEnabled = false },
            currentValueSelector: config => config.MicCaptureEnabled,
            settingName: "Microphone capture",
            applyMicCaptureLiveChange: true);
    }

    private async void HomeAutoDetectEnabledButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveHomeQuickSettingAsync(
            enabled: true,
            configUpdater: config => config with { AutoDetectEnabled = true },
            snapshotUpdater: snapshot => snapshot with { AutoDetectEnabled = true },
            currentValueSelector: config => config.AutoDetectEnabled,
            settingName: "Automatic meeting detection");
    }

    private async void HomeAutoDetectDisabledButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveHomeQuickSettingAsync(
            enabled: false,
            configUpdater: config => config with { AutoDetectEnabled = false },
            snapshotUpdater: snapshot => snapshot with { AutoDetectEnabled = false },
            currentValueSelector: config => config.AutoDetectEnabled,
            settingName: "Automatic meeting detection");
    }

    private void CloseHelpSurfaceButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseHeaderSurfaces();
    }

    private void HelpOpenLogsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenContainingFolder(AppDataPaths.GetGlobalLogPath());
    }

    private void HelpOpenDataFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenContainingFolder(AppDataPaths.GetAppRoot());
    }

    private void OpenSettingsSurface(SettingsWindowSection section)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsHostWindow(MeetingRecorderProductModule.Instance.GetSettingsSections())
            {
                Owner = this,
            };
            _settingsWindow.SectionRequested += sectionId => FocusSettingsSection(MapSettingsSectionId(sectionId));
            _settingsWindow.SaveRequested += (_, _) => SaveConfigButton_OnClick(this, new RoutedEventArgs());
            _detachedSettingsBody = DetachSettingsBody();
            if (_detachedSettingsBody is not null)
            {
                _settingsWindow.AttachBody(_detachedSettingsBody);
            }

            _settingsWindow.Closed += (_, _) =>
            {
                var detachedBody = _settingsWindow.DetachBody();
                RestoreSettingsBody(detachedBody);
                _settingsWindow = null;
            };
        }

        _settingsWindow.NavigateTo(GetSettingsSectionId(section));
        _settingsWindow.SetFooterStatus(ConfigSaveStatusTextBlock.Text);
        UpdateConfigActionState();
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
        FocusSettingsSection(section);
    }

    private void OpenSetupWindow(
        SetupWindowSection section,
        FrameworkElement targetSection,
        ModelsTabSetupState? setupState,
        TextBlock statusTextBlock)
    {
        OpenSettingsSurface(SettingsWindowSection.Setup);

        if (setupState is not null)
        {
            NavigateToModelsSetupSection(targetSection, setupState.PrimaryAction, statusTextBlock);
            return;
        }

        targetSection.BringIntoView();
    }

    private void CloseHeaderSurfaces()
    {
        _settingsWindow?.Close();
        _helpWindow?.Close();
    }

    private UIElement? DetachSettingsBody()
    {
        var body = SettingsBodyContentBorder.Child;
        SettingsBodyContentBorder.Child = null;
        return body;
    }

    private void RestoreSettingsBody(UIElement? body)
    {
        if (body is not null && SettingsBodyContentBorder.Child is null)
        {
            SettingsBodyContentBorder.Child = body;
        }
    }

    private static UIElement? DetachSetupBody(Border contentBorder)
    {
        var body = contentBorder.Child;
        contentBorder.Child = null;
        return body;
    }

    private static void RestoreSetupBody(Border contentBorder, UIElement? body)
    {
        if (body is not null && contentBorder.Child is null)
        {
            contentBorder.Child = body;
        }
    }

    private void FocusSettingsSection(SettingsWindowSection section)
    {
        SettingsSetupSectionPanel.Visibility = section == SettingsWindowSection.Setup ? Visibility.Visible : Visibility.Collapsed;
        SettingsGeneralSectionPanel.Visibility = section == SettingsWindowSection.General ? Visibility.Visible : Visibility.Collapsed;
        SettingsFilesSectionPanel.Visibility = section == SettingsWindowSection.Files ? Visibility.Visible : Visibility.Collapsed;
        SettingsUpdatesSectionPanel.Visibility = section == SettingsWindowSection.Updates ? Visibility.Visible : Visibility.Collapsed;
        SettingsAdvancedSectionPanel.Visibility = section == SettingsWindowSection.Advanced ? Visibility.Visible : Visibility.Collapsed;

        _ = Dispatcher.BeginInvoke(() =>
        {
            switch (section)
            {
                case SettingsWindowSection.Setup:
                    TranscriptionOverviewPrimaryButton.Focus();
                    break;
                case SettingsWindowSection.Files:
                    ConfigAudioOutputDirTextBox.Focus();
                    break;
                case SettingsWindowSection.Updates:
                    CheckForUpdatesButton.Focus();
                    break;
                case SettingsWindowSection.Advanced:
                    ConfigWorkDirTextBox.Focus();
                    break;
                case SettingsWindowSection.General:
                default:
                    ConfigMicCaptureCheckBox.Focus();
                    break;
            }
        }, DispatcherPriority.Background);
    }

    private void ShowHelpWindow()
    {
        var runtimeDiagnosticsText = BuildRuntimeDiagnosticsText();
        if (_helpWindow is null)
        {
            _helpWindow = new HelpHostWindow(
                MeetingRecorderProductModule.Instance.GetAboutContent(),
                OpenSpeakerLabelingSetupGuide,
                () => OpenContainingFolder(AppDataPaths.GetGlobalLogPath()),
                () => OpenContainingFolder(AppDataPaths.GetAppRoot()),
                OpenLatestReleasePage,
                runtimeDiagnosticsText);
            _helpWindow.Owner = this;
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }
        else
        {
            _helpWindow.SetRuntimeDiagnostics(runtimeDiagnosticsText);
        }

        if (!_helpWindow.IsVisible)
        {
            _helpWindow.Show();
        }

        _helpWindow.Activate();
    }

    private string BuildRuntimeDiagnosticsText()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "MeetingRecorder.product.json");
        var diagnosticsLines = new List<string>
        {
            $"Active install root: {NormalizeDirectoryPath(AppContext.BaseDirectory)}",
            $"Bundled manifest path: {manifestPath}",
        };
        var detectedAudioSource = _recordingCoordinator.ActiveSession?.Manifest.DetectedAudioSource ?? _lastObservedDetectedAudioSource;
        diagnosticsLines.Add($"Current detected audio source: {MainWindowInteractionLogic.BuildDetectedAudioSourceSummary(detectedAudioSource)}");
        if (detectedAudioSource is not null)
        {
            diagnosticsLines.Add($"Current audio source match: {detectedAudioSource.MatchKind} ({detectedAudioSource.Confidence} confidence)");
            diagnosticsLines.Add($"Current audio source app: {detectedAudioSource.AppName}");

            if (!string.IsNullOrWhiteSpace(detectedAudioSource.WindowTitle))
            {
                diagnosticsLines.Add($"Current audio source window: {detectedAudioSource.WindowTitle}");
            }

            if (!string.IsNullOrWhiteSpace(detectedAudioSource.BrowserTabTitle))
            {
                diagnosticsLines.Add($"Current audio source browser tab: {detectedAudioSource.BrowserTabTitle}");
            }
        }

        var loopbackStatus = _recordingCoordinator.GetLoopbackCaptureStatusSnapshot();
        if (loopbackStatus.ActiveSelection is { } activeSelection)
        {
            diagnosticsLines.Add($"Active loopback endpoint: {activeSelection.FriendlyName} ({activeSelection.Role})");
            diagnosticsLines.Add($"Loopback capture mode: {BuildLoopbackCaptureModeText(loopbackStatus)}");
        }

        if (loopbackStatus.PreferredSelection is { } preferredSelection)
        {
            diagnosticsLines.Add(
                $"Preferred loopback candidate: {preferredSelection.FriendlyName} ({preferredSelection.Role}); reason={preferredSelection.Reason}");
        }

        if (loopbackStatus.PendingSelection is { } pendingSelection)
        {
            diagnosticsLines.Add(
                $"Pending loopback candidate: {pendingSelection.FriendlyName} ({pendingSelection.Role}); stability={loopbackStatus.PendingSelectionStableCount}");
        }

        foreach (var entry in loopbackStatus.RecentTimeline)
        {
            diagnosticsLines.Add($"Capture event: {entry.Summary}");
        }

        if (!File.Exists(manifestPath))
        {
            diagnosticsLines.Add("Bundled manifest install root: unavailable (manifest not found).");
            return string.Join(Environment.NewLine, diagnosticsLines);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rawInstallRoot = document.RootElement
                .GetProperty("managedInstallLayout")
                .GetProperty("installRoot")
                .GetString();
            var expandedInstallRoot = string.IsNullOrWhiteSpace(rawInstallRoot)
                ? "<blank>"
                : Environment.ExpandEnvironmentVariables(rawInstallRoot);

            diagnosticsLines.Add($"Bundled manifest install root: {NormalizeDirectoryPath(expandedInstallRoot)}");
        }
        catch (Exception exception)
        {
            diagnosticsLines.Add($"Bundled manifest install root: unavailable ({exception.Message})");
        }

        return string.Join(Environment.NewLine, diagnosticsLines);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void OpenSetupSectionFromHome(
        SetupWindowSection setupSection,
        FrameworkElement targetSection,
        ModelsTabSetupState? setupState,
        TextBlock statusTextBlock)
    {
        CloseHeaderSurfaces();
        OpenSetupWindow(setupSection, targetSection, setupState, statusTextBlock);
    }

    private static string GetSettingsSectionId(SettingsWindowSection section)
    {
        return section switch
        {
            SettingsWindowSection.Setup => "setup",
            SettingsWindowSection.General => "general",
            SettingsWindowSection.Files => "files",
            SettingsWindowSection.Updates => "updates",
            SettingsWindowSection.Advanced => "advanced",
            _ => "general",
        };
    }

    private static SettingsWindowSection MapSettingsSectionId(string sectionId)
    {
        return sectionId switch
        {
            "setup" => SettingsWindowSection.Setup,
            "files" => SettingsWindowSection.Files,
            "updates" => SettingsWindowSection.Updates,
            "advanced" => SettingsWindowSection.Advanced,
            _ => SettingsWindowSection.General,
        };
    }

    private async void DetectionTimer_OnTick(object? sender, EventArgs e)
    {
        if (!TryBeginDetectionCycle())
        {
            return;
        }

        var detectionGeneration = Volatile.Read(ref _detectionCycleGeneration);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (IsShutdownRequested)
            {
                return;
            }

            await TryReloadConfigAsync();
            await EnsureConfiguredModelPathResolvedAsync("runtime check", _lifetimeCts.Token);
            if (IsShutdownRequested || detectionGeneration != Volatile.Read(ref _detectionCycleGeneration))
            {
                _logger.Log(
                    $"Discarded stale detection scan after {stopwatch.ElapsedMilliseconds}ms because detection was paused or shutdown started.");
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var activeSessionBeforeDetection = _recordingCoordinator.ActiveSession;
            var shouldRunMeetingDetection = MeetingDetectionRuntimePolicy.ShouldRun(
                _liveConfig.Current.AutoDetectEnabled,
                _recordingCoordinator.IsRecording,
                activeSessionBeforeDetection?.AutoStarted == true);
            var decision = shouldRunMeetingDetection
                ? await _meetingDetector.DetectBestCandidateAsync(_lifetimeCts.Token)
                : null;
            if (shouldRunMeetingDetection)
            {
                decision = await _teamsDetectionArbitrator.ApplyPreferredContextAsync(
                    decision,
                    _liveConfig.Current,
                    nowUtc,
                    _lifetimeCts.Token);
            }
            if (IsShutdownRequested || detectionGeneration != Volatile.Read(ref _detectionCycleGeneration))
            {
                _logger.Log(
                    $"Ignored detection scan result after {stopwatch.ElapsedMilliseconds}ms because detection was paused or shutdown started.");
                return;
            }

            _logger.Log(
                $"Detection scan completed in {stopwatch.ElapsedMilliseconds}ms. shouldRun={shouldRunMeetingDetection}, " +
                $"result='{decision?.SessionTitle ?? string.Empty}'.");
            _lastObservedDetectionDecision = decision;
            LogDetectionChange(decision);
            DetectionTextBlock.Text = MainWindowInteractionLogic.BuildDetectionSummary(
                decision,
                _liveConfig.Current.AutoDetectEnabled);
            UpdateDetectedAudioSourceSurface(decision);
            UpdateCurrentMeetingTitleStatus();

            if (!_recordingCoordinator.IsRecording &&
                HasReadyTranscriptionModel() &&
                _liveConfig.Current.AutoDetectEnabled &&
                decision is not null)
            {
                var shouldAutoStartQuietTeamsMeeting = ShouldAutoStartQuietTeamsMeeting(decision, nowUtc);
                var shouldRecoverFromRecentAutoStop = _autoRecordingContinuityPolicy.ShouldRecoverFromRecentAutoStop(
                    decision,
                    _recentAutoStopContext,
                    nowUtc);
                var manualStopSuppressionDisposition = _autoRecordingContinuityPolicy.GetManualStopSuppressionDisposition(
                    decision,
                    _manualStopSuppressionContext);
                if (manualStopSuppressionDisposition == ManualStopSuppressionDisposition.ReleaseSuppression)
                {
                    _manualStopSuppressionContext = null;
                }

                var shouldSuppressManualRestart = manualStopSuppressionDisposition == ManualStopSuppressionDisposition.SuppressAutoStart;
                if (!shouldSuppressManualRestart &&
                    (decision.ShouldStart || shouldAutoStartQuietTeamsMeeting || shouldRecoverFromRecentAutoStop))
                {
                    ClearAutoStopVisualState();
                    await _recordingCoordinator.StartAsync(
                        decision.Platform,
                        decision.SessionTitle,
                        decision.Signals,
                        autoStarted: true,
                        decision.DetectedAudioSource);
                    _lastPositiveDetectionUtc = nowUtc;
                    _recentAutoStopContext = null;
                    _manualStopSuppressionContext = null;
                    _lastAutoStopFingerprint = null;
                    UpdateCurrentMeetingEditor();
                    UpdateUi("Recording in progress.", DetectionTextBlock.Text);
                    AppendActivity(
                        shouldRecoverFromRecentAutoStop && !decision.ShouldStart
                            ? $"Resumed recording for '{decision.SessionTitle}' after a recent auto-stop."
                            : shouldAutoStartQuietTeamsMeeting
                                ? $"Auto-started recording for quiet Teams meeting '{decision.SessionTitle}' after sustained meeting detection."
                            : $"Auto-started recording for '{decision.SessionTitle}'.");
                }
            }
            else
            {
                ResetQuietTeamsAutoStartCandidate();
            }

            var activeSession = _recordingCoordinator.ActiveSession;
            if (activeSession is not null &&
                await TryReclassifyActiveSessionAsync(
                    activeSession,
                    decision,
                    nowUtc,
                    _lifetimeCts.Token))
            {
                activeSession = _recordingCoordinator.ActiveSession;
            }

            if (activeSession is not null)
            {
                var loopbackRefresh = await _recordingCoordinator.RefreshLoopbackCaptureAsync(
                    activeSession.Manifest.Platform,
                    decision?.DetectedAudioSource,
                    _lifetimeCts.Token);
                if (!string.IsNullOrWhiteSpace(loopbackRefresh.StatusMessage) &&
                    (loopbackRefresh.SwapPerformed || loopbackRefresh.SwapFailed))
                {
                    AppendActivity(loopbackRefresh.StatusMessage);
                }

                var microphoneRefresh = await _recordingCoordinator.RefreshMicrophoneCaptureAsync(_lifetimeCts.Token);
                if (!string.IsNullOrWhiteSpace(microphoneRefresh.StatusMessage) &&
                    (microphoneRefresh.SwapPerformed || microphoneRefresh.SwapFailed))
                {
                    AppendActivity(microphoneRefresh.StatusMessage);
                }

                UpdateCaptureStatusSurface();
            }

            var activeMeetingManagedSession = _recordingCoordinator.ActiveSession is { MeetingLifecycleManaged: true } reclassifiedSession
                ? reclassifiedSession
                : null;
            var hasRecentLoopbackActivity = activeMeetingManagedSession is not null &&
                HasRecentLoopbackActivity(activeMeetingManagedSession);
            var hasRecentMicrophoneActivity = activeMeetingManagedSession is not null &&
                HasRecentMicrophoneActivity(activeMeetingManagedSession);
            var shouldRefreshLastPositiveSignal = activeMeetingManagedSession is not null &&
                _autoRecordingContinuityPolicy.ShouldRefreshLastPositiveSignal(
                    decision,
                    activeMeetingManagedSession.Manifest.Platform,
                    activeMeetingManagedSession.Manifest.DetectedTitle,
                    hasRecentLoopbackActivity,
                    hasRecentMicrophoneActivity);
            var shouldClearAutoStopCountdown = _autoStopCountdownSecondsRemaining is null ||
                (activeMeetingManagedSession is not null &&
                 _autoRecordingContinuityPolicy.ShouldClearAutoStopCountdown(
                     decision,
                     activeMeetingManagedSession.Manifest.Platform,
                     activeMeetingManagedSession.Manifest.DetectedTitle,
                     hasRecentLoopbackActivity,
                     hasRecentMicrophoneActivity));
            if (shouldRefreshLastPositiveSignal && shouldClearAutoStopCountdown)
            {
                ClearAutoStopVisualState();
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
                     activeMeetingManagedSession is not null)
            {
                var activePlatform = activeMeetingManagedSession.Manifest.Platform;
                var elapsedSincePositive = _lastPositiveDetectionUtc.HasValue
                    ? nowUtc - _lastPositiveDetectionUtc.Value
                    : (TimeSpan?)null;
                var lastPositiveUtc = _lastPositiveDetectionUtc;

                if (elapsedSincePositive.HasValue && lastPositiveUtc.HasValue)
                {
                    var stopTimeout = _autoRecordingContinuityPolicy.GetAutoStopTimeout(
                        decision,
                        activePlatform,
                        activeMeetingManagedSession.Manifest.DetectedTitle,
                        TimeSpan.FromSeconds(_liveConfig.Current.MeetingStopTimeoutSeconds));
                    var remaining = stopTimeout - elapsedSincePositive.Value;
                    if (remaining <= TimeSpan.Zero)
                    {
                        _recentAutoStopContext = new RecentAutoStopContext(activePlatform, nowUtc);
                        AppendAutoStopStatus($"Auto-stop triggered after {Math.Ceiling(stopTimeout.TotalSeconds)} seconds without a strong meeting signal.");
                        _isRecordingTransitionInProgress = true;
                        _isAutoStopTransitionInProgress = true;
                        ClearAutoStopVisualState();
                        PauseDetectionDuringStopTransition();
                        UpdateUi("Auto-stopping recording.", "Meeting ended. Finalizing session...");
                        UpdateCurrentMeetingEditor();
                        UpdateAudioCaptureGraph();
                        try
                        {
                            await StopCurrentRecordingAsync("Meeting signals expired after the configured timeout.");
                        }
                        finally
                        {
                            _isRecordingTransitionInProgress = false;
                            _isAutoStopTransitionInProgress = false;
                            ResumeDetectionAfterStopTransitionIfNeeded();
                            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
                        }
                        _lastPositiveDetectionUtc = null;
                        _lastAutoStopFingerprint = null;
                    }
                    else
                    {
                        SetAutoStopCountdown(remaining);
                        AppendAutoStopStatus(
                            $"Auto-stop countdown active: {Math.Ceiling(remaining.TotalSeconds)} seconds remaining. Last strong meeting signal was at {lastPositiveUtc.Value:O}.");
                    }
                }
            }

            var activeSessionForMicPrompt = _recordingCoordinator.ActiveSession;
            if (activeSessionForMicPrompt is not null)
            {
                await TryPromoteActiveMeetingTitleAsync(activeSessionForMicPrompt, decision, _lifetimeCts.Token);
                await TryPromptToEnableMicCaptureAsync(activeSessionForMicPrompt, _lifetimeCts.Token);
                await TryCaptureTeamsAttendeesAsync(activeSessionForMicPrompt, _lifetimeCts.Token);
            }
        }
        catch (OperationCanceledException) when (IsShutdownRequested || detectionGeneration != Volatile.Read(ref _detectionCycleGeneration))
        {
            _logger.Log("Detection scan canceled because detection was paused or shutdown started.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Detection error: {exception.Message}");
        }
        finally
        {
            FinishDetectionCycle();
        }
    }

    private bool ShouldAutoStartQuietTeamsMeeting(DetectionDecision? decision, DateTimeOffset nowUtc)
    {
        if (decision is null ||
            !_autoRecordingContinuityPolicy.IsQuietSpecificTeamsMeetingCandidate(decision))
        {
            ResetQuietTeamsAutoStartCandidate();
            return false;
        }

        var quietCandidate = decision;
        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(quietCandidate.SessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ResetQuietTeamsAutoStartCandidate();
            return false;
        }

        var fingerprint = $"{quietCandidate.Platform}|{normalizedTitle}";
        if (!string.Equals(_quietTeamsAutoStartFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _quietTeamsAutoStartFingerprint = fingerprint;
            _quietTeamsAutoStartFirstObservedUtc = nowUtc;
        }

        if (!_quietTeamsAutoStartFirstObservedUtc.HasValue)
        {
            _quietTeamsAutoStartFirstObservedUtc = nowUtc;
            return false;
        }

        if (!_autoRecordingContinuityPolicy.ShouldAutoStartQuietSpecificTeamsMeeting(
                quietCandidate,
                _quietTeamsAutoStartFirstObservedUtc.Value,
                nowUtc))
        {
            return false;
        }

        ResetQuietTeamsAutoStartCandidate();
        return true;
    }

    private void ResetQuietTeamsAutoStartCandidate()
    {
        _quietTeamsAutoStartFingerprint = null;
        _quietTeamsAutoStartFirstObservedUtc = null;
    }

    private async Task<bool> TryRollOverManagedSessionAsync(
        ActiveRecordingSession activeSession,
        DetectionDecision decision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var previousPlatform = activeSession.Manifest.Platform;
        var previousTitle = activeSession.Manifest.DetectedTitle;
        _isRecordingTransitionInProgress = true;
        _isAutoStopTransitionInProgress = false;
        ClearAutoStopVisualState();
        PauseDetectionDuringStopTransition();
        UpdateUi("Switching meetings...", DetectionTextBlock.Text);

        try
        {
            await ApplyPendingCurrentMetadataAsync(cancellationToken);
            var manifestPath = await StopRecordingSessionAsync(
                $"Detected a new meeting '{decision.SessionTitle}' while '{previousTitle}' was still active.",
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                await _processingQueue.EnqueueAsync(manifestPath, cancellationToken);
                AppendActivity($"Queued session for processing: {manifestPath}");
            }

            await _recordingCoordinator.StartAsync(
                decision.Platform,
                decision.SessionTitle,
                decision.Signals,
                autoStarted: true,
                decision.DetectedAudioSource,
                cancellationToken);

            _lastPositiveDetectionUtc = nowUtc;
            _recentAutoStopContext = null;
            _manualStopSuppressionContext = null;
            _lastAutoStopFingerprint = null;
            UpdateCurrentMeetingEditor();
            UpdateDetectedAudioSourceSurface(decision);
            UpdateUi("Recording in progress.", DetectionTextBlock.Text);
            UpdateAudioCaptureGraph();
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "meeting rollover");
            AppendActivity(
                $"Started a new recording for '{decision.SessionTitle}' after closing the previous {previousPlatform} session '{previousTitle}'.");
            return true;
        }
        finally
        {
            _isRecordingTransitionInProgress = false;
            _isAutoStopTransitionInProgress = false;
            ResumeDetectionAfterStopTransitionIfNeeded();
            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
        }
    }

    private async Task<bool> TryReclassifyActiveSessionAsync(
        ActiveRecordingSession activeSession,
        DetectionDecision? decision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsShutdownRequested || decision is null)
        {
            return false;
        }

        var transition = MainWindowInteractionLogic.GetEligibleActiveSessionTransition(
            decision,
            activeSession.Manifest.Platform,
            activeSession.Manifest.DetectedTitle,
            activeSession.MeetingLifecycleManaged,
            _autoRecordingContinuityPolicy);
        if (transition == ActiveSessionTransitionKind.None)
        {
            return false;
        }

        if (transition == ActiveSessionTransitionKind.RollOver)
        {
            return await TryRollOverManagedSessionAsync(activeSession, decision, nowUtc, cancellationToken);
        }

        if (!_autoRecordingContinuityPolicy.ShouldReclassifyActiveSession(
                decision,
                activeSession.Manifest.Platform,
                activeSession.Manifest.DetectedTitle))
        {
            return false;
        }

        var previousPlatform = activeSession.Manifest.Platform;
        var reclassified = await _recordingCoordinator.ReclassifyActiveSessionAsync(
            decision.Platform,
            decision.SessionTitle,
            decision.Signals,
            decision.DetectedAudioSource,
            cancellationToken);
        if (!reclassified)
        {
            return false;
        }

        _lastPositiveDetectionUtc = nowUtc;
        _recentAutoStopContext = null;
        _manualStopSuppressionContext = null;
        _lastAutoStopFingerprint = null;
        var wasMeetingLifecycleManaged = activeSession.MeetingLifecycleManaged;
        activeSession.MeetingLifecycleManaged = true;
        _sessionTitleDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, decision.SessionTitle);
        UpdateCurrentMeetingEditor();
        UpdateDetectedAudioSourceSurface(decision);
        AppendActivity(
            wasMeetingLifecycleManaged
                ? $"Switched the active recording from {previousPlatform} to {decision.Platform} using the current detected meeting window '{decision.SessionTitle}'."
                : $"Reclassified active recording from {previousPlatform} to {decision.Platform}, switched to '{decision.SessionTitle}', and enabled automatic meeting-end stop handling.");
        return true;
    }

    private void UpdateCaptureStatusSurface()
    {
        var loopbackStatus = _recordingCoordinator.GetLoopbackCaptureStatusSnapshot();
        if (!loopbackStatus.IsRecording || loopbackStatus.ActiveSelection is null)
        {
            LoopbackCaptureStatusTextBlock.Text = "Capture status appears here while recording.";
            LoopbackCaptureRecentEventsTextBlock.Text = "Recent loopback events will be saved with the session.";
            return;
        }

        var statusParts = new List<string>
        {
            $"Active loopback endpoint: {loopbackStatus.ActiveSelection.FriendlyName} ({loopbackStatus.ActiveSelection.Role}).",
            $"Mode: {BuildLoopbackCaptureModeText(loopbackStatus)}.",
        };
        if (loopbackStatus.LastSuccessfulSwapAtUtc is { } lastSuccessfulSwapAtUtc)
        {
            statusParts.Add(
                $"Last successful capture swap: {TimeZoneInfo.ConvertTime(lastSuccessfulSwapAtUtc, TimeZoneInfo.Local):g}.");
        }

        if (loopbackStatus.IsSwapPending && loopbackStatus.PendingSelection is not null)
        {
            statusParts.Add(
                $"Candidate under review: {loopbackStatus.PendingSelection.FriendlyName} ({loopbackStatus.PendingSelection.Role}) {loopbackStatus.PendingSelectionStableCount}/2.");
        }

        LoopbackCaptureStatusTextBlock.Text = string.Join(" ", statusParts);
        LoopbackCaptureRecentEventsTextBlock.Text = loopbackStatus.RecentTimeline.Count == 0
            ? "Recent loopback events will appear here."
            : string.Join(Environment.NewLine, loopbackStatus.RecentTimeline.Select(entry => $"- {entry.Summary}"));
    }

    private string BuildLoopbackCaptureModeText(LoopbackCaptureStatusSnapshot loopbackStatus)
    {
        if (loopbackStatus.IsSwapPending)
        {
            return "Swapping loopback";
        }

        if (loopbackStatus.IsFallbackActive)
        {
            return "Fallback capture active";
        }

        return _recordingCoordinator.ActiveSession?.MicrophoneRecorder is null
            ? "Loopback live"
            : "Loopback + mic live";
    }

    private async void UpdateTimer_OnTick(object? sender, EventArgs e)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        UpdateUpdateActionButtons();
        await RunExternalAudioImportCycleAsync("background timer", _lifetimeCts.Token);
        await RunAutomaticUpdateCycleAsync("background timer", AppUpdateCheckTrigger.Scheduled, _lifetimeCts.Token);
    }

    private void AudioGraphTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateAudioCaptureGraph();
        UpdateCurrentRecordingElapsedText();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        if (_shutdownMode == AppShutdownMode.Immediate)
        {
            _shutdownInProgress = true;
            InvalidateDetectionCycle();
            _detectionTimer.Stop();
            _audioGraphTimer.Stop();
            _updateTimer.Stop();
            if (!_lifetimeCts.IsCancellationRequested)
            {
                _lifetimeCts.Cancel();
            }

            _allowClose = true;
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        InvalidateDetectionCycle();
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        if (!_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        _ = ShutdownAsync();
    }

    internal bool TryPrepareForInstallerShutdown()
    {
        _shutdownMode = MainWindowInteractionLogic.GetAppShutdownMode(
            installerRequestedShutdown: true,
            isRecording: _recordingCoordinator.IsRecording,
            isProcessingInProgress: _processingQueue.IsProcessingInProgress);
        if (_shutdownMode != AppShutdownMode.Immediate)
        {
            AppendActivity("Deferred installer shutdown because recording or processing is still active.");
            UpdateCheckStatusTextBlock.Text = "Update install deferred until recording and background processing are idle.";
            return false;
        }

        _shutdownInProgress = true;
        InvalidateDetectionCycle();
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        if (!_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        _allowClose = true;
        return true;
    }

    private async Task ShutdownAsync()
    {
        try
        {
            AppendActivity("Application shutdown requested.");

            using var shutdownUpdateCheckCts = new CancellationTokenSource(ShutdownUpdateCheckTimeout);
            await RunAutomaticUpdateCycleAsync("shutdown", AppUpdateCheckTrigger.Shutdown, shutdownUpdateCheckCts.Token);

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
            await Dispatcher.InvokeAsync(() =>
            {
                CloseHeaderSurfaces();
                _allowClose = true;

                if (Application.Current is { Dispatcher.HasShutdownStarted: false } application)
                {
                    if (IsVisible)
                    {
                        Close();
                    }

                    if (!application.Dispatcher.HasShutdownStarted)
                    {
                        application.Shutdown();
                    }

                    return;
                }

                Close();
            });
        }
    }

    private async Task StopCurrentRecordingAsync(
        string reason,
        bool enqueueForProcessing = true,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await ApplyPendingCurrentMetadataAsync(cancellationToken);
            var manifestPath = await StopRecordingSessionAsync(reason, cancellationToken);
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

            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "recording stop");
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
        finally
        {
            _logger.Log($"Stop foreground path completed in {stopwatch.ElapsedMilliseconds}ms. reason='{reason}'.");
        }
    }

    private Task<string?> StopRecordingSessionAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _recordingCoordinator.StopAsync(reason, cancellationToken), cancellationToken);
    }

    private void PauseDetectionDuringStopTransition()
    {
        InvalidateDetectionCycle();
        if (_detectionTimer.IsEnabled)
        {
            _detectionTimer.Stop();
        }
    }

    private void ResumeDetectionAfterStopTransitionIfNeeded()
    {
        if (IsShutdownRequested || _isUpdateInstallInProgress)
        {
            return;
        }

        if (!_detectionTimer.IsEnabled)
        {
            _detectionTimer.Start();
        }
    }

    private void SetAutoStopCountdown(TimeSpan remaining)
    {
        _autoStopCountdownSecondsRemaining = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        UpdateCurrentMeetingTitleStatus();
        UpdateAudioGraphTimerState();
        UpdateAudioCaptureGraph();
    }

    private void ClearAutoStopVisualState()
    {
        _autoStopCountdownSecondsRemaining = null;
        UpdateCurrentMeetingTitleStatus();
        UpdateAudioGraphTimerState();
        UpdateAudioCaptureGraph();
    }

    private async void RefreshMeetingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshMeetingListForCurrentContextAsync(bypassAttendeeNoMatchCacheForVisibleRows: true);
        AppendActivity("Refreshed recent and published meeting list.");
    }

    private async void RenameSelectedMeetingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            SelectedMeetingStatusTextBlock.Text = selectedMeetings.Length == 0
                ? "Select one meeting before renaming it."
                : "Select exactly one meeting to rename it directly, or use Apply Suggestions to Selected for a bulk pass.";
            AppendActivity("Select exactly one published meeting before renaming it directly.");
            return;
        }

        var selectedMeeting = selectedMeetings[0];
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

    private async void SuggestSelectedMeetingTitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            SelectedMeetingStatusTextBlock.Text = selectedMeetings.Length == 0
                ? "Select one meeting to preview a suggested title."
                : "Select exactly one meeting to preview a suggestion, or use Apply Suggestions to Selected for a bulk pass.";
            return;
        }

        var selectedMeeting = selectedMeetings[0];
        _isSuggestingMeetingTitle = true;
        UpdateMeetingActionState();
        SelectedMeetingStatusTextBlock.Text = $"Looking for a better title for '{selectedMeeting.Title}'...";

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var suggestion = await TryGetMeetingTitleSuggestionAsync(selectedMeeting, _lifetimeCts.Token);
            if (suggestion is null)
            {
                SelectedMeetingStatusTextBlock.Text =
                    $"No better Outlook or Teams title history match was found for '{selectedMeeting.Title}'.";
                return;
            }

            SelectedMeetingTitleTextBox.Text = suggestion.Title;
            SelectedMeetingStatusTextBlock.Text =
                $"Suggested '{suggestion.Title}' from {suggestion.Source}. Review it, then click Rename Meeting to apply it.";
            AppendActivity($"Suggested title '{suggestion.Title}' from {suggestion.Source} for '{selectedMeeting.Title}'.");
        }
        catch (Exception exception)
        {
            SelectedMeetingStatusTextBlock.Text = $"Unable to suggest a title: {exception.Message}";
            AppendActivity($"Failed to suggest a meeting title: {exception.Message}");
        }
        finally
        {
            _isSuggestingMeetingTitle = false;
            UpdateMeetingActionState();
        }
    }

    private async void ApplySuggestedMeetingTitlesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            SelectedMeetingStatusTextBlock.Text = "Select one or more meetings before applying suggested titles.";
            return;
        }

        _isApplyingSuggestedMeetingTitles = true;
        UpdateMeetingActionState();
        SelectedMeetingStatusTextBlock.Text = $"Checking {selectedMeetings.Length} selected meetings for better titles...";

        var renamedCount = 0;
        var unchangedCount = 0;
        var missingSuggestionCount = 0;
        var failureMessages = new List<string>();

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);

            foreach (var meeting in selectedMeetings)
            {
                try
                {
                    var suggestion = await TryGetMeetingTitleSuggestionAsync(meeting, _lifetimeCts.Token);
                    if (suggestion is null)
                    {
                        missingSuggestionCount++;
                        continue;
                    }

                    if (string.Equals(meeting.Title.Trim(), suggestion.Title.Trim(), StringComparison.Ordinal))
                    {
                        unchangedCount++;
                        continue;
                    }

                    var renamed = await _meetingOutputCatalogService.RenameMeetingAsync(
                        _liveConfig.Current.AudioOutputDir,
                        _liveConfig.Current.TranscriptOutputDir,
                        meeting.Source.Stem,
                        suggestion.Title,
                        _liveConfig.Current.WorkDir,
                        _lifetimeCts.Token);
                    renamedCount++;
                    AppendActivity($"Applied suggested title '{renamed.Title}' from {suggestion.Source} to '{meeting.Title}'.");
                }
                catch (Exception exception)
                {
                    failureMessages.Add($"{meeting.Title}: {exception.Message}");
                }
            }

            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "suggested titles applied");
            SelectedMeetingStatusTextBlock.Text =
                $"Applied {renamedCount} suggestion(s); skipped {missingSuggestionCount} without a better match, {unchangedCount} already matching, and {failureMessages.Count} failed.";
            if (failureMessages.Count > 0)
            {
                AppendActivity($"Bulk title suggestion failures: {string.Join(" | ", failureMessages)}");
            }
        }
        finally
        {
            _isApplyingSuggestedMeetingTitles = false;
            UpdateMeetingActionState();
        }
    }

    private void MeetingsSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        ApplyMeetingsWorkspaceView();
    }

    private async void MeetingsViewModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync();
    }

    private async void MeetingsSortKeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync();
    }

    private async void MeetingsSortDirectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync();
    }

    private async void MeetingsGroupKeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync();
    }

    private void MeetingsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMeetingEditor(MeetingsDataGrid.SelectedItem as MeetingListRow);
        UpdateSelectedMeetingInspector(MeetingsDataGrid.SelectedItem as MeetingListRow);
        UpdateMergeMeetingsEditor();
        UpdateMeetingCleanupRecommendationsEditor();
    }

    private void ExpandAllMeetingGroupsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllMeetingGroupExpansionStates(isExpanded: true);
    }

    private void CollapseAllMeetingGroupsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllMeetingGroupExpansionStates(isExpanded: false);
    }

    private void MeetingGroupExpander_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            ApplyMeetingGroupExpansionState(expander);
        }
    }

    private void MeetingGroupExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (_isApplyingMeetingGroupExpansionState ||
            sender is not Expander { DataContext: CollectionViewGroup { Name: string groupLabel } })
        {
            return;
        }

        _meetingGroupExpansionStates[groupLabel] = true;
    }

    private void MeetingGroupExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingMeetingGroupExpansionState ||
            sender is not Expander { DataContext: CollectionViewGroup { Name: string groupLabel } })
        {
            return;
        }

        _meetingGroupExpansionStates[groupLabel] = false;
    }

    private void SetAllMeetingGroupExpansionStates(bool isExpanded)
    {
        foreach (var groupLabel in _meetingGroupExpansionStates.Keys.ToArray())
        {
            _meetingGroupExpansionStates[groupLabel] = isExpanded;
        }

        ApplyMeetingGroupExpansionStateToVisibleGroups();
    }

    private async void MeetingPermanentDeleteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var targetMeetings = GetMeetingRowsForContextMenuAction(sender);
        if (targetMeetings.Length == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Select one or more meetings before trying to delete them permanently.";
            return;
        }

        if (IsMeetingActionInProgress())
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                "Wait for the current meeting action to finish before deleting meetings permanently.";
            return;
        }

        if (!TryConfirmPermanentDelete(targetMeetings))
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                "Permanent delete cancelled. Type DELETE exactly to confirm an irreversible delete.";
            return;
        }

        _isDeletingMeetings = true;
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text = targetMeetings.Length == 1
            ? $"Deleting '{targetMeetings[0].Title}' permanently..."
            : $"Deleting {targetMeetings.Length} meetings permanently...";

        try
        {
            foreach (var meeting in targetMeetings)
            {
                await _meetingCleanupExecutionService.DeleteMeetingPermanentlyAsync(meeting.Source, _lifetimeCts.Token);
            }

            MeetingCleanupRecommendationsStatusTextBlock.Text = targetMeetings.Length == 1
                ? $"Deleted '{targetMeetings[0].Title}' permanently."
                : $"Deleted {targetMeetings.Length} meetings permanently.";
            AppendActivity(
                targetMeetings.Length == 1
                    ? $"Permanently deleted published meeting '{targetMeetings[0].Title}'."
                    : $"Permanently deleted {targetMeetings.Length} published meetings.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Permanent delete failed: {exception.Message}";
            AppendActivity($"Permanent delete failed: {exception.Message}");
        }
        finally
        {
            _isDeletingMeetings = false;
            UpdateMeetingActionState();
        }
    }

    private async void MeetingRecommendedActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MeetingListRow row } ||
            row.PrimaryRecommendation is null)
        {
            return;
        }

        if (IsMeetingActionInProgress())
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                "Wait for the current meeting action to finish before applying another recommendation.";
            return;
        }

        var recommendation = row.PrimaryRecommendation;
        var actionLabel = MainWindowInteractionLogic.BuildMeetingCleanupActionLabel(recommendation.Action);

        _isApplyingMeetingCleanupRecommendations = true;
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text =
            $"Applying {actionLabel} for '{row.Title}'...";

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(new[] { recommendation }, "inline-row", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();

            var additionalRecommendationCount = Math.Max(0, row.RecommendationCount - 1);
            MeetingCleanupRecommendationsStatusTextBlock.Text = additionalRecommendationCount == 0
                ? $"Applied {actionLabel} for '{row.Title}'."
                : $"Applied {actionLabel} for '{row.Title}'. {additionalRecommendationCount} additional recommendation(s) remain in Cleanup Recommendations.";
            AppendActivity($"Applied inline cleanup recommendation '{actionLabel}' for '{row.Title}'.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Failed to apply {actionLabel} for '{row.Title}': {exception.Message}";
            AppendActivity($"Failed to apply inline cleanup recommendation for '{row.Title}': {exception.Message}");
        }
        finally
        {
            _isApplyingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private void MeetingCleanupRecommendationsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMeetingActionState();
    }

    private void ReviewMeetingCleanupSuggestionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        MarkMeetingCleanupHistoricalReviewCompleted();
        LegacyMeetingCleanupReviewBorder.Visibility = Visibility.Visible;
        LegacyMeetingCleanupReviewExpander.IsExpanded = true;
        MeetingCleanupRecommendationsDataGrid.Focus();
        MeetingCleanupRecommendationsStatusTextBlock.Text = "Review the cleanup suggestions below. Dismiss any you do not want to see again.";
        UpdateMeetingCleanupReviewBanner();
    }

    private async void ApplySafeMeetingCleanupFixesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var safeRecommendations = MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(_meetingCleanupRecommendations);
        if (safeRecommendations.Count == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "No safe cleanup fixes are available right now.";
            return;
        }

        _isApplyingSafeMeetingCleanupFixes = true;
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text = $"Applying {safeRecommendations.Count} safe cleanup fix(es)...";

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(safeRecommendations, "safe-fixes", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Applied {safeRecommendations.Count} safe cleanup fix(es).";
            AppendActivity($"Applied {safeRecommendations.Count} safe cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Failed to apply safe cleanup fixes: {exception.Message}";
            AppendActivity($"Failed to apply safe cleanup fixes: {exception.Message}");
        }
        finally
        {
            _isApplyingSafeMeetingCleanupFixes = false;
            UpdateMeetingActionState();
        }
    }

    private async void ApplySelectedMeetingCleanupRecommendationsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedRecommendations = GetSelectedMeetingCleanupRecommendationRows()
            .Select(row => row.Source)
            .ToArray();
        if (selectedRecommendations.Length == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Select one or more cleanup recommendations first.";
            return;
        }

        _isApplyingMeetingCleanupRecommendations = true;
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text = $"Applying {selectedRecommendations.Length} selected recommendation(s)...";

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(selectedRecommendations, "manual-review", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Applied {selectedRecommendations.Length} recommendation(s).";
            AppendActivity($"Applied {selectedRecommendations.Length} cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Failed to apply selected recommendations: {exception.Message}";
            AppendActivity($"Failed to apply selected cleanup recommendations: {exception.Message}");
        }
        finally
        {
            _isApplyingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private async void ApplyMeetingCleanupRecommendationsForSelectedMeetingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetingStems = GetSelectedMeetingRows()
            .Select(row => row.Source.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedMeetingStems.Count == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Select one or more meetings above before applying their recommended actions.";
            return;
        }

        var selectedRecommendations = _meetingCleanupRecommendations
            .Where(recommendation => recommendation.RelatedStems.All(selectedMeetingStems.Contains))
            .ToArray();
        if (selectedRecommendations.Length == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "No cleanup recommendations match the currently selected meetings.";
            return;
        }

        _isApplyingMeetingCleanupRecommendations = true;
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text =
            $"Applying {selectedRecommendations.Length} recommendation(s) for the selected meetings...";

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(selectedRecommendations, "selected-meetings", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Applied {selectedRecommendations.Length} recommendation(s) for the selected meetings.";
            AppendActivity($"Applied cleanup recommendations for {selectedMeetingStems.Count} selected meeting(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Failed to apply the selected meetings' cleanup recommendations: {exception.Message}";
            AppendActivity($"Failed to apply cleanup recommendations for selected meetings: {exception.Message}");
        }
        finally
        {
            _isApplyingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private async void DismissSelectedMeetingCleanupRecommendationsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedRecommendations = GetSelectedMeetingCleanupRecommendationRows()
            .Select(row => row.Source)
            .ToArray();
        if (selectedRecommendations.Length == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Select one or more cleanup recommendations to dismiss.";
            return;
        }

        _isDismissingMeetingCleanupRecommendations = true;
        UpdateMeetingActionState();

        try
        {
            var currentConfig = _liveConfig.Current;
            var mergedDismissals = currentConfig.DismissedMeetingRecommendations
                .Concat(selectedRecommendations.Select(recommendation => new DismissedMeetingRecommendation(
                    recommendation.Fingerprint,
                    DateTimeOffset.UtcNow)))
                .ToArray();
            await _liveConfig.SaveAsync(currentConfig with
            {
                DismissedMeetingRecommendations = mergedDismissals,
            }, _lifetimeCts.Token);
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Dismissed {selectedRecommendations.Length} cleanup recommendation(s).";
            AppendActivity($"Dismissed {selectedRecommendations.Length} cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Failed to dismiss recommendations: {exception.Message}";
            AppendActivity($"Failed to dismiss cleanup recommendations: {exception.Message}");
        }
        finally
        {
            _isDismissingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private void OpenRelatedMeetingCleanupRecommendationsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedRecommendations = GetSelectedMeetingCleanupRecommendationRows()
            .Select(row => row.Source)
            .ToArray();
        if (selectedRecommendations.Length == 0)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Select one or more cleanup recommendations first.";
            return;
        }

        var stemsToSelect = selectedRecommendations
            .SelectMany(recommendation => recommendation.RelatedStems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectMeetingsByStem(stemsToSelect);
        MeetingCleanupRecommendationsStatusTextBlock.Text =
            $"Selected {stemsToSelect.Length} related meeting(s) in the main list.";
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

    private async Task<string> QueueTranscriptRegenerationAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken)
    {
        return await QueueMeetingReprocessingAsync(
            meeting,
            "Queued to re-generate the transcript.",
            cancellationToken);
    }

    private async Task<string> QueueSpeakerLabelGenerationAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken)
    {
        return await QueueMeetingReprocessingAsync(
            meeting,
            "Queued to add speaker labels.",
            cancellationToken);
    }

    private async Task<string> QueueMeetingReprocessingAsync(
        MeetingOutputRecord meeting,
        string transcriptionQueuedMessage,
        CancellationToken cancellationToken)
    {
        var manifestPath = meeting.ManifestPath;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestPath = await _meetingOutputCatalogService.CreateSyntheticManifestForPublishedMeetingAsync(
                meeting,
                _liveConfig.Current.WorkDir,
                cancellationToken);
            AppendActivity($"Created a synthetic work manifest for '{meeting.Title}' from the published audio file.");
        }

        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var queuedManifest = manifest with
        {
            State = SessionState.Queued,
            ErrorSummary = null,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Queued, now, transcriptionQueuedMessage),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
        };

        await _manifestStore.SaveAsync(queuedManifest, manifestPath, cancellationToken);
        await _processingQueue.EnqueueAsync(manifestPath, cancellationToken);
        return manifestPath;
    }

    private async Task<SplitMeetingResult> QueueSplitMeetingAsync(
        MeetingOutputRecord meeting,
        TimeSpan splitPoint,
        CancellationToken cancellationToken)
    {
        var splitResult = await _meetingOutputCatalogService.SplitMeetingAsync(
            meeting,
            splitPoint,
            _liveConfig.Current.WorkDir,
            cancellationToken);

        await _processingQueue.EnqueueAsync(splitResult.FirstManifestPath, cancellationToken);
        await _processingQueue.EnqueueAsync(splitResult.SecondManifestPath, cancellationToken);
        return splitResult;
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
            await QueueTranscriptRegenerationAsync(selectedMeeting.Source, _lifetimeCts.Token);
            await RefreshMeetingListAsync(selectedMeeting.Source.Stem);
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
            var splitResult = await QueueSplitMeetingAsync(selectedMeeting.Source, splitPoint, _lifetimeCts.Token);
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
        SetConfigSaveStatus("Saving config...");

        try
        {
            var currentConfig = _liveConfig.Current;
            if (!double.TryParse(ConfigAutoDetectThresholdTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
            {
                throw new InvalidOperationException("Auto-detect audio threshold must be a number.");
            }

            if (!int.TryParse(ConfigMeetingStopTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stopTimeoutSeconds))
            {
                throw new InvalidOperationException("Meeting stop timeout must be a whole number of seconds.");
            }

            var nextConfig = new AppConfig
            {
                AudioOutputDir = ConfigAudioOutputDirTextBox.Text.Trim(),
                TranscriptOutputDir = ConfigTranscriptOutputDirTextBox.Text.Trim(),
                WorkDir = ConfigWorkDirTextBox.Text.Trim(),
                ModelCacheDir = currentConfig.ModelCacheDir,
                TranscriptionModelPath = currentConfig.TranscriptionModelPath,
                TranscriptionModelProfilePreference = currentConfig.TranscriptionModelProfilePreference,
                DiarizationAssetPath = currentConfig.DiarizationAssetPath,
                SpeakerLabelingModelProfilePreference = currentConfig.SpeakerLabelingModelProfilePreference,
                DiarizationAccelerationPreference = ConfigDiarizationGpuAccelerationCheckBox.IsChecked == true
                    ? InferenceAccelerationPreference.Auto
                    : InferenceAccelerationPreference.CpuOnly,
                MicCaptureEnabled = ConfigMicCaptureCheckBox.IsChecked == true,
                LaunchOnLoginEnabled = ConfigLaunchOnLoginCheckBox.IsChecked == true,
                AutoDetectEnabled = ConfigAutoDetectCheckBox.IsChecked == true,
                AutoDetectSecurityPromptMigrationApplied = currentConfig.AutoDetectSecurityPromptMigrationApplied,
                CalendarTitleFallbackEnabled = ConfigCalendarTitleFallbackCheckBox.IsChecked == true,
                MeetingAttendeeEnrichmentEnabled = ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked == true,
                UpdateCheckEnabled = ConfigUpdateCheckEnabledCheckBox.IsChecked == true,
                AutoInstallUpdatesEnabled = ConfigAutoInstallUpdatesCheckBox.IsChecked == true,
                UpdateFeedUrl = ConfigUpdateFeedUrlTextBox.Text.Trim(),
                PreferredTeamsIntegrationMode = ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue is PreferredTeamsIntegrationMode preferredTeamsIntegrationMode
                    ? preferredTeamsIntegrationMode
                    : currentConfig.PreferredTeamsIntegrationMode,
                TeamsGraphTenantId = currentConfig.TeamsGraphTenantId,
                TeamsGraphClientId = currentConfig.TeamsGraphClientId,
                TeamsCapabilitySnapshot = currentConfig.TeamsCapabilitySnapshot,
                BackgroundProcessingMode = ConfigBackgroundProcessingModeComboBox.SelectedValue is BackgroundProcessingMode backgroundProcessingMode
                    ? backgroundProcessingMode
                    : currentConfig.BackgroundProcessingMode,
                BackgroundSpeakerLabelingMode = ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValue is BackgroundSpeakerLabelingMode backgroundSpeakerLabelingMode
                    ? backgroundSpeakerLabelingMode
                    : currentConfig.BackgroundSpeakerLabelingMode,
                LastUpdateCheckUtc = currentConfig.LastUpdateCheckUtc,
                InstalledReleaseVersion = currentConfig.InstalledReleaseVersion,
                InstalledReleasePublishedAtUtc = currentConfig.InstalledReleasePublishedAtUtc,
                InstalledReleaseAssetSizeBytes = currentConfig.InstalledReleaseAssetSizeBytes,
                PendingUpdateZipPath = currentConfig.PendingUpdateZipPath,
                PendingUpdateVersion = currentConfig.PendingUpdateVersion,
                PendingUpdatePublishedAtUtc = currentConfig.PendingUpdatePublishedAtUtc,
                PendingUpdateAssetSizeBytes = currentConfig.PendingUpdateAssetSizeBytes,
                AutoDetectAudioPeakThreshold = threshold,
                MeetingStopTimeoutSeconds = stopTimeoutSeconds,
                MeetingsViewMode = currentConfig.MeetingsViewMode,
                MeetingsGroupedViewMigrationApplied = currentConfig.MeetingsGroupedViewMigrationApplied,
                MeetingsSortKey = currentConfig.MeetingsSortKey,
                MeetingsSortDescending = currentConfig.MeetingsSortDescending,
                MeetingsGroupKey = currentConfig.MeetingsGroupKey,
                DismissedMeetingRecommendations = currentConfig.DismissedMeetingRecommendations,
            };

            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);

            _pendingConfigEditorSnapshotRestore = null;
            var liveMicCaptureUpdated = true;
            if (currentConfig.MicCaptureEnabled != nextConfig.MicCaptureEnabled)
            {
                liveMicCaptureUpdated = await ApplyLiveMicCapturePreferenceIfNeededAsync(
                    nextConfig.MicCaptureEnabled,
                    "Settings save",
                    _lifetimeCts.Token);
            }

            if (liveMicCaptureUpdated)
            {
                _shellStatusOverride = null;
            }

            if (liveMicCaptureUpdated)
            {
                SetConfigSaveStatus("Config saved and applied to the running app.");
            }

            UpdateDashboardReadiness();
        }
        catch (Exception exception)
        {
            SetConfigSaveStatus($"Save failed: {exception.Message}");
            AppendActivity($"Config save failed: {exception.Message}");
        }
        finally
        {
            _isSavingConfig = false;
            UpdateConfigActionState();
        }
    }

    private async void RunTeamsIntegrationProbeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRunningTeamsIntegrationProbe)
        {
            return;
        }

        _isRunningTeamsIntegrationProbe = true;
        UpdateTeamsIntegrationProbeActionState();
        ConfigTeamsIntegrationStatusTextBlock.Text = "Running Teams probe...";
        ConfigTeamsIntegrationDetailTextBlock.Text =
            "Checking the heuristic detector and the Teams third-party API candidate.";
        ConfigTeamsIntegrationMetadataTextBlock.Text =
            "Last probe: pending current run." + Environment.NewLine +
            "Promotable path: calculating." + Environment.NewLine +
            "Block reason: none.";
        ConfigTeamsIntegrationBaselineTextBlock.Text =
            "Heuristic baseline: capturing the current local Teams detector result.";
        SetConfigSaveStatus("Running Teams probe...");

        try
        {
            var probeConfig = BuildTeamsProbeConfigFromEditor(_liveConfig.Current);
            var result = await _teamsIntegrationProbeService.RunAsync(
                probeConfig,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            _lastTeamsProbeBaselineSummary = result.HeuristicBaselineSummary;

            var editorSnapshot = ReadConfigEditorSnapshot();
            var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(_liveConfig.Current, editorSnapshot);
            _pendingConfigEditorSnapshotRestore = hasPendingChanges
                ? editorSnapshot
                : null;

            var updatedConfig = _liveConfig.Current with
            {
                TeamsCapabilitySnapshot = result.CapabilitySnapshot,
            };
            await _liveConfig.SaveAsync(updatedConfig, _lifetimeCts.Token);
            UpdateTeamsIntegrationProbePresentation(updatedConfig, result.HeuristicBaselineSummary);
            SetConfigSaveStatus("Teams probe completed and the capability snapshot was saved.");
            AppendActivity($"Teams probe completed. {result.CapabilitySnapshot.Summary}");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            ConfigTeamsIntegrationStatusTextBlock.Text = "Probe canceled.";
            ConfigTeamsIntegrationDetailTextBlock.Text = "The app is shutting down before the Teams probe completed.";
            ConfigTeamsIntegrationMetadataTextBlock.Text = BuildTeamsIntegrationMetadataText(_liveConfig.Current);
            ConfigTeamsIntegrationBaselineTextBlock.Text = _lastTeamsProbeBaselineSummary ??
                "Heuristic baseline: no saved probe result is available.";
        }
        catch (Exception exception)
        {
            ConfigTeamsIntegrationStatusTextBlock.Text = "Probe failed.";
            ConfigTeamsIntegrationDetailTextBlock.Text = exception.Message;
            ConfigTeamsIntegrationMetadataTextBlock.Text = BuildTeamsIntegrationMetadataText(_liveConfig.Current);
            ConfigTeamsIntegrationBaselineTextBlock.Text = _lastTeamsProbeBaselineSummary ??
                "Heuristic baseline: the probe did not finish cleanly.";
            SetConfigSaveStatus($"Teams probe failed: {exception.Message}");
            AppendActivity($"Teams probe failed: {exception.Message}");
        }
        finally
        {
            _isRunningTeamsIntegrationProbe = false;
            UpdateTeamsIntegrationProbeActionState();
        }
    }

    private void OpenTeamsThirdPartyApiGuideButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenExternalUrl(TeamsThirdPartyApiGuideUrl);
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
        OpenLatestReleasePage();
    }

    private void OpenLatestReleasePage()
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

    private void TranscriptionOverviewPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentTranscriptionSetupState is not { } setupState)
        {
            return;
        }

        if (setupState.PrimaryAction.Kind == ModelsTabSetupActionKind.OpenTranscriptionManagement)
        {
            NavigateToModelsSetupSection(
                SettingsTranscriptionSetupSectionBorder,
                setupState.PrimaryAction,
                ModelActionStatusTextBlock);
            return;
        }

        UseHighAccuracyTranscriptionProfileButton_OnClick(sender, e);
    }

    private async void UseStandardTranscriptionProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyCuratedModelProfilesAsync(
            TranscriptionModelProfilePreference.Standard,
            _liveConfig.Current.SpeakerLabelingModelProfilePreference,
            "Downloading the Standard transcription model...",
            "Transcription profile updated.",
            isTranscriptionHighAccuracyDownload: false,
            isSpeakerLabelingHighAccuracyDownload: false);
    }

    private async void UseHighAccuracyTranscriptionProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyCuratedModelProfilesAsync(
            TranscriptionModelProfilePreference.HighAccuracyDownloaded,
            _liveConfig.Current.SpeakerLabelingModelProfilePreference,
            "Trying the optional Higher Accuracy transcription download. If it does not finish, Setup will fall back to Standard when it can...",
            "Transcription profile updated.",
            isTranscriptionHighAccuracyDownload: true,
            isSpeakerLabelingHighAccuracyDownload: false);
    }

    private void ImportApprovedTranscriptionModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        ImportWhisperModelButton_OnClick(sender, e);
    }

    private void OpenTranscriptionModelFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenModelFolderButton_OnClick(sender, e);
    }

    private void DownloadRecommendedRemoteModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recommendedRow = GetRecommendedRemoteModelRow();
        if (recommendedRow is null)
        {
            ModelActionStatusTextBlock.Text =
                "No recommended GitHub model is available right now. Refresh GitHub Models or import an approved local file.";
            return;
        }

        AvailableRemoteModelsComboBox.SelectedItem = recommendedRow;
        DownloadSelectedRemoteModelButton_OnClick(sender, e);
    }

    private void SpeakerLabelingOverviewPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentSpeakerLabelingSetupState is not { } setupState)
        {
            return;
        }

        if (setupState.PrimaryAction.Kind == ModelsTabSetupActionKind.OpenSpeakerLabelingManagement)
        {
            NavigateToModelsSetupSection(
                SettingsSpeakerLabelingSetupSectionBorder,
                setupState.PrimaryAction,
                DiarizationActionStatusTextBlock);
            return;
        }

        UseHighAccuracySpeakerLabelingProfileButton_OnClick(sender, e);
    }

    private async void UseStandardSpeakerLabelingProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyCuratedModelProfilesAsync(
            _liveConfig.Current.TranscriptionModelProfilePreference,
            SpeakerLabelingModelProfilePreference.Standard,
            "Downloading the Standard speaker-labeling bundle...",
            "Speaker-labeling profile updated.",
            isTranscriptionHighAccuracyDownload: false,
            isSpeakerLabelingHighAccuracyDownload: false);
    }

    private async void SkipSpeakerLabelingForNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyCuratedModelProfilesAsync(
            _liveConfig.Current.TranscriptionModelProfilePreference,
            SpeakerLabelingModelProfilePreference.Disabled,
            "Turning speaker labeling off for now...",
            "Speaker-labeling preference updated.",
            isTranscriptionHighAccuracyDownload: false,
            isSpeakerLabelingHighAccuracyDownload: false);
    }

    private async void UseHighAccuracySpeakerLabelingProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyCuratedModelProfilesAsync(
            _liveConfig.Current.TranscriptionModelProfilePreference,
            SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
            "Trying the optional Higher Accuracy speaker-labeling download. If it does not finish, speaker labeling stays optional and Setup can retry later...",
            "Speaker-labeling profile updated.",
            isTranscriptionHighAccuracyDownload: false,
            isSpeakerLabelingHighAccuracyDownload: true);
    }

    private async void SetupSpeakerLabelingRunModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSpeakerLabelingModeSelectors ||
            !_isUiReady ||
            SetupSpeakerLabelingRunModeComboBox.SelectedValue is not BackgroundSpeakerLabelingMode selectedMode)
        {
            return;
        }

        await SaveSpeakerLabelingRunModeQuickSettingAsync(selectedMode, "Setup");
    }

    private void ImportApprovedSpeakerLabelingButton_OnClick(object sender, RoutedEventArgs e)
    {
        ImportDiarizationAssetButton_OnClick(sender, e);
    }

    private void OpenSpeakerLabelingAssetFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDiarizationFolderButton_OnClick(sender, e);
    }

    private void DownloadRecommendedDiarizationBundleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var recommendedRow = GetRecommendedRemoteDiarizationAssetRow();
        if (recommendedRow is null)
        {
            DiarizationActionStatusTextBlock.Text =
                "No recommended speaker-labeling model bundle is available right now. Refresh Diarization Assets, open local setup help, import an approved local bundle or files, or open the asset folder.";
            return;
        }

        AvailableRemoteDiarizationAssetsComboBox.SelectedItem = recommendedRow;
        DownloadSelectedRemoteDiarizationAssetButton_OnClick(sender, e);
    }

    private void SpeakerLabelingSetupGuideLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSpeakerLabelingSetupGuide();
    }

    private void OpenSpeakerLabelingSetupGuide()
    {
        var resolution = ModelsTabGuidance.ResolveSpeakerLabelingSetupGuidePath(
            AppContext.BaseDirectory,
            new Uri(SpeakerLabelingSetupGuideFallbackUrl, UriKind.Absolute));

        if (!resolution.UsedFallback && !string.IsNullOrWhiteSpace(resolution.LocalPath))
        {
            DiarizationActionStatusTextBlock.Text = "Opened the bundled local setup guide. Use it to review local install and import options.";
            OpenPath(resolution.LocalPath);
            return;
        }

        DiarizationActionStatusTextBlock.Text = "Local setup guide was not found. Opened the GitHub setup guide instead.";
        OpenExternalUrl(resolution.Uri.ToString());
    }

    private void NavigateToModelsSetupSection(
        FrameworkElement section,
        ModelsTabSetupAction action,
        TextBlock statusTextBlock)
    {
        statusTextBlock.Text = action.NextStepStatusText;
        section.BringIntoView();

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (FindName(action.FocusTargetName) is FrameworkElement focusTarget &&
                    focusTarget.IsVisible &&
                    focusTarget.IsEnabled)
                {
                    focusTarget.Focus();
                    Keyboard.Focus(focusTarget);
                }
            }));
    }

    private async Task ApplyCuratedModelProfilesAsync(
        TranscriptionModelProfilePreference transcriptionProfile,
        SpeakerLabelingModelProfilePreference speakerLabelingProfile,
        string startingStatus,
        string successPrefix,
        bool isTranscriptionHighAccuracyDownload,
        bool isSpeakerLabelingHighAccuracyDownload)
    {
        _isDownloadingRemoteModel = isTranscriptionHighAccuracyDownload;
        _isDownloadingRemoteDiarizationAsset = isSpeakerLabelingHighAccuracyDownload;
        _isActivatingModel = !isTranscriptionHighAccuracyDownload &&
            transcriptionProfile == TranscriptionModelProfilePreference.Standard;
        UpdateModelActionButtons();
        UpdateDiarizationActionButtons();

        ModelActionStatusTextBlock.Text = startingStatus;
        DiarizationActionStatusTextBlock.Text = startingStatus;

        try
        {
            var provisioningResult = await _modelProvisioningService.ProvisionAsync(
                new ModelProvisioningRequest(
                    InstallRoot: AppContext.BaseDirectory,
                    ModelCatalogPath: _meetingRecorderModelCatalogService.GetBundledCatalogPath(),
                    UpdateFeedUrl: _liveConfig.Current.UpdateFeedUrl,
                    TranscriptionProfile: transcriptionProfile,
                    SpeakerLabelingProfile: speakerLabelingProfile,
                    RespectExistingConfigPreferences: false),
                _lifetimeCts.Token);
            var nextConfig = provisioningResult.Config with
            {
                BackgroundSpeakerLabelingMode = MainWindowInteractionLogic.ResolveBackgroundSpeakerLabelingModeAfterProfileSelection(
                    provisioningResult.Config.BackgroundSpeakerLabelingMode,
                    speakerLabelingProfile),
            };
            var speakerLabelingModeAutoEnabled =
                speakerLabelingProfile != SpeakerLabelingModelProfilePreference.Disabled &&
                provisioningResult.Config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
                nextConfig.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Throttled;

            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);
            RefreshWhisperModelStatus();
            RefreshDiarizationAssetStatus();
            ApplyProvisioningResultToSetupStatus(provisioningResult.Result, successPrefix);
            if (speakerLabelingModeAutoEnabled)
            {
                DiarizationActionStatusTextBlock.Text =
                    $"{DiarizationActionStatusTextBlock.Text} Automatic speaker labeling will now run in Throttled mode.".Trim();
            }
            AppendActivity(
                $"Updated curated model profiles. Transcription requested={provisioningResult.Result.Transcription.RequestedProfile}; speaker labeling requested={provisioningResult.Result.SpeakerLabeling.RequestedProfile}.");
        }
        catch (Exception exception)
        {
            ModelActionStatusTextBlock.Text = $"Transcription setup update failed: {exception.Message}";
            DiarizationActionStatusTextBlock.Text = $"Speaker-labeling setup update failed: {exception.Message}";
            AppendActivity($"Curated model profile update failed: {exception.Message}");
        }
        finally
        {
            _isDownloadingRemoteModel = false;
            _isDownloadingRemoteDiarizationAsset = false;
            _isActivatingModel = false;
            ResetModelDownloadProgress();
            UpdateModelActionButtons();
            UpdateDiarizationActionButtons();
        }
    }

    private void ApplyProvisioningResultToSetupStatus(ModelProvisioningResult provisioningResult, string successPrefix)
    {
        ModelActionStatusTextBlock.Text = $"{successPrefix} {provisioningResult.Transcription.Detail}".Trim();
        DiarizationActionStatusTextBlock.Text = $"{successPrefix} {provisioningResult.SpeakerLabeling.Detail}".Trim();
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
            var profilePreference = _meetingRecorderModelCatalogService.ResolveTranscriptionProfilePreference(
                _bundledModelCatalog,
                _liveConfig.Current.ModelCacheDir,
                selectedModel.Source.ModelPath);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                TranscriptionModelPath = selectedModel.Source.ModelPath,
                TranscriptionModelProfilePreference = profilePreference,
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
            var profilePreference = _meetingRecorderModelCatalogService.ResolveTranscriptionProfilePreference(
                _bundledModelCatalog,
                _liveConfig.Current.ModelCacheDir,
                imported.ModelPath);
            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                TranscriptionModelPath = imported.ModelPath,
                TranscriptionModelProfilePreference = profilePreference,
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
                TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Custom,
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
            var profilePreference = _meetingRecorderModelCatalogService.ResolveSpeakerLabelingProfilePreference(
                _bundledModelCatalog,
                _liveConfig.Current.ModelCacheDir,
                installed.AssetRootPath);
            var nextConfig = _liveConfig.Current with
            {
                DiarizationAssetPath = installed.AssetRootPath,
                SpeakerLabelingModelProfilePreference = profilePreference,
                BackgroundSpeakerLabelingMode = MainWindowInteractionLogic.ResolveBackgroundSpeakerLabelingModeAfterProfileSelection(
                    _liveConfig.Current.BackgroundSpeakerLabelingMode,
                    profilePreference),
            };
            var speakerLabelingModeAutoEnabled =
                _liveConfig.Current.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
                nextConfig.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Throttled;
            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);
            RefreshDiarizationAssetStatus();
            await RefreshRemoteDiarizationAssetCatalogAsync(manual: false, _lifetimeCts.Token);
            DiarizationActionStatusTextBlock.Text =
                $"Installed '{selectedAsset.Source.FileName}' into '{installed.AssetRootPath}'. Speaker labeling is now {GetDiarizationAvailabilityText(installed)}.";
            if (speakerLabelingModeAutoEnabled)
            {
                DiarizationActionStatusTextBlock.Text =
                    $"{DiarizationActionStatusTextBlock.Text} Automatic speaker labeling will now run in Throttled mode.".Trim();
            }
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
                Title = "Select a diarization model bundle or supporting file",
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
            var nextConfig = _liveConfig.Current with
            {
                DiarizationAssetPath = installed.AssetRootPath,
                SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Custom,
                BackgroundSpeakerLabelingMode = MainWindowInteractionLogic.ResolveBackgroundSpeakerLabelingModeAfterProfileSelection(
                    _liveConfig.Current.BackgroundSpeakerLabelingMode,
                    SpeakerLabelingModelProfilePreference.Custom),
            };
            var speakerLabelingModeAutoEnabled =
                _liveConfig.Current.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
                nextConfig.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Throttled;
            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);
            RefreshDiarizationAssetStatus();
            DiarizationActionStatusTextBlock.Text =
                $"Imported '{Path.GetFileName(dialog.FileName)}' into '{installed.AssetRootPath}'. Speaker labeling is now {GetDiarizationAvailabilityText(installed)}.";
            if (speakerLabelingModeAutoEnabled)
            {
                DiarizationActionStatusTextBlock.Text =
                    $"{DiarizationActionStatusTextBlock.Text} Automatic speaker labeling will now run in Throttled mode.".Trim();
            }
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
        UpdateDetectedAudioSourceSurface(null);
        HomePrimaryActionButton.Content = _isRecordingTransitionInProgress && !_recordingCoordinator.IsRecording
            ? "STARTING"
            : "START";
        StopButton.Content = _isRecordingTransitionInProgress
            ? "STOPPING"
            : "STOP";
        HomePrimaryActionButton.IsEnabled = !_recordingCoordinator.IsRecording &&
            !_isUpdateInstallInProgress &&
            !_isRecordingTransitionInProgress &&
            HasReadyTranscriptionModel();
        StopButton.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        CurrentMeetingTitleTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        CurrentMeetingProjectTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        CurrentMeetingKeyAttendeesTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
        UpdateCurrentMeetingTitleStatus();
        UpdateAudioGraphTimerState();
        UpdateCaptureStatusSurface();
        UpdateUpdateActionButtons();
        UpdateDashboardReadiness();
    }

    private void UpdateDetectedAudioSourceSurface(DetectionDecision? decision)
    {
        var audioSource = _recordingCoordinator.ActiveSession?.Manifest.DetectedAudioSource
            ?? decision?.DetectedAudioSource;
        _lastObservedDetectedAudioSource = audioSource;

        CurrentDetectedAudioSourceTextBlock.Text = audioSource is null
            ? "Detected audio source: waiting for supported meeting audio."
            : $"Detected audio source: {MainWindowInteractionLogic.BuildDetectedAudioSourceSummary(audioSource)}";

        UpdateCaptureStatusSurface();
        _helpWindow?.SetRuntimeDiagnostics(BuildRuntimeDiagnosticsText());
    }

    private void UpdateCurrentMeetingEditor()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            _sessionTitleDraftTracker.Clear();
            _sessionProjectDraftTracker.Clear();
            _sessionKeyAttendeesDraftTracker.Clear();
            _currentMeetingOptionalMetadataSaveTimer.Stop();
        }

        var nextTitleText = activeSession is null
            ? string.Empty
            : _sessionTitleDraftTracker.GetDisplayTitle(
                activeSession.Manifest.SessionId,
                activeSession.Manifest.DetectedTitle);
        var nextProjectText = activeSession is null
            ? string.Empty
            : _sessionProjectDraftTracker.GetDisplayTitle(
                activeSession.Manifest.SessionId,
                activeSession.Manifest.ProjectName ?? string.Empty);
        var nextKeyAttendeesText = activeSession is null
            ? string.Empty
            : _sessionKeyAttendeesDraftTracker.GetDisplayTitle(
                activeSession.Manifest.SessionId,
                FormatKeyAttendeesForDisplay(activeSession.Manifest.KeyAttendees));

        _isUpdatingCurrentMeetingEditor = true;
        try
        {
            if (!string.Equals(CurrentMeetingTitleTextBox.Text, nextTitleText, StringComparison.Ordinal))
            {
                CurrentMeetingTitleTextBox.Text = nextTitleText;
            }

            if (!string.Equals(CurrentMeetingProjectTextBox.Text, nextProjectText, StringComparison.Ordinal))
            {
                CurrentMeetingProjectTextBox.Text = nextProjectText;
            }

            if (!string.Equals(CurrentMeetingKeyAttendeesTextBox.Text, nextKeyAttendeesText, StringComparison.Ordinal))
            {
                CurrentMeetingKeyAttendeesTextBox.Text = nextKeyAttendeesText;
            }

            var isEnabled = activeSession is not null && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;
            CurrentMeetingTitleTextBox.IsEnabled = isEnabled;
            CurrentMeetingProjectTextBox.IsEnabled = isEnabled;
            CurrentMeetingKeyAttendeesTextBox.IsEnabled = isEnabled;
        }
        finally
        {
            _isUpdatingCurrentMeetingEditor = false;
        }

        UpdateCurrentMeetingTitleStatus();
        UpdateCurrentRecordingElapsedText();
        UpdateAudioGraphTimerState();
    }

    private static string? NormalizeOptionalMeetingMetadataText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string FormatKeyAttendeesForDisplay(IReadOnlyList<string>? keyAttendees)
    {
        return keyAttendees is null || keyAttendees.Count == 0
            ? string.Empty
            : string.Join(", ", keyAttendees);
    }

    internal static IReadOnlyList<string> ParseDelimitedKeyAttendeesText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return MeetingMetadataNameMatcher.MergeNames(
            value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Array.Empty<string>());
    }

    private Task RefreshMeetingListAsync()
    {
        return RefreshMeetingListForCurrentContextAsync();
    }

    private Task RefreshMeetingListForCurrentContextAsync(bool bypassAttendeeNoMatchCacheForVisibleRows = false)
    {
        return RefreshMeetingListAsync(
            selectedStem: null,
            refreshMode: ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)
                ? MeetingRefreshMode.Full
                : MeetingRefreshMode.Fast,
            bypassAttendeeNoMatchCacheForVisibleRows);
    }

    private Task RefreshMeetingListAsync(MeetingRefreshMode refreshMode, bool bypassAttendeeNoMatchCacheForVisibleRows = false)
    {
        return RefreshMeetingListAsync(
            selectedStem: null,
            refreshMode,
            bypassAttendeeNoMatchCacheForVisibleRows);
    }

    private async Task RefreshMeetingListAsync(
        string? selectedStem = null,
        MeetingRefreshMode refreshMode = MeetingRefreshMode.Full,
        bool bypassAttendeeNoMatchCacheForVisibleRows = false)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        CancelMeetingBackgroundWork();
        Interlocked.Increment(ref _meetingBaselineRefreshOperations);
        UpdateMeetingActionState();

        var refreshVersion = Interlocked.Increment(ref _meetingRefreshVersion);
        var config = _liveConfig.Current;
        var selectedStems = string.IsNullOrWhiteSpace(selectedStem)
            ? GetSelectedMeetingRows().Select(row => row.Source.Stem).ToArray()
            : [selectedStem];
        var forcedVisibleStems = bypassAttendeeNoMatchCacheForVisibleRows
            ? GetVisibleMeetingRows().Select(row => row.Source.Stem).ToArray()
            : Array.Empty<string>();
        SelectedMeetingStatusTextBlock.Text = "Loading recent and published meetings...";
        if (refreshMode == MeetingRefreshMode.Full)
        {
            _hasCompletedFullMeetingsRefresh = false;
        }

        UpdateMeetingsRefreshStateText();
        _logger.Log(
            $"Meeting list refresh {refreshVersion} started. " +
            $"mode='{refreshMode}', audioDir='{config.AudioOutputDir}', transcriptDir='{config.TranscriptOutputDir}', workDir='{config.WorkDir}', selectedStem='{selectedStem ?? string.Empty}'.");

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

            _meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();
            _allMeetingRows = BuildMeetingRows(records, _meetingCleanupRecommendations);
            MeetingsDataGrid.ItemsSource = _allMeetingRows;
            ApplyMeetingsWorkspaceView(selectedStems);
            _logger.Log(
                $"Meeting list refresh {refreshVersion} completed. " +
                $"rows={_allMeetingRows.Length}, audio={_allMeetingRows.Count(row => !string.IsNullOrWhiteSpace(row.Source.AudioPath))}, " +
                $"transcripts={_allMeetingRows.Count(row => !string.IsNullOrWhiteSpace(row.Source.MarkdownPath) || !string.IsNullOrWhiteSpace(row.Source.JsonPath))}, " +
                $"manifests={_allMeetingRows.Count(row => !string.IsNullOrWhiteSpace(row.Source.ManifestPath))}.");

            if (refreshMode == MeetingRefreshMode.Full)
            {
                var backgroundToken = CreateMeetingBackgroundWorkToken();
                StartMeetingCleanupRecommendationRefresh(records, refreshVersion, backgroundToken);
                StartMeetingAttendeeBackfillRefresh(records, refreshVersion, config, forcedVisibleStems, backgroundToken);
                TryMarkFullMeetingsRefreshCompleted(refreshVersion);
            }

            UpdateMeetingsRefreshStateText();
        }
        catch (OperationCanceledException)
        {
            _logger.Log($"Meeting list refresh {refreshVersion} was canceled.");
            // Ignore refresh cancellation during shutdown or superseded refresh requests.
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting list refresh {refreshVersion} failed: {exception}");
            AppendActivity($"Failed to load recent and published meetings: {exception.Message}");
            _meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();
            _allMeetingRows = Array.Empty<MeetingListRow>();
            MeetingsDataGrid.ItemsSource = _allMeetingRows;
            MeetingCleanupRecommendationsDataGrid.ItemsSource = Array.Empty<MeetingCleanupRecommendationRow>();
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Cleanup suggestions are unavailable because the meeting list failed to load.";
            MeetingCleanupReviewBannerBorder.Visibility = Visibility.Collapsed;
            MeetingCleanupReviewBannerTextBlock.Text = string.Empty;
            UpdateSelectedMeetingEditor(null);
            UpdateMeetingsRefreshStateText("Meeting details unavailable. Refresh to retry.");
        }
        finally
        {
            Interlocked.Decrement(ref _meetingBaselineRefreshOperations);
            UpdateMeetingsRefreshStateText();
            UpdateMeetingActionState();
        }
    }

    private void StartMeetingCleanupRecommendationRefresh(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _meetingCleanupRefreshOperations);
        UpdateMeetingsRefreshStateText();
        _ = RunMeetingCleanupRecommendationRefreshAsync(records, refreshVersion, cancellationToken);
    }

    private async Task RunMeetingCleanupRecommendationRefreshAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspections = await BuildMeetingInspectionsAsync(records, cancellationToken);
            var visibleRecommendations = (await BuildVisibleMeetingCleanupRecommendationsAsync(inspections, cancellationToken)).ToArray();
            if (refreshVersion != Volatile.Read(ref _meetingRefreshVersion) || cancellationToken.IsCancellationRequested || IsShutdownRequested)
            {
                return;
            }

            _meetingCleanupRecommendations = visibleRecommendations;
            ApplyMeetingRowsUpdate(records, _meetingCleanupRecommendations, preserveEditorDrafts: true);
        }
        catch (OperationCanceledException)
        {
            // Ignore superseded or shutdown background work.
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting cleanup recommendation refresh {refreshVersion} failed: {exception}");
        }
        finally
        {
            Interlocked.Decrement(ref _meetingCleanupRefreshOperations);
            TryMarkFullMeetingsRefreshCompleted(refreshVersion);
            UpdateMeetingsRefreshStateText();
            UpdateMeetingActionState();
        }
    }

    private void StartMeetingAttendeeBackfillRefresh(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        AppConfig config,
        IReadOnlyList<string> forcedVisibleStems,
        CancellationToken cancellationToken)
    {
        if (!config.MeetingAttendeeEnrichmentEnabled)
        {
            return;
        }

        _meetingAttendeeBackfillAttemptedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _meetingAttendeeBackfillForcedStems = new HashSet<string>(forcedVisibleStems, StringComparer.OrdinalIgnoreCase);
        Interlocked.Increment(ref _meetingAttendeeBackfillOperations);
        UpdateMeetingsRefreshStateText();
        _ = RunMeetingAttendeeBackfillRefreshAsync(records, refreshVersion, config, cancellationToken);
    }

    private async Task RunMeetingAttendeeBackfillRefreshAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var workingRecords = records;
            while (!cancellationToken.IsCancellationRequested &&
                   !IsShutdownRequested &&
                   refreshVersion == Volatile.Read(ref _meetingRefreshVersion) &&
                   ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem))
            {
                var forceStems = _meetingAttendeeBackfillForcedStems.Count == 0
                    ? null
                    : (IReadOnlySet<string>)_meetingAttendeeBackfillForcedStems;
                var result = await _meetingsAttendeeBackfillService.BackfillBatchAsync(
                    workingRecords,
                    config,
                    DateTimeOffset.UtcNow,
                    forceStems,
                    _meetingAttendeeBackfillAttemptedStems,
                    cancellationToken);
                if (result.ProcessedStems.Count == 0)
                {
                    break;
                }

                foreach (var processedStem in result.ProcessedStems)
                {
                    _meetingAttendeeBackfillAttemptedStems.Add(processedStem);
                    _meetingAttendeeBackfillForcedStems.Remove(processedStem);
                }

                workingRecords = result.Records;
                if (result.UpdatedAnyMeeting &&
                    refreshVersion == Volatile.Read(ref _meetingRefreshVersion) &&
                    !cancellationToken.IsCancellationRequested &&
                    !IsShutdownRequested)
                {
                    ApplyMeetingRowsUpdate(workingRecords, _meetingCleanupRecommendations, preserveEditorDrafts: true);
                }

                if (!result.HasRemainingCandidates)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore superseded or shutdown background work.
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting attendee backfill refresh {refreshVersion} failed: {exception}");
        }
        finally
        {
            Interlocked.Decrement(ref _meetingAttendeeBackfillOperations);
            TryMarkFullMeetingsRefreshCompleted(refreshVersion);
            UpdateMeetingsRefreshStateText();
            UpdateMeetingActionState();
        }
    }

    private void ApplyMeetingRowsUpdate(
        IReadOnlyList<MeetingOutputRecord> records,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        bool preserveEditorDrafts)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        var selectedStems = GetSelectedMeetingRows()
            .Select(row => row.Source.Stem)
            .ToArray();
        _meetingCleanupRecommendations = recommendations.ToArray();
        _allMeetingRows = BuildMeetingRows(records, _meetingCleanupRecommendations);
        MeetingsDataGrid.ItemsSource = _allMeetingRows;
        ApplyMeetingsWorkspaceView(selectedStems, preserveEditorDrafts);
        UpdateProcessingQueueStatusUi();
    }

    private MeetingListRow[] BuildMeetingRows(
        IReadOnlyList<MeetingOutputRecord> records,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations)
    {
        var recommendationsByStem = recommendations
            .SelectMany(
                recommendation => recommendation.RelatedStems.Select(stem => new
                {
                    Stem = stem,
                    Recommendation = recommendation,
                }))
            .GroupBy(item => item.Stem, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MeetingCleanupRecommendation>)group.Select(item => item.Recommendation).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return records
            .Select(record => new MeetingListRow(
                record,
                recommendationsByStem.TryGetValue(record.Stem, out var recordRecommendations)
                    ? recordRecommendations
                    : Array.Empty<MeetingCleanupRecommendation>()))
            .ToArray();
    }

    private CancellationToken CreateMeetingBackgroundWorkToken()
    {
        CancelMeetingBackgroundWork();
        _meetingBackgroundWorkCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        return _meetingBackgroundWorkCts.Token;
    }

    private void CancelMeetingBackgroundWork()
    {
        if (_meetingBackgroundWorkCts is null)
        {
            return;
        }

        try
        {
            _meetingBackgroundWorkCts.Cancel();
        }
        catch
        {
            // Best effort only while replacing background work.
        }
        finally
        {
            _meetingBackgroundWorkCts.Dispose();
            _meetingBackgroundWorkCts = null;
        }
    }

    private void TryMarkFullMeetingsRefreshCompleted(int refreshVersion)
    {
        if (refreshVersion != Volatile.Read(ref _meetingRefreshVersion))
        {
            return;
        }

        if (Volatile.Read(ref _meetingCleanupRefreshOperations) > 0 ||
            Volatile.Read(ref _meetingAttendeeBackfillOperations) > 0)
        {
            return;
        }

        _hasCompletedFullMeetingsRefresh = true;
    }

    private void UpdateMeetingsRefreshStateText(string? overrideText = null)
    {
        _currentMeetingsRefreshStateText = BuildMeetingsRefreshStateText(overrideText);
        MeetingsRefreshStateTextBlock.Text = _currentMeetingsRefreshStateText ?? string.Empty;
        MeetingsRefreshStateTextBlock.Visibility = string.IsNullOrWhiteSpace(_currentMeetingsRefreshStateText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateProcessingQueueStatusUi();
    }

    private string? BuildMeetingsRefreshStateText(string? overrideText = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideText))
        {
            return overrideText;
        }

        if (Volatile.Read(ref _meetingBaselineRefreshOperations) > 0)
        {
            return "Loading recent and published meetings...";
        }

        if (_hasPendingMeetingsRefreshRequest &&
            MainWindowInteractionLogic.ShouldDeferMeetingRefresh(
                _recordingCoordinator.IsRecording,
                ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem)))
        {
            return _recordingCoordinator.IsRecording
                ? "Meeting list update queued until recording stops."
                : "Meeting list update queued until Meetings is visible.";
        }

        if (Volatile.Read(ref _meetingCleanupRefreshOperations) > 0 &&
            Volatile.Read(ref _meetingAttendeeBackfillOperations) > 0)
        {
            return "Loading details and cleanup suggestions in the background.";
        }

        if (Volatile.Read(ref _meetingCleanupRefreshOperations) > 0)
        {
            return "Loading cleanup suggestions in the background.";
        }

        if (Volatile.Read(ref _meetingAttendeeBackfillOperations) > 0)
        {
            return "Enriching attendees for recent meetings in the background.";
        }

        return null;
    }

    private async Task HandleMeetingsWorkspacePreferenceChangedAsync()
    {
        if (_isUpdatingMeetingsWorkspaceControls || !_isUiReady)
        {
            return;
        }

        UpdateMeetingsWorkspaceControlState();
        ApplyMeetingsWorkspaceView();

        try
        {
            var currentConfig = _liveConfig.Current;
            var updatedConfig = currentConfig with
            {
                MeetingsViewMode = GetSelectedMeetingsViewMode(),
                MeetingsSortKey = GetSelectedMeetingsSortKey(),
                MeetingsSortDescending = GetSelectedMeetingsSortDescending(),
                MeetingsGroupKey = GetSelectedMeetingsGroupKey(),
            };

            if (currentConfig == updatedConfig)
            {
                return;
            }

            await _liveConfig.SaveAsync(updatedConfig, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown races.
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to save Meetings view preferences: {exception.Message}");
        }
    }

    private void ApplyMeetingsWorkspaceView(
        IReadOnlyList<string>? preferredSelectedStems = null,
        bool preserveEditorDrafts = false)
    {
        if (MeetingsDataGrid.ItemsSource is null)
        {
            return;
        }

        var selectedViewMode = GetSelectedMeetingsViewMode();
        var selectedGroupKey = GetSelectedMeetingsGroupKey();
        var searchText = MeetingsSearchTextBox.Text;
        var selectedStems = preferredSelectedStems?.Count > 0
            ? preferredSelectedStems
            : GetSelectedMeetingRows().Select(row => row.Source.Stem).ToArray();
        var visibleRows = _allMeetingRows
            .Where(row => MainWindowInteractionLogic.MeetingMatchesWorkspaceSearch(
                searchText,
                row.Title,
                row.ProjectName,
                row.Platform,
                row.Status,
                row.Source.Attendees,
                row.Source.KeyAttendees))
            .ToArray();
        ApplyMeetingGroupDisplayLabels(visibleRows);
        ResetMeetingGroupExpansionState(selectedViewMode, selectedGroupKey, visibleRows);
        var visibleStemSet = new HashSet<string>(visibleRows.Select(row => row.Source.Stem), StringComparer.OrdinalIgnoreCase);
        var view = CollectionViewSource.GetDefaultView(MeetingsDataGrid.ItemsSource);
        if (view is null)
        {
            return;
        }

        using (view.DeferRefresh())
        {
            view.Filter = item => item is MeetingListRow row && visibleStemSet.Contains(row.Source.Stem);
            view.SortDescriptions.Clear();
            if (view is ListCollectionView listView)
            {
                listView.GroupDescriptions.Clear();
                if (selectedViewMode == MeetingsViewMode.Grouped)
                {
                    listView.GroupDescriptions.Add(
                        new PropertyGroupDescription(
                            MainWindowInteractionLogic.GetMeetingWorkspaceGroupPropertyName(selectedGroupKey)));
                }
            }

            if (selectedViewMode == MeetingsViewMode.Grouped)
            {
                view.SortDescriptions.Add(
                    new SortDescription(
                        MainWindowInteractionLogic.GetMeetingWorkspaceGroupSortPropertyName(selectedGroupKey),
                        GetMeetingsGroupSortDirection(selectedGroupKey)));
            }

            view.SortDescriptions.Add(
                new SortDescription(
                    MainWindowInteractionLogic.GetMeetingWorkspaceSortPropertyName(GetSelectedMeetingsSortKey()),
                    GetSelectedMeetingsSortDescending()
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending));
        }

        ReselectMeetingRows(selectedStems);
        UpdateMeetingsWorkspaceControlState();
        _ = Dispatcher.BeginInvoke(ApplyMeetingGroupExpansionStateToVisibleGroups, DispatcherPriority.Background);
        UpdateMeetingCleanupRecommendationsEditor(visibleRows);
        UpdateSelectedMeetingEditor(MeetingsDataGrid.SelectedItem as MeetingListRow, preserveEditorDrafts);
    }

    private void ApplyMeetingGroupDisplayLabels(IReadOnlyList<MeetingListRow> visibleRows)
    {
        foreach (var row in _allMeetingRows)
        {
            row.ResetGroupLabels();
        }

        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.WeekGroupBaseLabel,
            (row, label) => row.WeekGroupLabel = label);
        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.MonthGroupBaseLabel,
            (row, label) => row.MonthGroupLabel = label);
        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.PlatformGroupBaseLabel,
            (row, label) => row.PlatformGroupLabel = label);
        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.StatusGroupBaseLabel,
            (row, label) => row.StatusGroupLabel = label);
        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.ClientProjectGroupBaseLabel,
            (row, label) => row.ClientProjectGroupLabel = label);
        ApplyMeetingGroupDisplayLabels(
            visibleRows,
            row => row.AttendeeGroupBaseLabel,
            (row, label) => row.AttendeeGroupLabel = label);
    }

    private static void ApplyMeetingGroupDisplayLabels(
        IReadOnlyList<MeetingListRow> visibleRows,
        Func<MeetingListRow, string> getBaseLabel,
        Action<MeetingListRow, string> setDisplayLabel)
    {
        var countsByLabel = visibleRows
            .GroupBy(getBaseLabel, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var row in visibleRows)
        {
            var baseLabel = getBaseLabel(row);
            var itemCount = countsByLabel.TryGetValue(baseLabel, out var count) ? count : 0;
            setDisplayLabel(row, MainWindowInteractionLogic.FormatMeetingWorkspaceGroupHeader(baseLabel, itemCount));
        }
    }

    private void ResetMeetingGroupExpansionState(
        MeetingsViewMode viewMode,
        MeetingsGroupKey groupKey,
        IReadOnlyList<MeetingListRow> visibleRows)
    {
        if (viewMode != MeetingsViewMode.Grouped)
        {
            _meetingGroupExpansionStates.Clear();
            return;
        }

        var orderedLabels = GetOrderedMeetingGroupLabels(visibleRows, groupKey);
        _meetingGroupExpansionStates = new Dictionary<string, bool>(
            MainWindowInteractionLogic.InitializeMeetingWorkspaceGroupExpansionState(orderedLabels),
            StringComparer.Ordinal);
    }

    private IReadOnlyList<string> GetOrderedMeetingGroupLabels(
        IReadOnlyList<MeetingListRow> visibleRows,
        MeetingsGroupKey groupKey)
    {
        var orderedRows = groupKey switch
        {
            MeetingsGroupKey.Week => GetMeetingsGroupSortDirection(groupKey) == ListSortDirection.Descending
                ? visibleRows.OrderByDescending(row => row.WeekGroupSortValue)
                : visibleRows.OrderBy(row => row.WeekGroupSortValue),
            MeetingsGroupKey.Month => GetMeetingsGroupSortDirection(groupKey) == ListSortDirection.Descending
                ? visibleRows.OrderByDescending(row => row.MonthGroupSortValue)
                : visibleRows.OrderBy(row => row.MonthGroupSortValue),
            MeetingsGroupKey.Platform => visibleRows.OrderBy(row => row.PlatformGroupLabel, StringComparer.Ordinal),
            MeetingsGroupKey.Status => visibleRows.OrderBy(row => row.StatusGroupLabel, StringComparer.Ordinal),
            MeetingsGroupKey.ClientProject => visibleRows.OrderBy(row => row.ClientProjectGroupLabel, StringComparer.Ordinal),
            MeetingsGroupKey.Attendee => visibleRows.OrderBy(row => row.AttendeeGroupLabel, StringComparer.Ordinal),
            _ => visibleRows.OrderByDescending(row => row.WeekGroupSortValue),
        };

        return orderedRows
            .Select(row => row.GetGroupLabel(groupKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void ApplyMeetingGroupExpansionStateToVisibleGroups()
    {
        if (GetSelectedMeetingsViewMode() != MeetingsViewMode.Grouped)
        {
            return;
        }

        foreach (var expander in FindVisualChildren<Expander>(MeetingsDataGrid)
                     .Where(expander => expander.DataContext is CollectionViewGroup))
        {
            ApplyMeetingGroupExpansionState(expander);
        }
    }

    private void ApplyMeetingGroupExpansionState(Expander expander)
    {
        if (expander.DataContext is not CollectionViewGroup group)
        {
            return;
        }

        var groupLabel = group.Name as string ?? string.Empty;
        if (!_meetingGroupExpansionStates.TryGetValue(groupLabel, out var isExpanded))
        {
            isExpanded = true;
        }

        _isApplyingMeetingGroupExpansionState = true;
        try
        {
            expander.IsExpanded = isExpanded;
        }
        finally
        {
            _isApplyingMeetingGroupExpansionState = false;
        }
    }

    private void UpdateMeetingsWorkspaceControlState()
    {
        var isGroupedView = GetSelectedMeetingsViewMode() == MeetingsViewMode.Grouped;
        MeetingsGroupKeyComboBox.IsEnabled = isGroupedView;
        ExpandAllMeetingGroupsButton.Visibility = isGroupedView ? Visibility.Visible : Visibility.Collapsed;
        CollapseAllMeetingGroupsButton.Visibility = isGroupedView ? Visibility.Visible : Visibility.Collapsed;
        ExpandAllMeetingGroupsButton.IsEnabled = isGroupedView && !IsMeetingActionInProgress();
        CollapseAllMeetingGroupsButton.IsEnabled = isGroupedView && !IsMeetingActionInProgress();
    }

    private MeetingListRow[] GetVisibleMeetingRows()
    {
        var view = CollectionViewSource.GetDefaultView(MeetingsDataGrid.ItemsSource);
        if (view is null)
        {
            return Array.Empty<MeetingListRow>();
        }

        return view
            .Cast<object>()
            .OfType<MeetingListRow>()
            .ToArray();
    }

    private void ReselectMeetingRows(IReadOnlyList<string> selectedStems)
    {
        var visibleRows = GetVisibleMeetingRows();
        var stemSet = selectedStems.Count == 0
            ? null
            : new HashSet<string>(selectedStems, StringComparer.OrdinalIgnoreCase);

        MeetingsDataGrid.SelectedItems.Clear();
        MeetingListRow? selectedRow = null;
        foreach (var row in visibleRows)
        {
            if (stemSet is null || !stemSet.Contains(row.Source.Stem))
            {
                continue;
            }

            MeetingsDataGrid.SelectedItems.Add(row);
            selectedRow ??= row;
        }

        MeetingsDataGrid.SelectedItem = selectedRow;
    }

    private MeetingsViewMode GetSelectedMeetingsViewMode()
    {
        return MeetingsViewModeComboBox.SelectedValue is MeetingsViewMode value
            ? value
            : MeetingsViewMode.Grouped;
    }

    private MeetingsSortKey GetSelectedMeetingsSortKey()
    {
        return MeetingsSortKeyComboBox.SelectedValue is MeetingsSortKey value
            ? value
            : MeetingsSortKey.Started;
    }

    private bool GetSelectedMeetingsSortDescending()
    {
        return MeetingsSortDirectionComboBox.SelectedValue is bool value
            ? value
            : true;
    }

    private MeetingsGroupKey GetSelectedMeetingsGroupKey()
    {
        return MeetingsGroupKeyComboBox.SelectedValue is MeetingsGroupKey value
            ? value
            : MeetingsGroupKey.Week;
    }

    private void LoadMeetingsWorkspacePreferences(AppConfig config)
    {
        _isUpdatingMeetingsWorkspaceControls = true;
        try
        {
            MeetingsViewModeComboBox.SelectedValue = config.MeetingsViewMode;
            MeetingsSortKeyComboBox.SelectedValue = config.MeetingsSortKey;
            MeetingsSortDirectionComboBox.SelectedValue = config.MeetingsSortDescending;
            MeetingsGroupKeyComboBox.SelectedValue = config.MeetingsGroupKey;
        }
        finally
        {
            _isUpdatingMeetingsWorkspaceControls = false;
        }

        UpdateMeetingsWorkspaceControlState();
    }

    private static ListSortDirection GetMeetingsGroupSortDirection(MeetingsGroupKey groupKey)
    {
        return groupKey is MeetingsGroupKey.Week or MeetingsGroupKey.Month
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
    }

    private void UpdateSelectedMeetingEditor(MeetingListRow? row, bool preserveDraftInputs = false)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            SelectedMeetingTitleTextBox.Text = string.Empty;
            SelectedMeetingStatusTextBlock.Text = "Select a meeting to rename it directly, or select multiple meetings to apply suggestions in bulk.";
            UpdateSelectedMeetingInspector(null);
            UpdateMeetingProjectEditor(selectedMeetings, preserveDraftInputs);
            UpdateSpeakerLabelEditor(null, preserveDraftInputs);
            UpdateSplitMeetingEditor();
            UpdateMergeMeetingsEditor();
            UpdateMeetingActionState();
            return;
        }

        if (selectedMeetings.Length > 1)
        {
            SelectedMeetingTitleTextBox.Text = string.Empty;
            SelectedMeetingStatusTextBlock.Text =
                $"Selected {selectedMeetings.Length} meetings. Use Apply Suggestions to Selected for bulk renaming, or reduce the selection to one meeting to edit it directly.";
            UpdateSelectedMeetingInspector(row);
            UpdateMeetingProjectEditor(selectedMeetings, preserveDraftInputs);
            UpdateSpeakerLabelEditor(null, preserveDraftInputs);
            UpdateSplitMeetingEditor();
            UpdateMergeMeetingsEditor();
            UpdateMeetingActionState();
            return;
        }

        if (!preserveDraftInputs ||
            row is null ||
            !MainWindowInteractionLogic.HasPendingMeetingRename(row.Title, SelectedMeetingTitleTextBox.Text))
        {
            SelectedMeetingTitleTextBox.Text = row?.Title ?? string.Empty;
        }

        SelectedMeetingStatusTextBlock.Text = row is null
            ? "Select a meeting to rename it directly, or select multiple meetings to apply suggestions in bulk."
            : IsMeetingMarkedAsap(row)
                ? $"Status: {row.Status}. Marked ASAP in the processing queue. {row.RegenerationStatusText}"
                : $"Status: {row.Status}. {row.RegenerationStatusText}";
        UpdateSelectedMeetingInspector(row);
        UpdateMeetingProjectEditor(selectedMeetings, preserveDraftInputs);
        UpdateSpeakerLabelEditor(row, preserveDraftInputs);
        UpdateSplitMeetingEditor();
        UpdateMergeMeetingsEditor();
        UpdateMeetingActionState();
    }

    private void UpdateSelectedMeetingInspector(MeetingListRow? row)
    {
        if (row is null)
        {
            MeetingWorkspaceStatusTextBlock.Text = "Recent and published meetings from the current audio, transcript, and work-session folders.";
            SelectedMeetingInspectorStatusTextBlock.Text = "Select one meeting to review its details here. Multi-selection still drives bulk actions separately.";
            SelectedMeetingInspectorTitleTextBlock.Text = "No meeting selected";
            SelectedMeetingInspectorStartedTextBlock.Text = "Unknown";
            SelectedMeetingInspectorProjectTextBlock.Text = "None";
            SelectedMeetingInspectorDurationTextBlock.Text = "Unknown";
            SelectedMeetingInspectorPlatformTextBlock.Text = "Unknown";
            SelectedMeetingInspectorPublishedStatusTextBlock.Text = "Unknown";
            SelectedMeetingInspectorTranscriptModelTextBlock.Text = "Not recorded";
            SelectedMeetingInspectorSpeakerLabelsTextBlock.Text = "Speaker labels are missing.";
            SelectedMeetingInspectorDetectedAudioSourceTextBlock.Text = "Not captured.";
            SelectedMeetingInspectorCaptureDiagnosticsTextBlock.Text = "No capture diagnostics were recorded.";
            SelectedMeetingInspectorRecommendationItemsControl.ItemsSource = Array.Empty<string>();
            SelectedMeetingInspectorAttendeesItemsControl.ItemsSource = Array.Empty<string>();
            SelectedMeetingInspectorAttendeesEmptyTextBlock.Text = "No attendees captured yet.";
            return;
        }

        var inspectorState = MainWindowInteractionLogic.BuildMeetingInspectorState(row.Source, row.Recommendations);
        var selectedCount = GetSelectedMeetingRows().Length;
        MeetingWorkspaceStatusTextBlock.Text = selectedCount <= 1
            ? $"Focused on '{row.Title}'. Open artifacts directly or reveal compact tools below when you need deeper edits."
            : $"{selectedCount} meetings selected. Bulk actions stay available in the context menu and the compact drafts below.";
        var asapInspectorText = IsMeetingMarkedAsap(row)
            ? " This meeting is marked ASAP in the processing queue."
            : string.Empty;
        SelectedMeetingInspectorStatusTextBlock.Text = selectedCount <= 1
            ? $"Focused details for the current meeting selection.{asapInspectorText}"
            : $"Focused details for '{row.Title}'. {selectedCount} meetings are selected for bulk actions.{asapInspectorText}";
        SelectedMeetingInspectorTitleTextBlock.Text = inspectorState.Title;
        SelectedMeetingInspectorStartedTextBlock.Text = inspectorState.StartedAtUtc;
        SelectedMeetingInspectorProjectTextBlock.Text = string.IsNullOrWhiteSpace(inspectorState.ProjectName)
            ? "None"
            : inspectorState.ProjectName;
        SelectedMeetingInspectorDurationTextBlock.Text = inspectorState.Duration;
        SelectedMeetingInspectorPlatformTextBlock.Text = inspectorState.Platform;
        SelectedMeetingInspectorPublishedStatusTextBlock.Text = inspectorState.Status;
        SelectedMeetingInspectorTranscriptModelTextBlock.Text = inspectorState.TranscriptionModelFileName;
        SelectedMeetingInspectorSpeakerLabelsTextBlock.Text = inspectorState.SpeakerLabelState;
        SelectedMeetingInspectorDetectedAudioSourceTextBlock.Text = inspectorState.DetectedAudioSourceSummary;
        SelectedMeetingInspectorCaptureDiagnosticsTextBlock.Text = inspectorState.CaptureDiagnosticsSummary;
        SelectedMeetingInspectorRecommendationItemsControl.ItemsSource = inspectorState.RecommendationBadges;
        SelectedMeetingInspectorAttendeesItemsControl.ItemsSource = inspectorState.AttendeeNames;
        SelectedMeetingInspectorAttendeesEmptyTextBlock.Text = inspectorState.AttendeeNames.Count == 0
            ? "No attendees captured yet."
            : string.Empty;
    }

    private void UpdateMeetingProjectEditor(IReadOnlyList<MeetingListRow> selectedMeetings, bool preserveDraftInputs = false)
    {
        var recentProjects = _allMeetingRows
            .Select(row => row.Source.ProjectName)
            .Where(projectName => !string.IsNullOrWhiteSpace(projectName))
            .Select(projectName => projectName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(projectName => projectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedMeetingProjectComboBox.ItemsSource = recentProjects;

        if (selectedMeetings.Count == 0)
        {
            SelectedMeetingProjectComboBox.Text = string.Empty;
            SelectedMeetingProjectStatusTextBlock.Text = "Select one or more meetings to tag a project.";
            return;
        }

        var distinctProjects = selectedMeetings
            .Select(row => row.Source.ProjectName?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var projectText = distinctProjects.Length == 1
            ? distinctProjects[0]
            : string.Empty;

        if (!preserveDraftInputs || !HasPendingMeetingProjectDraft(selectedMeetings))
        {
            SelectedMeetingProjectComboBox.Text = projectText;
        }

        SelectedMeetingProjectStatusTextBlock.Text = selectedMeetings.Count == 1
            ? string.IsNullOrWhiteSpace(projectText)
                ? "Add an optional project label to make this meeting easier to find later."
                : $"Project '{projectText}' is stored with this meeting."
            : distinctProjects.Length == 1
                ? $"Selected {selectedMeetings.Count} meetings with shared project '{projectText}'."
                : $"Selected {selectedMeetings.Count} meetings with mixed projects. Enter one value to apply it to all selected meetings.";
    }

    private void SelectedMeetingProjectComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        UpdateMeetingActionState();
    }

    private void SelectedMeetingProjectComboBox_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        UpdateMeetingActionState();
    }

    private async void ApplyMeetingProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyMeetingProjectChangeAsync(SelectedMeetingProjectComboBox.Text, clearProject: false);
    }

    private async void ClearMeetingProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ApplyMeetingProjectChangeAsync(projectName: null, clearProject: true);
    }

    private async Task ApplyMeetingProjectChangeAsync(string? projectName, bool clearProject)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            SelectedMeetingProjectStatusTextBlock.Text = "Select one or more meetings before updating the project.";
            return;
        }

        var normalizedProjectName = clearProject ? null : projectName?.Trim();
        if (!clearProject && string.IsNullOrWhiteSpace(normalizedProjectName))
        {
            SelectedMeetingProjectStatusTextBlock.Text = "Enter a project name, or use Clear to remove the current project.";
            return;
        }

        _isUpdatingMeetingProject = true;
        UpdateMeetingActionState();
        SelectedMeetingProjectStatusTextBlock.Text = clearProject
            ? $"Clearing project metadata for {selectedMeetings.Length} meeting(s)..."
            : $"Applying project '{normalizedProjectName}' to {selectedMeetings.Length} meeting(s)...";

        try
        {
            foreach (var selectedMeeting in selectedMeetings)
            {
                await _meetingOutputCatalogService.UpdateMeetingProjectAsync(
                    _liveConfig.Current.AudioOutputDir,
                    _liveConfig.Current.TranscriptOutputDir,
                    selectedMeeting.Source.Stem,
                    normalizedProjectName,
                    _liveConfig.Current.WorkDir,
                    _lifetimeCts.Token);
            }

            await RefreshMeetingListAsync();
            SelectMeetingsByStem(selectedMeetings.Select(row => row.Source.Stem).ToArray());
            SelectedMeetingProjectStatusTextBlock.Text = clearProject
                ? $"Cleared project metadata for {selectedMeetings.Length} meeting(s)."
                : $"Applied project '{normalizedProjectName}' to {selectedMeetings.Length} meeting(s).";
        }
        catch (Exception exception)
        {
            SelectedMeetingProjectStatusTextBlock.Text = $"Project update failed: {exception.Message}";
            AppendActivity($"Meeting project update failed: {exception.Message}");
        }
        finally
        {
            _isUpdatingMeetingProject = false;
            UpdateMeetingActionState();
        }
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

    private void UpdateSpeakerLabelEditor(MeetingListRow? row, bool preserveDraftInputs = false)
    {
        if (preserveDraftInputs && HasPendingSpeakerLabelChanges())
        {
            UpdateMeetingActionState();
            return;
        }

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

        var selectedMeetings = GetSelectedMeetingRows();
        var selectedRecommendationRows = GetSelectedMeetingCleanupRecommendationRows();
        var singleSelectedMeeting = selectedMeetings.Length == 1 ? selectedMeetings[0] : null;
        var splitMeeting = selectedMeetings.Length == 1 ? selectedMeetings[0] : null;
        var isMeetingActionInProgress = IsMeetingActionInProgress();
        var canEditSplitPoint = splitMeeting?.Source.Duration is { } splitDuration &&
            splitDuration > TimeSpan.FromSeconds(2) &&
            !isMeetingActionInProgress;
        var matchingSelectedMeetingRecommendations = _meetingCleanupRecommendations
            .Where(recommendation => recommendation.RelatedStems.All(stem =>
                selectedMeetings.Any(row => string.Equals(row.Source.Stem, stem, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var hasSpeakerLabels = SpeakerLabelsEditorDataGrid.ItemsSource is IEnumerable<SpeakerLabelEditorRow> labelRows &&
            labelRows.Any();
        var visibleCleanupRecommendationRows = MeetingCleanupRecommendationsDataGrid.ItemsSource is IEnumerable<MeetingCleanupRecommendationRow> cleanupRows &&
            cleanupRows.Any();
        var workspaceToolState = MainWindowInteractionLogic.BuildMeetingWorkspaceToolState(
            selectedMeetings.Length,
            hasSpeakerLabels,
            visibleCleanupRecommendationRows);

        RefreshMeetingsButton.Content = Volatile.Read(ref _meetingBaselineRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh List";
        RenameSelectedMeetingButton.Content = _isRenamingMeeting ? "Renaming..." : "Rename Meeting";
        ApplySpeakerNamesButton.Content = _isApplyingSpeakerNames ? "Applying..." : "Apply Speaker Names";
        SplitSelectedMeetingButton.Content = _isSplittingMeeting ? "Splitting..." : "Split Into Two";
        MergeSelectedMeetingsButton.Content = _isMergingMeetings ? "Merging..." : "Merge Selected Meetings";
        ApplySelectedMeetingCleanupRecommendationsButton.Content = _isApplyingMeetingCleanupRecommendations
            ? "Applying..."
            : "Apply Selected Recommendation(s)";
        ApplyMeetingCleanupRecommendationsForSelectedMeetingsButton.Content = _isApplyingMeetingCleanupRecommendations
            ? "Applying..."
            : "Apply Recommended Actions for Selected Meetings";
        DismissSelectedMeetingCleanupRecommendationsButton.Content = _isDismissingMeetingCleanupRecommendations
            ? "Dismissing..."
            : "Dismiss Selected Recommendation(s)";
        ApplySafeMeetingCleanupFixesButton.Content = _isApplyingSafeMeetingCleanupFixes
            ? "Applying..."
            : "Apply Safe Fixes";
        LegacyMeetingCleanupReviewBorder.Visibility = workspaceToolState.ShowCleanupTray ? Visibility.Visible : Visibility.Collapsed;
        LegacyMeetingActionDraftsBorder.Visibility = workspaceToolState.ShowWorkspaceTools ? Visibility.Visible : Visibility.Collapsed;
        MeetingProjectToolCard.Visibility = workspaceToolState.ShowProjectTool ? Visibility.Visible : Visibility.Collapsed;
        SingleMeetingActionsHeadingTextBlock.Visibility = workspaceToolState.ShowSingleMeetingActions ? Visibility.Visible : Visibility.Collapsed;
        MultiMeetingActionsHeadingTextBlock.Visibility = workspaceToolState.ShowMultiMeetingActions ? Visibility.Visible : Visibility.Collapsed;
        MeetingTitleAndTranscriptToolCard.Visibility = workspaceToolState.ShowTitleAndTranscriptTool ? Visibility.Visible : Visibility.Collapsed;
        SplitMeetingToolCard.Visibility = workspaceToolState.ShowSplitTool ? Visibility.Visible : Visibility.Collapsed;
        MergeMeetingsToolCard.Visibility = workspaceToolState.ShowMergeTool ? Visibility.Visible : Visibility.Collapsed;
        SpeakerLabelsToolCard.Visibility = workspaceToolState.ShowSpeakerLabelsTool ? Visibility.Visible : Visibility.Collapsed;

        RefreshMeetingsButton.IsEnabled = !isMeetingActionInProgress;
        OpenSelectedTranscriptButton.IsEnabled = singleSelectedMeeting?.CanOpenTranscriptArtifact == true && !isMeetingActionInProgress;
        OpenSelectedAudioButton.IsEnabled = singleSelectedMeeting?.CanOpenAudioArtifact == true && !isMeetingActionInProgress;
        ReviewCleanupSuggestionsActionButton.IsEnabled = visibleCleanupRecommendationRows && !isMeetingActionInProgress;
        RenameMeetingActionButton.IsEnabled = singleSelectedMeeting is not null && !isMeetingActionInProgress;
        SuggestMeetingTitleActionButton.IsEnabled = singleSelectedMeeting is not null && !isMeetingActionInProgress;
        RetryTranscriptActionButton.IsEnabled = singleSelectedMeeting?.CanRegenerateTranscript == true && !isMeetingActionInProgress;
        var isSelectedMeetingAsap = IsMeetingMarkedAsap(singleSelectedMeeting);
        ProcessAsapActionButton.Content = isSelectedMeetingAsap
            ? "Clear ASAP"
            : _isUpdatingRushProcessing
                ? "Updating..."
                : "Process ASAP...";
        ProcessAsapActionButton.Visibility = singleSelectedMeeting is not null &&
            (CanChangeRushProcessing(singleSelectedMeeting) || isSelectedMeetingAsap)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProcessAsapActionButton.IsEnabled = singleSelectedMeeting is not null &&
            (CanChangeRushProcessing(singleSelectedMeeting) || isSelectedMeetingAsap) &&
            !isMeetingActionInProgress;
        SplitMeetingActionButton.IsEnabled = splitMeeting is not null && canEditSplitPoint;
        MergeMeetingsActionButton.IsEnabled = selectedMeetings.Length >= 2 && !isMeetingActionInProgress;
        SelectedMeetingTitleTextBox.IsEnabled = singleSelectedMeeting is not null && !isMeetingActionInProgress;
        SelectedMeetingProjectComboBox.IsEnabled = selectedMeetings.Length > 0 && !isMeetingActionInProgress;
        RenameSelectedMeetingButton.IsEnabled = singleSelectedMeeting is not null &&
            !isMeetingActionInProgress &&
            MainWindowInteractionLogic.HasPendingMeetingRename(singleSelectedMeeting.Title, SelectedMeetingTitleTextBox.Text);
        ApplyMeetingProjectButton.Content = _isUpdatingMeetingProject ? "Applying..." : "Apply Project";
        ClearMeetingProjectButton.Content = _isUpdatingMeetingProject ? "Clearing..." : "Clear";
        ApplyMeetingProjectButton.IsEnabled = selectedMeetings.Length > 0 &&
            !isMeetingActionInProgress &&
            !string.IsNullOrWhiteSpace(SelectedMeetingProjectComboBox.Text);
        ClearMeetingProjectButton.IsEnabled = selectedMeetings.Length > 0 &&
            !isMeetingActionInProgress &&
            selectedMeetings.Any(row => !string.IsNullOrWhiteSpace(row.Source.ProjectName));
        ApplySpeakerNamesButton.IsEnabled = singleSelectedMeeting is not null &&
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
        MeetingCleanupRecommendationsDataGrid.IsEnabled = !isMeetingActionInProgress;
        ApplySelectedMeetingCleanupRecommendationsButton.IsEnabled = selectedRecommendationRows.Length > 0 && !isMeetingActionInProgress;
        ApplyMeetingCleanupRecommendationsForSelectedMeetingsButton.IsEnabled =
            matchingSelectedMeetingRecommendations.Length > 0 && !isMeetingActionInProgress;
        DismissSelectedMeetingCleanupRecommendationsButton.IsEnabled = selectedRecommendationRows.Length > 0 && !isMeetingActionInProgress;
        OpenRelatedMeetingCleanupRecommendationsButton.IsEnabled = selectedRecommendationRows.Length > 0 && !isMeetingActionInProgress;
        ApplySafeMeetingCleanupFixesButton.IsEnabled =
            MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(_meetingCleanupRecommendations).Count > 0 &&
            !isMeetingActionInProgress;
        ReviewMeetingCleanupSuggestionsButton.IsEnabled = !isMeetingActionInProgress;
        UpdateMeetingsContextMenuState();
    }

    private bool IsMeetingActionInProgress()
    {
        return Volatile.Read(ref _meetingBaselineRefreshOperations) > 0 ||
               _isRenamingMeeting ||
               _isRetryingMeeting ||
               _isSuggestingMeetingTitle ||
               _isApplyingSuggestedMeetingTitles ||
               _isUpdatingMeetingProject ||
               _isApplyingSpeakerNames ||
               _isMergingMeetings ||
               _isSplittingMeeting ||
               _isArchivingMeetings ||
               _isUpdatingRushProcessing ||
               _isDeletingMeetings ||
               _isApplyingMeetingCleanupRecommendations ||
               _isDismissingMeetingCleanupRecommendations ||
               _isApplyingSafeMeetingCleanupFixes;
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

    private async Task<MeetingTitleSuggestion?> TryGetMeetingTitleSuggestionAsync(
        MeetingListRow row,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MeetingSessionManifest? manifest = null;
        if (!string.IsNullOrWhiteSpace(row.Source.ManifestPath) && File.Exists(row.Source.ManifestPath))
        {
            try
            {
                manifest = await _manifestStore.LoadAsync(row.Source.ManifestPath, cancellationToken);
            }
            catch
            {
                manifest = null;
            }
        }

        return _meetingTitleSuggestionService.TrySuggestTitle(
            row.Source,
            manifest,
            MeetingTitleSuggestionMode.Interactive);
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
        ConfigModelStorageSummaryTextBlock.Text = config.ModelCacheDir;
        ConfigTranscriptionStorageTextBlock.Text = config.TranscriptionModelPath;
        ConfigSpeakerLabelingStorageTextBlock.Text = config.DiarizationAssetPath;
        ConfigDiarizationGpuAccelerationCheckBox.IsChecked =
            config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto;
        ConfigAutoDetectThresholdTextBox.Text = config.AutoDetectAudioPeakThreshold.ToString("0.###", CultureInfo.InvariantCulture);
        ConfigMeetingStopTimeoutTextBox.Text = config.MeetingStopTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ConfigMicCaptureCheckBox.IsChecked = config.MicCaptureEnabled;
        ConfigLaunchOnLoginCheckBox.IsChecked = config.LaunchOnLoginEnabled;
        ConfigAutoDetectCheckBox.IsChecked = config.AutoDetectEnabled;
        ConfigCalendarTitleFallbackCheckBox.IsChecked = config.CalendarTitleFallbackEnabled;
        ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked = config.MeetingAttendeeEnrichmentEnabled;
        ConfigUpdateCheckEnabledCheckBox.IsChecked = config.UpdateCheckEnabled;
        ConfigAutoInstallUpdatesCheckBox.IsChecked = config.AutoInstallUpdatesEnabled;
        ConfigUpdateFeedUrlTextBox.Text = config.UpdateFeedUrl;
        ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue = config.PreferredTeamsIntegrationMode;
        ConfigBackgroundProcessingModeComboBox.SelectedValue = config.BackgroundProcessingMode;
        SetSpeakerLabelingModeSelectors(config.BackgroundSpeakerLabelingMode);
        UpdateDiarizationAccelerationStatusText(config, _currentDiarizationAssetStatus);
        UpdateTeamsIntegrationProbePresentation(config);
    }

    private void InitializeConfigEditorSelectionControls()
    {
        ConfigPreferredTeamsIntegrationModeComboBox.DisplayMemberPath = nameof(SelectionOption<PreferredTeamsIntegrationMode>.Label);
        ConfigPreferredTeamsIntegrationModeComboBox.SelectedValuePath = nameof(SelectionOption<PreferredTeamsIntegrationMode>.Value);
        ConfigPreferredTeamsIntegrationModeComboBox.ItemsSource = new[]
        {
            new SelectionOption<PreferredTeamsIntegrationMode>(PreferredTeamsIntegrationMode.Auto, "Auto"),
            new SelectionOption<PreferredTeamsIntegrationMode>(PreferredTeamsIntegrationMode.FallbackOnly, "Fallback only"),
            new SelectionOption<PreferredTeamsIntegrationMode>(PreferredTeamsIntegrationMode.ThirdPartyApi, "Third-party API"),
        };

        ConfigBackgroundProcessingModeComboBox.DisplayMemberPath = nameof(SelectionOption<BackgroundProcessingMode>.Label);
        ConfigBackgroundProcessingModeComboBox.SelectedValuePath = nameof(SelectionOption<BackgroundProcessingMode>.Value);
        ConfigBackgroundProcessingModeComboBox.ItemsSource = new[]
        {
            new SelectionOption<BackgroundProcessingMode>(BackgroundProcessingMode.Responsive, "Responsive"),
            new SelectionOption<BackgroundProcessingMode>(BackgroundProcessingMode.Balanced, "Balanced"),
            new SelectionOption<BackgroundProcessingMode>(BackgroundProcessingMode.FastestDrain, "Fastest drain"),
        };

        ConfigBackgroundSpeakerLabelingModeComboBox.DisplayMemberPath = nameof(SelectionOption<BackgroundSpeakerLabelingMode>.Label);
        ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValuePath = nameof(SelectionOption<BackgroundSpeakerLabelingMode>.Value);
        var speakerLabelingModeOptions = new[]
        {
            new SelectionOption<BackgroundSpeakerLabelingMode>(BackgroundSpeakerLabelingMode.Deferred, "Deferred"),
            new SelectionOption<BackgroundSpeakerLabelingMode>(BackgroundSpeakerLabelingMode.Throttled, "Throttled"),
            new SelectionOption<BackgroundSpeakerLabelingMode>(BackgroundSpeakerLabelingMode.Inline, "Inline"),
        };
        ConfigBackgroundSpeakerLabelingModeComboBox.ItemsSource = speakerLabelingModeOptions;
        SetupSpeakerLabelingRunModeComboBox.DisplayMemberPath = nameof(SelectionOption<BackgroundSpeakerLabelingMode>.Label);
        SetupSpeakerLabelingRunModeComboBox.SelectedValuePath = nameof(SelectionOption<BackgroundSpeakerLabelingMode>.Value);
        SetupSpeakerLabelingRunModeComboBox.ItemsSource = speakerLabelingModeOptions;
    }

    private void RegisterConfigEditorChangeHandlers()
    {
        foreach (var textBox in new[]
                 {
                     ConfigAudioOutputDirTextBox,
                     ConfigTranscriptOutputDirTextBox,
                     ConfigWorkDirTextBox,
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
                     ConfigDiarizationGpuAccelerationCheckBox,
                     ConfigLaunchOnLoginCheckBox,
                     ConfigAutoDetectCheckBox,
                     ConfigCalendarTitleFallbackCheckBox,
                     ConfigMeetingAttendeeEnrichmentCheckBox,
                     ConfigUpdateCheckEnabledCheckBox,
                     ConfigAutoInstallUpdatesCheckBox,
                 })
        {
            checkBox.Checked += ConfigEditorValueChanged;
            checkBox.Unchecked += ConfigEditorValueChanged;
        }

        ConfigPreferredTeamsIntegrationModeComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigBackgroundProcessingModeComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigBackgroundSpeakerLabelingModeComboBox.SelectionChanged += ConfigEditorValueChanged;
    }

    private void ConfigEditorValueChanged(object sender, RoutedEventArgs e)
    {
        UpdateConfigActionState();
    }

    private void ConfigEditorValueChanged(object sender, SelectionChangedEventArgs e)
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
        var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(
            _liveConfig.Current,
            ReadConfigEditorSnapshot());

        _settingsWindow?.SetSaveActionState(
            _isSavingConfig ? "Saving..." : "Save Changes",
            !_isSavingConfig && hasPendingChanges);
        UpdateTeamsIntegrationProbeActionState();
    }

    private void SetConfigSaveStatus(string statusText)
    {
        ConfigSaveStatusTextBlock.Text = statusText;
        _settingsWindow?.SetFooterStatus(statusText);
    }

    private ConfigEditorSnapshot ReadConfigEditorSnapshot()
    {
        return new ConfigEditorSnapshot(
            ConfigAudioOutputDirTextBox.Text,
            ConfigTranscriptOutputDirTextBox.Text,
            ConfigWorkDirTextBox.Text,
            ConfigDiarizationGpuAccelerationCheckBox.IsChecked == true,
            ConfigAutoDetectThresholdTextBox.Text,
            ConfigMeetingStopTimeoutTextBox.Text,
            ConfigMicCaptureCheckBox.IsChecked == true,
            ConfigLaunchOnLoginCheckBox.IsChecked == true,
            ConfigAutoDetectCheckBox.IsChecked == true,
            ConfigCalendarTitleFallbackCheckBox.IsChecked == true,
            ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked == true,
            ConfigUpdateCheckEnabledCheckBox.IsChecked == true,
            ConfigAutoInstallUpdatesCheckBox.IsChecked == true,
            ConfigUpdateFeedUrlTextBox.Text,
            ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue is PreferredTeamsIntegrationMode preferredTeamsIntegrationMode
                ? preferredTeamsIntegrationMode
                : _liveConfig.Current.PreferredTeamsIntegrationMode,
            ConfigBackgroundProcessingModeComboBox.SelectedValue is BackgroundProcessingMode backgroundProcessingMode
                ? backgroundProcessingMode
                : _liveConfig.Current.BackgroundProcessingMode,
            ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValue is BackgroundSpeakerLabelingMode backgroundSpeakerLabelingMode
                ? backgroundSpeakerLabelingMode
                : _liveConfig.Current.BackgroundSpeakerLabelingMode);
    }

    private void ApplyConfigEditorSnapshot(ConfigEditorSnapshot snapshot)
    {
        ConfigAudioOutputDirTextBox.Text = snapshot.AudioOutputDir;
        ConfigTranscriptOutputDirTextBox.Text = snapshot.TranscriptOutputDir;
        ConfigWorkDirTextBox.Text = snapshot.WorkDir;
        ConfigDiarizationGpuAccelerationCheckBox.IsChecked = snapshot.UseGpuAcceleration;
        ConfigAutoDetectThresholdTextBox.Text = snapshot.AutoDetectThresholdText;
        ConfigMeetingStopTimeoutTextBox.Text = snapshot.MeetingStopTimeoutText;
        ConfigMicCaptureCheckBox.IsChecked = snapshot.MicCaptureEnabled;
        ConfigLaunchOnLoginCheckBox.IsChecked = snapshot.LaunchOnLoginEnabled;
        ConfigAutoDetectCheckBox.IsChecked = snapshot.AutoDetectEnabled;
        ConfigCalendarTitleFallbackCheckBox.IsChecked = snapshot.CalendarTitleFallbackEnabled;
        ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked = snapshot.MeetingAttendeeEnrichmentEnabled;
        ConfigUpdateCheckEnabledCheckBox.IsChecked = snapshot.UpdateCheckEnabled;
        ConfigAutoInstallUpdatesCheckBox.IsChecked = snapshot.AutoInstallUpdatesEnabled;
        ConfigUpdateFeedUrlTextBox.Text = snapshot.UpdateFeedUrl;
        ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue = snapshot.PreferredTeamsIntegrationMode;
        ConfigBackgroundProcessingModeComboBox.SelectedValue = snapshot.BackgroundProcessingMode;
        SetSpeakerLabelingModeSelectors(snapshot.BackgroundSpeakerLabelingMode);
    }

    private void SetSpeakerLabelingModeSelectors(BackgroundSpeakerLabelingMode mode)
    {
        _isSynchronizingSpeakerLabelingModeSelectors = true;
        try
        {
            ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValue = mode;
            SetupSpeakerLabelingRunModeComboBox.SelectedValue = mode;
        }
        finally
        {
            _isSynchronizingSpeakerLabelingModeSelectors = false;
        }

        SetupSpeakerLabelingRunModeHelpTextBlock.Text = BuildSpeakerLabelingRunModeHelpText(mode);
    }

    private static string BuildSpeakerLabelingRunModeHelpText(BackgroundSpeakerLabelingMode mode)
    {
        return mode switch
        {
            BackgroundSpeakerLabelingMode.Deferred =>
                "Deferred publishes audio and transcripts first, so speaker labels are skipped in the main pass until you rerun speaker labeling later.",
            BackgroundSpeakerLabelingMode.Inline =>
                "Inline runs speaker labeling in the main processing pass for the fastest labeled output, but it can delay transcript publishing on longer meetings.",
            _ =>
                "Throttled is recommended. Speaker labeling still runs automatically after transcription, while leaving the queue more responsive for the rest of the app.",
        };
    }

    private async Task SaveSpeakerLabelingRunModeQuickSettingAsync(BackgroundSpeakerLabelingMode selectedMode, string source)
    {
        if (_isSavingConfig)
        {
            SetSpeakerLabelingModeSelectors(_liveConfig.Current.BackgroundSpeakerLabelingMode);
            return;
        }

        var currentConfig = _liveConfig.Current;
        if (currentConfig.BackgroundSpeakerLabelingMode == selectedMode)
        {
            SetSpeakerLabelingModeSelectors(selectedMode);
            return;
        }

        var editorSnapshot = ReadConfigEditorSnapshot();
        var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(currentConfig, editorSnapshot);
        _pendingConfigEditorSnapshotRestore = hasPendingChanges
            ? editorSnapshot with { BackgroundSpeakerLabelingMode = selectedMode }
            : null;

        try
        {
            await _liveConfig.SaveAsync(currentConfig with
            {
                BackgroundSpeakerLabelingMode = selectedMode,
            }, _lifetimeCts.Token);

            DiarizationActionStatusTextBlock.Text = selectedMode switch
            {
                BackgroundSpeakerLabelingMode.Deferred =>
                    "Speaker labeling will stay deferred. New transcripts publish first, and you can run labels later when needed.",
                BackgroundSpeakerLabelingMode.Inline =>
                    "Speaker labeling will now run inline during normal processing.",
                _ =>
                    "Speaker labeling will now run automatically in Throttled mode.",
            };
            SetConfigSaveStatus($"Speaker labeling mode updated from {source}.");
            AppendActivity($"Speaker labeling mode set to {selectedMode} from {source}.");
            UpdateDashboardReadiness();
        }
        catch (Exception exception)
        {
            _pendingConfigEditorSnapshotRestore = null;
            _shellStatusOverride = new ShellStatusState(
                "SAVE FAILED",
                "Open Settings",
                ShellStatusTarget.SettingsGeneral,
                "Settings");
            SetSpeakerLabelingModeSelectors(currentConfig.BackgroundSpeakerLabelingMode);
            SetConfigSaveStatus($"Speaker labeling mode update failed: {exception.Message}");
            DiarizationActionStatusTextBlock.Text = $"Speaker-labeling mode update failed: {exception.Message}";
            AppendActivity($"Speaker labeling mode update failed from {source}: {exception.Message}");
            UpdateDashboardReadiness();
        }
    }

    private AppConfig BuildTeamsProbeConfigFromEditor(AppConfig currentConfig)
    {
        return currentConfig with
        {
            PreferredTeamsIntegrationMode = ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue is PreferredTeamsIntegrationMode preferredTeamsIntegrationMode
                ? preferredTeamsIntegrationMode
                : currentConfig.PreferredTeamsIntegrationMode,
        };
    }

    private void UpdateTeamsIntegrationProbePresentation(AppConfig config, string? heuristicBaselineSummary = null)
    {
        var snapshot = config.TeamsCapabilitySnapshot ?? new TeamsCapabilitySnapshot();
        ConfigTeamsIntegrationStatusTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.Summary)
            ? "Fallback only."
            : snapshot.Summary;
        ConfigTeamsIntegrationDetailTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "The local Teams detector remains active until you validate a stronger integration path."
            : snapshot.Detail;
        ConfigTeamsIntegrationMetadataTextBlock.Text = BuildTeamsIntegrationMetadataText(config);
        ConfigTeamsIntegrationBaselineTextBlock.Text =
            !string.IsNullOrWhiteSpace(heuristicBaselineSummary)
                ? heuristicBaselineSummary
                : !string.IsNullOrWhiteSpace(_lastTeamsProbeBaselineSummary)
                    ? _lastTeamsProbeBaselineSummary
                    : snapshot.HeuristicBaselineReady
                        ? "Heuristic baseline was captured during the most recent Teams probe."
                        : "Heuristic baseline: run the Teams probe to capture what the local detector would do right now.";
        UpdateTeamsIntegrationProbeActionState();
    }

    private static string BuildTeamsIntegrationMetadataText(AppConfig config)
    {
        var snapshot = config.TeamsCapabilitySnapshot ?? new TeamsCapabilitySnapshot();
        var lastProbeText = snapshot.LastProbeUtc.HasValue
            ? snapshot.LastProbeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : "not run yet";
        var promotablePath = ResolvePromotableTeamsIntegrationLabel(snapshot);
        var blockReason = ResolveTeamsIntegrationBlockReason(snapshot);

        return "Last probe: " + lastProbeText + Environment.NewLine +
            "Promotable path: " + promotablePath + Environment.NewLine +
            "Block reason: " + blockReason;
    }

    private static string ResolvePromotableTeamsIntegrationLabel(TeamsCapabilitySnapshot snapshot)
    {
        if (snapshot.ThirdPartyApi.Status == TeamsThirdPartyApiStatus.ReadableStateAvailable ||
            snapshot.ThirdPartyApiReadableStateSupported)
        {
            return "Third-party API";
        }

        return "None";
    }

    private static string ResolveTeamsIntegrationBlockReason(TeamsCapabilitySnapshot snapshot)
    {
        if (snapshot.ThirdPartyApi.Status == TeamsThirdPartyApiStatus.BlockedByTeamsPolicy &&
            !string.IsNullOrWhiteSpace(snapshot.ThirdPartyApi.Detail))
        {
            return snapshot.ThirdPartyApi.Detail;
        }
        return "none.";
    }

    private void UpdateTeamsIntegrationProbeActionState()
    {
        RunTeamsIntegrationProbeButton.Content = _isRunningTeamsIntegrationProbe
            ? "Running Probe..."
            : "Run Teams Probe";
        RunTeamsIntegrationProbeButton.IsEnabled = !_isRunningTeamsIntegrationProbe;
    }

    private async Task TryReloadConfigAsync()
    {
        var reloaded = await _liveConfig.ReloadIfChangedAsync(_lifetimeCts.Token);
        if (reloaded is not null)
        {
            SetConfigSaveStatus("Config file changed on disk and was reloaded.");
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
            if (_pendingConfigEditorSnapshotRestore is { } editorSnapshot)
            {
                ApplyConfigEditorSnapshot(editorSnapshot);
                _pendingConfigEditorSnapshotRestore = null;
                UpdateDashboardReadiness();
            }
            TrySyncLaunchOnLoginSetting(e.CurrentConfig, $"config {e.Source.ToString().ToLowerInvariant()}");
            var meetingDataChanged = MainWindowInteractionLogic.ShouldRefreshMeetingCatalogForConfigChange(
                e.PreviousConfig,
                e.CurrentConfig);
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
            if (meetingDataChanged)
            {
                RequestMeetingRefreshForCurrentContext(
                    MeetingRefreshMode.Full,
                    $"config {e.Source.ToString().ToLowerInvariant()}");
            }
        });
    }

    private void ApplyConfigToUi(AppConfig config, string? statusMessage, bool refreshSetupDiagnostics = true)
    {
        ModelPathRun.Text = config.TranscriptionModelPath;
        DiarizationAssetPathRun.Text = config.DiarizationAssetPath;
        LoadConfigEditorValues(config);
        LoadMeetingsWorkspacePreferences(config);
        if (refreshSetupDiagnostics)
        {
            RefreshWhisperModelStatus();
            RefreshDiarizationAssetStatus();
        }
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
            var executablePath = UpdateInstallerLaunchBuilder.ResolveInstalledAppExecutablePath(
                Environment.ProcessPath,
                AppContext.BaseDirectory,
                "MeetingRecorder.App.exe");
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
        await RunAutomaticUpdateCycleAsync(source, AppUpdateCheckTrigger.Scheduled, cancellationToken);
    }

    private async Task RunAutomaticUpdateCycleAsync(
        string source,
        AppUpdateCheckTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (trigger != AppUpdateCheckTrigger.Shutdown && IsShutdownRequested)
        {
            return;
        }

        try
        {
            if (trigger != AppUpdateCheckTrigger.Shutdown)
            {
                await TryInstallAvailableUpdateIfIdleAsync(source, cancellationToken);
            }

            if (ShouldRunAutomaticUpdateCheck(trigger, DateTimeOffset.UtcNow))
            {
                await CheckForUpdatesAsync(source, manual: false, cancellationToken);
            }
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

    private bool ShouldRunAutomaticUpdateCheck(AppUpdateCheckTrigger trigger, DateTimeOffset nowUtc)
    {
        var config = _liveConfig.Current;
        return _appUpdateSchedulePolicy.ShouldRunAutomaticCheck(
            config.UpdateCheckEnabled,
            config.LastUpdateCheckUtc,
            nowUtc,
            ScheduledUpdateCheckCadence,
            trigger);
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
        var localUpdateState = BuildLocalUpdateState(config);
        if (!_appUpdateInstallPolicy.ShouldRetryPendingInstall(
                config.PendingUpdateZipPath,
                config.PendingUpdateVersion,
                AppBranding.Version,
                _recordingCoordinator.IsRecording,
                _processingQueue.IsProcessingInProgress,
                _isUpdateInstallInProgress))
        {
            return false;
        }

        if (MainWindowInteractionLogic.IsPendingUpdateAlreadyInstalled(config, localUpdateState))
        {
            AppendActivity($"Clearing pending update {FormatVersionLabel(config.PendingUpdateVersion)} because this app version is already installed.");
            var promotedConfig = MainWindowInteractionLogic.PromotePendingUpdateToInstalledReleaseMetadata(config, AppBranding.Version);
            await _liveConfig.SaveAsync(promotedConfig, cancellationToken);
            _lastUpdateCheckResult = new AppUpdateCheckResult(
                AppUpdateStatusKind.UpToDate,
                AppBranding.Version,
                AppBranding.Version,
                null,
                _lastUpdateCheckResult?.ReleasePageUrl,
                promotedConfig.InstalledReleasePublishedAtUtc,
                promotedConfig.InstalledReleaseAssetSizeBytes,
                false,
                false,
                false,
                $"You are already on version {FormatVersionLabel(AppBranding.Version)}.");
            ApplyUpdateCheckResult(_lastUpdateCheckResult, manual: false);
            AppendActivity($"Updated installed release metadata from the pending {FormatVersionLabel(AppBranding.Version)} package and stopped the retry loop.");
            return true;
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

        RequestApplicationShutdownForInstallerHandoff();
    }

    private void LaunchDownloadedUpdateInstaller(string downloadedPath, AppUpdateCheckResult updateResult)
    {
        var installRoot = UpdateInstallerLaunchBuilder.ResolveInstalledAppRoot(Environment.ProcessPath, AppContext.BaseDirectory);
        var deploymentCliPath = Path.Combine(installRoot, UpdateInstallerLaunchBuilder.DeploymentCliExecutableName);
        if (!File.Exists(deploymentCliPath))
        {
            throw new InvalidOperationException($"The updater helper '{deploymentCliPath}' is missing from the installed app folder.");
        }

        var startInfo = UpdateInstallerLaunchBuilder.Build(
            deploymentCliPath,
            downloadedPath,
            installRoot,
            Environment.ProcessId,
            updateResult);

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the downloaded update installer.");
    }

    private void RequestApplicationShutdownForInstallerHandoff()
    {
        _shutdownInProgress = true;
        InvalidateDetectionCycle();
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        if (!_lifetimeCts.IsCancellationRequested)
        {
            _lifetimeCts.Cancel();
        }

        CloseHeaderSurfaces();
        _allowClose = true;

        if (Application.Current is { Dispatcher.HasShutdownStarted: false } application)
        {
            application.Shutdown();
            return;
        }

        Close();
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
        var installedDiagnostics = InstalledApplicationDiagnosticsService.Inspect(
            Environment.ProcessPath,
            AppContext.BaseDirectory);

        return new AppUpdateLocalState(
            AppBranding.Version,
            string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
                ? AppBranding.Version
                : config.InstalledReleaseVersion,
            installedDiagnostics.InstalledReleasePublishedAtUtc ?? config.InstalledReleasePublishedAtUtc,
            installedDiagnostics.InstalledReleaseAssetSizeBytes ?? config.InstalledReleaseAssetSizeBytes);
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
        var installedDiagnostics = InstalledApplicationDiagnosticsService.Inspect(
            Environment.ProcessPath,
            AppContext.BaseDirectory);

        InstalledAppVersionTextBlock.Text = FormatVersionLabel(AppBranding.Version);
        InstalledReleaseVersionTextBlock.Text = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
            ? FormatVersionLabel(AppBranding.Version)
            : FormatVersionLabel(config.InstalledReleaseVersion);
        InstalledOnTextBlock.Text = FormatUpdateTimestamp(installedDiagnostics.InstalledAtUtc);
        InstalledReleasePublishedAtTextBlock.Text = FormatUpdateTimestamp(
            installedDiagnostics.InstalledReleasePublishedAtUtc ?? config.InstalledReleasePublishedAtUtc);
        InstalledReleaseAssetSizeTextBlock.Text = FormatUpdateSize(
            installedDiagnostics.InstalledReleaseAssetSizeBytes ?? config.InstalledReleaseAssetSizeBytes);
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
        var dependencyState = MainWindowInteractionLogic.BuildConfigDependencyState(
            ConfigUpdateCheckEnabledCheckBox.IsChecked == true,
            ConfigAutoDetectCheckBox.IsChecked == true,
            _liveConfig.Current.MicCaptureEnabled,
            ConfigMicCaptureCheckBox.IsChecked == true,
            _recordingCoordinator.IsRecording);

        ConfigAutoInstallUpdatesCheckBox.IsEnabled = dependencyState.AutoInstallUpdatesEnabled;
        ConfigAutoInstallDependencyTextBlock.Text = dependencyState.AutoInstallUpdatesHint;

        ConfigMicCaptureWarningTextBlock.Text = dependencyState.MicCaptureWarning;
        ConfigMicCaptureWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(dependencyState.MicCaptureWarning)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ConfigMicCapturePendingBadgeTextBlock.Text = dependencyState.MicCapturePendingBadgeText;
        ConfigMicCapturePendingBadge.Visibility = string.IsNullOrWhiteSpace(dependencyState.MicCapturePendingBadgeText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ConfigAutoDetectThresholdTextBox.IsEnabled = dependencyState.AutoDetectTuningEnabled;
        ConfigMeetingStopTimeoutTextBox.IsEnabled = dependencyState.AutoDetectTuningEnabled;
        ConfigAutoDetectSettingsHintTextBlock.Text = dependencyState.AutoDetectSettingsHint;
    }

    private void UpdateDashboardReadiness()
    {
        var hasValidModel = HasReadyTranscriptionModel();
        var editorSnapshot = ReadConfigEditorSnapshot();
        var pendingMicCaptureEnabled = editorSnapshot.MicCaptureEnabled;
        var hasPendingMicCaptureChange = _liveConfig.Current.MicCaptureEnabled != pendingMicCaptureEnabled;
        var pendingAutoDetectEnabled = editorSnapshot.AutoDetectEnabled;
        var hasPendingAutoDetectChange = _liveConfig.Current.AutoDetectEnabled != pendingAutoDetectEnabled;
        var diarizationReady = _currentDiarizationAssetStatus?.IsReady == true;

        DashboardModelReadinessTextBlock.Text = hasValidModel
            ? "Ready. A valid Whisper model is active for transcript generation."
            : "Action needed. Download or import a valid Whisper model before relying on transcript output.";
        DashboardDiarizationReadinessTextBlock.Text = diarizationReady
            ? "Ready. The optional diarization bundle is installed when you need transcripts grouped by speaker."
            : "Optional. Transcripts still work normally without speaker labeling.";
        var liveMicCaptureEnabled = _recordingCoordinator.ActiveSession?.MicrophoneRecorder is not null;
        DashboardMicCaptureReadinessTextBlock.Text = BuildMicCaptureReadinessText(
            _liveConfig.Current.MicCaptureEnabled,
            _recordingCoordinator.IsRecording,
            liveMicCaptureEnabled,
            hasPendingMicCaptureChange,
            pendingMicCaptureEnabled);
        DashboardAutoDetectReadinessTextBlock.Text = BuildAutoDetectReadinessText(
            _liveConfig.Current.AutoDetectEnabled,
            hasValidModel,
            hasPendingAutoDetectChange,
            pendingAutoDetectEnabled);
        var micCaptureWarning = MainWindowInteractionLogic.BuildMicCaptureWarning(
            _liveConfig.Current.MicCaptureEnabled,
            _recordingCoordinator.IsRecording,
            hasPendingMicCaptureChange,
            pendingMicCaptureEnabled);
        DashboardMicCaptureWarningTextBlock.Text = micCaptureWarning;
        DashboardMicCaptureWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(micCaptureWarning)
            ? Visibility.Collapsed
            : Visibility.Visible;

        HomeMicCaptureQuickSettingSummaryTextBlock.Text = BuildHomeMicCaptureQuickSettingSummary(
            _liveConfig.Current.MicCaptureEnabled,
            _recordingCoordinator.IsRecording,
            liveMicCaptureEnabled);
        HomeAutoDetectQuickSettingSummaryTextBlock.Text = BuildHomeAutoDetectQuickSettingSummary(
            _liveConfig.Current.AutoDetectEnabled,
            hasValidModel);
        UpdateHomeQuickSettingButtons();

        var shellStatus = _shellStatusOverride ?? MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel,
            _recordingCoordinator.IsRecording,
            _liveConfig.Current.MicCaptureEnabled,
            _liveConfig.Current.AutoDetectEnabled,
            _liveConfig.Current.UpdateCheckEnabled,
            _liveConfig.Current.AutoInstallUpdatesEnabled,
            _lastUpdateCheckResult);

        HeaderShellStatusLabelTextBlock.Text = shellStatus.Headline;
        HeaderShellStatusDetailTextBlock.Text = shellStatus.Body;
        HeaderShellStatusActionButton.Tag = shellStatus.Target;
        HeaderShellStatusActionButton.Content = shellStatus.ActionLabel ?? string.Empty;
        HeaderShellStatusActionButton.Visibility = string.IsNullOrWhiteSpace(shellStatus.ActionLabel)
            ? Visibility.Hidden
            : Visibility.Visible;

        DashboardPrimaryActionHeadlineTextBlock.Text = shellStatus.Headline;
        DashboardPrimaryActionBodyTextBlock.Text = shellStatus.Body;
        DashboardPrimaryActionButton.Tag = shellStatus.Target;
        DashboardPrimaryActionButton.Content = shellStatus.ActionLabel ?? string.Empty;
        DashboardPrimaryActionButton.Visibility = HeaderShellStatusActionButton.Visibility;

        ApplyShellStatusChrome(shellStatus);
    }

    private static string BuildMicCaptureReadinessText(
        bool savedMicCaptureEnabled,
        bool isRecording,
        bool liveMicCaptureEnabled,
        bool hasPendingMicCaptureChange,
        bool pendingMicCaptureEnabled)
    {
        if (hasPendingMicCaptureChange)
        {
            if (isRecording)
            {
                return pendingMicCaptureEnabled
                    ? "Pending save. Microphone capture will turn on from now on after you save Settings."
                    : "Pending save. Microphone capture will turn off from now on after you save Settings.";
            }

            return pendingMicCaptureEnabled
                ? "Pending save. Microphone capture will turn on after you save Settings."
                : "Pending save. Microphone capture will turn off after you save Settings.";
        }

        if (isRecording)
        {
            return liveMicCaptureEnabled
                ? "On now and for future recordings. Your microphone is captured alongside meeting audio."
                : "Off from now on. Turn it on when you want your own voice included in this recording.";
        }

        return savedMicCaptureEnabled
            ? "On for future recordings. Your microphone is captured alongside meeting audio."
            : "Off. Turn it on when you want your own voice included in future recordings.";
    }

    private static string BuildHomeMicCaptureQuickSettingSummary(
        bool savedMicCaptureEnabled,
        bool isRecording,
        bool liveMicCaptureEnabled)
    {
        if (isRecording)
        {
            return liveMicCaptureEnabled
                ? "On now and for future recordings."
                : "Off from now on. Your voice is excluded.";
        }

        return savedMicCaptureEnabled
            ? "On for future recordings."
            : "Off. Your voice is excluded.";
    }

    private static string BuildAutoDetectReadinessText(
        bool savedAutoDetectEnabled,
        bool transcriptionReady,
        bool hasPendingAutoDetectChange,
        bool pendingAutoDetectEnabled)
    {
        if (!transcriptionReady)
        {
            return "Blocked until transcription is ready. Finish Setup before automatic meeting detection can start.";
        }

        if (hasPendingAutoDetectChange)
        {
            return pendingAutoDetectEnabled
                ? "Pending save. Auto-detection will turn on after you save Settings."
                : "Pending save. Auto-detection will turn off after you save Settings.";
        }

        return savedAutoDetectEnabled
            ? "On. The app watches supported meetings automatically."
            : "Off. Use manual start and stop unless you turn auto-detection back on.";
    }

    private static string BuildHomeAutoDetectQuickSettingSummary(bool autoDetectEnabled, bool transcriptionReady)
    {
        if (!transcriptionReady)
        {
            return "Finish Setup before auto-detect can turn on.";
        }

        return autoDetectEnabled
            ? "On. Supported meetings are watched."
            : "Off. Start and stop manually.";
    }

    private void UpdateHomeQuickSettingButtons()
    {
        SetQuickSettingButtonState(HomeMicCaptureEnabledButton, _liveConfig.Current.MicCaptureEnabled);
        SetQuickSettingButtonState(HomeMicCaptureDisabledButton, !_liveConfig.Current.MicCaptureEnabled);
        SetQuickSettingButtonState(HomeAutoDetectEnabledButton, _liveConfig.Current.AutoDetectEnabled);
        SetQuickSettingButtonState(HomeAutoDetectDisabledButton, !_liveConfig.Current.AutoDetectEnabled);
        var autoDetectControlsEnabled = HasReadyTranscriptionModel() && !_isUpdateInstallInProgress;
        HomeAutoDetectEnabledButton.IsEnabled = autoDetectControlsEnabled;
        HomeAutoDetectDisabledButton.IsEnabled = autoDetectControlsEnabled;
    }

    private static void SetQuickSettingButtonState(Button button, bool isActive)
    {
        button.Tag = isActive ? "Active" : null;
    }

    private void ApplyShellStatusChrome(ShellStatusState shellStatus)
    {
        if (_shellStatusOverride is not null)
        {
            SetShellStatusBrushes("AppDangerBackgroundBrush", "AppDangerOutlineBrush", "AppDangerBrush", "AppTextBrush");
            return;
        }

        switch (shellStatus.Target)
        {
            case ShellStatusTarget.SettingsSetup:
            case ShellStatusTarget.SettingsGeneral:
                SetShellStatusBrushes("AppWarningBackgroundBrush", "AppWarningOutlineBrush", "AppWarningBrush", "AppTextBrush");
                break;
            case ShellStatusTarget.SettingsUpdates:
                SetShellStatusBrushes("AppMutedCardBrush", "AppSecondaryBrush", "AppSecondaryBrush", "AppTextBrush");
                break;
            case ShellStatusTarget.None when _recordingCoordinator.IsRecording:
                SetShellStatusBrushes("AppMutedCardBrush", "AppSignalBrush", "AppSignalBrush", "AppTextBrush");
                break;
            default:
                SetShellStatusBrushes("AppCardBrush", "AppOutlineBrush", "AppSignalBrush", "AppMutedTextBrush");
                break;
        }
    }

    private void SetShellStatusBrushes(
        string backgroundResourceKey,
        string borderResourceKey,
        string labelResourceKey,
        string detailResourceKey)
    {
        HeaderShellStatusBorder.Background = (Brush)FindResource(backgroundResourceKey);
        HeaderShellStatusBorder.BorderBrush = (Brush)FindResource(borderResourceKey);
        HeaderShellStatusDot.Fill = (Brush)FindResource(labelResourceKey);
        HeaderShellStatusLabelTextBlock.Foreground = (Brush)FindResource(labelResourceKey);
        HeaderShellStatusDetailTextBlock.Foreground = (Brush)FindResource(detailResourceKey);
    }

    private async Task SaveHomeQuickSettingAsync(
        bool enabled,
        Func<AppConfig, AppConfig> configUpdater,
        Func<ConfigEditorSnapshot, ConfigEditorSnapshot> snapshotUpdater,
        Func<AppConfig, bool> currentValueSelector,
        string settingName,
        bool applyMicCaptureLiveChange = false)
    {
        if (_isSavingConfig)
        {
            return;
        }

        var currentConfig = _liveConfig.Current;
        if (currentValueSelector(currentConfig) == enabled)
        {
            var liveMicCaptureUpdated = true;
            if (applyMicCaptureLiveChange)
            {
                liveMicCaptureUpdated = await ApplyLiveMicCapturePreferenceIfNeededAsync(
                    enabled,
                    $"{settingName} Home quick setting",
                    _lifetimeCts.Token);
            }

            if (liveMicCaptureUpdated)
            {
                _shellStatusOverride = null;
            }

            UpdateDashboardReadiness();
            return;
        }

        var editorSnapshot = ReadConfigEditorSnapshot();
        var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(currentConfig, editorSnapshot);
        _pendingConfigEditorSnapshotRestore = hasPendingChanges
            ? snapshotUpdater(editorSnapshot)
            : null;

        try
        {
            await _liveConfig.SaveAsync(configUpdater(currentConfig), _lifetimeCts.Token);
            var liveMicCaptureUpdated = true;
            if (applyMicCaptureLiveChange)
            {
                liveMicCaptureUpdated = await ApplyLiveMicCapturePreferenceIfNeededAsync(
                    enabled,
                    $"{settingName} Home quick setting",
                    _lifetimeCts.Token);
            }

            _shellStatusOverride = null;
            if (liveMicCaptureUpdated)
            {
                SetConfigSaveStatus($"{settingName} updated from Home.");
            }

            AppendActivity($"{settingName} {(enabled ? "enabled" : "disabled")} from Home.");
            UpdateDashboardReadiness();
        }
        catch (Exception exception)
        {
            _pendingConfigEditorSnapshotRestore = null;
            _shellStatusOverride = new ShellStatusState(
                "SAVE FAILED",
                "Open Settings",
                ShellStatusTarget.SettingsGeneral,
                "Settings");
            SetConfigSaveStatus($"Quick setting failed: {exception.Message}");
            AppendActivity($"{settingName} quick setting failed: {exception.Message}");
            UpdateDashboardReadiness();
        }
    }

    private async Task<bool> ApplyLiveMicCapturePreferenceIfNeededAsync(
        bool enabled,
        string source,
        CancellationToken cancellationToken)
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return true;
        }

        var liveMicCaptureEnabled = activeSession.MicrophoneRecorder is not null;
        if (liveMicCaptureEnabled == enabled)
        {
            return true;
        }

        try
        {
            var changed = await _recordingCoordinator.SetMicrophoneCaptureEnabledAsync(enabled, cancellationToken);
            if (!changed)
            {
                return true;
            }

            UpdateCurrentMeetingEditor();
            UpdateAudioCaptureGraph();
            UpdateDashboardReadiness();
            AppendActivity($"Microphone capture {(enabled ? "enabled" : "disabled")} for the current recording from now on via {source}.");
            return true;
        }
        catch (Exception exception)
        {
            _shellStatusOverride = new ShellStatusState(
                "MIC LIVE",
                "Saved for next recording",
                ShellStatusTarget.SettingsGeneral,
                "Settings");
            SetConfigSaveStatus($"Microphone capture was saved, but the current recording could not be updated: {exception.Message}");
            AppendActivity($"Live microphone capture update failed from {source}: {exception.Message}");
            UpdateDashboardReadiness();
            return false;
        }
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

    private async Task TryPromptToEnableMicCaptureAsync(ActiveRecordingSession activeSession, CancellationToken cancellationToken)
    {
        if (IsShutdownRequested || _isMicCaptureEnablePromptInProgress)
        {
            return;
        }

        var microphoneSnapshot = _microphoneActivityProbe.Capture(Math.Clamp(_liveConfig.Current.AutoDetectAudioPeakThreshold, 0.01d, 1d));
        var alreadyPromptedForSession = string.Equals(
            _micCaptureEnablePromptedSessionId,
            activeSession.Manifest.SessionId,
            StringComparison.Ordinal);
        if (!MainWindowInteractionLogic.ShouldPromptToEnableMicCapture(
                _liveConfig.Current.MicCaptureEnabled,
                _recordingCoordinator.IsRecording,
                microphoneSnapshot.IsActive,
                alreadyPromptedForSession))
        {
            return;
        }

        _micCaptureEnablePromptedSessionId = activeSession.Manifest.SessionId;
        _isMicCaptureEnablePromptInProgress = true;

        try
        {
            var promptMessage = MainWindowInteractionLogic.BuildEnableMicCapturePromptMessage(activeSession.Manifest.DetectedTitle);
            var promptResult = MessageBox.Show(
                this,
                promptMessage,
                "Turn On Microphone Capture?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (promptResult != MessageBoxResult.Yes)
            {
                AppendActivity(
                    $"Detected live microphone activity while microphone capture was off for '{activeSession.Manifest.DetectedTitle}'. User chose to keep the setting off.");
                return;
            }

            await _liveConfig.SaveAsync(_liveConfig.Current with
            {
                MicCaptureEnabled = true,
            }, cancellationToken);
            var liveMicCaptureUpdated = await ApplyLiveMicCapturePreferenceIfNeededAsync(
                enabled: true,
                source: "Microphone activity prompt",
                cancellationToken);

            AppendActivity(
                liveMicCaptureUpdated
                    ? $"Detected live microphone activity while microphone capture was off for '{activeSession.Manifest.DetectedTitle}'. Enabled microphone capture from now on for this recording and future recordings."
                    : $"Detected live microphone activity while microphone capture was off for '{activeSession.Manifest.DetectedTitle}'. Enabled microphone capture for future recordings, but the current recording could not be updated.");
        }
        finally
        {
            _isMicCaptureEnablePromptInProgress = false;
        }
    }

    private async Task<IReadOnlyList<MeetingInspectionRecord>> BuildMeetingInspectionsAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var manifestsByStem = LoadMeetingManifestsByStem(records, cancellationToken);
            var inspections = new List<MeetingInspectionRecord>(records.Count);
            var isDiarizationReady = _currentDiarizationAssetStatus?.IsReady
                ?? _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady;

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                manifestsByStem.TryGetValue(record.Stem, out var manifest);
                string? suggestedTitle = null;
                string? suggestedTitleSource = null;
                if (ShouldTryMeetingTitleSuggestion(record))
                {
                    var suggestion = _meetingTitleSuggestionService.TrySuggestTitle(
                        record,
                        manifest,
                        MeetingTitleSuggestionMode.Passive);
                    suggestedTitle = suggestion?.Title;
                    suggestedTitleSource = suggestion?.Source;
                }

                inspections.Add(new MeetingInspectionRecord(record, manifest, suggestedTitle, suggestedTitleSource, isDiarizationReady));
            }

            return (IReadOnlyList<MeetingInspectionRecord>)inspections;
        }, cancellationToken);
    }

    private Dictionary<string, MeetingSessionManifest> LoadMeetingManifestsByStem(
        IReadOnlyList<MeetingOutputRecord> records,
        CancellationToken cancellationToken)
    {
        var manifestsByStem = new Dictionary<string, MeetingSessionManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(record.ManifestPath) || !File.Exists(record.ManifestPath))
            {
                continue;
            }

            try
            {
                var manifest = _manifestStore.LoadAsync(record.ManifestPath, cancellationToken).GetAwaiter().GetResult();
                manifestsByStem[record.Stem] = manifest;
            }
            catch
            {
                // Ignore malformed or partially-written manifests while building recommendations.
            }
        }

        return manifestsByStem;
    }

    private async Task<IReadOnlyList<MeetingCleanupRecommendation>> BuildVisibleMeetingCleanupRecommendationsAsync(
        IReadOnlyList<MeetingInspectionRecord> inspections,
        CancellationToken cancellationToken)
    {
        var allRecommendations = await Task.Run(
            () => MeetingCleanupRecommendationEngine.Analyze(inspections),
            cancellationToken);
        await PruneDismissedMeetingCleanupRecommendationsAsync(allRecommendations, cancellationToken);
        var dismissedFingerprints = new HashSet<string>(
            _liveConfig.Current.DismissedMeetingRecommendations.Select(item => item.Fingerprint),
            StringComparer.Ordinal);
        return allRecommendations
            .Where(recommendation => !dismissedFingerprints.Contains(recommendation.Fingerprint))
            .ToArray();
    }

    private async Task PruneDismissedMeetingCleanupRecommendationsAsync(
        IReadOnlyList<MeetingCleanupRecommendation> activeRecommendations,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (currentConfig.DismissedMeetingRecommendations.Count == 0)
        {
            return;
        }

        var activeFingerprints = new HashSet<string>(
            activeRecommendations.Select(recommendation => recommendation.Fingerprint),
            StringComparer.Ordinal);
        var prunedDismissals = currentConfig.DismissedMeetingRecommendations
            .Where(item => activeFingerprints.Contains(item.Fingerprint))
            .ToArray();
        if (prunedDismissals.Length == currentConfig.DismissedMeetingRecommendations.Count)
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            DismissedMeetingRecommendations = prunedDismissals,
        }, cancellationToken);
    }

    private static bool ShouldTryMeetingTitleSuggestion(MeetingOutputRecord record)
    {
        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(record.Title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return false;
        }

        return record.Platform switch
        {
            MeetingPlatform.Teams => normalizedTitle is "microsoft teams" or "teams" or "search" or "chat" or "calls",
            MeetingPlatform.GoogleMeet => normalizedTitle is "google meet" or "meet",
            MeetingPlatform.Manual => normalizedTitle.StartsWith("manual session ", StringComparison.Ordinal),
            _ => normalizedTitle is "meeting" or "detected meeting",
        };
    }

    private void UpdateMeetingCleanupRecommendationsEditor(MeetingListRow[]? currentRows = null)
    {
        var rows = currentRows ??
            GetVisibleMeetingRows();
        var visibleStemSet = rows
            .Select(row => row.Source.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopedRecommendations = visibleStemSet.Count == 0
            ? Array.Empty<MeetingCleanupRecommendation>()
            : _meetingCleanupRecommendations
                .Where(recommendation => recommendation.RelatedStems.Any(visibleStemSet.Contains))
                .ToArray();
        var rowsByStem = rows.ToDictionary(row => row.Source.Stem, StringComparer.OrdinalIgnoreCase);
        var visibleRecommendations = MainWindowInteractionLogic.FilterMeetingCleanupRecommendations(
            scopedRecommendations,
            GetSelectedMeetingRows().Select(row => row.Source.Stem).ToArray());
        var recommendationRows = visibleRecommendations
            .Select(recommendation => new MeetingCleanupRecommendationRow(recommendation, rowsByStem))
            .ToArray();

        MeetingCleanupRecommendationsDataGrid.ItemsSource = recommendationRows;
        var hasMeetingsFilter = !string.IsNullOrWhiteSpace(MeetingsSearchTextBox.Text);

        MeetingCleanupRecommendationsStatusTextBlock.Text = recommendationRows.Length == 0
            ? (_meetingCleanupRecommendations.Length == 0
                ? "No cleanup suggestions are active right now."
                : hasMeetingsFilter
                    ? "No cleanup suggestions match the current Meetings search filter."
                    : "No cleanup suggestions match the current meeting selection.")
            : GetSelectedMeetingRows().Length == 0
                ? hasMeetingsFilter
                    ? $"Showing {recommendationRows.Length} cleanup suggestion(s) within the current Meetings search filter."
                    : $"Showing {recommendationRows.Length} cleanup suggestion(s) across the library."
                : hasMeetingsFilter
                    ? $"Showing {recommendationRows.Length} cleanup suggestion(s) within the current Meetings search filter, with the current meeting selection prioritized first."
                    : $"Showing {recommendationRows.Length} cleanup suggestion(s) across the library, with the current meeting selection prioritized first.";

        UpdateMeetingCleanupReviewBanner();
        UpdateMeetingActionState();
    }

    private bool HasPendingMeetingProjectDraft(IReadOnlyList<MeetingListRow> selectedMeetings)
    {
        if (selectedMeetings.Count == 0)
        {
            return false;
        }

        var distinctProjects = selectedMeetings
            .Select(row => row.Source.ProjectName?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currentProjectText = distinctProjects.Length == 1
            ? distinctProjects[0]
            : string.Empty;

        return !string.Equals(
            currentProjectText,
            SelectedMeetingProjectComboBox.Text?.Trim() ?? string.Empty,
            StringComparison.Ordinal);
    }

    private void UpdateMeetingCleanupReviewBanner()
    {
        if (HasCompletedMeetingCleanupHistoricalReview() || _meetingCleanupRecommendations.Length == 0)
        {
            MeetingCleanupReviewBannerBorder.Visibility = Visibility.Collapsed;
            MeetingCleanupReviewBannerTextBlock.Text = string.Empty;
            return;
        }

        var safeRecommendationCount = MainWindowInteractionLogic
            .GetAutoApplicableMeetingCleanupRecommendations(_meetingCleanupRecommendations)
            .Count;
        var impactedMeetingCount = _meetingCleanupRecommendations
            .SelectMany(recommendation => recommendation.RelatedStems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        MeetingCleanupReviewBannerBorder.Visibility = Visibility.Visible;
        MeetingCleanupReviewBannerTextBlock.Text =
            $"Found {_meetingCleanupRecommendations.Length} cleanup suggestion(s) across {impactedMeetingCount} meeting(s). " +
            $"You can review them below or apply {safeRecommendationCount} safe fix(es) now. Rows marked Safe Fix are included in that bulk action.";
    }

    private bool HasCompletedMeetingCleanupHistoricalReview()
    {
        return File.Exists(GetMeetingCleanupHistoricalReviewMarkerPath());
    }

    private void MarkMeetingCleanupHistoricalReviewCompleted()
    {
        var markerPath = GetMeetingCleanupHistoricalReviewMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private string GetMeetingCleanupHistoricalReviewMarkerPath()
    {
        var configDirectory = Path.GetDirectoryName(_liveConfig.ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var appRoot = Directory.GetParent(configDirectory)?.FullName
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");
        return Path.Combine(appRoot, "reviews", MeetingCleanupHistoricalReviewMarkerFileName);
    }

    private MeetingCleanupRecommendationRow[] GetSelectedMeetingCleanupRecommendationRows()
    {
        return MeetingCleanupRecommendationsDataGrid.SelectedItems
            .OfType<MeetingCleanupRecommendationRow>()
            .ToArray();
    }

    private async Task ExecuteMeetingCleanupRecommendationsAsync(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        string archiveLabel,
        CancellationToken cancellationToken)
    {
        if (recommendations.Count == 0)
        {
            return;
        }

        var archiveRoot = MeetingCleanupExecutionService.GetArchiveRoot(_liveConfig.Current.AudioOutputDir);
        var archiveDirectory = MeetingCleanupExecutionService.CreateExecutionArchiveDirectory(archiveRoot, archiveLabel);

        foreach (var recommendation in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var meetingsByStem = _meetingOutputCatalogService.ListMeetings(
                    _liveConfig.Current.AudioOutputDir,
                    _liveConfig.Current.TranscriptOutputDir,
                    _liveConfig.Current.WorkDir)
                .ToDictionary(record => record.Stem, StringComparer.OrdinalIgnoreCase);

            switch (recommendation.Action)
            {
                case MeetingCleanupAction.Archive:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var archiveMeeting))
                    {
                        continue;
                    }

                    await _meetingCleanupExecutionService.ArchiveMeetingAsync(
                        archiveMeeting,
                        archiveDirectory,
                        ResolveMeetingCleanupArchiveCategory(recommendation),
                        cancellationToken);
                    break;

                case MeetingCleanupAction.Merge:
                    if (recommendation.RelatedStems.Count < 2)
                    {
                        continue;
                    }

                    if (!meetingsByStem.TryGetValue(recommendation.RelatedStems[0], out var firstMeeting) ||
                        !meetingsByStem.TryGetValue(recommendation.RelatedStems[1], out var secondMeeting))
                    {
                        continue;
                    }

                    await _meetingCleanupExecutionService.MergeMeetingsAsync(
                        firstMeeting,
                        secondMeeting,
                        recommendation.SuggestedTitle ?? firstMeeting.Title,
                        _liveConfig.Current.AudioOutputDir,
                        _liveConfig.Current.TranscriptOutputDir,
                        archiveDirectory,
                        cancellationToken);
                    break;

                case MeetingCleanupAction.Rename:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var renameMeeting) ||
                        string.IsNullOrWhiteSpace(recommendation.SuggestedTitle))
                    {
                        continue;
                    }

                    await _meetingCleanupExecutionService.RenameMeetingAsync(
                        _liveConfig.Current.AudioOutputDir,
                        _liveConfig.Current.TranscriptOutputDir,
                        _liveConfig.Current.WorkDir,
                        renameMeeting,
                        recommendation.SuggestedTitle,
                        cancellationToken);
                    break;

                case MeetingCleanupAction.RegenerateTranscript:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var regenerateMeeting))
                    {
                        continue;
                    }

                    await QueueTranscriptRegenerationAsync(regenerateMeeting, cancellationToken);
                    break;

                case MeetingCleanupAction.GenerateSpeakerLabels:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var speakerLabelMeeting))
                    {
                        continue;
                    }

                    await QueueSpeakerLabelGenerationAsync(speakerLabelMeeting, cancellationToken);
                    break;

                case MeetingCleanupAction.Split:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var splitMeeting) ||
                        recommendation.SuggestedSplitPoint is not { } suggestedSplitPoint)
                    {
                        continue;
                    }

                    await QueueSplitMeetingAsync(splitMeeting, suggestedSplitPoint, cancellationToken);
                    break;
            }
        }
    }

    private static string ResolveMeetingCleanupArchiveCategory(MeetingCleanupRecommendation recommendation)
    {
        return MeetingCleanupExecutionService.GetArchiveCategory(recommendation);
    }

    private void SelectMeetingsByStem(IReadOnlyList<string> stems)
    {
        var targetStems = new HashSet<string>(stems, StringComparer.OrdinalIgnoreCase);
        MeetingsDataGrid.SelectedItems.Clear();
        if (MeetingsDataGrid.ItemsSource is not IEnumerable<MeetingListRow> rows)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (!targetStems.Contains(row.Source.Stem))
            {
                continue;
            }

            MeetingsDataGrid.SelectedItems.Add(row);
        }
    }

    private async Task TryPromoteActiveMeetingTitleAsync(
        ActiveRecordingSession activeSession,
        DetectionDecision? decision,
        CancellationToken cancellationToken)
    {
        if (IsShutdownRequested || decision is null)
        {
            return;
        }

        if (activeSession.Manifest.Platform != decision.Platform)
        {
            return;
        }

        var proposedTitle = decision.SessionTitle.Trim();
        var proposalCameFromCalendarFallback = decision.Signals.Any(signal =>
            string.Equals(signal.Source, "calendar-title-fallback", StringComparison.OrdinalIgnoreCase));
        if (!MainWindowInteractionLogic.ShouldAutoPromoteActiveMeetingTitle(
                activeSession.Manifest.Platform,
                activeSession.Manifest.DetectedTitle,
                CurrentMeetingTitleTextBox.Text,
                proposedTitle,
                proposalCameFromCalendarFallback))
        {
            return;
        }

        var renamed = await _recordingCoordinator.RenameActiveSessionAsync(proposedTitle, cancellationToken);
        if (!renamed)
        {
            return;
        }

        _sessionTitleDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, proposedTitle);
        UpdateCurrentMeetingEditor();
        AppendActivity(
            proposalCameFromCalendarFallback
                ? $"Updated active meeting title to '{proposedTitle}' from the Outlook calendar fallback."
                : $"Updated active meeting title to '{proposedTitle}' from the detected attendee context.");
    }

    private async Task TryCaptureTeamsAttendeesAsync(
        ActiveRecordingSession activeSession,
        CancellationToken cancellationToken)
    {
        if (activeSession.Manifest.Platform != MeetingPlatform.Teams ||
            !_liveConfig.Current.MeetingAttendeeEnrichmentEnabled)
        {
            return;
        }

        if (!await _teamsAttendeeCaptureGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var sessionId = activeSession.Manifest.SessionId;
            var attendees = await _teamsLiveAttendeeCaptureService.TryCaptureAttendeesAsync(cancellationToken);
            if (attendees.Count == 0)
            {
                return;
            }

            var updated = await _recordingCoordinator.MergeActiveSessionAttendeesAsync(sessionId, attendees, cancellationToken);
            if (updated)
            {
                UpdateCurrentMeetingEditor();
            }
        }
        catch
        {
            // Best-effort live attendee capture must never affect recording stability.
        }
        finally
        {
            _teamsAttendeeCaptureGate.Release();
        }
    }

    private async Task ApplyPendingCurrentMetadataAsync(CancellationToken cancellationToken)
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        await TryApplyDeferredMeetingReclassificationAsync(activeSession, cancellationToken);
        activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        var pendingTitle = CurrentMeetingTitleTextBox.Text.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(pendingTitle)
            ? activeSession.Manifest.DetectedTitle
            : pendingTitle;
        var normalizedProjectName = NormalizeOptionalMeetingMetadataText(CurrentMeetingProjectTextBox.Text);
        var pendingKeyAttendees = ParseDelimitedKeyAttendeesText(CurrentMeetingKeyAttendeesTextBox.Text);
        if (string.Equals(normalizedTitle, activeSession.Manifest.DetectedTitle, StringComparison.Ordinal) &&
            string.Equals(normalizedProjectName, activeSession.Manifest.ProjectName, StringComparison.Ordinal) &&
            (activeSession.Manifest.KeyAttendees ?? Array.Empty<string>()).SequenceEqual(pendingKeyAttendees, StringComparer.Ordinal))
        {
            return;
        }

        var updated = await _recordingCoordinator.UpdateActiveSessionMetadataAsync(
            normalizedTitle,
            normalizedProjectName,
            pendingKeyAttendees,
            cancellationToken);
        if (!updated)
        {
            return;
        }

        _sessionTitleDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, normalizedTitle);
        _sessionProjectDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, normalizedProjectName ?? string.Empty);
        _sessionKeyAttendeesDraftTracker.MarkPersisted(
            activeSession.Manifest.SessionId,
            FormatKeyAttendeesForDisplay(pendingKeyAttendees));

        UpdateCurrentMeetingEditor();
        AppendActivity($"Applied current meeting metadata before publishing for '{normalizedTitle}'.");
    }

    private void ScheduleCurrentMeetingOptionalMetadataSave()
    {
        if (!_isUiReady ||
            _isUpdatingCurrentMeetingEditor ||
            _isRecordingTransitionInProgress ||
            _isUpdateInstallInProgress ||
            _recordingCoordinator.ActiveSession is null)
        {
            return;
        }

        _currentMeetingOptionalMetadataSaveTimer.Stop();
        _currentMeetingOptionalMetadataSaveTimer.Start();
    }

    private async void CurrentMeetingOptionalMetadataSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _currentMeetingOptionalMetadataSaveTimer.Stop();
        if (IsShutdownRequested)
        {
            return;
        }

        try
        {
            await PersistCurrentMeetingOptionalMetadataAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during shutdown or rapid session transitions.
        }
    }

    private async Task PersistCurrentMeetingOptionalMetadataAsync(CancellationToken cancellationToken)
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        await TryApplyDeferredMeetingReclassificationAsync(activeSession, cancellationToken);
        activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        var normalizedProjectName = NormalizeOptionalMeetingMetadataText(CurrentMeetingProjectTextBox.Text);
        var pendingKeyAttendees = ParseDelimitedKeyAttendeesText(CurrentMeetingKeyAttendeesTextBox.Text);
        if (string.Equals(normalizedProjectName, activeSession.Manifest.ProjectName, StringComparison.Ordinal) &&
            (activeSession.Manifest.KeyAttendees ?? Array.Empty<string>()).SequenceEqual(pendingKeyAttendees, StringComparer.Ordinal))
        {
            return;
        }

        try
        {
            var updated = await _recordingCoordinator.UpdateActiveSessionMetadataAsync(
                activeSession.Manifest.DetectedTitle,
                normalizedProjectName,
                pendingKeyAttendees,
                cancellationToken);
            if (!updated)
            {
                return;
            }

            _sessionProjectDraftTracker.MarkPersisted(activeSession.Manifest.SessionId, normalizedProjectName ?? string.Empty);
            _sessionKeyAttendeesDraftTracker.MarkPersisted(
                activeSession.Manifest.SessionId,
                FormatKeyAttendeesForDisplay(pendingKeyAttendees));
            UpdateCurrentMeetingEditor();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendActivity($"Live current meeting metadata save failed: {exception.Message}");
        }
    }

    private void UpdateCurrentMeetingTitleStatus()
    {
        if (_isAutoStopTransitionInProgress)
        {
            CurrentMeetingTitleStatusTextBlock.Text = "Auto-stopping current session and finalizing artifacts.";
            return;
        }

        if (_autoStopCountdownSecondsRemaining is { } countdownSeconds)
        {
            CurrentMeetingTitleStatusTextBlock.Text = $"Auto-stop in {countdownSeconds}s unless the meeting resumes.";
            return;
        }

        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            CurrentMeetingTitleStatusTextBlock.Text = "Start recording to apply a custom session title.";
            return;
        }

        var pendingTitle = CurrentMeetingTitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pendingTitle))
        {
            CurrentMeetingTitleStatusTextBlock.Text = "Leave blank to keep the detected meeting title.";
            return;
        }

        var deferredReclassification = MainWindowInteractionLogic.GetEligibleActiveSessionReclassification(
            _lastObservedDetectionDecision,
            activeSession.Manifest.Platform,
            activeSession.Manifest.DetectedTitle,
            _autoRecordingContinuityPolicy);
        var stemPlatform = deferredReclassification?.Platform ?? activeSession.Manifest.Platform;
        var stem = _pathBuilder.BuildFileStem(stemPlatform, activeSession.Manifest.StartedAtUtc, pendingTitle);
        if (string.Equals(pendingTitle, activeSession.Manifest.DetectedTitle, StringComparison.Ordinal))
        {
            CurrentMeetingTitleStatusTextBlock.Text = $"Publish stem: {stem}";
            return;
        }

        CurrentMeetingTitleStatusTextBlock.Text = $"Pending stem: {stem}. Applied when recording stops.";
    }

    private async Task<bool> TryApplyDeferredMeetingReclassificationAsync(
        ActiveRecordingSession activeSession,
        CancellationToken cancellationToken)
    {
        var deferredReclassification = MainWindowInteractionLogic.GetEligibleActiveSessionReclassification(
            _lastObservedDetectionDecision,
            activeSession.Manifest.Platform,
            activeSession.Manifest.DetectedTitle,
            _autoRecordingContinuityPolicy);
        if (deferredReclassification is null)
        {
            return false;
        }

        return await TryReclassifyActiveSessionAsync(
            activeSession,
            deferredReclassification,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private void UpdateCurrentRecordingElapsedText()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            CurrentRecordingElapsedGrid.Visibility = Visibility.Collapsed;
            CurrentRecordingElapsedTextBlock.Text = string.Empty;
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - activeSession.Manifest.StartedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        CurrentRecordingElapsedGrid.Visibility = Visibility.Visible;
        CurrentRecordingElapsedTextBlock.Text = FormatRecordingElapsed(elapsed);
    }

    private static string FormatRecordingElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1d)
        {
            return elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
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
            DiarizationAccelerationDetailsTextBlock.Text = "GPU acceleration status is unavailable until diarization assets can be inspected.";
            UpdateDiarizationAccelerationStatusText(_liveConfig.Current, status: null);
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
                    DiarizationActionStatusTextBlock.Text =
                        "No downloadable diarization assets were found in the current GitHub source. Refresh again later, open local setup help, import an approved local bundle or files, or open the asset folder.";
                }

                return;
            }

            if (!_diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady)
            {
                DiarizationActionStatusTextBlock.Text =
                    "No diarization model bundle is installed yet. Choose the recommended bundle, open local setup help, import approved local bundle or files, or open the asset folder.";
            }
            else if (manual)
            {
                DiarizationActionStatusTextBlock.Text =
                    "GitHub diarization asset list refreshed. You can download a bundle, open local setup help, import approved local files, or open the asset folder.";
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
                DiarizationActionStatusTextBlock.Text =
                    $"Failed to load GitHub diarization assets: {exception.Message} Refresh again, open local setup help, import an approved local bundle or files, or open the asset folder.";
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
        _currentWhisperModelDisplayState = state;
        WhisperModelStatusTextBlock.Text = state.StatusText;
        WhisperModelStatusTextBlock.Foreground = state.IsHealthy ? HealthyModelStatusBrush : UnhealthyModelStatusBrush;
        WhisperModelDetailsTextBlock.Text = state.DetailsText;
        ModelHealthBannerTextBlock.Text = state.DashboardBannerText ?? string.Empty;
        ModelHealthBanner.Visibility = string.IsNullOrWhiteSpace(state.DashboardBannerText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateModelsTabGuidance();
        UpdateDashboardReadiness();
    }

    private void ApplyDiarizationAssetStatus(DiarizationAssetInstallStatus status)
    {
        _currentDiarizationAssetStatus = status;
        DiarizationAssetStatusTextBlock.Text = status.StatusText;
        DiarizationAssetStatusTextBlock.Foreground = status.IsReady ? HealthyModelStatusBrush : UnhealthyModelStatusBrush;
        DiarizationAssetDetailsTextBlock.Text = status.DetailsText;
        DiarizationAccelerationDetailsTextBlock.Text = BuildDiarizationAccelerationDetails(status);
        UpdateDiarizationAccelerationStatusText(_liveConfig.Current, status);
        UpdateModelsTabGuidance();
        UpdateDiarizationActionButtons();
        UpdateDashboardReadiness();
    }

    private void UpdateDiarizationAccelerationStatusText(AppConfig config, DiarizationAssetInstallStatus? status)
    {
        var gpuEnabled = config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto;
        var preferenceText = gpuEnabled
            ? "GPU acceleration is enabled. The worker will try DirectML first and fall back to CPU automatically."
            : "GPU acceleration is disabled. Speaker labeling will stay on CPU until you turn it back on.";

        var availabilityText = status?.GpuAccelerationAvailable switch
        {
            true => "A DirectML-compatible GPU path was detected the last time speaker labeling initialized.",
            false when !string.IsNullOrWhiteSpace(status?.DiagnosticMessage)
                => $"The last GPU probe fell back to CPU: {status.DiagnosticMessage}",
            false => "The last speaker-labeling run used CPU because no compatible GPU path was detected.",
            _ => "GPU capability will be confirmed after speaker labeling assets are installed and a diarization run starts.",
        };

        ConfigDiarizationAccelerationStatusTextBlock.Text = $"{preferenceText} {availabilityText}";
    }

    private static string BuildDiarizationAccelerationDetails(DiarizationAssetInstallStatus status)
    {
        var providerText = status.EffectiveExecutionProvider switch
        {
            DiarizationExecutionProvider.Directml => "Last effective diarization provider: DirectML.",
            DiarizationExecutionProvider.Cpu => "Last effective diarization provider: CPU.",
            _ => "No diarization run has reported an effective provider yet.",
        };

        var availabilityText = status.GpuAccelerationAvailable switch
        {
            true => "DirectML probe: available.",
            false => "DirectML probe: unavailable, so CPU fallback is expected.",
            _ => "DirectML probe: not recorded yet.",
        };

        if (string.IsNullOrWhiteSpace(status.DiagnosticMessage))
        {
            return $"{providerText} {availabilityText}";
        }

        return $"{providerText} {availabilityText} Diagnostic: {status.DiagnosticMessage}";
    }

    private void UpdateModelsTabGuidance()
    {
        var recommendedRemoteModel = GetRecommendedRemoteModelRow()?.Source;
        var transcriptionRequestedProfile = _liveConfig.Current.TranscriptionModelProfilePreference;
        var transcriptionActiveProfile = _currentWhisperModelDisplayState?.IsHealthy == true
            ? _meetingRecorderModelCatalogService.ResolveTranscriptionProfilePreference(
                _bundledModelCatalog,
                _liveConfig.Current.ModelCacheDir,
                _liveConfig.Current.TranscriptionModelPath)
            : transcriptionRequestedProfile == TranscriptionModelProfilePreference.Custom
                ? TranscriptionModelProfilePreference.Custom
                : TranscriptionModelProfilePreference.Standard;
        var transcriptionRetryRecommended =
            transcriptionRequestedProfile == TranscriptionModelProfilePreference.HighAccuracyDownloaded &&
            transcriptionActiveProfile != TranscriptionModelProfilePreference.HighAccuracyDownloaded;
        var transcriptionState = MainWindowInteractionLogic.BuildModelsTabTranscriptionSetupState(
            transcriptionRequestedProfile,
            transcriptionActiveProfile,
            _currentWhisperModelDisplayState?.IsHealthy == true,
            transcriptionRetryRecommended);
        _currentTranscriptionSetupState = transcriptionState;

        ApplySetupOverviewStatusChip(
            TranscriptionOverviewStatusChipBorder,
            TranscriptionOverviewStatusTextBlock,
            transcriptionState.Status,
            _currentWhisperModelDisplayState?.IsHealthy == true);
        TranscriptionOverviewSummaryTextBlock.Text = transcriptionState.Body;
        TranscriptionOverviewPrimaryButton.Content = transcriptionState.PrimaryActionLabel;

        if (recommendedRemoteModel is null)
        {
            RecommendedRemoteModelNameTextBlock.Text = "No GitHub model recommendation is loaded yet.";
            RecommendedRemoteModelSummaryTextBlock.Text =
                "Refresh GitHub Models to load the recommended download, or import an approved local model file.";
        }
        else
        {
            var sizeText = recommendedRemoteModel.FileSizeBytes.HasValue
                ? FormatBytes(recommendedRemoteModel.FileSizeBytes.Value)
                : "unknown size";
            RecommendedRemoteModelNameTextBlock.Text = recommendedRemoteModel.FileName;
            RecommendedRemoteModelSummaryTextBlock.Text =
                $"{recommendedRemoteModel.Description} Download size: {sizeText}.";
        }

        var recommendedDiarizationAsset = GetRecommendedRemoteDiarizationAssetRow()?.Source;
        var speakerLabelingRequestedProfile = _liveConfig.Current.SpeakerLabelingModelProfilePreference;
        var speakerLabelingActiveProfile = _currentDiarizationAssetStatus?.IsReady == true
            ? _meetingRecorderModelCatalogService.ResolveSpeakerLabelingProfilePreference(
                _bundledModelCatalog,
                _liveConfig.Current.ModelCacheDir,
                _liveConfig.Current.DiarizationAssetPath)
            : speakerLabelingRequestedProfile is SpeakerLabelingModelProfilePreference.Custom or SpeakerLabelingModelProfilePreference.Disabled
                ? speakerLabelingRequestedProfile
                : SpeakerLabelingModelProfilePreference.Standard;
        var speakerLabelingRetryRecommended =
            speakerLabelingRequestedProfile == SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded &&
            speakerLabelingActiveProfile != SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded;
        var speakerLabelingState = MainWindowInteractionLogic.BuildModelsTabSpeakerLabelingSetupState(
            speakerLabelingRequestedProfile,
            speakerLabelingActiveProfile,
            _currentDiarizationAssetStatus?.IsReady == true,
            speakerLabelingRetryRecommended,
            _liveConfig.Current.BackgroundSpeakerLabelingMode);
        _currentSpeakerLabelingSetupState = speakerLabelingState;
        var speakerLabelingRunsAutomatically =
            _currentDiarizationAssetStatus?.IsReady == true &&
            _liveConfig.Current.BackgroundSpeakerLabelingMode != BackgroundSpeakerLabelingMode.Deferred;

        ApplySetupOverviewStatusChip(
            SpeakerLabelingOverviewStatusChipBorder,
            SpeakerLabelingOverviewStatusTextBlock,
            speakerLabelingState.Status,
            speakerLabelingRunsAutomatically);
        SpeakerLabelingOverviewSummaryTextBlock.Text = speakerLabelingState.Body;
        SpeakerLabelingOverviewPrimaryButton.Content = speakerLabelingState.PrimaryActionLabel;

        TranscriptionRetryStatusTextBlock.Text = transcriptionRetryRecommended
            ? "Higher Accuracy was requested earlier, but the Standard model is active right now. Retry Higher Accuracy when downloads are available."
            : string.Empty;
        TranscriptionRetryStatusTextBlock.Visibility = transcriptionRetryRecommended
            ? Visibility.Visible
            : Visibility.Collapsed;
        SpeakerLabelingRetryStatusTextBlock.Text = speakerLabelingRetryRecommended
            ? "Higher Accuracy was requested earlier, but the Standard bundle is active right now. Retry Higher Accuracy when downloads are available."
            : string.Empty;
        SpeakerLabelingRetryStatusTextBlock.Visibility = speakerLabelingRetryRecommended
            ? Visibility.Visible
            : Visibility.Collapsed;

        var alternateLocationsState = ModelsTabGuidance.BuildAlternatePublicDownloadLocationsState(
            ModelsTabGuidance.GetSpeakerLabelingAlternatePublicDownloadLocations());
        AlternatePublicDownloadLocationsItemsControl.ItemsSource = alternateLocationsState.Locations;
        AlternatePublicDownloadLocationsEmptyStateTextBlock.Text = alternateLocationsState.EmptyStateText;
        AlternatePublicDownloadLocationsEmptyStateTextBlock.Visibility = alternateLocationsState.Locations.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (recommendedDiarizationAsset is null)
        {
            RecommendedDiarizationBundleNameTextBlock.Text = "No recommended diarization model bundle is loaded yet.";
            RecommendedDiarizationBundleSummaryTextBlock.Text =
                "Refresh Diarization Assets, open the local setup guide, import an approved local bundle or files, or open the asset folder.";
        }
        else
        {
            var sizeText = recommendedDiarizationAsset.FileSizeBytes.HasValue
                ? FormatBytes(recommendedDiarizationAsset.FileSizeBytes.Value)
                : "unknown size";
            RecommendedDiarizationBundleNameTextBlock.Text = recommendedDiarizationAsset.FileName;
            RecommendedDiarizationBundleSummaryTextBlock.Text =
                $"{recommendedDiarizationAsset.Description} Download size: {sizeText}.";
        }
    }

    private static void ApplySetupOverviewStatusChip(Border chipBorder, TextBlock statusTextBlock, string statusText, bool isReady)
    {
        statusTextBlock.Text = statusText;
        var isOptional = statusText.Contains("Optional", StringComparison.OrdinalIgnoreCase);
        statusTextBlock.Foreground = isReady
            ? HealthyModelStatusBrush
            : isOptional
                ? CreateBrush(0x8A, 0x5A, 0x00)
                : UnhealthyModelStatusBrush;
        chipBorder.Background = isReady
            ? HealthyModelStatusChipBackgroundBrush
            : isOptional
                ? CreateBrush(0xF6, 0xE8, 0xC7)
                : UnhealthyModelStatusChipBackgroundBrush;
        chipBorder.BorderBrush = isReady
            ? HealthyModelStatusChipBorderBrush
            : isOptional
                ? CreateBrush(0xD7, 0xB6, 0x71)
                : UnhealthyModelStatusChipBorderBrush;
    }

    private void UpdateModelActionButtons()
    {
        var isModelActionInProgress =
            _isRefreshingModelStatus ||
            Volatile.Read(ref _remoteModelRefreshOperations) > 0 ||
            _isActivatingModel ||
            _isDownloadingRemoteModel ||
            _isImportingModel;
        var recommendedRemoteModel = GetRecommendedRemoteModelRow();
        var hasHealthyModel = _currentWhisperModelDisplayState?.IsHealthy == true;

        RefreshModelStatusButton.Content = _isRefreshingModelStatus ? "Refreshing..." : "Refresh Status";
        RefreshRemoteModelsButton.Content = Volatile.Read(ref _remoteModelRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh GitHub Models";
        UseStandardTranscriptionProfileButton.Content = _isActivatingModel && !_isDownloadingRemoteModel
            ? "Applying..."
            : "Use Standard";
        UseHighAccuracyTranscriptionProfileButton.Content = _isDownloadingRemoteModel
            ? "Downloading..."
            : "Use Higher Accuracy";
        DownloadRecommendedRemoteModelButton.Content = _isDownloadingRemoteModel
            ? "Downloading..."
            : "Download Recommended Model";
        DownloadSelectedRemoteModelButton.Content = _isDownloadingRemoteModel
            ? "Downloading..."
            : "Download Selected Model";
        ImportApprovedTranscriptionModelButton.Content = _isImportingModel
            ? "Importing..."
            : "Import approved file";
        OpenTranscriptionModelFolderButton.Content = "Open model folder";
        ImportWhisperModelButton.Content = _isImportingModel
            ? "Importing..."
            : "Import Existing File";
        ActivateSelectedModelButton.Content = _isActivatingModel
            ? "Switching..."
            : "Use Selected Model";

        UseStandardTranscriptionProfileButton.IsEnabled = !isModelActionInProgress;
        UseHighAccuracyTranscriptionProfileButton.IsEnabled = !isModelActionInProgress;
        RefreshModelStatusButton.IsEnabled = !isModelActionInProgress;
        RefreshRemoteModelsButton.IsEnabled = !isModelActionInProgress;
        DownloadRecommendedRemoteModelButton.IsEnabled = !isModelActionInProgress &&
            recommendedRemoteModel is not null;
        DownloadSelectedRemoteModelButton.IsEnabled = !isModelActionInProgress &&
            AvailableRemoteModelsComboBox.SelectedItem is WhisperRemoteModelListRow;
        ImportApprovedTranscriptionModelButton.IsEnabled = !isModelActionInProgress;
        OpenTranscriptionModelFolderButton.IsEnabled = true;
        ImportWhisperModelButton.IsEnabled = !isModelActionInProgress;
        OpenModelFolderButton.IsEnabled = true;
        ActivateSelectedModelButton.IsEnabled = !isModelActionInProgress &&
            AvailableModelsComboBox.SelectedItem is WhisperModelListRow selectedRow &&
            !selectedRow.Source.IsConfigured &&
            selectedRow.Source.Status.Kind == WhisperModelStatusKind.Valid;
        TranscriptionOverviewPrimaryButton.IsEnabled = hasHealthyModel ||
            (!isModelActionInProgress && recommendedRemoteModel is not null);
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
        var recommendedRemoteAsset = GetRecommendedRemoteDiarizationAssetRow();
        var diarizationReady = _currentDiarizationAssetStatus?.IsReady == true;

        RefreshRemoteDiarizationAssetsButton.Content = Volatile.Read(ref _remoteDiarizationRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh Diarization Assets";
        UseStandardSpeakerLabelingProfileButton.Content = !isBusy && !_isDownloadingRemoteDiarizationAsset
            ? "Use Standard"
            : "Applying...";
        SkipSpeakerLabelingForNowButton.Content = !isBusy
            ? "Skip for now"
            : "Applying...";
        UseHighAccuracySpeakerLabelingProfileButton.Content = _isDownloadingRemoteDiarizationAsset
            ? "Downloading..."
            : "Use Higher Accuracy";
        DownloadRecommendedDiarizationBundleButton.Content = _isDownloadingRemoteDiarizationAsset
            ? "Downloading..."
            : "Download Recommended Bundle";
        DownloadSelectedRemoteDiarizationAssetButton.Content = _isDownloadingRemoteDiarizationAsset
            ? "Downloading..."
            : "Download Selected Asset";
        ImportApprovedSpeakerLabelingButton.Content = _isImportingDiarizationAsset
            ? "Importing..."
            : "Import approved file";
        OpenSpeakerLabelingAssetFolderButton.Content = "Open asset folder";
        ImportDiarizationAssetButton.Content = _isImportingDiarizationAsset
            ? "Importing..."
            : "Import Existing File";

        UseStandardSpeakerLabelingProfileButton.IsEnabled = !isBusy;
        SkipSpeakerLabelingForNowButton.IsEnabled = !isBusy;
        UseHighAccuracySpeakerLabelingProfileButton.IsEnabled = !isBusy;
        RefreshRemoteDiarizationAssetsButton.IsEnabled = !isBusy;
        DownloadRecommendedDiarizationBundleButton.IsEnabled = !isBusy &&
            recommendedRemoteAsset is not null;
        DownloadSelectedRemoteDiarizationAssetButton.IsEnabled = !isBusy &&
            AvailableRemoteDiarizationAssetsComboBox.SelectedItem is DiarizationRemoteAssetListRow;
        ImportApprovedSpeakerLabelingButton.IsEnabled = !isBusy;
        OpenSpeakerLabelingAssetFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_liveConfig.Current.DiarizationAssetPath);
        ImportDiarizationAssetButton.IsEnabled = !isBusy;
        OpenDiarizationFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_liveConfig.Current.DiarizationAssetPath);
        SpeakerLabelingOverviewPrimaryButton.IsEnabled = !isBusy;
        DiarizationOperationProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool HasReadyTranscriptionModel()
    {
        return _currentWhisperModelDisplayState?.IsHealthy == true;
    }

    private void ShowTranscriptionSetupRequiredMessage()
    {
        MessageBox.Show(
            "Recording is blocked until transcription is ready. Open Settings > Setup to download Standard, try Higher Accuracy, or import an approved local model.",
            AppBranding.DisplayNameWithVersion,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        OpenSetupWindow(
            SetupWindowSection.Transcription,
            SettingsTranscriptionSetupSectionBorder,
            _currentTranscriptionSetupState,
            ModelActionStatusTextBlock);
    }

    private static string GetDiarizationAvailabilityText(DiarizationAssetInstallStatus status)
    {
        return status.IsReady ? "available" : "still unavailable";
    }

    private WhisperRemoteModelListRow? GetRecommendedRemoteModelRow()
    {
        return AvailableRemoteModelsComboBox.Items
            .OfType<WhisperRemoteModelListRow>()
            .FirstOrDefault(row => row.Source.IsRecommended) ??
            AvailableRemoteModelsComboBox.Items
                .OfType<WhisperRemoteModelListRow>()
                .FirstOrDefault();
    }

    private DiarizationRemoteAssetListRow? GetRecommendedRemoteDiarizationAssetRow()
    {
        return AvailableRemoteDiarizationAssetsComboBox.Items
            .OfType<DiarizationRemoteAssetListRow>()
            .FirstOrDefault(row => row.Source.IsRecommended) ??
            AvailableRemoteDiarizationAssetsComboBox.Items
                .OfType<DiarizationRemoteAssetListRow>()
                .FirstOrDefault();
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
            : $"{decision.Platform}|{decision.ShouldStart}|{decision.ShouldKeepRecording}|{decision.SessionTitle}|{decision.Reason}|{decision.DetectedAudioSource?.AppName}|{decision.DetectedAudioSource?.WindowTitle}|{decision.DetectedAudioSource?.BrowserTabTitle}|{decision.DetectedAudioSource?.MatchKind}|{decision.DetectedAudioSource?.Confidence}";

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
        var detectedAudioSource = decision.DetectedAudioSource is null
            ? "none"
            : MainWindowInteractionLogic.BuildDetectedAudioSourceSummary(decision.DetectedAudioSource);
        AppendActivity(
            $"Detection candidate: platform={decision.Platform}; title='{decision.SessionTitle}'; confidence={decision.Confidence:P0}; shouldStart={decision.ShouldStart}; shouldKeepRecording={decision.ShouldKeepRecording}; reason='{decision.Reason}'; signals={signals}");
        if (decision.DetectedAudioSource is not null)
        {
            AppendActivity($"Detection audio source: {detectedAudioSource}.");
        }
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

    private void MeetingListItem_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem item || item.DataContext is not MeetingListRow row)
        {
            return;
        }

        var selectedRows = GetSelectedMeetingRows();
        if (!selectedRows.Any(selectedRow =>
                string.Equals(selectedRow.Source.Stem, row.Source.Stem, StringComparison.OrdinalIgnoreCase)))
        {
            MeetingsDataGrid.SelectedItems.Clear();
            MeetingsDataGrid.SelectedItem = row;
            MeetingsDataGrid.SelectedItems.Add(row);
        }
        else
        {
            MeetingsDataGrid.SelectedItem = row;
        }
    }

    private void RevealMeetingDrafts(Control focusTarget)
    {
        LegacyMeetingActionDraftsBorder.Visibility = Visibility.Visible;
        LegacyMeetingActionDraftsExpander.IsExpanded = true;
        focusTarget.BringIntoView();
        _ = Dispatcher.BeginInvoke(focusTarget.Focus, DispatcherPriority.Background);
    }

    private void OpenSelectedTranscriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenMeetingTranscriptMenuItem_OnClick(sender, e);
    }

    private void OpenSelectedAudioButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenMeetingAudioMenuItem_OnClick(sender, e);
    }

    private void ReviewCleanupSuggestionsActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReviewMeetingCleanupSuggestionsButton_OnClick(sender, e);
    }

    private void RenameMeetingActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedMeetingTitleTextBox.SelectAll();
        RevealMeetingDrafts(SelectedMeetingTitleTextBox);
        SelectedMeetingStatusTextBlock.Text = "Edit the meeting title, then click Rename Meeting to apply it.";
    }

    private void SuggestMeetingTitleActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        SuggestMeetingTitleContextMenuItem_OnClick(sender, e);
    }

    private void RetryTranscriptActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        RetryMeetingTranscriptContextMenuItem_OnClick(sender, e);
    }

    private async void ProcessAsapActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await UpdateRushProcessingForSelectionAsync(sender);
    }

    private void SplitMeetingActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        SplitSelectedMeetingPointTextBox.SelectAll();
        RevealMeetingDrafts(SplitSelectedMeetingPointTextBox);
        SplitSelectedMeetingStatusTextBlock.Text = "Choose a split point, then click Split Into Two.";
    }

    private void MergeMeetingsActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        RevealMeetingDrafts(MergeSelectedMeetingsTitleTextBox);
        MergeSelectedMeetingsStatusTextBlock.Text = "Review the merged title, then click Merge Selected Meetings.";
    }

    private void MeetingsContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        UpdateMeetingsContextMenuState();
    }

    private void UpdateMeetingsContextMenuState()
    {
        if (!_isUiReady)
        {
            return;
        }

        var selectedMeetings = GetSelectedMeetingRows();
        var focusedMeeting = MeetingsDataGrid.SelectedItem as MeetingListRow;
        var hasRecommendationInSelection = selectedMeetings.Any(row => row.PrimaryRecommendation is not null);
        var canAddSpeakerLabels = selectedMeetings.Any(CanQueueSpeakerLabelsForMeeting);
        var canProcessAsap = focusedMeeting is not null && CanChangeRushProcessing(focusedMeeting);
        var isSelectedMeetingAsap = IsMeetingMarkedAsap(focusedMeeting);
        var contextState = MainWindowInteractionLogic.BuildMeetingContextActionState(
            selectedMeetings.Length,
            focusedMeeting is not null,
            focusedMeeting?.CanOpenAudioArtifact == true,
            focusedMeeting?.CanOpenTranscriptArtifact == true,
            hasRecommendationInSelection,
            focusedMeeting?.CanRegenerateTranscript == true,
            canAddSpeakerLabels,
            canProcessAsap,
            isSelectedMeetingAsap,
            IsMeetingActionInProgress());

        OpenMeetingTranscriptMenuItem.IsEnabled = contextState.CanOpenTranscript;
        OpenMeetingAudioMenuItem.IsEnabled = contextState.CanOpenAudio;
        OpenMeetingContainingFolderMenuItem.IsEnabled = contextState.CanOpenContainingFolder;
        CopyMeetingTranscriptPathMenuItem.IsEnabled = contextState.CanCopyTranscriptPath;
        CopyMeetingAudioPathMenuItem.IsEnabled = contextState.CanCopyAudioPath;
        OpenMeetingTranscriptMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingAudioMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingContainingFolderMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        CopyMeetingTranscriptPathMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        CopyMeetingAudioPathMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        SingleMeetingContextMenuSeparator.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyMeetingRecommendedActionMenuItem.IsEnabled = contextState.CanApplyRecommendedAction;
        RenameMeetingContextMenuItem.IsEnabled = contextState.CanRename;
        SuggestMeetingTitleContextMenuItem.IsEnabled = contextState.CanSuggestTitle;
        RetryMeetingTranscriptContextMenuItem.IsEnabled = contextState.CanRegenerateTranscript;
        ReTranscribeMeetingWithDifferentModelMenuItem.IsEnabled = contextState.CanReTranscribeWithDifferentModel;
        AddSpeakerLabelsContextMenuItem.IsEnabled = contextState.CanAddSpeakerLabels;
        ProcessAsapContextMenuItem.IsEnabled = contextState.CanProcessAsap || contextState.CanClearAsap;
        ProcessAsapContextMenuItem.Header = contextState.CanClearAsap ? "Clear ASAP" : "Process ASAP...";
        SplitMeetingContextMenuItem.IsEnabled = contextState.CanSplit;
        ArchiveMeetingContextMenuItem.IsEnabled = contextState.CanArchive;
        DeleteMeetingPermanentlyMenuItem.IsEnabled = contextState.CanDeletePermanently;
        ApplyMeetingRecommendedActionMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        RenameMeetingContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        SuggestMeetingTitleContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        RetryMeetingTranscriptContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ReTranscribeMeetingWithDifferentModelMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        AddSpeakerLabelsContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ProcessAsapContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup &&
            (contextState.CanProcessAsap || contextState.CanClearAsap)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SplitMeetingContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ArchiveMeetingContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteMeetingPermanentlyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyRecommendationsForSelectedMeetingsMenuItem.IsEnabled = contextState.CanApplyRecommendationsForSelection;
        MergeSelectedMeetingsContextMenuItem.IsEnabled = contextState.CanMergeSelected;
        ReTranscribeSelectedMeetingsWithModelMenuItem.IsEnabled = contextState.CanReTranscribeSelectedWithModel;
        AddSpeakerLabelsToSelectedMeetingsMenuItem.IsEnabled = contextState.CanAddSpeakerLabelsToSelected;
        ArchiveSelectedMeetingsMenuItem.IsEnabled = contextState.CanArchiveSelected;
        DeleteSelectedMeetingsPermanentlyMenuItem.IsEnabled = contextState.CanDeleteSelectedPermanently;
        BulkMeetingContextMenuSeparator.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyRecommendationsForSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        MergeSelectedMeetingsContextMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ReTranscribeSelectedMeetingsWithModelMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        AddSpeakerLabelsToSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ArchiveSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteSelectedMeetingsPermanentlyMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenMeetingTranscriptMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is MeetingListRow row)
        {
            OpenPath(row.PrimaryTranscriptPath ?? string.Empty);
        }
    }

    private void OpenMeetingAudioMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is MeetingListRow row)
        {
            OpenPath(row.Source.AudioPath ?? string.Empty);
        }
    }

    private void OpenMeetingContainingFolderMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is MeetingListRow row)
        {
            OpenContainingFolder(GetPreferredMeetingFolderPath(row));
        }
    }

    private void CopyMeetingTranscriptPathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is MeetingListRow row)
        {
            TryCopyPathToClipboard(row.PrimaryTranscriptPath, "transcript");
        }
    }

    private void CopyMeetingAudioPathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is MeetingListRow row)
        {
            TryCopyPathToClipboard(row.Source.AudioPath, "audio");
        }
    }

    private async void ApplyMeetingRecommendedActionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow row || row.PrimaryRecommendation is null)
        {
            return;
        }

        await ApplyPrimaryRecommendationsAsync([row], "context-single");
    }

    private void RenameMeetingContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        LegacyMeetingActionDraftsBorder.Visibility = Visibility.Visible;
        LegacyMeetingActionDraftsExpander.IsExpanded = true;
        SelectedMeetingTitleTextBox.Focus();
        SelectedMeetingTitleTextBox.SelectAll();
        SelectedMeetingStatusTextBlock.Text = "Edit the meeting title, then click Rename Meeting to apply it.";
    }

    private async void SuggestMeetingTitleContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        SuggestSelectedMeetingTitleButton_OnClick(sender, e);
        await Task.CompletedTask;
    }

    private async void RetryMeetingTranscriptContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        RetrySelectedMeetingButton_OnClick(sender, e);
        await Task.CompletedTask;
    }

    private void ReTranscribeMeetingWithDifferentModelMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSetupWindow(
            SetupWindowSection.Transcription,
            SettingsTranscriptionSetupSectionBorder,
            _currentTranscriptionSetupState,
            ModelActionStatusTextBlock);
        AppendActivity("Open Setup to choose a different Whisper model, then re-generate the transcript for the selected meeting.");
    }

    private async void AddSpeakerLabelsContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            return;
        }

        await QueueSpeakerLabelsForMeetingsAsync(selectedMeetings, "context-single-speaker-labels");
    }

    private async void ProcessAsapContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await UpdateRushProcessingForSelectionAsync(sender);
    }

    private void SplitMeetingContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        LegacyMeetingActionDraftsBorder.Visibility = Visibility.Visible;
        LegacyMeetingActionDraftsExpander.IsExpanded = true;
        SplitSelectedMeetingPointTextBox.Focus();
        SplitSelectedMeetingPointTextBox.SelectAll();
        SplitSelectedMeetingStatusTextBlock.Text = "Choose a split point, then click Split Into Two.";
    }

    private async void ArchiveMeetingContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            return;
        }

        await ArchiveMeetingsAsync(selectedMeetings, "context-single-archive");
    }

    private async void ApplyRecommendationsForSelectedMeetingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            return;
        }

        var selectedStems = selectedMeetings
            .Select(row => row.Source.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedRecommendations = _meetingCleanupRecommendations
            .Where(recommendation => recommendation.RelatedStems.All(selectedStems.Contains))
            .ToArray();

        await ApplyMeetingRecommendationsAsync(selectedRecommendations, "context-bulk");
    }

    private async void MergeSelectedMeetingsContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        LegacyMeetingActionDraftsBorder.Visibility = Visibility.Visible;
        LegacyMeetingActionDraftsExpander.IsExpanded = true;
        MergeSelectedMeetingsButton_OnClick(sender, e);
        await Task.CompletedTask;
    }

    private void ReTranscribeSelectedMeetingsWithModelMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSetupWindow(
            SetupWindowSection.Transcription,
            SettingsTranscriptionSetupSectionBorder,
            _currentTranscriptionSetupState,
            ModelActionStatusTextBlock);
        AppendActivity("Open Setup to choose a different Whisper model, then re-generate transcripts for the selected meetings.");
    }

    private async void AddSpeakerLabelsToSelectedMeetingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            return;
        }

        await QueueSpeakerLabelsForMeetingsAsync(selectedMeetings, "context-bulk-speaker-labels");
    }

    private async void ArchiveSelectedMeetingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 0)
        {
            return;
        }

        await ArchiveMeetingsAsync(selectedMeetings, "context-bulk-archive");
    }

    private void DeleteSelectedMeetingsPermanentlyMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        MeetingPermanentDeleteMenuItem_OnClick(sender, e);
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

    private async Task ApplyPrimaryRecommendationsAsync(IReadOnlyList<MeetingListRow> rows, string archiveLabel)
    {
        var recommendations = rows
            .Select(row => row.PrimaryRecommendation)
            .Where(recommendation => recommendation is not null)
            .Cast<MeetingCleanupRecommendation>()
            .ToArray();

        await ApplyMeetingRecommendationsAsync(recommendations, archiveLabel);
    }

    private async Task ApplyMeetingRecommendationsAsync(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        string archiveLabel)
    {
        if (recommendations.Count == 0 || IsMeetingActionInProgress())
        {
            return;
        }

        _isApplyingMeetingCleanupRecommendations = true;
        UpdateMeetingActionState();
        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(recommendations, archiveLabel, _lifetimeCts.Token);
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Meeting action failed: {exception.Message}";
            AppendActivity($"Meeting action failed: {exception.Message}");
        }
        finally
        {
            _isApplyingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private async Task ArchiveMeetingsAsync(IReadOnlyList<MeetingListRow> meetings, string archiveLabel)
    {
        if (meetings.Count == 0 || IsMeetingActionInProgress())
        {
            return;
        }

        _isArchivingMeetings = true;
        UpdateMeetingActionState();
        try
        {
            var archiveRoot = MeetingCleanupExecutionService.GetArchiveRoot(_liveConfig.Current.AudioOutputDir);
            var archiveDirectory = MeetingCleanupExecutionService.CreateExecutionArchiveDirectory(archiveRoot, archiveLabel);
            foreach (var meeting in meetings)
            {
                await _meetingCleanupExecutionService.ArchiveMeetingAsync(
                    meeting.Source,
                    archiveDirectory,
                    "manual-archive",
                    _lifetimeCts.Token);
            }

            MeetingCleanupRecommendationsStatusTextBlock.Text = meetings.Count == 1
                ? $"Archived '{meetings[0].Title}'."
                : $"Archived {meetings.Count} meetings.";
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text = $"Archive failed: {exception.Message}";
            AppendActivity($"Archive failed: {exception.Message}");
        }
        finally
        {
            _isArchivingMeetings = false;
            UpdateMeetingActionState();
        }
    }

    private async Task QueueSpeakerLabelsForMeetingsAsync(IReadOnlyList<MeetingListRow> meetings, string activityLabel)
    {
        if (meetings.Count == 0 || IsMeetingActionInProgress())
        {
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        try
        {
            foreach (var meeting in meetings.Where(CanQueueSpeakerLabelsForMeeting))
            {
                await QueueSpeakerLabelGenerationAsync(meeting.Source, _lifetimeCts.Token);
            }

            AppendActivity($"Queued speaker labeling from {activityLabel}.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            SpeakerNamesStatusTextBlock.Text = $"Failed to queue speaker labels: {exception.Message}";
            AppendActivity($"Failed to queue speaker labels: {exception.Message}");
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private bool CanQueueSpeakerLabelsForMeeting(MeetingListRow row)
    {
        var diarizationReady = _currentDiarizationAssetStatus?.IsReady
            ?? _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady;
        return diarizationReady &&
               row.CanRegenerateTranscript &&
               !row.Source.HasSpeakerLabels;
    }

    private bool CanChangeRushProcessing(MeetingListRow row)
    {
        return !string.IsNullOrWhiteSpace(row.Source.ManifestPath) &&
               row.Source.ManifestState is SessionState.Queued or SessionState.Processing or SessionState.Finalizing;
    }

    private bool IsMeetingMarkedAsap(MeetingListRow? row)
    {
        return row is not null &&
               !string.IsNullOrWhiteSpace(row.Source.ManifestPath) &&
               string.Equals(
                   _latestProcessingQueueStatusSnapshot.RushRequest?.ManifestPath,
                   row.Source.ManifestPath,
                   StringComparison.Ordinal);
    }

    private void TryCopyPathToClipboard(string? path, string artifactLabel)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendActivity($"No {artifactLabel} path is available to copy.");
            return;
        }

        try
        {
            Clipboard.SetText(path);
            AppendActivity($"Copied {artifactLabel} path: {path}");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to copy {artifactLabel} path: {exception.Message}");
        }
    }

    private static string GetPreferredMeetingFolderPath(MeetingListRow row)
    {
        return row.PrimaryTranscriptPath
            ?? row.Source.AudioPath
            ?? row.Source.ManifestPath
            ?? string.Empty;
    }

    private MeetingListRow[] GetMeetingRowsForContextMenuAction(object sender)
    {
        var selectedRows = GetSelectedMeetingRows();
        if (TryGetMeetingRowFromSender(sender) is not { } contextRow)
        {
            return selectedRows;
        }

        return selectedRows.Any(row =>
                   string.Equals(row.Source.Stem, contextRow.Source.Stem, StringComparison.OrdinalIgnoreCase))
            ? selectedRows
            : [contextRow];
    }

    private async Task UpdateRushProcessingForSelectionAsync(object sender)
    {
        var targetMeetings = GetMeetingRowsForContextMenuAction(sender);
        if (targetMeetings.Length != 1)
        {
            SelectedMeetingStatusTextBlock.Text = "Select exactly one queued or processing meeting before changing ASAP processing.";
            return;
        }

        var meeting = targetMeetings[0];
        if (!CanChangeRushProcessing(meeting) || string.IsNullOrWhiteSpace(meeting.Source.ManifestPath))
        {
            SelectedMeetingStatusTextBlock.Text = $"'{meeting.Title}' is not eligible for ASAP processing.";
            return;
        }

        _isUpdatingRushProcessing = true;
        UpdateMeetingActionState();
        try
        {
            if (IsMeetingMarkedAsap(meeting))
            {
                SelectedMeetingStatusTextBlock.Text = $"Clearing ASAP processing for '{meeting.Title}'...";
                await _processingQueue.ClearRushProcessingAsync(meeting.Source.ManifestPath, _lifetimeCts.Token);
                SelectedMeetingStatusTextBlock.Text = $"Cleared ASAP processing for '{meeting.Title}'.";
                AppendActivity($"Cleared ASAP processing for '{meeting.Title}'.");
                return;
            }

            var behavior = PromptRushProcessingBehavior(meeting);
            if (behavior is null)
            {
                SelectedMeetingStatusTextBlock.Text = $"ASAP processing canceled for '{meeting.Title}'.";
                return;
            }

            SelectedMeetingStatusTextBlock.Text = $"Marking '{meeting.Title}' for ASAP processing...";
            await _processingQueue.RequestRushProcessingAsync(meeting.Source.ManifestPath, behavior.Value, _lifetimeCts.Token);
            var behaviorLabel = behavior.Value == RushProcessingBehavior.RunNextIgnoreRecordingPause
                ? "run next and ignore recording pause"
                : "run next only";
            SelectedMeetingStatusTextBlock.Text = $"Marked '{meeting.Title}' as ASAP ({behaviorLabel}).";
            AppendActivity($"Marked '{meeting.Title}' as ASAP ({behaviorLabel}).");
        }
        catch (Exception exception)
        {
            SelectedMeetingStatusTextBlock.Text = $"Unable to update ASAP processing: {exception.Message}";
        }
        finally
        {
            _isUpdatingRushProcessing = false;
            UpdateMeetingActionState();
        }
    }

    private RushProcessingBehavior? PromptRushProcessingBehavior(MeetingListRow meeting)
    {
        var selectionWindow = new Window
        {
            Title = "Process ASAP",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            Background = Brushes.White,
        };

        RushProcessingBehavior? result = null;
        var nextOnlyButton = new Button
        {
            Content = "Run next only",
            Width = 150,
            Height = 34,
            IsDefault = true,
        };
        nextOnlyButton.Click += (_, _) =>
        {
            result = RushProcessingBehavior.RunNextOnly;
            selectionWindow.DialogResult = true;
            selectionWindow.Close();
        };

        var ignorePauseButton = new Button
        {
            Content = "Run next and ignore recording pause",
            Width = 240,
            Height = 34,
            Margin = new Thickness(12, 0, 0, 0),
        };
        ignorePauseButton.Click += (_, _) =>
        {
            result = RushProcessingBehavior.RunNextIgnoreRecordingPause;
            selectionWindow.DialogResult = true;
            selectionWindow.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 34,
            Margin = new Thickness(12, 0, 0, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) =>
        {
            selectionWindow.DialogResult = false;
            selectionWindow.Close();
        };

        selectionWindow.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Process '{meeting.Title}' ASAP",
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Margin = new Thickness(0, 10, 0, 0),
                        Text = "Choose whether this meeting should simply run next, or run next even while a live recording is keeping responsive background work paused.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Margin = new Thickness(0, 16, 0, 0),
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            nextOnlyButton,
                            ignorePauseButton,
                            cancelButton,
                        },
                    },
                },
            },
        };

        return selectionWindow.ShowDialog() == true
            ? result
            : null;
    }

    private bool TryConfirmPermanentDelete(IReadOnlyList<MeetingListRow> meetings)
    {
        var titleText = meetings.Count == 1
            ? $"Delete '{meetings[0].Title}' permanently?"
            : $"Delete {meetings.Count} meetings permanently?";
        var bodyText = meetings.Count == 1
            ? "This permanently deletes the published audio, transcript files, ready marker, and the linked work-session folder when present. This cannot be undone."
            : $"This permanently deletes the published audio, transcript files, ready markers, and linked work-session folders for {meetings.Count} meetings when present. This cannot be undone.";

        var confirmationWindow = new Window
        {
            Title = "Delete Permanently",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            Background = Brushes.White,
        };

        var confirmationTextBox = new TextBox
        {
            MinWidth = 220,
            Height = 32,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var deleteButton = new Button
        {
            Content = "Delete Permanently",
            Width = 150,
            Height = 34,
            IsDefault = true,
            IsEnabled = false,
        };

        deleteButton.Click += (_, _) =>
        {
            if (!MainWindowInteractionLogic.IsValidPermanentDeleteConfirmationText(confirmationTextBox.Text))
            {
                return;
            }

            confirmationWindow.DialogResult = true;
            confirmationWindow.Close();
        };

        confirmationTextBox.TextChanged += (_, _) =>
        {
            deleteButton.IsEnabled =
                MainWindowInteractionLogic.IsValidPermanentDeleteConfirmationText(confirmationTextBox.Text);
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 34,
            Margin = new Thickness(10, 0, 0, 0),
            IsCancel = true,
        };

        cancelButton.Click += (_, _) =>
        {
            confirmationWindow.DialogResult = false;
            confirmationWindow.Close();
        };

        confirmationWindow.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = titleText,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Margin = new Thickness(0, 10, 0, 0),
                        Text = bodyText,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Margin = new Thickness(0, 10, 0, 0),
                        Text = "Type DELETE exactly to continue.",
                        FontWeight = FontWeights.SemiBold,
                    },
                    confirmationTextBox,
                    new StackPanel
                    {
                        Margin = new Thickness(0, 16, 0, 0),
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            deleteButton,
                            cancelButton,
                        },
                    },
                },
            },
        };

        return confirmationWindow.ShowDialog() == true;
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

        var selectedFileName = AvailableRemoteModelsComboBox.SelectedItem is WhisperRemoteModelListRow selectedRow
            ? selectedRow.Source.FileName
            : null;

        AvailableRemoteModelsComboBox.ItemsSource = rows;
        var nextSelectedFileName = MainWindowInteractionLogic.GetPreferredRemoteModelSelectionFileName(
            remoteModels,
            selectedFileName);
        var nextSelectedRow = rows.FirstOrDefault(row =>
            string.Equals(row.Source.FileName, nextSelectedFileName, StringComparison.OrdinalIgnoreCase)) ??
            rows.FirstOrDefault();

        AvailableRemoteModelsComboBox.SelectedItem = nextSelectedRow;
        UpdateSelectedRemoteModelEditor(nextSelectedRow);
        UpdateModelsTabGuidance();
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
        UpdateModelsTabGuidance();
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
        BuildAudioGraphPoints(levels, width, height, _audioGraphPoints);
        if (!ReferenceEquals(AudioCaptureGraphPolyline.Points, _audioGraphPoints))
        {
            AudioCaptureGraphPolyline.Points = _audioGraphPoints;
        }

        AudioCaptureGraphStatusTextBlock.Text = statusText + $" Current peak: {currentPeak:0.000}.";
        UpdateCaptureStatusSurface();
    }

    private (IReadOnlyList<double> Levels, double CurrentPeak, string StatusText) BuildAudioGraphSnapshot()
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (_isAutoStopTransitionInProgress)
        {
            Array.Clear(_audioGraphCombinedLevels);
            return (_audioGraphCombinedLevels, 0d, "Auto-stopping.");
        }

        if (activeSession is null)
        {
            Array.Clear(_audioGraphCombinedLevels);
            return (_audioGraphCombinedLevels, 0d, "Idle.");
        }

        activeSession.LoopbackRecorder.LevelHistory.CopySnapshot(_audioGraphLoopbackLevels);
        if (activeSession.MicrophoneRecorder is not null)
        {
            activeSession.MicrophoneRecorder.LevelHistory.CopySnapshot(_audioGraphMicrophoneLevels);
        }
        else
        {
            Array.Clear(_audioGraphMicrophoneLevels);
        }

        for (var index = 0; index < _audioGraphCombinedLevels.Length; index++)
        {
            _audioGraphCombinedLevels[index] = Math.Max(_audioGraphLoopbackLevels[index], _audioGraphMicrophoneLevels[index]);
        }

        var currentPeak = _audioGraphCombinedLevels[^1];
        var liveStatusText = activeSession.MicrophoneRecorder is null
            ? "Loopback live."
            : "Loopback + mic live.";
        var statusText = _autoStopCountdownSecondsRemaining is { } countdownSeconds
            ? $"Auto-stop in {countdownSeconds}s."
            : liveStatusText;
        return (_audioGraphCombinedLevels, currentPeak, statusText);
    }

    private static void BuildAudioGraphPoints(
        IReadOnlyList<double> levels,
        double width,
        double height,
        PointCollection points)
    {
        if (levels.Count == 0)
        {
            if (points.Count != 2)
            {
                points.Clear();
                points.Add(new Point(0d, height - 2d));
                points.Add(new Point(width, height - 2d));
            }
            else
            {
                points[0] = new Point(0d, height - 2d);
                points[1] = new Point(width, height - 2d);
            }

            return;
        }

        if (points.Count != levels.Count)
        {
            points.Clear();
            for (var index = 0; index < levels.Count; index++)
            {
                points.Add(new Point());
            }
        }

        var usableHeight = Math.Max(1d, height - 4d);
        var denominator = Math.Max(1, levels.Count - 1);
        for (var index = 0; index < levels.Count; index++)
        {
            var x = (width * index) / denominator;
            var level = Math.Clamp(levels[index], 0d, 1d);
            var y = height - 2d - (usableHeight * level);
            points[index] = new Point(x, y);
        }
    }

    private void UpdateAudioGraphTimerState()
    {
        var shouldRun = !IsShutdownRequested &&
            !ReferenceEquals(MainTabControl.SelectedItem, MeetingsTabItem) &&
            (_recordingCoordinator.IsRecording || _autoStopCountdownSecondsRemaining is not null || _isAutoStopTransitionInProgress);

        if (!shouldRun)
        {
            UpdateAudioCaptureGraph();
            UpdateCurrentRecordingElapsedText();
            if (_audioGraphTimer.IsEnabled)
            {
                _audioGraphTimer.Stop();
                _logger.Log("Suppressed Home audio graph updates because Home is hidden or no live recording state is visible.");
            }

            return;
        }

        UpdateAudioCaptureGraph();
        UpdateCurrentRecordingElapsedText();
        if (!_audioGraphTimer.IsEnabled)
        {
            _audioGraphTimer.Start();
        }
    }

    private bool TryBeginDetectionCycle()
    {
        if (Interlocked.CompareExchange(ref _detectionCycleActive, 1, 0) == 0)
        {
            return true;
        }

        _logger.Log("Skipped overlapping detection scan because the previous scan is still running.");
        return false;
    }

    private void FinishDetectionCycle()
    {
        Interlocked.Exchange(ref _detectionCycleActive, 0);
    }

    private void InvalidateDetectionCycle()
    {
        Interlocked.Increment(ref _detectionCycleGeneration);
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
                "No downloadable diarization model bundle assets were found in the current release. Upload a diarization bundle or supporting model assets to make them appear here.";
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
                : row.Source.Kind == DiarizationRemoteAssetKind.Bundle
                    ? "Use this when you want a different bundle than the default recommendation."
                    : "Use this only if your diarization setup needs a specific supporting file.");
        UpdateDiarizationActionButtons();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record SelectionOption<TValue>(TValue Value, string Label);

    private sealed class MeetingListRow
    {
        public MeetingListRow(MeetingOutputRecord source, IReadOnlyList<MeetingCleanupRecommendation> recommendations)
        {
            Source = source;
            Recommendations = recommendations;
            Title = source.Title;
            ProjectName = source.ProjectName?.Trim() ?? string.Empty;
            StartedAtUtcSortValue = source.StartedAtUtc;
            StartedAtUtc = MainWindowInteractionLogic.FormatMeetingWorkspaceStartedAt(source.StartedAtUtc);
            DurationSortValue = source.Duration ?? TimeSpan.Zero;
            Duration = FormatDuration(source.Duration);
            Platform = source.Platform.ToString();
            Status = MeetingOutputStatusResolver.ResolveDisplayStatus(source);
            WeekGroupSortValue = MainWindowInteractionLogic.GetMeetingWorkspaceWeekGroupStart(source.StartedAtUtc);
            WeekGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.Week,
                source.StartedAtUtc,
                Platform,
                Status);
            WeekGroupLabel = WeekGroupBaseLabel;
            MonthGroupSortValue = MainWindowInteractionLogic.GetMeetingWorkspaceMonthGroupStart(source.StartedAtUtc);
            MonthGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.Month,
                source.StartedAtUtc,
                Platform,
                Status);
            MonthGroupLabel = MonthGroupBaseLabel;
            PlatformGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.Platform,
                source.StartedAtUtc,
                Platform,
                Status);
            PlatformGroupLabel = PlatformGroupBaseLabel;
            StatusGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.Status,
                source.StartedAtUtc,
                Platform,
                Status);
            StatusGroupLabel = StatusGroupBaseLabel;
            ClientProjectGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.ClientProject,
                source.StartedAtUtc,
                Platform,
                Status,
                source.ProjectName,
                source.Attendees,
                source.KeyAttendees);
            ClientProjectGroupLabel = ClientProjectGroupBaseLabel;
            AttendeeGroupBaseLabel = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
                MeetingsGroupKey.Attendee,
                source.StartedAtUtc,
                Platform,
                Status,
                source.ProjectName,
                source.Attendees,
                source.KeyAttendees);
            AttendeeGroupLabel = AttendeeGroupBaseLabel;
            PrimaryRecommendation = MainWindowInteractionLogic.GetPrimaryMeetingCleanupRecommendation(recommendations);
            RecommendationCount = recommendations.Count;
            Recommended = MainWindowInteractionLogic.BuildMeetingCleanupBadgeText(recommendations);
            RecommendedActionLabel = PrimaryRecommendation is null
                ? string.Empty
                : MainWindowInteractionLogic.BuildMeetingCleanupActionLabel(PrimaryRecommendation.Action);
            RecommendedActionToolTip = PrimaryRecommendation is null
                ? string.Empty
                : RecommendationCount <= 1
                    ? $"Apply {RecommendedActionLabel} for this meeting."
                    : $"Apply {RecommendedActionLabel} for this meeting now. {RecommendationCount - 1} additional recommendation(s) remain in Cleanup Recommendations.";
            CanApplyRecommendedAction = PrimaryRecommendation is not null;
            RecommendedActionVisibility = CanApplyRecommendedAction ? Visibility.Visible : Visibility.Collapsed;
            CanRegenerateTranscript =
                !string.IsNullOrWhiteSpace(source.ManifestPath) ||
                !string.IsNullOrWhiteSpace(source.AudioPath);
            RegenerationStatusText =
                !string.IsNullOrWhiteSpace(source.ManifestPath)
                    ? "Transcript re-generation is available for this session."
                    : !string.IsNullOrWhiteSpace(source.AudioPath)
                    ? "Transcript re-generation is available by rebuilding a work session from the published audio file."
                        : "Transcript re-generation is unavailable because no source audio is available.";
            AudioActionLabel = MainWindowInteractionLogic.BuildMeetingArtifactActionLabel(source.AudioPath);
            AudioActionToolTip = MainWindowInteractionLogic.BuildMeetingArtifactToolTip(source.AudioPath, "Audio");
            CanOpenAudioArtifact = !string.IsNullOrWhiteSpace(source.AudioPath);
            TranscriptActionLabel = MainWindowInteractionLogic.BuildMeetingArtifactActionLabel(source.MarkdownPath ?? source.JsonPath);
            TranscriptActionToolTip = MainWindowInteractionLogic.BuildMeetingArtifactToolTip(source.MarkdownPath ?? source.JsonPath, "Transcript");
            CanOpenTranscriptArtifact = !string.IsNullOrWhiteSpace(source.MarkdownPath ?? source.JsonPath);
            PrimaryTranscriptPath = source.MarkdownPath ?? source.JsonPath;
        }

        public MeetingOutputRecord Source { get; }

        public IReadOnlyList<MeetingCleanupRecommendation> Recommendations { get; }

        public string Title { get; set; }

        public string ProjectName { get; set; }

        public DateTimeOffset StartedAtUtcSortValue { get; }

        public string StartedAtUtc { get; set; }

        public TimeSpan DurationSortValue { get; }

        public string Duration { get; set; }

        public string Platform { get; set; }

        public string Status { get; set; }

        public DateTime WeekGroupSortValue { get; }

        public string WeekGroupBaseLabel { get; }

        public string WeekGroupLabel { get; set; }

        public DateTime MonthGroupSortValue { get; }

        public string MonthGroupBaseLabel { get; }

        public string MonthGroupLabel { get; set; }

        public string PlatformGroupBaseLabel { get; }

        public string PlatformGroupLabel { get; set; }

        public string StatusGroupBaseLabel { get; }

        public string StatusGroupLabel { get; set; }

        public string ClientProjectGroupBaseLabel { get; }

        public string ClientProjectGroupLabel { get; set; }

        public string AttendeeGroupBaseLabel { get; }

        public string AttendeeGroupLabel { get; set; }

        public string Recommended { get; set; }

        public MeetingCleanupRecommendation? PrimaryRecommendation { get; }

        public int RecommendationCount { get; }

        public string RecommendedActionLabel { get; }

        public string RecommendedActionToolTip { get; }

        public bool CanApplyRecommendedAction { get; }

        public Visibility RecommendedActionVisibility { get; }

        public bool CanRegenerateTranscript { get; set; }

        public string RegenerationStatusText { get; set; }

        public string AudioActionLabel { get; }

        public string AudioActionToolTip { get; }

        public bool CanOpenAudioArtifact { get; }

        public string TranscriptActionLabel { get; }

        public string TranscriptActionToolTip { get; }

        public bool CanOpenTranscriptArtifact { get; }

        public string? PrimaryTranscriptPath { get; set; }

        public void ResetGroupLabels()
        {
            WeekGroupLabel = WeekGroupBaseLabel;
            MonthGroupLabel = MonthGroupBaseLabel;
            PlatformGroupLabel = PlatformGroupBaseLabel;
            StatusGroupLabel = StatusGroupBaseLabel;
            ClientProjectGroupLabel = ClientProjectGroupBaseLabel;
            AttendeeGroupLabel = AttendeeGroupBaseLabel;
        }

        public string GetGroupLabel(MeetingsGroupKey groupKey)
        {
            return groupKey switch
            {
                MeetingsGroupKey.Week => WeekGroupLabel,
                MeetingsGroupKey.Month => MonthGroupLabel,
                MeetingsGroupKey.Platform => PlatformGroupLabel,
                MeetingsGroupKey.Status => StatusGroupLabel,
                MeetingsGroupKey.ClientProject => ClientProjectGroupLabel,
                MeetingsGroupKey.Attendee => AttendeeGroupLabel,
                _ => WeekGroupLabel,
            };
        }

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

    private sealed class MeetingCleanupRecommendationRow
    {
        public MeetingCleanupRecommendationRow(
            MeetingCleanupRecommendation source,
            IReadOnlyDictionary<string, MeetingListRow> rowsByStem)
        {
            Source = source;
            ActionLabel = source.Action switch
            {
                MeetingCleanupAction.Archive => "Archive",
                MeetingCleanupAction.Merge => "Merge",
                MeetingCleanupAction.Split => "Split",
                MeetingCleanupAction.Rename => "Rename",
                MeetingCleanupAction.RegenerateTranscript => "Retry Transcript",
                MeetingCleanupAction.GenerateSpeakerLabels => "Add Speaker Labels",
                _ => "Review",
            };
            ConfidenceLabel = source.Confidence.ToString();
            SafetyLabel = MainWindowInteractionLogic.BuildMeetingCleanupSafetyLabel(source);
            Summary = source.Title;
            RelatedMeetingsLabel = string.Join(
                ", ",
                source.RelatedStems
                    .Select(stem => rowsByStem.TryGetValue(stem, out var row) ? row.Title : stem));
        }

        public MeetingCleanupRecommendation Source { get; }

        public string ActionLabel { get; }

        public string ConfidenceLabel { get; }

        public string SafetyLabel { get; }

        public string Summary { get; }

        public string RelatedMeetingsLabel { get; }
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
