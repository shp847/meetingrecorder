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
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
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

internal enum BacklogRushChoice
{
    ThisBacklogOnly = 0,
    ThisAndFutureMeetings = 1,
}

public partial class MainWindow : Window
{
    private const int AudioGraphPointCount = 120;
    private const int RecentCaptureActivitySampleCount = 24;
    private const int MaxActivityLogLines = 300;
    private const string MeetingCleanupHistoricalReviewMarkerFileName = "meeting-cleanup-review-v1.done";
    private const int AutomationPolicyRevision = 1;
    private const string SpeakerLabelingSetupGuideFallbackUrl = "https://github.com/shp847/meetingrecorder/blob/main/SETUP.md#speaker-labeling-optional";
    private const string TeamsThirdPartyApiGuideUrl = "https://support.microsoft.com/en-au/office/connect-to-third-party-devices-in-microsoft-teams-aabca9f2-47bb-407f-9f9b-81a104a883d6";
    private static readonly TimeSpan ShutdownUpdateCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecordingStorageAutoStartBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TeamsRecordingPlaybackAbsentBeforeMerge = TimeSpan.FromMinutes(2);
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
    private readonly VoiceProfileStore _voiceProfileStore;
    private readonly SpeakerNameCorrectionService _speakerNameCorrectionService;
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
    private readonly NextBestActionResolver _homeCommandCenterResolver = new();
    private readonly MeetingViewPresetResolver _meetingViewPresetResolver = new();
    private readonly MeetingRecommendationResolver _meetingRecommendationResolver = new();
    private readonly MeetingActionCatalog _meetingActionCatalog = new();
    private readonly BacklogExperienceResolver _backlogExperienceResolver = new();
    private readonly ExternalAudioImportService _externalAudioImportService;
    private readonly ExternalAudioImportReadinessCoordinator _externalAudioImportReadinessCoordinator;
    private readonly ImportInboxReconciliationService _importInboxReconciliationService;
    private readonly ImportInboxIntakeService _importInboxIntakeService;
    private readonly Guid _importInboxLeaseOwnerId = Guid.NewGuid();
    private readonly AutoRecordingContinuityPolicy _autoRecordingContinuityPolicy;
    private readonly ContinuityCutoverPolicy _continuityCutoverPolicy = new();
    private readonly ContinuityShadowEngine _continuityShadowEngine = new();
    private readonly ContinuityShadowMeter _continuityShadowMeter = new();
    private readonly TeamsIntegrationProbeService _teamsIntegrationProbeService;
    private readonly TeamsDetectionArbitrator _teamsDetectionArbitrator;
    private readonly ISummarySecretStore _summarySecretStore;
    private readonly SummaryProviderValidationService _summaryProviderValidationService;
    private readonly PublishedMeetingSummaryService _publishedMeetingSummaryService;
    private readonly HttpClient _summaryProviderHttpClient;
    private readonly ModelProxyClient _summaryModelCatalogClient;
    private readonly SessionTitleDraftTracker _sessionTitleDraftTracker;
    private readonly SessionTitleDraftTracker _sessionProjectDraftTracker;
    private readonly SessionTitleDraftTracker _sessionKeyAttendeesDraftTracker;
    private readonly MeetingTitleSuggestionService _meetingTitleSuggestionService;
    private readonly TeamsLiveAttendeeCaptureService _teamsLiveAttendeeCaptureService;
    private readonly OutlookCalendarMeetingTitleProvider _outlookCalendarMeetingTitleProvider;
    private readonly MeetingsAttendeeBackfillService _meetingsAttendeeBackfillService;
    private readonly MeetingCleanupAutoApplyCacheService _meetingCleanupAutoApplyCacheService;
    private readonly MeetingCleanupWorkLedgerService _meetingCleanupWorkLedgerService;
    private readonly DispatcherTimer _detectionTimer;
    private readonly DispatcherTimer _audioGraphTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _processingQueueStatusTimer;
    private readonly DispatcherTimer _currentMeetingOptionalMetadataSaveTimer;
    private readonly SemaphoreSlim _updateOperationGate = new(1, 1);
    private readonly SemaphoreSlim _externalAudioImportGate = new(1, 1);
    private readonly SemaphoreSlim _teamsAttendeeCaptureGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Queue<string> _activityLogLines = new();
    private readonly CallbackIntentDispatcher _callbackIntentDispatcher = new();
    private readonly double[] _audioGraphLoopbackLevels = new double[AudioGraphPointCount];
    private readonly double[] _audioGraphMicrophoneLevels = new double[AudioGraphPointCount];
    private readonly double[] _audioGraphCombinedLevels = new double[AudioGraphPointCount];
    private readonly PointCollection _audioGraphPoints = new(AudioGraphPointCount);
    private DateTimeOffset? _lastPositiveDetectionUtc;
    private RecentAutoStopContext? _recentAutoStopContext;
    private ContinuityGraceReceipt? _continuityGraceReceipt;
    private ManualStopSuppressionContext? _manualStopSuppressionContext;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private bool _skipShutdownUpdateCheck;
    private bool _isRecordingTransitionInProgress;
    private bool _isAutoStopTransitionInProgress;
    private bool _isRefreshingModelProxySummaryModels;
    private bool _isRefreshingOpenAiSummaryModels;
    private bool _summaryProviderValidationIsCurrent;
    private bool _isSynchronizingSummaryModelSelectors;
    private IReadOnlyList<ModelProxyModelInfo> _modelProxySummaryModels = Array.Empty<ModelProxyModelInfo>();
    private IReadOnlyList<ModelProxyModelInfo> _openAiSummaryModels = Array.Empty<ModelProxyModelInfo>();
    private string? _modelProxySummaryDefaultModel;
    private string? _modelProxySummaryCatalogState;
    private const string ModelProxyProviderDefaultSelection = "__modelproxy_provider_default__";
    private string? _openAiSummaryDefaultModel;
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
    private bool _isDispatchingPendingMeetingCleanupWork;
    private DateTimeOffset? _meetingCleanupSchedulerFailureBackoffUntilUtc;
    private DateTimeOffset? _lastMeetingCleanupSchedulerRefreshUtc;
    private DateTimeOffset? _lastImportInboxReconciliationUtc;
    private string? _lastCleanupSchedulerStatusLog;
    private string? _lastCleanupSchedulerDispatchDetail;
    private CleanupSchedulerDispatchRequest? _pendingCleanupSchedulerDispatch;
    private bool _isDeletingMeetings;
    private bool _isArchivingMeetings;
    private bool _isUpdatingRushProcessing;
    private bool _isRushingBacklog;
    private bool _isSavingConfig;
    private bool _isTestingDiarizationGpuAcceleration;
    private bool _isTestingTranscriptionCliProvider;
    private bool _isTestingDiarizationCliProvider;
    private bool _isRunningTeamsIntegrationProbe;
    private bool _isValidatingModelProxySummaryProvider;
    private bool _isValidatingOpenAiSummaryProvider;
    private bool _isGeneratingMeetingSummary;
    private bool _isQueueingExternalAudioImports;
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
    private bool _isPersistingInitialMeetingsViewPreset;
    private int _remoteModelRefreshOperations;
    private bool _isActivatingModel;
    private bool _isDownloadingRemoteModel;
    private CancellationTokenSource? _modelProvisioningCts;
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
    private long _callbackIntentRevision;
    private int _detectionCycleActive;
    private int _detectionCycleGeneration;
    private CancellationTokenSource? _meetingBackgroundWorkCts;
    private Guid _meetingBackgroundWorkCancellationIdentity;
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
    private string? _quietGoogleMeetAutoStartFingerprint;
    private DateTimeOffset? _quietGoogleMeetAutoStartFirstObservedUtc;
    private DateTimeOffset? _recordingStorageBackoffUntilUtc;
    private string? _lastTeamsProbeBaselineSummary;
    private string? _pendingMeetingsRefreshSelectedStem;
    private AppUpdateCheckResult? _lastUpdateCheckResult;
    private DateTimeOffset? _summaryProviderValidationObservedAtUtc;
    private WhisperModelStatusDisplayState? _currentWhisperModelDisplayState;
    private DiarizationAssetInstallStatus? _currentDiarizationAssetStatus;
    private ModelsTabSetupState? _currentTranscriptionSetupState;
    private ModelsTabSetupState? _currentSpeakerLabelingSetupState;
    private SettingsHostWindow? _settingsWindow;
    private bool _settingsSectionsArranged;
    private HelpHostWindow? _helpWindow;
    private MeetingDetailWindow? _meetingDetailWindow;
    private string? _openMeetingDetailStem;
    private UIElement? _detachedSettingsBody;
    private ShellStatusState? _shellStatusOverride;
    private ConfigEditorSnapshot? _pendingConfigEditorSnapshotRestore;
    private bool _isSynchronizingSpeakerLabelingModeSelectors;
    private MeetingCleanupRecommendation[] _meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();
    private MeetingListRow[] _allMeetingRows = Array.Empty<MeetingListRow>();
    private DateTimeOffset? _lastSuccessfulMeetingsRefreshUtc;
    private bool _hasMeetingsRefreshFailure;
    private List<ExternalAudioImportReviewRow> _externalAudioImportRows = [];
    private ExternalAudioImportReviewRow? _selectedExternalAudioImportRow;
    private bool _isUpdatingExternalAudioImportEditor;
    private bool _isRefreshingExternalAudioImportGrid;
    private Dictionary<string, bool> _meetingGroupExpansionStates = new(StringComparer.Ordinal);
    private bool _isApplyingMeetingGroupExpansionState;
    private AppShutdownMode _shutdownMode = AppShutdownMode.Deferred;
    private MeetingRefreshMode _pendingMeetingsRefreshMode = MeetingRefreshMode.Fast;
    private ProcessingQueueStatusSnapshot _latestProcessingQueueStatusSnapshot;
    private BacklogExperienceState? _currentBacklogExperienceState;
    private BacklogRecoveryMetadata? _currentBacklogRecoveryMetadata;
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
        _voiceProfileStore = new VoiceProfileStore(AppDataPaths.GetVoiceProfileStorePath());
        _speakerNameCorrectionService = new SpeakerNameCorrectionService(
            _meetingOutputCatalogService,
            _manifestStore,
            new SpeakerNameLearningService(_voiceProfileStore),
            new VoiceProfileMatcher(),
            _voiceProfileStore);
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
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromSeconds(15),
            logger.Log);
        _meetingTitleSuggestionService = new MeetingTitleSuggestionService(_outlookCalendarMeetingTitleProvider);
        _meetingsAttendeeBackfillService = new MeetingsAttendeeBackfillService(
            _outlookCalendarMeetingTitleProvider,
            _meetingOutputCatalogService,
            new MeetingsAttendeeBackfillCacheService());
        _meetingCleanupAutoApplyCacheService = new MeetingCleanupAutoApplyCacheService();
        _meetingCleanupWorkLedgerService = new MeetingCleanupWorkLedgerService();
        _meetingCleanupWorkLedgerService.MigrateLegacyEntries(_meetingCleanupAutoApplyCacheService.GetEntries());
        _microphoneActivityProbe = new SystemMicrophoneActivityProbe();
        _diarizationAssetCatalogService = new DiarizationAssetCatalogService();
        _processingQueue = new ProcessingQueueService(
            liveConfig,
            _manifestStore,
            logger,
            calendarMeetingMetadataEnricher,
            isSpeakerLabelingAvailableProvider: () =>
                _currentDiarizationAssetStatus?.IsReady ??
                _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady);
        _latestProcessingQueueStatusSnapshot = _processingQueue.GetStatusSnapshot();
        _whisperModelService = new WhisperModelService(new WhisperNetModelDownloader());
        _whisperModelCatalogService = new WhisperModelCatalogService(_whisperModelService);
        var updateFeedClient = new HttpAppUpdateFeedClient();
        _whisperModelReleaseCatalogService = new WhisperModelReleaseCatalogService(updateFeedClient, _whisperModelService);
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
        _externalAudioImportReadinessCoordinator = new ExternalAudioImportReadinessCoordinator();
        _importInboxReconciliationService = new ImportInboxReconciliationService();
        _importInboxIntakeService = new ImportInboxIntakeService(_pathBuilder);
        _autoRecordingContinuityPolicy = new AutoRecordingContinuityPolicy();
        var teamsThirdPartyApiAdapter = new UnavailableTeamsThirdPartyApiAdapter();
        _teamsIntegrationProbeService = new TeamsIntegrationProbeService(
            () => _meetingDetector.DetectBestCandidateAsync(_lifetimeCts.Token),
            teamsThirdPartyApiAdapter);
        _teamsDetectionArbitrator = new TeamsDetectionArbitrator(
            teamsThirdPartyApiAdapter);
        _summarySecretStore = FileSummarySecretStore.CreateDefault();
        _summaryProviderHttpClient = new HttpClient();
        var summaryChatClient = new SummaryChatClient(_summaryProviderHttpClient);
        _summaryModelCatalogClient = new ModelProxyClient(_summaryProviderHttpClient);
        _summaryProviderValidationService = new SummaryProviderValidationService(
            summaryChatClient,
            _summaryModelCatalogClient);
        _publishedMeetingSummaryService = new PublishedMeetingSummaryService(
            new MeetingSummarizationProvider(_summarySecretStore, summaryChatClient, _summaryModelCatalogClient),
            _manifestStore);
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
        _processingQueue.WorkCompleted += ProcessingQueue_OnWorkCompleted;

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
            MeetingsPresetComboBox.DisplayMemberPath = nameof(SelectionOption<MeetingsViewPreset>.Label);
            MeetingsPresetComboBox.SelectedValuePath = nameof(SelectionOption<MeetingsViewPreset>.Value);
            MeetingsPresetComboBox.ItemsSource = new[]
            {
                new SelectionOption<MeetingsViewPreset>(MeetingsViewPreset.Recent, "Recent"),
                new SelectionOption<MeetingsViewPreset>(MeetingsViewPreset.NeedsAttention, "Needs Attention"),
                new SelectionOption<MeetingsViewPreset>(MeetingsViewPreset.Processing, "Processing"),
                new SelectionOption<MeetingsViewPreset>(MeetingsViewPreset.Archived, "Archived"),
                new SelectionOption<MeetingsViewPreset>(MeetingsViewPreset.Custom, "Custom"),
            };

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
            _logger.Log($"Startup processing failed: {exception}");
            AppendActivity("Startup processing did not finish.");
        }
    }

    private void ScheduleStartupWarmup()
    {
        if (_isStartupWarmupQueued || IsShutdownRequested)
        {
            return;
        }

        var callbackIntent = TryBeginCallbackIntent("startup-warmup", "startup-warmup");
        if (callbackIntent is null)
        {
            return;
        }

        _isStartupWarmupQueued = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunStartupWarmupAsync(callbackIntent)),
            DispatcherPriority.Background);
    }

    private async Task RunStartupWarmupAsync(CallbackIntent callbackIntent)
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
            await RefreshModelProxySummaryModelsAsync(manual: false, _lifetimeCts.Token);
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "startup warmup");
            ScheduleDeferredStartupMaintenance();
        }
        catch (OperationCanceledException)
        {
            AppendActivity("Startup warmup was canceled during shutdown.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Startup warmup failed: {exception}");
            AppendActivity("Startup warmup did not finish.");
        }
        finally
        {
            if (!IsShutdownRequested)
            {
                EnsureInteractiveTimersStarted();
            }

            _isStartupWarmupQueued = false;
            CompleteCallbackIntent(callbackIntent);
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

        var callbackIntent = TryBeginCallbackIntent("startup-maintenance", "deferred-startup-maintenance");
        if (callbackIntent is null)
        {
            return;
        }

        _isDeferredStartupMaintenanceQueued = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunDeferredStartupMaintenanceAsync(callbackIntent)),
            DispatcherPriority.Background);
    }

    private async Task RunDeferredStartupMaintenanceAsync(CallbackIntent callbackIntent)
    {
        try
        {
            if (IsShutdownRequested)
            {
                return;
            }

            await _processingQueue.ResumePendingSessionsAsync(
                _meetingCleanupWorkLedgerService.GetQueuedManifestPaths(),
                _lifetimeCts.Token);
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Full, "cleanup scheduler startup");
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
            _logger.Log($"Deferred startup maintenance failed: {exception}");
            AppendActivity("Deferred startup maintenance did not finish.");
        }
        finally
        {
            _isDeferredStartupMaintenanceQueued = false;
            CompleteCallbackIntent(callbackIntent);
        }
    }

    internal async Task ResumePendingProcessingAfterMaintenanceAsync(CancellationToken cancellationToken)
    {
        var callbackIntent = TryBeginCallbackIntent("startup-recovery", "published-repair-resume");
        if (callbackIntent is null)
        {
            return;
        }

        try
        {
            await _processingQueue.ResumePendingSessionsAsync(
                _meetingCleanupWorkLedgerService.GetQueuedManifestPaths(),
                cancellationToken);
        }
        finally
        {
            CompleteCallbackIntent(callbackIntent);
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

    private async Task RunDeferredMeetingsRefreshAsync(CallbackIntent callbackIntent)
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
            _callbackIntentDispatcher.Complete(callbackIntent);
            _isDeferredMeetingsRefreshQueued = false;
            UpdateMeetingsRefreshStateText();
            SchedulePendingMeetingsRefreshIfReady();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        PersistCallbackTraceForRecovery();
        _detectionTimer.Stop();
        _audioGraphTimer.Stop();
        _updateTimer.Stop();
        _processingQueueStatusTimer.Stop();
        _liveConfig.Changed -= LiveConfig_OnChanged;
        _processingQueue.StatusChanged -= ProcessingQueue_OnStatusChanged;
        _processingQueue.WorkCompleted -= ProcessingQueue_OnWorkCompleted;
        _processingQueue.Dispose();
        _summaryProviderHttpClient.Dispose();
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

    private void PersistCallbackTraceForRecovery()
    {
        try
        {
            var path = Path.Combine(AppDataPaths.GetAppRoot(), "callback-intent-trace.json");
            new CallbackIntentTraceStore(path)
                .SaveAsync(_callbackIntentDispatcher.Snapshot(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            _logger.Log($"Could not persist bounded callback trace during shutdown: {exception.GetType().Name}.");
        }
    }

    private CallbackIntent? TryBeginRecordingTransitionCallback(string edge = "manual-recording-transition")
    {
        return TryBeginCallbackIntent("recording-transition", edge);
    }

    private CallbackIntent? TryBeginCallbackIntent(string key, string edge)
    {
        var intent = new CallbackIntent(
            key,
            "main-window",
            edge,
            ++_callbackIntentRevision);
        var outcome = _callbackIntentDispatcher.Enqueue(intent);
        if (outcome == CallbackIntentOutcome.Accepted &&
            _callbackIntentDispatcher.TryBegin(key, out var activeIntent))
        {
            PersistCallbackTraceForRecovery();
            return activeIntent;
        }

        PersistCallbackTraceForRecovery();
        _logger.Log($"Callback was not scheduled. key='{key}', edge='{edge}', outcome='{outcome}'.");
        return null;
    }

    private void CompleteRecordingTransitionCallback(CallbackIntent callbackIntent)
    {
        CompleteCallbackIntent(callbackIntent);
    }

    private void CompleteCallbackIntent(CallbackIntent callbackIntent)
    {
        _callbackIntentDispatcher.Complete(callbackIntent);
        PersistCallbackTraceForRecovery();
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

    private void ProcessingQueue_OnWorkCompleted(ProcessingWorkCompletion completion)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (completion.Priority == ProcessingWorkPriority.Cleanup)
            {
                _meetingCleanupWorkLedgerService.RecordCompletionForManifest(
                    completion.ManifestPath,
                    completion.Succeeded,
                    completion.Detail);
            }
            UpdateMeetingCleanupReviewBanner();
            TryScheduleMeetingCleanupAutomaticBatchRefill(DateTimeOffset.UtcNow);
        }), DispatcherPriority.Background);
    }

    private void ApplyProcessingQueueStatusSnapshot(ProcessingQueueStatusSnapshot snapshot)
    {
        var previousSnapshot = _latestProcessingQueueStatusSnapshot;
        _latestProcessingQueueStatusSnapshot = snapshot;
        if (snapshot.RunState == ProcessingQueueRunState.Processing &&
            !string.IsNullOrWhiteSpace(snapshot.CurrentManifestPath))
        {
            _meetingCleanupWorkLedgerService.RecordProcessingForManifest(snapshot.CurrentManifestPath);
        }
        UpdateProcessingQueueStatusUi();
        UpdateProcessingQueueStatusTimerState();
        UpdateUpdateActionButtons();
        if (previousSnapshot.RunState != ProcessingQueueRunState.Idle &&
            snapshot.RunState == ProcessingQueueRunState.Idle)
        {
            _ = TryInstallAvailableUpdateIfIdleAsync("processing idle", _lifetimeCts.Token);
        }

        UpdateDashboardReadiness();
    }

    private void ProcessingQueueStatusTimer_OnTick(object? sender, EventArgs e)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        ApplyProcessingQueueStatusSnapshot(_processingQueue.GetStatusSnapshot());
        UpdateProcessingQueueStatusUi();
        UpdateProcessingQueueStatusTimerState();
    }

    private void UpdateProcessingQueueStatusTimerState()
    {
        var shouldRun = _latestProcessingQueueStatusSnapshot.TotalRemainingCount > 0 ||
                        _latestProcessingQueueStatusSnapshot.RunState is ProcessingQueueRunState.Processing or ProcessingQueueRunState.Paused or ProcessingQueueRunState.Queued ||
                        BuildBacklogRecoveryMetadata() is not null;
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
        var recovery = BuildBacklogRecoveryMetadata();
        var backlogExperience = _backlogExperienceResolver.Resolve(
            new BacklogExperienceInput(
                _latestProcessingQueueStatusSnapshot,
                persistedBacklog,
                recovery,
                nowUtc));
        _currentBacklogExperienceState = backlogExperience;
        _currentBacklogRecoveryMetadata = recovery;
        var headerState = MainWindowInteractionLogic.BuildProcessingQueueHeaderState(
            _latestProcessingQueueStatusSnapshot,
            persistedBacklog,
            nowUtc,
            recovery);
        HeaderQueueStatusBorder.Visibility = headerState.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        HeaderQueueStatusLabelTextBlock.Text = headerState.Label;
        HeaderQueueStatusDetailTextBlock.Text = headerState.Detail;

        var stripState = MainWindowInteractionLogic.BuildMeetingsProcessingStripState(
            _latestProcessingQueueStatusSnapshot,
            _currentMeetingsRefreshStateText,
            persistedBacklog,
            nowUtc,
            recovery);
        MeetingsProcessingStatusBorder.Visibility = stripState.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        MeetingsProcessingStatusLine1TextBlock.Text = stripState.Line1;
        MeetingsProcessingStatusLine1TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line1) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsProcessingStatusLine2TextBlock.Text = stripState.Line2;
        MeetingsProcessingStatusLine2TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line2) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsProcessingStatusLine3TextBlock.Text = stripState.Line3;
        MeetingsProcessingStatusLine3TextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.Line3) ? Visibility.Collapsed : Visibility.Visible;
        MeetingsRefreshStateTextBlock.Text = stripState.SecondaryText ?? string.Empty;
        MeetingsRefreshStateTextBlock.Visibility = string.IsNullOrWhiteSpace(stripState.SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
        var hasBacklog = _latestProcessingQueueStatusSnapshot.TotalRemainingCount > 0 ||
                         persistedBacklog is not null;
        BacklogExperienceActionButton.Content = backlogExperience.ActionLabel ?? string.Empty;
        BacklogExperienceActionButton.ToolTip = backlogExperience.FailureReason ?? backlogExperience.Detail;
        BacklogExperienceActionButton.Visibility = backlogExperience.HasAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        BacklogExperienceActionButton.IsEnabled = backlogExperience.HasAction && !IsMeetingActionInProgress();
        MeetingsProcessingActionsPanel.Visibility = hasBacklog || backlogExperience.HasAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        RushBacklogButton.Content = _isRushingBacklog ? "Rushing..." : "Rush Backlog...";
        RushBacklogButton.IsEnabled = hasBacklog && !_isRushingBacklog && !IsMeetingActionInProgress();
        UpdateOpenMeetingDetailAsapStatus();
    }

    private BacklogRecoveryMetadata? BuildBacklogRecoveryMetadata()
    {
        var row = _allMeetingRows
            .Where(candidate => candidate.Source.ManifestState == SessionState.Failed ||
                candidate.PrimaryRecommendation.Kind is
                    MeetingPrimaryRecommendationKind.RecoverTranscript or
                    MeetingPrimaryRecommendationKind.ReviewMissingTranscript or
                    MeetingPrimaryRecommendationKind.Blocked or
                    MeetingPrimaryRecommendationKind.RepairSpeakerLabels)
            .OrderByDescending(candidate => candidate.Source.ManifestState == SessionState.Failed)
            .ThenByDescending(candidate => candidate.PrimaryRecommendation.Severity)
            .ThenBy(candidate => candidate.Source.Stem, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return new BacklogRecoveryMetadata(
            row.Source.Stem,
            row.Title,
            row.Source.ManifestState == SessionState.Failed,
            row.PrimaryRecommendation.Kind,
            row.PrimaryRecommendation.ActionTarget,
            row.PrimaryRecommendation.Reason,
            row.CanRegenerateTranscript);
    }

    private void UpdateOpenMeetingDetailAsapStatus()
    {
        if (_meetingDetailWindow is null || string.IsNullOrWhiteSpace(_openMeetingDetailStem) ||
            _latestProcessingQueueStatusSnapshot.RushRequest is not { LifecycleText: { Length: > 0 } lifecycleText } rushRequest)
        {
            return;
        }

        var row = FindMeetingRowByStem(_openMeetingDetailStem);
        if (row is not null &&
            string.Equals(row.Source.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal))
        {
            _meetingDetailWindow.SetMaintenanceStatus(
                $"{lifecycleText}. Clear ASAP releases only this meeting's future priority; it does not cancel current work.");
        }
    }

    private async void BacklogExperienceActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsMeetingActionInProgress() || _currentBacklogExperienceState is not { HasAction: true } state)
        {
            return;
        }

        switch (state.ActionTarget)
        {
            case MeetingRecommendationActionTarget.SettingsSetup:
                OpenSettingsSurface(SettingsWindowSection.Setup);
                return;
            case MeetingRecommendationActionTarget.CheckAgain:
                MeetingWorkspaceStatusTextBlock.Text = "Refreshing backlog status...";
                await RefreshMeetingListAsync();
                return;
            case MeetingRecommendationActionTarget.MeetingDetails:
                var stem = _currentBacklogRecoveryMetadata?.MeetingStem;
                var row = _allMeetingRows.FirstOrDefault(candidate =>
                    string.Equals(candidate.Source.Stem, stem, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    MeetingWorkspaceStatusTextBlock.Text = "Refresh meeting status before reviewing this recovery step.";
                    return;
                }

                MeetingsDataGrid.SelectedItem = row;
                OpenMeetingDetails(row);
                return;
            case MeetingRecommendationActionTarget.None:
            case MeetingRecommendationActionTarget.CleanupReview:
            default:
                return;
        }
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

        var callbackIntent = TryBeginRecordingTransitionCallback();
        if (callbackIntent is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _isRecordingTransitionInProgress = true;
        UpdateUi("Starting recording...", DetectionTextBlock.Text);

        try
        {
            _recentAutoStopContext = null;
            _manualStopSuppressionContext = null;
            _recordingStorageBackoffUntilUtc = null;
            ClearAutoStopVisualState();
            var observedAtUtc = DateTimeOffset.UtcNow;
            var playbackProvenance = TryCreateTeamsRecordingPlaybackProvenance(_lastObservedDetectionDecision, observedAtUtc);
            await _recordingCoordinator.StartAsync(
                playbackProvenance is null ? MeetingPlatform.Manual : MeetingPlatform.Teams,
                playbackProvenance is null
                    ? $"Manual session {DateTimeOffset.Now:yyyy-MM-dd HH:mm}"
                    : _lastObservedDetectionDecision!.SessionTitle,
                playbackProvenance is null ? Array.Empty<DetectionSignal>() : _lastObservedDetectionDecision!.Signals,
                autoStarted: false,
                detectedAudioSource: playbackProvenance is null ? null : _lastObservedDetectionDecision!.DetectedAudioSource,
                teamsRecordingPlayback: playbackProvenance);

            UpdateCurrentMeetingEditor();
            UpdateUi("Recording in progress.", "Manual recording started.");
            UpdateAudioCaptureGraph();
            AppendActivity(playbackProvenance is null
                ? "Manual recording started."
                : "Manual recording started for detected Teams recording playback.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Start recording failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.StartRecording,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            UpdateUi("Unable to start recording.", DetectionTextBlock.Text);
        }
        finally
        {
            CompleteRecordingTransitionCallback(callbackIntent);
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

        var callbackIntent = TryBeginRecordingTransitionCallback();
        if (callbackIntent is null)
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
        _continuityGraceReceipt = null;
        try
        {
            await StopCurrentRecordingAsync("Manual stop requested.");
        }
        finally
        {
            CompleteRecordingTransitionCallback(callbackIntent);
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
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveRoute("updates"));
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
        var callbackIntent = new CallbackIntent(
            "meetings-refresh",
            "main-window",
            "deferred-meetings-refresh",
            ++_callbackIntentRevision);
        var callbackOutcome = _callbackIntentDispatcher.Enqueue(callbackIntent);
        if (callbackOutcome is CallbackIntentOutcome.DeclinedCycle or CallbackIntentOutcome.DeclinedOverload ||
            !_callbackIntentDispatcher.TryBegin("meetings-refresh", out var scheduledIntent))
        {
            _isDeferredMeetingsRefreshQueued = false;
            PersistCallbackTraceForRecovery();
            _logger.Log($"Deferred meeting refresh callback was not scheduled. outcome='{callbackOutcome}'.");
            UpdateMeetingsRefreshStateText();
            return;
        }
        UpdateMeetingsRefreshStateText();
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = RunDeferredMeetingsRefreshAsync(scheduledIntent!)),
            DispatcherPriority.Background);
    }

    private void HeaderSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.Recording);
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
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigMicCaptureCheckBox"));
    }

    private void OpenAutoDetectSettingsFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigAutoDetectCheckBox"));
    }

    private void OpenMeetingFilesSettingsFromHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigAudioOutputDirTextBox"));
    }

    private void HeaderShellStatusActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: { } rawTarget })
        {
            return;
        }

        switch (rawTarget)
        {
            case HomeCommandCenterTarget commandCenterTarget:
                OpenHomeCommandCenterTarget(commandCenterTarget);
                break;
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

    private void OpenHomeCommandCenterTarget(HomeCommandCenterTarget target)
    {
        switch (target)
        {
            case HomeCommandCenterTarget.SettingsSetup:
                OpenSettingsSurface(SettingsWindowSection.Setup);
                break;
            case HomeCommandCenterTarget.SettingsRecording:
                OpenSettingsSurface(SettingsWindowSection.Recording);
                break;
            case HomeCommandCenterTarget.SettingsFilesAndUpdates:
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigAudioOutputDirTextBox"));
                break;
            case HomeCommandCenterTarget.SettingsUpdates:
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveRoute("updates"));
                break;
            case HomeCommandCenterTarget.SettingsSummaries:
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigSummaryGenerationEnabledCheckBox"));
                break;
            case HomeCommandCenterTarget.SettingsAdvanced:
                OpenSettingsSurface(SettingsWindowSection.Advanced);
                break;
            case HomeCommandCenterTarget.Meetings:
                MainTabControl.SelectedItem = MeetingsTabItem;
                break;
            case HomeCommandCenterTarget.None:
            default:
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
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveRoute("updates"));
                break;
            case ShellStatusTarget.SettingsGeneral:
                OpenSettingsSurface(SettingsWindowSection.Recording);
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
        OpenSettingsSurface(new SettingsNavigationTarget(section));
    }

    private void OpenSettingsSurface(SettingsNavigationTarget target)
    {
        EnsureSettingsSectionsArranged();
        if (!target.IsKnownRoute)
        {
            _logger.Log("Ignored an unknown Settings route and opened Recording without changing configuration.");
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsHostWindow(MeetingRecorderProductModule.Instance.GetSettingsSections())
            {
                Owner = this,
            };
            _settingsWindow.SectionRequested += sectionId =>
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveRoute(sectionId));
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

        _settingsWindow.NavigateTo(GetSettingsSectionId(target.Section));
        _settingsWindow.SetFooterStatus(ConfigSaveStatusTextBlock.Text);
        UpdateConfigActionState();
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
        FocusSettingsTarget(target);
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

    private void EnsureSettingsSectionsArranged()
    {
        if (_settingsSectionsArranged)
        {
            return;
        }

        MoveSettingsChild(SettingsProcessingContentPanel, SettingsProcessingAccelerationPanel);
        MoveSettingsChild(SettingsProcessingContentPanel, SettingsProcessingVoiceProfilesPanel);
        MoveSettingsChild(SettingsProcessingContentPanel, SettingsProcessingWorkPlanPanel);
        MoveSettingsChild(SettingsSummariesContentPanelHost, SettingsSummariesContentPanel);
        MoveSettingsChild(SettingsAdvancedContentPanel, SettingsAdvancedTranscriptionProviderPanel);
        MoveSettingsChild(SettingsAdvancedContentPanel, SettingsAdvancedDiarizationProviderPanel);
        MoveSettingsChild(SettingsRecordingContentPanel, SettingsRecordingAssistancePanel);
        MoveSettingsChildren(SettingsFilesAndUpdatesContentPanel, SettingsUpdatesContentPanel);
        _settingsSectionsArranged = true;
    }

    private static void MoveSettingsChild(Panel destination, UIElement child)
    {
        var parent = LogicalTreeHelper.GetParent(child) ?? VisualTreeHelper.GetParent(child);
        if (parent is Panel source)
        {
            source.Children.Remove(child);
        }
        else if (parent is ContentControl contentControl)
        {
            contentControl.Content = null;
        }

        destination.Children.Add(child);
    }

    private static void MoveSettingsChildren(Panel destination, Panel source)
    {
        foreach (var child in source.Children.Cast<UIElement>().ToArray())
        {
            source.Children.Remove(child);
            destination.Children.Add(child);
        }
    }

    private void FocusSettingsTarget(SettingsNavigationTarget target)
    {
        SettingsSetupSectionPanel.Visibility = target.Section == SettingsWindowSection.Setup ? Visibility.Visible : Visibility.Collapsed;
        SettingsRecordingSectionPanel.Visibility = target.Section == SettingsWindowSection.Recording ? Visibility.Visible : Visibility.Collapsed;
        SettingsProcessingSectionPanel.Visibility = target.Section == SettingsWindowSection.Processing ? Visibility.Visible : Visibility.Collapsed;
        SettingsSummariesSectionPanel.Visibility = target.Section == SettingsWindowSection.Summaries ? Visibility.Visible : Visibility.Collapsed;
        SettingsFilesAndUpdatesSectionPanel.Visibility = target.Section == SettingsWindowSection.FilesAndUpdates ? Visibility.Visible : Visibility.Collapsed;
        SettingsUpdatesSectionPanel.Visibility = Visibility.Collapsed;
        SettingsAdvancedSectionPanel.Visibility = target.Section == SettingsWindowSection.Advanced ? Visibility.Visible : Visibility.Collapsed;

        _ = Dispatcher.BeginInvoke(() =>
        {
            var requestedControl = target.ControlId is null
                ? null
                : FindName(target.ControlId) as Control;
            if (requestedControl is { IsVisible: true })
            {
                requestedControl.BringIntoView();
                requestedControl.Focus();
                return;
            }

            switch (target.Section)
            {
                case SettingsWindowSection.Setup:
                    TranscriptionOverviewPrimaryButton.Focus();
                    break;
                case SettingsWindowSection.Processing:
                    ConfigDiarizationGpuAccelerationCheckBox.Focus();
                    break;
                case SettingsWindowSection.Summaries:
                    ConfigSummaryGenerationEnabledCheckBox.Focus();
                    break;
                case SettingsWindowSection.FilesAndUpdates:
                    ConfigAudioOutputDirTextBox.Focus();
                    break;
                case SettingsWindowSection.Advanced:
                    ConfigWorkDirTextBox.Focus();
                    break;
                case SettingsWindowSection.Recording:
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
            SettingsWindowSection.Recording => "recording",
            SettingsWindowSection.Processing => "processing",
            SettingsWindowSection.Summaries => "summaries",
            SettingsWindowSection.FilesAndUpdates => "files-and-updates",
            SettingsWindowSection.Advanced => "advanced",
            _ => "recording",
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
            if (TryCreateTeamsRecordingPlaybackProvenance(decision, nowUtc) is not null)
            {
                DetectionTextBlock.Text += " Teams recording playback detected; fragments will merge after the player closes.";
            }
            UpdateDetectedAudioSourceSurface(decision);
            UpdateCurrentMeetingTitleStatus();

            if (!_recordingCoordinator.IsRecording &&
                HasReadyTranscriptionModel() &&
                _liveConfig.Current.AutoDetectEnabled &&
                decision is not null)
            {
                var shouldAutoStartQuietTeamsMeeting = ShouldAutoStartQuietTeamsMeeting(decision, nowUtc);
                var shouldAutoStartQuietGoogleMeet = ShouldAutoStartQuietGoogleMeet(decision, nowUtc);
                var shouldRecoverFromRecentAutoStop = _autoRecordingContinuityPolicy.ShouldRecoverFromRecentAutoStop(
                    decision,
                    _recentAutoStopContext,
                    nowUtc);
                if (_liveConfig.Current.MeetingIdentityContinuityEnabled &&
                    _recentAutoStopContext is { } recentAutoStop)
                {
                    var recoveryDecision = _continuityCutoverPolicy.Evaluate(new ContinuityCutoverInput(
                        ContinuityCutoverMode.Matcher,
                        recentAutoStop.SessionRevision,
                        recentAutoStop.IdentitySnapshot,
                        _manifestStore.CreateIdentitySnapshotForComparison(
                            decision.Platform,
                            decision.SessionTitle,
                            decision.DetectedAudioSource,
                            nowUtc),
                        MeetingIdentityVerdict.Unknown,
                        recentAutoStop.WasManuallyStopped,
                        ExistingGrace: null,
                        NowUtc: nowUtc));
                    shouldRecoverFromRecentAutoStop = recoveryDecision.Action == ContinuityLifecycleAction.Continue;
                }
                var manualStopSuppressionDisposition = _autoRecordingContinuityPolicy.GetManualStopSuppressionDisposition(
                    decision,
                    _manualStopSuppressionContext);
                if (manualStopSuppressionDisposition == ManualStopSuppressionDisposition.ReleaseSuppression)
                {
                    _manualStopSuppressionContext = null;
                }

                var shouldSuppressManualRestart = manualStopSuppressionDisposition == ManualStopSuppressionDisposition.SuppressAutoStart;
                var isRecordingStorageBackoffActive = _recordingStorageBackoffUntilUtc is { } storageBackoffUntilUtc &&
                    nowUtc < storageBackoffUntilUtc;
                if (!shouldSuppressManualRestart &&
                    !isRecordingStorageBackoffActive &&
                    (decision.ShouldStart || shouldAutoStartQuietTeamsMeeting || shouldAutoStartQuietGoogleMeet || shouldRecoverFromRecentAutoStop))
                {
                    var callbackIntent = TryBeginRecordingTransitionCallback("automatic-start");
                    if (callbackIntent is null)
                    {
                        _logger.Log("Automatic recording start was deferred by the callback dispatcher.");
                    }
                    else
                    {
                        _isRecordingTransitionInProgress = true;
                        try
                        {
                            ClearAutoStopVisualState();
                            await _recordingCoordinator.StartAsync(
                                decision.Platform,
                                decision.SessionTitle,
                                decision.Signals,
                                autoStarted: true,
                                decision.DetectedAudioSource,
                                teamsRecordingPlayback: TryCreateTeamsRecordingPlaybackProvenance(decision, nowUtc));
                            _lastPositiveDetectionUtc = nowUtc;
                            _recentAutoStopContext = null;
                            _manualStopSuppressionContext = null;
                            _recordingStorageBackoffUntilUtc = null;
                            _lastAutoStopFingerprint = null;
                            UpdateCurrentMeetingEditor();
                            UpdateUi("Recording in progress.", DetectionTextBlock.Text);
                            if (shouldRecoverFromRecentAutoStop)
                            {
                                RecordRecentAutoStopShadow(decision, nowUtc);
                            }
                            AppendActivity(
                                shouldRecoverFromRecentAutoStop && !decision.ShouldStart
                                    ? $"Resumed recording for '{decision.SessionTitle}' after a recent auto-stop."
                                    : shouldAutoStartQuietTeamsMeeting
                                        ? $"Auto-started recording for quiet Teams meeting '{decision.SessionTitle}' after sustained meeting detection."
                                        : shouldAutoStartQuietGoogleMeet
                                            ? $"Auto-started recording for quiet Google Meet '{decision.SessionTitle}' after sustained Meet detection."
                                            : $"Auto-started recording for '{decision.SessionTitle}'.");
                        }
                        finally
                        {
                            CompleteRecordingTransitionCallback(callbackIntent);
                            _isRecordingTransitionInProgress = false;
                            UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
                        }
                    }
                }
            }
            else
            {
                ResetQuietTeamsAutoStartCandidate();
                ResetQuietGoogleMeetAutoStartCandidate();
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

            var playbackProvenance = TryCreateTeamsRecordingPlaybackProvenance(decision, nowUtc);
            if (playbackProvenance is not null)
            {
                await _recordingCoordinator.UpdateTeamsRecordingPlaybackAsync(playbackProvenance, _lifetimeCts.Token);
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
            if (activeMeetingManagedSession is not null && _liveConfig.Current.MeetingIdentityContinuityEnabled)
            {
                var legacyVerdict = shouldRefreshLastPositiveSignal
                    ? MeetingIdentityVerdict.SameMeeting
                    : MeetingIdentityVerdict.Unknown;
                var cutover = _continuityCutoverPolicy.Evaluate(new ContinuityCutoverInput(
                    ContinuityCutoverMode.Matcher,
                    (int)(activeMeetingManagedSession.Manifest.StartedAtUtc.UtcDateTime.Ticks % int.MaxValue),
                    _manifestStore.GetIdentitySnapshotForComparison(activeMeetingManagedSession.Manifest),
                    decision is null
                        ? null
                        : _manifestStore.CreateIdentitySnapshotForComparison(
                            decision.Platform,
                            decision.SessionTitle,
                            decision.DetectedAudioSource,
                            nowUtc),
                    legacyVerdict,
                    IsManualStop: _manualStopSuppressionContext is not null,
                    _continuityGraceReceipt,
                    nowUtc));
                _continuityGraceReceipt = cutover.GraceReceipt;
                shouldRefreshLastPositiveSignal = cutover.Action is ContinuityLifecycleAction.Continue or ContinuityLifecycleAction.Grace;
                shouldClearAutoStopCountdown = shouldRefreshLastPositiveSignal;
            }
            else
            {
                _continuityGraceReceipt = null;
            }
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
                        var callbackIntent = TryBeginRecordingTransitionCallback("automatic-stop");
                        if (callbackIntent is null)
                        {
                            SetAutoStopCountdown(TimeSpan.FromSeconds(1));
                            AppendAutoStopStatus("Auto-stop deferred while a recording transition is active.");
                        }
                        else
                        {
                            _recentAutoStopContext = new RecentAutoStopContext(
                                activePlatform,
                                nowUtc,
                                _manifestStore.GetIdentitySnapshotForComparison(activeMeetingManagedSession.Manifest),
                                (int)(activeMeetingManagedSession.Manifest.StartedAtUtc.UtcDateTime.Ticks % int.MaxValue));
                            _continuityGraceReceipt = null;
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
                                CompleteRecordingTransitionCallback(callbackIntent);
                                _isRecordingTransitionInProgress = false;
                                _isAutoStopTransitionInProgress = false;
                                ResumeDetectionAfterStopTransitionIfNeeded();
                                UpdateUi(StatusTextBlock.Text, DetectionTextBlock.Text);
                            }
                            _lastPositiveDetectionUtc = null;
                            _lastAutoStopFingerprint = null;
                        }
                    }
                    else
                    {
                        SetAutoStopCountdown(remaining);
                        AppendAutoStopStatus(
                            $"Auto-stop countdown active: {Math.Ceiling(remaining.TotalSeconds)} seconds remaining. Last strong meeting signal was at {lastPositiveUtc.Value:O}.");
                    }
                }
            }

            if (activeMeetingManagedSession is not null)
            {
                RecordContinuationShadow(
                    activeMeetingManagedSession,
                    decision,
                    shouldRefreshLastPositiveSignal ? MeetingIdentityVerdict.SameMeeting : MeetingIdentityVerdict.Unknown,
                    shouldRefreshLastPositiveSignal ? "legacy-continuation" : "legacy-continuation-unknown",
                    nowUtc);
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
        catch (InsufficientRecordingStorageException exception)
        {
            _recordingStorageBackoffUntilUtc = DateTimeOffset.UtcNow.Add(RecordingStorageAutoStartBackoff);
            _logger.Log($"Recording storage unavailable: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.StartRecording,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText);
            UpdateUi("Recording paused until disk space is available.", DetectionTextBlock.Text);
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting detection failed: {exception}");
            AppendActivity("Meeting detection did not finish this cycle.");
        }
        finally
        {
            FinishDetectionCycle();
        }
    }

    private bool ShouldAutoStartQuietTeamsMeeting(DetectionDecision? decision, DateTimeOffset nowUtc)
    {
        if (decision is null ||
            !_autoRecordingContinuityPolicy.IsSpecificTeamsAutoStartObservation(decision))
        {
            ResetQuietTeamsAutoStartCandidate();
            return false;
        }

        var teamsCandidate = decision;
        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(teamsCandidate.SessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ResetQuietTeamsAutoStartCandidate();
            return false;
        }

        var fingerprint = $"{teamsCandidate.Platform}|{normalizedTitle}";
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

        var shouldAutoStartQuiet =
            _autoRecordingContinuityPolicy.ShouldAutoStartQuietSpecificTeamsMeeting(
                teamsCandidate,
                _quietTeamsAutoStartFirstObservedUtc.Value,
                nowUtc);
        var shouldAutoStartAmbiguousActive =
            _autoRecordingContinuityPolicy.ShouldAutoStartAmbiguousActiveSpecificTeamsMeeting(
                teamsCandidate,
                _quietTeamsAutoStartFirstObservedUtc.Value,
                nowUtc);
        if (!shouldAutoStartQuiet && !shouldAutoStartAmbiguousActive)
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

    private bool ShouldAutoStartQuietGoogleMeet(DetectionDecision? decision, DateTimeOffset nowUtc)
    {
        if (decision is null ||
            !_autoRecordingContinuityPolicy.IsQuietSpecificGoogleMeetCandidate(decision))
        {
            ResetQuietGoogleMeetAutoStartCandidate();
            return false;
        }

        var quietCandidate = decision;
        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(quietCandidate.SessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ResetQuietGoogleMeetAutoStartCandidate();
            return false;
        }

        var fingerprint = $"{quietCandidate.Platform}|{normalizedTitle}";
        if (!string.Equals(_quietGoogleMeetAutoStartFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _quietGoogleMeetAutoStartFingerprint = fingerprint;
            _quietGoogleMeetAutoStartFirstObservedUtc = nowUtc;
        }

        if (!_quietGoogleMeetAutoStartFirstObservedUtc.HasValue)
        {
            _quietGoogleMeetAutoStartFirstObservedUtc = nowUtc;
            return false;
        }

        if (!_autoRecordingContinuityPolicy.ShouldAutoStartQuietSpecificGoogleMeet(
                quietCandidate,
                _quietGoogleMeetAutoStartFirstObservedUtc.Value,
                nowUtc))
        {
            return false;
        }

        ResetQuietGoogleMeetAutoStartCandidate();
        return true;
    }

    private void ResetQuietGoogleMeetAutoStartCandidate()
    {
        _quietGoogleMeetAutoStartFingerprint = null;
        _quietGoogleMeetAutoStartFirstObservedUtc = null;
    }

    private async Task<bool> TryRollOverManagedSessionAsync(
        ActiveRecordingSession activeSession,
        DetectionDecision decision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var callbackIntent = TryBeginRecordingTransitionCallback("automatic-rollover");
        if (callbackIntent is null)
        {
            _logger.Log("Automatic meeting rollover was deferred by the callback dispatcher.");
            return false;
        }

        var previousPlatform = activeSession.Manifest.Platform;
        var previousTitle = activeSession.Manifest.DetectedTitle;
        _isRecordingTransitionInProgress = true;
        _isAutoStopTransitionInProgress = false;
        ClearAutoStopVisualState();
        PauseDetectionDuringStopTransition();
        UpdateUi("Switching meetings...", DetectionTextBlock.Text);

        try
        {
            await ApplyPendingCurrentMetadataAsync(
                cancellationToken,
                applyDeferredReclassification: false);
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
                cancellationToken,
                TryCreateTeamsRecordingPlaybackProvenance(decision, nowUtc));

            _lastPositiveDetectionUtc = nowUtc;
            _recentAutoStopContext = null;
            _continuityGraceReceipt = null;
            _manualStopSuppressionContext = null;
            _recordingStorageBackoffUntilUtc = null;
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
            CompleteRecordingTransitionCallback(callbackIntent);
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

        if (_liveConfig.Current.MeetingIdentityContinuityEnabled)
        {
            var matcherDecision = _continuityCutoverPolicy.Evaluate(new ContinuityCutoverInput(
                ContinuityCutoverMode.Matcher,
                (int)(activeSession.Manifest.StartedAtUtc.UtcDateTime.Ticks % int.MaxValue),
                _manifestStore.GetIdentitySnapshotForComparison(activeSession.Manifest),
                _manifestStore.CreateIdentitySnapshotForComparison(
                    decision.Platform,
                    decision.SessionTitle,
                    decision.DetectedAudioSource,
                    nowUtc),
                MeetingIdentityVerdict.Unknown,
                IsManualStop: _manualStopSuppressionContext is not null,
                _continuityGraceReceipt,
                nowUtc));
            _continuityGraceReceipt = matcherDecision.GraceReceipt;
            if (matcherDecision.Action is not ContinuityLifecycleAction.DifferentMeeting)
            {
                RecordReclassificationShadow(
                    activeSession,
                    decision,
                    matcherDecision.Verdict,
                    matcherDecision.ReasonCode,
                    nowUtc);
                return false;
            }
        }

        var transition = MainWindowInteractionLogic.GetEligibleActiveSessionTransition(
            decision,
            activeSession.Manifest.Platform,
            activeSession.Manifest.DetectedTitle,
            activeSession.MeetingLifecycleManaged,
            _autoRecordingContinuityPolicy);
        if (transition == ActiveSessionTransitionKind.None)
        {
            RecordReclassificationShadow(activeSession, decision, MeetingIdentityVerdict.Unknown, "legacy-no-transition", nowUtc);
            return false;
        }

        if (transition == ActiveSessionTransitionKind.RollOver)
        {
            var rolledOver = await TryRollOverManagedSessionAsync(activeSession, decision, nowUtc, cancellationToken);
            RecordReclassificationShadow(
                activeSession,
                decision,
                rolledOver ? MeetingIdentityVerdict.DifferentMeeting : MeetingIdentityVerdict.Unknown,
                rolledOver ? "legacy-rollover" : "legacy-rollover-unavailable",
                nowUtc);
            return rolledOver;
        }

        if (!_autoRecordingContinuityPolicy.ShouldReclassifyActiveSession(
                decision,
                activeSession.Manifest.Platform,
                activeSession.Manifest.DetectedTitle))
        {
            RecordReclassificationShadow(activeSession, decision, MeetingIdentityVerdict.Unknown, "legacy-reclassify-rejected", nowUtc);
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
            RecordReclassificationShadow(activeSession, decision, MeetingIdentityVerdict.Unknown, "legacy-reclassify-unavailable", nowUtc);
            return false;
        }

        RecordReclassificationShadow(activeSession, decision, MeetingIdentityVerdict.DifferentMeeting, "legacy-reclassified", nowUtc);

        _lastPositiveDetectionUtc = nowUtc;
        _recentAutoStopContext = null;
        _continuityGraceReceipt = null;
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

    private void RecordReclassificationShadow(
        ActiveRecordingSession activeSession,
        DetectionDecision decision,
        MeetingIdentityVerdict legacyVerdict,
        string legacyReasonCode,
        DateTimeOffset nowUtc)
    {
        try
        {
            RecordContinuityShadow(
                ContinuityDecisionBoundary.RolloverOrReclassify,
                activeSession.Manifest.SessionId,
                (int)(activeSession.Manifest.StartedAtUtc.UtcDateTime.Ticks % int.MaxValue),
                _manifestStore.GetIdentitySnapshotForComparison(activeSession.Manifest),
                decision,
                legacyVerdict,
                legacyReasonCode,
                nowUtc);
        }
        catch
        {
            // Shadow evaluation is best-effort and must not change a committed legacy action.
        }
    }

    private void RecordContinuationShadow(
        ActiveRecordingSession activeSession,
        DetectionDecision? decision,
        MeetingIdentityVerdict legacyVerdict,
        string legacyReasonCode,
        DateTimeOffset nowUtc)
    {
        if (decision is null)
        {
            return;
        }

        try
        {
            RecordContinuityShadow(
                ContinuityDecisionBoundary.Continuation,
                activeSession.Manifest.SessionId,
                (int)(activeSession.Manifest.StartedAtUtc.UtcDateTime.Ticks % int.MaxValue),
                _manifestStore.GetIdentitySnapshotForComparison(activeSession.Manifest),
                decision,
                legacyVerdict,
                legacyReasonCode,
                nowUtc);
        }
        catch
        {
            // Shadow evaluation is best-effort and must not change a committed legacy action.
        }
    }

    private void RecordRecentAutoStopShadow(DetectionDecision decision, DateTimeOffset nowUtc)
    {
        try
        {
            RecordContinuityShadow(
                ContinuityDecisionBoundary.RecentAutoStopRecovery,
                $"auto-stop-{nowUtc.UtcDateTime.Ticks:x}",
                0,
                existingIdentity: null,
                decision,
                MeetingIdentityVerdict.SameMeeting,
                "legacy-auto-stop-recovered",
                nowUtc);
        }
        catch
        {
            // Shadow evaluation is best-effort and must not change a committed legacy action.
        }
    }

    private void RecordContinuityShadow(
        ContinuityDecisionBoundary boundary,
        string correlationSource,
        int inputRevision,
        MeetingIdentitySnapshot? existingIdentity,
        DetectionDecision decision,
        MeetingIdentityVerdict legacyVerdict,
        string legacyReasonCode,
        DateTimeOffset nowUtc)
    {
        var receipt = _continuityShadowEngine.Evaluate(
            new ContinuityShadowInput(
                CreateOpaqueContinuityCorrelationId(correlationSource),
                inputRevision,
                boundary,
                existingIdentity,
                _manifestStore.CreateIdentitySnapshotForComparison(
                    decision.Platform,
                    decision.SessionTitle,
                    decision.DetectedAudioSource,
                    nowUtc),
                legacyVerdict,
                legacyReasonCode,
                ContinuityShadowScenarioLabel.Unlabeled),
            nowUtc);
        _continuityShadowMeter.TryRecord(receipt, TimeSpan.FromMilliseconds(25));
    }

    private static string CreateOpaqueContinuityCorrelationId(string sessionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private void UpdateCaptureStatusSurface()
    {
        var loopbackStatus = _recordingCoordinator.GetLoopbackCaptureStatusSnapshot();
        var captureTruth = GetHomeCaptureTruth(loopbackStatus);
        HomeCaptureTruthTextBlock.Text = BuildHomeCaptureTruthText(captureTruth);
        if (!loopbackStatus.IsRecording || loopbackStatus.ActiveSelection is null)
        {
            LoopbackCaptureStatusTextBlock.Text = "Capture status appears here while recording.";
            LoopbackCaptureRecentEventsTextBlock.Text = "Recent loopback events will be saved with the session.";
            if (_isUiReady)
            {
                UpdateDashboardReadiness();
            }

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

        if (_isUiReady)
        {
            UpdateDashboardReadiness();
        }
    }

    private static HomeCaptureTruth GetHomeCaptureTruth(LoopbackCaptureStatusSnapshot loopbackStatus)
    {
        return !loopbackStatus.IsRecording
            ? HomeCaptureTruth.StaticReadiness
            : loopbackStatus.ActiveSelection is null
                ? HomeCaptureTruth.Unavailable
                : loopbackStatus.IsSwapPending
                    ? HomeCaptureTruth.Degraded
                    : loopbackStatus.IsFallbackActive
                        ? HomeCaptureTruth.FallbackOutput
                        : HomeCaptureTruth.LiveOutput;
    }

    private static string BuildHomeCaptureTruthText(HomeCaptureTruth truth)
    {
        return truth switch
        {
            HomeCaptureTruth.LiveOutput => "Live output. Active capture source reported.",
            HomeCaptureTruth.FallbackOutput => "Fallback output. Active fallback source reported.",
            HomeCaptureTruth.Degraded => "Degraded. Capture source is changing.",
            HomeCaptureTruth.Unavailable => "Unavailable. No active capture source is reported.",
            _ => "Static readiness. Capture source is selected when recording starts.",
        };
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
        TryScheduleMeetingCleanupAutomaticBatchRefill(DateTimeOffset.UtcNow);
        if (!_lastMeetingCleanupSchedulerRefreshUtc.HasValue ||
            DateTimeOffset.UtcNow - _lastMeetingCleanupSchedulerRefreshUtc.Value >= TimeSpan.FromMinutes(5))
        {
            _lastMeetingCleanupSchedulerRefreshUtc = DateTimeOffset.UtcNow;
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Full, "cleanup scheduler cadence");
        }
        await RunExternalAudioImportCycleAsync("background timer", _lifetimeCts.Token);
        await RunAutomaticUpdateCycleAsync("background timer", AppUpdateCheckTrigger.Scheduled, _lifetimeCts.Token);
    }

    private void TryScheduleMeetingCleanupAutomaticBatchRefill(DateTimeOffset nowUtc)
    {
        var maximumOutstanding = BackgroundProcessingPolicy.IsOvernightDrainWindowActive(_liveConfig.Current)
            ? MeetingCleanupAutoApplyPlanner.MaxAutomaticFixesPerBatch
            : 1;
        var outstandingCleanupCount = _meetingCleanupWorkLedgerService.GetEntries()
            .Count(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing);
        if (IsAutomaticCleanupSchedulerBlocked() ||
            outstandingCleanupCount >= maximumOutstanding ||
            GetEligibleScheduledIncrementalRecommendations().Count == 0)
        {
            return;
        }

        RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Full, "automatic cleanup batch refill");
    }

    private bool IsAutomaticCleanupSchedulerBlocked()
    {
        return !string.IsNullOrWhiteSpace(GetAutomaticCleanupSchedulerBlockerReason());
    }

    private string? GetAutomaticCleanupSchedulerBlockerReason()
    {
        if (IsShutdownRequested)
        {
            return "Automatic cleanup is stopping because the app is shutting down.";
        }

        if (_recordingCoordinator.IsRecording)
        {
            return "Automatic cleanup is paused by the live recording.";
        }

        if (_isApplyingSafeMeetingCleanupFixes)
        {
            return "Automatic cleanup is waiting for the active cleanup batch.";
        }

        if (_meetingCleanupSchedulerFailureBackoffUntilUtc is { } backoffUntil &&
            DateTimeOffset.UtcNow < backoffUntil)
        {
            return "Automatic cleanup is waiting for the cleanup catalog retry backoff.";
        }

        if (IsUserMeetingMaintenanceInProgress())
        {
            return "Automatic cleanup is waiting for the current user maintenance action.";
        }

        return null;
    }

    private void RecordCleanupSchedulerDispatchDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail) ||
            string.Equals(_lastCleanupSchedulerDispatchDetail, detail, StringComparison.Ordinal))
        {
            return;
        }

        _lastCleanupSchedulerDispatchDetail = detail;
        _logger.Log($"Cleanup scheduler dispatch: {detail}");
    }

    private bool IsUserMeetingMaintenanceInProgress()
    {
        return _isRenamingMeeting ||
               _isRetryingMeeting ||
               _isSuggestingMeetingTitle ||
               _isApplyingSuggestedMeetingTitles ||
               _isUpdatingMeetingProject ||
               _isApplyingSpeakerNames ||
               _isGeneratingMeetingSummary ||
               _isMergingMeetings ||
               _isSplittingMeeting ||
               _isArchivingMeetings ||
               _isUpdatingRushProcessing ||
               _isDeletingMeetings ||
               _isApplyingMeetingCleanupRecommendations ||
               _isDismissingMeetingCleanupRecommendations ||
               _isRushingBacklog ||
               _isQueueingExternalAudioImports;
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

    internal void RequestFatalUiShutdown()
    {
        _skipShutdownUpdateCheck = true;
        AppendActivity("Fatal UI error requested application shutdown.");
        if (_shutdownInProgress)
        {
            return;
        }

        Close();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            AppendActivity("Application shutdown requested.");

            if (!_skipShutdownUpdateCheck)
            {
                using var shutdownUpdateCheckCts = new CancellationTokenSource(ShutdownUpdateCheckTimeout);
                await RunAutomaticUpdateCycleAsync("shutdown", AppUpdateCheckTrigger.Shutdown, shutdownUpdateCheckCts.Token);
            }
            else
            {
                AppendActivity("Skipped shutdown update check after fatal UI error.");
            }

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
            _logger.Log($"Application shutdown failed: {exception}");
            AppendActivity("Application shutdown did not finish cleanly.");
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
            TryScheduleMeetingCleanupAutomaticBatchRefill(DateTimeOffset.UtcNow);
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
            _logger.Log($"Stop recording failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.StopRecording,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Meeting rename failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.RenameMeeting,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Meeting title suggestion failed: {exception}");
            SelectedMeetingStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.SuggestMeetingTitle,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting title suggestion did not finish.");
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
                    _logger.Log($"Suggested meeting-title update failed: {exception}");
                    failureMessages.Add(UserActionCopyResolver.Resolve(
                        UserActionIntent.SuggestMeetingTitle,
                        UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
        await HandleMeetingsWorkspacePreferenceChangedAsync(customized: true);
    }

    private async void MeetingsSortKeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync(customized: true);
    }

    private async void MeetingsSortDirectionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync(customized: true);
    }

    private async void MeetingsGroupKeyComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync(customized: true);
    }

    private async void MeetingsPresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await HandleMeetingsWorkspacePreferenceChangedAsync(customized: false);
    }

    private void MeetingsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMeetingEditor(MeetingsDataGrid.SelectedItem as MeetingListRow);
        UpdateSelectedMeetingInspector(MeetingsDataGrid.SelectedItem as MeetingListRow);
        UpdateMergeMeetingsEditor();
        UpdateMeetingCleanupRecommendationsEditor();
        RefreshOpenMeetingDetailWindow();
    }

    private void MeetingsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetMeetingRowFromSender(e.OriginalSource) is { } row)
        {
            OpenMeetingDetails(row);
            e.Handled = true;
            return;
        }

        OpenMeetingDetailsForSelection();
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
        var deleteCopy = UserActionCopyResolver.Resolve(UserActionIntent.DeleteMeetings);
        MeetingCleanupRecommendationsStatusTextBlock.Text = deleteCopy.ProgressText;

        try
        {
            foreach (var meeting in targetMeetings)
            {
                await _meetingCleanupExecutionService.DeleteMeetingPermanentlyAsync(meeting.Source, _lifetimeCts.Token);
            }

            MeetingCleanupRecommendationsStatusTextBlock.Text = deleteCopy.SuccessText;
            AppendActivity(
                targetMeetings.Length == 1
                    ? $"Permanently deleted published meeting '{targetMeetings[0].Title}'."
                    : $"Permanently deleted {targetMeetings.Length} published meetings.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Permanent meeting deletion failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DeleteMeetings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Permanent meeting deletion did not finish.");
        }
        finally
        {
            _isDeletingMeetings = false;
            UpdateMeetingActionState();
        }
    }

    private async void MeetingRecommendedActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MeetingListRow row })
        {
            return;
        }

        await OpenMeetingRecommendationAsync(row);
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
        MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ApplyCleanupRecommendations).ProgressText;

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(safeRecommendations, "safe-fixes", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations).SuccessText;
            AppendActivity($"Applied {safeRecommendations.Count} safe cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Safe cleanup recommendation application failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Safe cleanup recommendations were not applied.");
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
        MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ApplyCleanupRecommendations).ProgressText;

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(selectedRecommendations, "manual-review", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations).SuccessText;
            AppendActivity($"Applied {selectedRecommendations.Length} cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Selected cleanup recommendation application failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Selected cleanup recommendations were not applied.");
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
        MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ApplyCleanupRecommendations).ProgressText;

        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(selectedRecommendations, "selected-meetings", _lifetimeCts.Token);
            MarkMeetingCleanupHistoricalReviewCompleted();
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations).SuccessText;
            AppendActivity($"Applied cleanup recommendations for {selectedMeetingStems.Count} selected meeting(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Selected-meeting cleanup recommendation application failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Selected-meeting cleanup recommendations were not applied.");
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
        MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.DismissCleanupRecommendations).ProgressText;

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
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DismissCleanupRecommendations).SuccessText;
            AppendActivity($"Dismissed {selectedRecommendations.Length} cleanup recommendation(s).");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Cleanup recommendation dismissal failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DismissCleanupRecommendations,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Cleanup recommendations were not dismissed.");
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

        var labelRows = (SpeakerLabelsEditorDataGrid.ItemsSource as IEnumerable<SpeakerLabelEditorRow>)?.ToArray();
        if (labelRows is null)
        {
            SpeakerNamesStatusTextBlock.Text = "No Diarization Labels are loaded for the selected meeting.";
            return;
        }

        var request = BuildSpeakerNameReviewRequest(
            labelRows.Select(row => new SpeakerNameReviewRow(
                row.SpeakerId,
                row.OriginalLabel,
                row.EditedLabel,
                row.ProfileId,
                row.ExpectedNameSource,
                RejectSuggestion: false,
                row.ArtifactRevision,
                row.SuggestedDisplayName)));
        if (request is null)
        {
            SpeakerNamesStatusTextBlock.Text = "No Meeting Display Name changes are pending, or meeting speaker data needs reload.";
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        SpeakerNamesStatusTextBlock.Text = "Applying Meeting Display Name changes...";

        try
        {
            var result = await _speakerNameCorrectionService.ApplyReviewAsync(
                selectedMeeting.Source,
                request,
                _liveConfig.Current.SpeakerNameLearningMode,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);

            if (result.RequiresReload)
            {
                SpeakerNamesStatusTextBlock.Text = result.LearningWarning ?? "Meeting speaker data changed. Reload before applying name changes.";
                AppendActivity($"Preserved Meeting Display Name drafts for '{selectedMeeting.Title}' because speaker data changed.");
                return;
            }

            UpdateSelectedMeetingEditor(selectedMeeting);
            await RefreshVoiceProfileSettingsAsync();
            SpeakerNamesStatusTextBlock.Text = result.LearningWarning is null
                ? $"Applied Meeting Display Name changes for '{selectedMeeting.Title}'. Learned {result.LearningResult.CreatedCount + result.LearningResult.UpdatedCount} local Voice Profile sample(s)."
                : $"Applied Meeting Display Name changes for '{selectedMeeting.Title}'. {result.LearningWarning}";
            AppendActivity($"Applied Meeting Display Name changes for '{selectedMeeting.Title}'.");
        }
        catch (Exception exception)
        {
            SpeakerNamesStatusTextBlock.Text = "Unable to apply Meeting Display Name changes. Reload and try again.";
            _logger.Log($"Meeting display-name update failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyMeetingDisplayNames,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private async Task<string> QueueTranscriptRegenerationAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken,
        ProcessingWorkPriority processingPriority = ProcessingWorkPriority.Normal,
        string? cleanupFingerprint = null)
    {
        return await QueueMeetingReprocessingAsync(
            meeting,
            "Queued to re-generate the transcript.",
            forceSpeakerLabeling: false,
            forceTranscription: true,
            cancellationToken,
            processingPriority,
            cleanupFingerprint);
    }

    private async Task<string> QueueSpeakerLabelGenerationAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken,
        ProcessingWorkPriority processingPriority = ProcessingWorkPriority.Normal,
        string? cleanupFingerprint = null)
    {
        return await QueueMeetingReprocessingAsync(
            meeting,
            "Queued to add speaker labels.",
            forceSpeakerLabeling: true,
            forceTranscription: false,
            cancellationToken,
            processingPriority,
            cleanupFingerprint);
    }

    private async Task<string> QueueSpeakerLabelRepairAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken,
        ProcessingWorkPriority processingPriority = ProcessingWorkPriority.Normal,
        string? cleanupFingerprint = null)
    {
        return await QueueMeetingReprocessingAsync(
            meeting,
            "Queued to repair speaker labels.",
            forceSpeakerLabeling: true,
            forceTranscription: false,
            cancellationToken,
            processingPriority,
            cleanupFingerprint);
    }

    private async Task<string> QueueMeetingReprocessingAsync(
        MeetingOutputRecord meeting,
        string transcriptionQueuedMessage,
        bool forceSpeakerLabeling,
        bool forceTranscription,
        CancellationToken cancellationToken,
        ProcessingWorkPriority processingPriority = ProcessingWorkPriority.Normal,
        string? cleanupFingerprint = null)
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

        if (forceSpeakerLabeling &&
            await _meetingOutputCatalogService.TrySeedTranscriptionSnapshotForPublishedMeetingAsync(
                meeting,
                manifestPath,
                cancellationToken))
        {
            AppendActivity($"Loaded existing transcript text for speaker labeling on '{meeting.Title}'.");
        }

        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var queuedManifest = MainWindowInteractionLogic.BuildQueuedMeetingReprocessingManifest(
            manifest,
            now,
            transcriptionQueuedMessage,
            forceSpeakerLabeling,
            forceTranscription);

        await _manifestStore.SaveAsync(queuedManifest, manifestPath, cancellationToken);
        await _processingQueue.EnqueueAsync(manifestPath, processingPriority, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cleanupFingerprint))
        {
            _meetingCleanupWorkLedgerService.Record(cleanupFingerprint, CleanupWorkState.Queued, manifestPath);
        }
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
            _logger.Log($"Transcript regeneration failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.RegenerateTranscript,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Meeting merge failed: {exception}");
            MergeSelectedMeetingsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.MergeMeetings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting merge did not finish.");
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
            _logger.Log($"Meeting split failed: {exception}");
            SplitSelectedMeetingStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.SplitMeeting,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting split did not finish.");
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

            if (!int.TryParse(ConfigSummaryRequestTimeoutTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var summaryRequestTimeoutSeconds))
            {
                throw new InvalidOperationException("Summary request timeout must be a whole number of seconds.");
            }

            if (!int.TryParse(ConfigSummaryTranscriptChunkTargetTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var summaryTranscriptChunkTarget))
            {
                throw new InvalidOperationException("Summary chunk token target must be a whole number.");
            }

            if (!int.TryParse(ConfigSummaryTranscriptChunkOverlapTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var summaryTranscriptChunkOverlap))
            {
                throw new InvalidOperationException("Summary chunk overlap must be a whole number.");
            }

            var summaryGenerationMode = ConfigSummaryGenerationEnabledCheckBox.IsChecked == true
                ? MeetingSummaryGenerationMode.Enabled
                : MeetingSummaryGenerationMode.Disabled;
            var summaryProviderPreference = ConfigSummaryProviderPreferenceComboBox.SelectedValue is MeetingSummaryProviderPreference selectedSummaryProviderPreference
                ? selectedSummaryProviderPreference
                : currentConfig.SummaryProviderPreference;
            var usesHostedSummaryRoute = summaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
                summaryProviderPreference is MeetingSummaryProviderPreference.LocalThenOpenAi or MeetingSummaryProviderPreference.OpenAiOnly;
            var hostedConsentVersion = currentConfig.SummaryHostedRouteConsentVersion;
            var hostedConsentGrantedAtUtc = currentConfig.SummaryHostedRouteConsentGrantedAtUtc;
            if (usesHostedSummaryRoute && hostedConsentVersion < SummaryExperienceResolver.HostedRouteConsentPolicyVersion)
            {
                var consent = MessageBox.Show(
                    this,
                    "Hosted summaries can send published transcript text to the selected hosted provider. Continue only if you authorize that route and any configured fallback.",
                    "Authorize hosted summaries",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (consent != MessageBoxResult.Yes)
                {
                    SetConfigSaveStatus("Hosted summary changes were not saved.");
                    return;
                }

                hostedConsentVersion = SummaryExperienceResolver.HostedRouteConsentPolicyVersion;
                hostedConsentGrantedAtUtc = DateTimeOffset.UtcNow;
            }
            else if (!usesHostedSummaryRoute)
            {
                hostedConsentVersion = 0;
                hostedConsentGrantedAtUtc = null;
            }

            var nextConfig = new AppConfig
            {
                AudioOutputDir = ConfigAudioOutputDirTextBox.Text.Trim(),
                TranscriptOutputDir = ConfigTranscriptOutputDirTextBox.Text.Trim(),
                WorkDir = ConfigWorkDirTextBox.Text.Trim(),
                ImportInboxDir = ConfigImportInboxDirTextBox.Text.Trim(),
                ImportInboxEnabled = ConfigImportInboxEnabledCheckBox.IsChecked == true,
                ImportInboxScanIntervalSeconds = currentConfig.ImportInboxScanIntervalSeconds,
                ImportInboxMaxBatchSize = currentConfig.ImportInboxMaxBatchSize,
                ImportInboxArchiveAfterQueueEnabled = ConfigImportInboxArchiveAfterQueueEnabledCheckBox.IsChecked == true,
                ImportInboxMoveBlockedToErrorEnabled = ConfigImportInboxMoveBlockedToErrorEnabledCheckBox.IsChecked == true,
                ModelCacheDir = currentConfig.ModelCacheDir,
                TranscriptionModelPath = currentConfig.TranscriptionModelPath,
                TranscriptionModelProfilePreference = currentConfig.TranscriptionModelProfilePreference,
                DiarizationAssetPath = currentConfig.DiarizationAssetPath,
                SpeakerLabelingModelProfilePreference = currentConfig.SpeakerLabelingModelProfilePreference,
                DiarizationAccelerationPreference = ConfigDiarizationGpuAccelerationCheckBox.IsChecked == true
                    ? InferenceAccelerationPreference.Auto
                    : InferenceAccelerationPreference.CpuOnly,
                DiarizationAccelerationSecurityPromptMigrationApplied = true,
                MicCaptureEnabled = ConfigMicCaptureCheckBox.IsChecked == true,
                SpeakerNameLearningMode = ConfigSpeakerNameLearningCheckBox.IsChecked == true
                    ? SpeakerNameLearningMode.LocalAutoLearn
                    : SpeakerNameLearningMode.Disabled,
                SpeakerNameAutoApplyConfidenceThreshold = currentConfig.SpeakerNameAutoApplyConfidenceThreshold,
                SpeakerNameSuggestionConfidenceThreshold = currentConfig.SpeakerNameSuggestionConfidenceThreshold,
                SpeakerNameMatchMarginThreshold = currentConfig.SpeakerNameMatchMarginThreshold,
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
                ProcessingSpeedProfile = ProcessingSpeedProfile.Normal,
                OvernightDrainStartLocal = ConfigOvernightDrainStartTextBox.Text.Trim(),
                OvernightDrainEndLocal = ConfigOvernightDrainEndTextBox.Text.Trim(),
                PreviousProcessingSpeedProfile = currentConfig.PreviousProcessingSpeedProfile,
                ProcessingScheduleMigrationApplied = true,
                InitialProcessingStrategy = ConfigInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy initialProcessingStrategy
                    ? initialProcessingStrategy
                    : currentConfig.InitialProcessingStrategy,
                OvernightInitialProcessingStrategy = ConfigOvernightInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy overnightInitialProcessingStrategy
                    ? overnightInitialProcessingStrategy
                    : currentConfig.OvernightInitialProcessingStrategy,
                IncrementalWorkPlan = BuildIncrementalWorkPlanFromEditor(),
                TranscriptionProviderPreference = ConfigTranscriptionProviderPreferenceComboBox.SelectedValue is TranscriptionProviderPreference transcriptionProviderPreference
                    ? transcriptionProviderPreference
                    : currentConfig.TranscriptionProviderPreference,
                TranscriptionCliPath = ConfigTranscriptionCliPathTextBox.Text.Trim(),
                TranscriptionCliArguments = ConfigTranscriptionCliArgumentsTextBox.Text.Trim(),
                TranscriptionCliProviderProbe = currentConfig.TranscriptionCliProviderProbe,
                DiarizationProviderPreference = ConfigDiarizationProviderPreferenceComboBox.SelectedValue is DiarizationProviderPreference diarizationProviderPreference
                    ? diarizationProviderPreference
                    : currentConfig.DiarizationProviderPreference,
                DiarizationCliPath = ConfigDiarizationCliPathTextBox.Text.Trim(),
                DiarizationCliArguments = ConfigDiarizationCliArgumentsTextBox.Text.Trim(),
                DiarizationCliProviderProbe = currentConfig.DiarizationCliProviderProbe,
                SummaryGenerationMode = summaryGenerationMode,
                SummaryProviderPreference = summaryProviderPreference,
                SummaryModelProxyBaseUrl = ConfigSummaryModelProxyBaseUrlTextBox.Text.Trim(),
                SummaryModelProxyModel = ConfigSummaryModelProxyModelTextBox.Text.Trim(),
                SummaryOpenAiModel = ConfigSummaryOpenAiModelTextBox.Text.Trim(),
                SummaryHostedRouteConsentVersion = hostedConsentVersion,
                SummaryHostedRouteConsentGrantedAtUtc = hostedConsentGrantedAtUtc,
                SummaryReasoningEffort = ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort summaryReasoningEffort
                    ? summaryReasoningEffort
                    : currentConfig.SummaryReasoningEffort,
                SummaryRequestTimeoutSeconds = summaryRequestTimeoutSeconds,
                SummaryTranscriptChunkTokenTarget = summaryTranscriptChunkTarget,
                SummaryTranscriptChunkOverlapTokens = summaryTranscriptChunkOverlap,
                SpeakerLabelingSecurityPromptMigrationApplied = true,
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

            nextConfig = BacklogAccelerationProfileResolver.Apply(
                nextConfig,
                ConfigBacklogAccelerationProfileComboBox.SelectedValue is BacklogAccelerationProfile profile
                    ? profile
                    : currentConfig.BacklogAccelerationProfile);

            if (!await CanApplyImportInboxSettingsAsync(currentConfig, nextConfig, _lifetimeCts.Token))
            {
                return;
            }

            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);
            await SavePendingSummaryProviderSecretsAsync(_lifetimeCts.Token);

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
            _logger.Log($"Settings save failed: {exception}");
            SetConfigSaveStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.SaveSettings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Settings were not saved.");
        }
        finally
        {
            _isSavingConfig = false;
            UpdateConfigActionState();
        }
    }

    private async Task<bool> CanApplyImportInboxSettingsAsync(
        AppConfig currentConfig,
        AppConfig nextConfig,
        CancellationToken cancellationToken)
    {
        var journal = new ImportInboxJournalStore(Path.Combine(
            currentConfig.WorkDir,
            "import-inbox",
            "journal.json"));
        try
        {
            var decision = ImportInboxConfigurationChangePolicy.Evaluate(
                currentConfig,
                nextConfig,
                await journal.LoadAsync(cancellationToken));
            if (decision.CanApply)
            {
                return true;
            }

            SetConfigSaveStatus(decision.Message);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            SetConfigSaveStatus("Meeting Recorder could not verify active Inbox items. Inbox settings were not changed.");
            return false;
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
            "Checking whether local detection or a supported integration is available.";
        ConfigTeamsIntegrationAdvancedDetailTextBlock.Text =
            "Detailed probe diagnostics will be available here after the check finishes.";
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
            ConfigTeamsIntegrationDetailTextBlock.Text = "The app is shutting down before the capability check completed.";
            ConfigTeamsIntegrationAdvancedDetailTextBlock.Text = "No completed probe diagnostics are available.";
            ConfigTeamsIntegrationMetadataTextBlock.Text = BuildTeamsIntegrationMetadataText(_liveConfig.Current);
            ConfigTeamsIntegrationBaselineTextBlock.Text = _lastTeamsProbeBaselineSummary ??
                "Heuristic baseline: no saved probe result is available.";
        }
        catch (Exception exception)
        {
            _logger.Log($"Teams integration probe failed: {exception}");
            ConfigTeamsIntegrationStatusTextBlock.Text = "Probe failed.";
            ConfigTeamsIntegrationDetailTextBlock.Text =
                "The capability check did not finish. Keep using local detection, then retry or review Advanced probe diagnostics.";
            ConfigTeamsIntegrationAdvancedDetailTextBlock.Text = exception.Message;
            ConfigTeamsIntegrationMetadataTextBlock.Text = BuildTeamsIntegrationMetadataText(_liveConfig.Current);
            ConfigTeamsIntegrationBaselineTextBlock.Text = _lastTeamsProbeBaselineSummary ??
                "Heuristic baseline: the probe did not finish cleanly.";
            SetConfigSaveStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.None,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Teams integration probe did not finish.");
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

    private async void AddAudioFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add Audio Files",
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.mp4|All files|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await AddExternalAudioImportsAsync(dialog.FileNames, ExternalAudioImportMethod.FilePicker);
    }

    private void MeetingsWorkspaceGrid_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MeetingsWorkspaceGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
        {
            return;
        }

        await AddExternalAudioImportsAsync(droppedPaths, ExternalAudioImportMethod.DragDrop);
    }

    private void ExternalAudioImportDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingExternalAudioImportGrid)
        {
            return;
        }

        _selectedExternalAudioImportRow = ExternalAudioImportDataGrid.SelectedItem as ExternalAudioImportReviewRow;
        UpdateExternalAudioImportReviewState();
    }

    private void ExternalAudioImportTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingExternalAudioImportEditor || _selectedExternalAudioImportRow is null)
        {
            return;
        }

        _selectedExternalAudioImportRow.UpdateTitle(ExternalAudioImportTitleTextBox.Text);
        RefreshExternalAudioImportGrid();
        UpdateExternalAudioImportReviewState();
    }

    private void ExternalAudioImportStartedAtTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingExternalAudioImportEditor || _selectedExternalAudioImportRow is null)
        {
            return;
        }

        _selectedExternalAudioImportRow.UpdateStartedAtInput(ExternalAudioImportStartedAtTextBox.Text);
        RefreshExternalAudioImportGrid();
        UpdateExternalAudioImportReviewState();
    }

    private void ExternalAudioImportProjectTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingExternalAudioImportEditor || _selectedExternalAudioImportRow is null)
        {
            return;
        }

        _selectedExternalAudioImportRow.UpdateProjectName(ExternalAudioImportProjectTextBox.Text);
        RefreshExternalAudioImportGrid();
        UpdateExternalAudioImportReviewState();
    }

    private async void QueueExternalAudioImportsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_externalAudioImportRows.Count == 0)
        {
            return;
        }

        ApplyExternalAudioImportSetupState();
        var readiness = ResolveExternalAudioImportReadiness(DateTimeOffset.UtcNow);
        var queueableRows = _externalAudioImportRows
            .Where(row => row.CanQueue || row.CanStageForSetup)
            .ToArray();
        if (queueableRows.Length == 0)
        {
            ExternalAudioImportDetailStatusTextBlock.Text =
                "No files are ready to queue yet. Fix any setup or validation issues first.";
            UpdateExternalAudioImportReviewState();
            return;
        }

        if (!await _externalAudioImportGate.WaitAsync(0, _lifetimeCts.Token))
        {
            ExternalAudioImportDetailStatusTextBlock.Text =
                "Another import action is still finishing. Wait, then queue the reviewed files again.";
            UpdateExternalAudioImportReviewState();
            return;
        }

        _isQueueingExternalAudioImports = true;
        UpdateMeetingActionState();
        try
        {
            var successfullyQueuedRows = new List<ExternalAudioImportReviewRow>(queueableRows.Length);
            var blockedBySetupRows = new List<ExternalAudioImportReviewRow>(queueableRows.Length);
            foreach (var row in queueableRows)
            {
                row.ClearQueueError();
                if (!row.TryBuildRequest(out var request, out var validationMessage))
                {
                    row.SetQueueError(validationMessage);
                    continue;
                }

                try
                {
                    var queued = await _externalAudioImportService.QueueImportAsync(
                        _liveConfig.Current,
                        request,
                        DateTimeOffset.UtcNow,
                        readiness,
                        _lifetimeCts.Token);
                    if (queued.ImportJobState == ExternalAudioImportJobState.BlockedBySetup)
                    {
                        blockedBySetupRows.Add(row);
                        AppendActivity($"Paused imported audio '{queued.Title}' until transcription setup is ready.");
                    }
                    else
                    {
                        await _processingQueue.EnqueueAsync(queued.ManifestPath, _lifetimeCts.Token);
                        successfullyQueuedRows.Add(row);
                        AppendActivity($"Queued imported audio '{queued.Title}' for transcription.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.Log($"External audio import queue failed: {exception}");
                    row.SetQueueError(UserActionCopyResolver.Resolve(
                        UserActionIntent.ImportAudio,
                        UserActionBlockedReasonKind.OperationFailed).BlockedText);
                }
            }

            if (successfullyQueuedRows.Count > 0 || blockedBySetupRows.Count > 0)
            {
                _externalAudioImportRows = _externalAudioImportRows
                    .Except(successfullyQueuedRows.Concat(blockedBySetupRows))
                    .ToList();
                _selectedExternalAudioImportRow = _externalAudioImportRows.FirstOrDefault();
                await RefreshMeetingListAsync();
            }

            if (_externalAudioImportRows.Count == 0)
            {
                ExternalAudioImportDetailStatusTextBlock.Text = blockedBySetupRows.Count == 0
                    ? $"Queued {successfullyQueuedRows.Count} imported audio file(s)."
                    : $"Staged {blockedBySetupRows.Count} import(s) safely. Complete Setup, then choose Resume Blocked Imports.";
            }
            else if (successfullyQueuedRows.Count > 0 || blockedBySetupRows.Count > 0)
            {
                ExternalAudioImportDetailStatusTextBlock.Text =
                    $"Queued {successfullyQueuedRows.Count} import(s) and staged {blockedBySetupRows.Count} setup-blocked import(s). Review the remaining rows for errors.";
                ExternalAudioImportDataGrid.Focus();
            }

            UpdateExternalAudioImportReviewState();
        }
        catch (OperationCanceledException)
        {
            ExternalAudioImportDetailStatusTextBlock.Text = "Audio import queueing was canceled.";
            UpdateExternalAudioImportReviewState();
        }
        finally
        {
            _isQueueingExternalAudioImports = false;
            _externalAudioImportGate.Release();
            UpdateMeetingActionState();
        }
    }

    private void RemoveExternalAudioImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedExternalAudioImportRow is null)
        {
            return;
        }

        _externalAudioImportRows.Remove(_selectedExternalAudioImportRow);
        _selectedExternalAudioImportRow = _externalAudioImportRows.FirstOrDefault();
        ExternalAudioImportDetailStatusTextBlock.Text =
            "Removed this row from review. Its original source file was not changed.";
        UpdateExternalAudioImportReviewState();
    }

    private async void RetryExternalAudioImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var row = _selectedExternalAudioImportRow;
        if (row is null || !row.CanRetry || _isQueueingExternalAudioImports)
        {
            return;
        }

        if (!await _externalAudioImportGate.WaitAsync(0, _lifetimeCts.Token))
        {
            ExternalAudioImportDetailStatusTextBlock.Text =
                "Another import action is still finishing. Wait, then retry this reviewed file again.";
            UpdateExternalAudioImportReviewState();
            return;
        }

        _isQueueingExternalAudioImports = true;
        ExternalAudioImportDetailStatusTextBlock.Text = "Rechecking the selected source without changing it.";
        UpdateExternalAudioImportReviewState();
        try
        {
            var candidates = await _externalAudioImportService.BuildImportCandidatesAsync(
                _liveConfig.Current,
                [row.SourcePath],
                row.ImportMethod,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            var refreshedCandidate = candidates.SingleOrDefault();
            if (refreshedCandidate is null)
            {
                row.SetQueueError("The selected source is no longer available for review.");
            }
            else if (_externalAudioImportRows.Contains(row))
            {
                row.RefreshCandidate(refreshedCandidate);
            }
        }
        catch (OperationCanceledException)
        {
            ExternalAudioImportDetailStatusTextBlock.Text = "Import recheck was canceled.";
        }
        catch (Exception exception)
        {
            _logger.Log($"External audio import retry failed: {exception}");
            row.SetQueueError(UserActionCopyResolver.Resolve(
                UserActionIntent.ImportAudio,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
        finally
        {
            _isQueueingExternalAudioImports = false;
            _externalAudioImportGate.Release();
            UpdateExternalAudioImportReviewState();
        }
    }

    private void SkipDuplicateExternalAudioImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedExternalAudioImportRow is not { CanSkipDuplicate: true } row)
        {
            return;
        }

        _externalAudioImportRows.Remove(row);
        _selectedExternalAudioImportRow = _externalAudioImportRows.FirstOrDefault();
        ExternalAudioImportDetailStatusTextBlock.Text =
            "Skipped duplicate from review. The existing import and original source were not changed.";
        UpdateExternalAudioImportReviewState();
    }

    private void OpenExternalAudioImportSetupButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsWindowSection.Setup);
    }

    private void OpenExternalAudioImportInboxButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigImportInboxEnabledCheckBox"));
    }

    private async void ResumeBlockedAudioImportsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isQueueingExternalAudioImports ||
            !await _externalAudioImportGate.WaitAsync(0, _lifetimeCts.Token))
        {
            return;
        }

        _isQueueingExternalAudioImports = true;
        UpdateExternalAudioImportReviewState();
        try
        {
            var readiness = ResolveExternalAudioImportReadiness(DateTimeOffset.UtcNow);
            if (!readiness.CanQueue)
            {
                ExternalAudioImportDetailStatusTextBlock.Text = readiness.RecoveryText;
                return;
            }

            var resumed = await _externalAudioImportReadinessCoordinator.ResumeBlockedJobsAsync(
                _liveConfig.Current.WorkDir,
                readiness,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            foreach (var manifestPath in resumed.ManifestPaths)
            {
                await _processingQueue.EnqueueAsync(manifestPath, _lifetimeCts.Token);
            }

            ExternalAudioImportDetailStatusTextBlock.Text = resumed.ManifestPaths.Count == 0
                ? resumed.StagedWorkUnavailableCount == 0
                    ? "No verified setup-blocked imports are waiting to resume."
                    : "A blocked import needs its staged copy repaired before it can resume."
                : $"Resumed {resumed.ManifestPaths.Count} verified staged import(s).";
            if (resumed.ManifestPaths.Count > 0)
            {
                AppendActivity($"Resumed {resumed.ManifestPaths.Count} staged import(s) after transcription setup became ready.");
                await RefreshMeetingListAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ExternalAudioImportDetailStatusTextBlock.Text = "Resume blocked imports was canceled.";
        }
        catch (Exception exception)
        {
            _logger.Log($"Blocked import resume failed: {exception}");
            ExternalAudioImportDetailStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ImportAudio,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
        }
        finally
        {
            _isQueueingExternalAudioImports = false;
            _externalAudioImportGate.Release();
            UpdateExternalAudioImportReviewState();
        }
    }

    private void ConfigPathLink_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPath(_liveConfig.ConfigPath);
    }

    private async Task AddExternalAudioImportsAsync(
        IEnumerable<string> selectedPaths,
        ExternalAudioImportMethod importMethod)
    {
        var expandedPaths = ExpandExternalAudioImportSelection(selectedPaths);
        if (expandedPaths.Length == 0)
        {
            ExternalAudioImportDetailStatusTextBlock.Text =
                "Choose at least one readable audio file to import.";
            UpdateExternalAudioImportReviewState();
            return;
        }

        try
        {
            var candidates = await _externalAudioImportService.BuildImportCandidatesAsync(
                _liveConfig.Current,
                expandedPaths,
                importMethod,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            if (candidates.Count == 0)
            {
                ExternalAudioImportDetailStatusTextBlock.Text =
                    "No supported audio files were found in the selection.";
                UpdateExternalAudioImportReviewState();
                return;
            }

            var nextSelectedRow = MergeExternalAudioImportCandidates(candidates);
            ApplyExternalAudioImportSetupState();
            _selectedExternalAudioImportRow = nextSelectedRow ?? _selectedExternalAudioImportRow ?? _externalAudioImportRows.FirstOrDefault();
            UpdateExternalAudioImportReviewState();
            ExternalAudioImportDataGrid.Focus();
            AppendActivity($"Added {candidates.Count} audio file(s) to the import review.");
        }
        catch (OperationCanceledException)
        {
            ExternalAudioImportDetailStatusTextBlock.Text = "Adding audio files was canceled.";
            UpdateExternalAudioImportReviewState();
        }
        catch (Exception exception)
        {
            _logger.Log($"Audio import review failed: {exception}");
            ExternalAudioImportDetailStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ImportAudio,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Audio import review did not finish.");
            UpdateExternalAudioImportReviewState();
        }
    }

    private ExternalAudioImportReviewRow? MergeExternalAudioImportCandidates(
        IReadOnlyList<ExternalAudioImportCandidate> candidates)
    {
        var existingKeys = _externalAudioImportRows
            .Select(row => row.SourceIdentityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ExternalAudioImportReviewRow? firstAddedRow = null;
        foreach (var candidate in candidates)
        {
            var nextRow = new ExternalAudioImportReviewRow(candidate);
            if (!existingKeys.Add(nextRow.SourceIdentityKey))
            {
                continue;
            }

            _externalAudioImportRows.Add(nextRow);
            firstAddedRow ??= nextRow;
        }

        return firstAddedRow;
    }

    private void ApplyExternalAudioImportSetupState()
    {
        var readiness = ResolveExternalAudioImportReadiness(DateTimeOffset.UtcNow);
        var setupMessage = readiness.CanQueue
            ? null
            : readiness.RecoveryText;
        foreach (var row in _externalAudioImportRows)
        {
            row.SetSetupBlocked(!readiness.CanQueue && row.Preflight.IsSuccess, setupMessage);
        }

        ResumeBlockedAudioImportsButton.IsEnabled = readiness.CanQueue && !_isQueueingExternalAudioImports;
    }

    private ExternalAudioImportReadinessSnapshot ResolveExternalAudioImportReadiness(DateTimeOffset checkedAtUtc)
    {
        try
        {
            var status = _whisperModelService.Inspect(_liveConfig.Current.TranscriptionModelPath);
            return ExternalAudioImportReadinessResolver.Resolve(
                status.Kind,
                _liveConfig.Current.TranscriptionModelPath,
                checkedAtUtc);
        }
        catch
        {
            return ExternalAudioImportReadinessResolver.ResolveUnknown(
                _liveConfig.Current.TranscriptionModelPath,
                checkedAtUtc);
        }
    }

    private void RefreshExternalAudioImportGrid()
    {
        _isRefreshingExternalAudioImportGrid = true;
        try
        {
            ExternalAudioImportDataGrid.ItemsSource = null;
            ExternalAudioImportDataGrid.ItemsSource = _externalAudioImportRows;
            if (_selectedExternalAudioImportRow is not null &&
                _externalAudioImportRows.Contains(_selectedExternalAudioImportRow))
            {
                ExternalAudioImportDataGrid.SelectedItem = _selectedExternalAudioImportRow;
            }
        }
        finally
        {
            _isRefreshingExternalAudioImportGrid = false;
        }
    }

    private void UpdateExternalAudioImportReviewState()
    {
        ApplyExternalAudioImportSetupState();
        var hasRows = _externalAudioImportRows.Count > 0;
        ExternalAudioImportReviewBorder.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        if (!hasRows)
        {
            RefreshExternalAudioImportGrid();
            _selectedExternalAudioImportRow = null;
            _isUpdatingExternalAudioImportEditor = true;
            try
            {
                ExternalAudioImportTitleTextBox.Text = string.Empty;
                ExternalAudioImportStartedAtTextBox.Text = string.Empty;
                ExternalAudioImportProjectTextBox.Text = string.Empty;
            }
            finally
            {
                _isUpdatingExternalAudioImportEditor = false;
            }

            ExternalAudioImportSummaryTextBlock.Text =
                "Add recordings from another source, review them here, then queue the valid files for transcription and optional speaker labeling.";
            return;
        }

        RefreshExternalAudioImportGrid();
        var reviewSummary = ExternalAudioImportReviewProjection.Summarize(
            _externalAudioImportRows.Select(row => row.ReviewProjection));
        var stageableCount = _externalAudioImportRows.Count(row => row.CanQueue || row.CanStageForSetup);
        var blockedCount = reviewSummary.SetupBlockedCount;
        ExternalAudioImportSummaryTextBlock.Text = reviewSummary.StatusText;
        AddAudioFilesButton.IsEnabled = !_isQueueingExternalAudioImports;
        AddMoreAudioFilesButton.IsEnabled = !_isQueueingExternalAudioImports;
        QueueExternalAudioImportsButton.Content = _isQueueingExternalAudioImports ? "Queueing..." : "Queue Valid";
        QueueExternalAudioImportsButton.IsEnabled = stageableCount > 0 && !_isQueueingExternalAudioImports && !IsMeetingActionInProgress();
        RetryExternalAudioImportButton.IsEnabled = _selectedExternalAudioImportRow?.CanRetry == true && !_isQueueingExternalAudioImports;
        SkipDuplicateExternalAudioImportButton.IsEnabled = _selectedExternalAudioImportRow?.CanSkipDuplicate == true && !_isQueueingExternalAudioImports;
        RemoveExternalAudioImportButton.IsEnabled = _selectedExternalAudioImportRow is not null && !_isQueueingExternalAudioImports;
        OpenExternalAudioImportSetupButton.IsEnabled = blockedCount > 0 && !_isQueueingExternalAudioImports;
        OpenExternalAudioImportInboxButton.IsEnabled = !_isQueueingExternalAudioImports;
        ExternalAudioImportTitleTextBox.IsEnabled = _selectedExternalAudioImportRow is not null && !_isQueueingExternalAudioImports;
        ExternalAudioImportStartedAtTextBox.IsEnabled = _selectedExternalAudioImportRow is not null && !_isQueueingExternalAudioImports;
        ExternalAudioImportProjectTextBox.IsEnabled = _selectedExternalAudioImportRow is not null && !_isQueueingExternalAudioImports;
        UpdateSelectedExternalAudioImportEditor();
    }

    private void UpdateSelectedExternalAudioImportEditor()
    {
        _isUpdatingExternalAudioImportEditor = true;
        try
        {
            if (_selectedExternalAudioImportRow is null)
            {
                ExternalAudioImportTitleTextBox.Text = string.Empty;
                ExternalAudioImportStartedAtTextBox.Text = string.Empty;
                ExternalAudioImportProjectTextBox.Text = string.Empty;
                ExternalAudioImportDetailStatusTextBlock.Text =
                    "Select one import row to adjust the title, start time, or optional project before queueing.";
                return;
            }

            ExternalAudioImportTitleTextBox.Text = _selectedExternalAudioImportRow.EditableTitle;
            ExternalAudioImportStartedAtTextBox.Text = _selectedExternalAudioImportRow.StartedAtInputText;
            ExternalAudioImportProjectTextBox.Text = _selectedExternalAudioImportRow.ProjectName;
            ExternalAudioImportDetailStatusTextBlock.Text = _selectedExternalAudioImportRow.DetailStatusText;
        }
        finally
        {
            _isUpdatingExternalAudioImportEditor = false;
        }
    }

    private static string[] ExpandExternalAudioImportSelection(IEnumerable<string> selectedPaths)
    {
        return selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path =>
            {
                if (File.Exists(path))
                {
                    return [path];
                }

                if (Directory.Exists(path))
                {
                    return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
                }

                return Array.Empty<string>();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        await InstallAvailableUpdateAsync("manual install", manual: true, allowProcessingOverride: false, queueWhenProcessingBlocked: true, _lifetimeCts.Token);
    }

    private async void InstallQueuedUpdateNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        await InstallAvailableUpdateAsync("manual install override", manual: true, allowProcessingOverride: true, queueWhenProcessingBlocked: false, _lifetimeCts.Token);
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
                result.LatestAssetSizeBytes,
                _lifetimeCts.Token);
            UpdateCheckStatusTextBlock.Text = $"Downloaded {FormatVersionLabel(result.LatestVersion)} to '{downloadedPath}'.";
            AppendActivity($"Downloaded update {FormatVersionLabel(result.LatestVersion)} to '{downloadedPath}'.");
            OpenContainingFolder(downloadedPath);
        }
        catch (Exception exception)
        {
            _logger.Log($"Update download failed: {exception}");
            UpdateCheckStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.InstallUpdates,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Update download did not finish.");
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
            "Downloading the recommended Standard transcription model...",
            "Recommended transcription setup updated.",
            isTranscriptionHighAccuracyDownload: false,
            isSpeakerLabelingHighAccuracyDownload: false,
            provisionSpeakerLabeling: false);
    }

    private void CancelRecommendedTranscriptionSetupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_modelProvisioningCts is not { IsCancellationRequested: false })
        {
            return;
        }

        _modelProvisioningCts.Cancel();
        ModelActionStatusTextBlock.Text = "Canceling the transcription download. Your current valid model selection will stay unchanged.";
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
        bool isSpeakerLabelingHighAccuracyDownload,
        bool provisionSpeakerLabeling = true)
    {
        _isDownloadingRemoteModel = isTranscriptionHighAccuracyDownload;
        _isDownloadingRemoteDiarizationAsset = isSpeakerLabelingHighAccuracyDownload;
        _isActivatingModel = !isTranscriptionHighAccuracyDownload &&
            transcriptionProfile == TranscriptionModelProfilePreference.Standard;
        UpdateModelActionButtons();
        UpdateDiarizationActionButtons();

        ModelActionStatusTextBlock.Text = startingStatus;
        if (provisionSpeakerLabeling)
        {
            DiarizationActionStatusTextBlock.Text = startingStatus;
        }

        try
        {
            using var provisioningCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _modelProvisioningCts = provisioningCts;
            var transcriptionProgress = new Progress<FileDownloadProgress>(ReportCuratedTranscriptionDownloadProgress);
            _isDownloadingRemoteModel = true;
            UpdateModelActionButtons();
            var provisioningResult = await _modelProvisioningService.ProvisionAsync(
                new ModelProvisioningRequest(
                    InstallRoot: AppContext.BaseDirectory,
                    ModelCatalogPath: _meetingRecorderModelCatalogService.GetBundledCatalogPath(),
                    UpdateFeedUrl: _liveConfig.Current.UpdateFeedUrl,
                    TranscriptionProfile: transcriptionProfile,
                    SpeakerLabelingProfile: speakerLabelingProfile,
                    RespectExistingConfigPreferences: false,
                    ProvisionSpeakerLabeling: provisionSpeakerLabeling,
                    TranscriptionDownloadProgress: transcriptionProgress),
                provisioningCts.Token);
            var nextConfig = provisioningResult.Config with
            {
                BackgroundSpeakerLabelingMode = provisionSpeakerLabeling
                    ? MainWindowInteractionLogic.ResolveBackgroundSpeakerLabelingModeAfterProfileSelection(
                        provisioningResult.Config.BackgroundSpeakerLabelingMode,
                        speakerLabelingProfile)
                    : provisioningResult.Config.BackgroundSpeakerLabelingMode,
                SpeakerLabelingSecurityPromptMigrationApplied = provisionSpeakerLabeling
                    ? true
                    : provisioningResult.Config.SpeakerLabelingSecurityPromptMigrationApplied,
            };
            var speakerLabelingModeAutoEnabled =
                provisionSpeakerLabeling &&
                speakerLabelingProfile != SpeakerLabelingModelProfilePreference.Disabled &&
                provisioningResult.Config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
                nextConfig.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Throttled;

            await _liveConfig.SaveAsync(nextConfig, _lifetimeCts.Token);
            RefreshWhisperModelStatus();
            RefreshDiarizationAssetStatus();
            ApplyProvisioningResultToSetupStatus(
                provisioningResult.Result,
                successPrefix,
                updateSpeakerLabelingStatus: provisionSpeakerLabeling);
            if (speakerLabelingModeAutoEnabled)
            {
                DiarizationActionStatusTextBlock.Text =
                    $"{DiarizationActionStatusTextBlock.Text} Automatic speaker labeling will now run in Throttled mode.".Trim();
            }
            AppendActivity(
                provisionSpeakerLabeling
                    ? $"Updated curated model profiles. Transcription requested={provisioningResult.Result.Transcription.RequestedProfile}; speaker labeling requested={provisioningResult.Result.SpeakerLabeling.RequestedProfile}."
                    : $"Updated recommended transcription setup. Transcription requested={provisioningResult.Result.Transcription.RequestedProfile}; optional speaker labeling was left unchanged.");
        }
        catch (OperationCanceledException) when (!IsShutdownRequested)
        {
            ModelActionStatusTextBlock.Text =
                "Transcription download canceled. Your current valid model selection is unchanged. Retry, import an approved file, or open diagnostics from Setup.";
            if (provisionSpeakerLabeling)
            {
                DiarizationActionStatusTextBlock.Text = "Setup update canceled before any new model profile was applied.";
            }
            AppendActivity("Curated transcription setup canceled by the user.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Curated model profile update failed: {exception}");
            var copy = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed);
            ModelActionStatusTextBlock.Text = copy.BlockedText;
            DiarizationActionStatusTextBlock.Text = copy.BlockedText;
            AppendActivity("Curated model profile update did not finish.");
        }
        finally
        {
            _modelProvisioningCts?.Dispose();
            _modelProvisioningCts = null;
            _isDownloadingRemoteModel = false;
            _isDownloadingRemoteDiarizationAsset = false;
            _isActivatingModel = false;
            ResetModelDownloadProgress();
            UpdateModelActionButtons();
            UpdateDiarizationActionButtons();
        }
    }

    private void ApplyProvisioningResultToSetupStatus(
        ModelProvisioningResult provisioningResult,
        string successPrefix,
        bool updateSpeakerLabelingStatus = true)
    {
        var transcriptionPrefix = provisioningResult.Transcription.IsReady &&
                                  !provisioningResult.Transcription.RetryRecommended
            ? successPrefix
            : "Transcription setup needs attention.";
        ModelActionStatusTextBlock.Text = $"{transcriptionPrefix} {provisioningResult.Transcription.Detail}".Trim();
        if (updateSpeakerLabelingStatus)
        {
            DiarizationActionStatusTextBlock.Text = $"{successPrefix} {provisioningResult.SpeakerLabeling.Detail}".Trim();
        }
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
            _logger.Log($"Whisper model activation failed: {exception}");
            ModelActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Whisper model activation did not finish.");
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
            _logger.Log($"Whisper model download failed: {exception}");
            ModelActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Whisper model download did not finish.");
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
            _logger.Log($"Whisper model import failed: {exception}");
            ModelActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Whisper model import did not finish.");
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
                SpeakerLabelingSecurityPromptMigrationApplied = true,
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
            _logger.Log($"Diarization asset download failed: {exception}");
            DiarizationActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Speaker-labeling asset download did not finish.");
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
                SpeakerLabelingSecurityPromptMigrationApplied = true,
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
            _logger.Log($"Diarization asset import failed: {exception}");
            DiarizationActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Speaker-labeling asset import did not finish.");
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
        UpdateRecordingControlState();
    }

    private void UpdateRecordingControlState()
    {
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

            _allMeetingRows = BuildMeetingRows(records, _meetingCleanupRecommendations);
            _lastSuccessfulMeetingsRefreshUtc = DateTimeOffset.UtcNow;
            _hasMeetingsRefreshFailure = false;
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
                StartMeetingCleanupRecommendationRefresh(
                    records,
                    refreshVersion,
                    refreshMode,
                    backgroundToken,
                    _meetingBackgroundWorkCancellationIdentity);
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
            _hasMeetingsRefreshFailure = true;
            _logger.Log($"Meeting library refresh failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshMeetingDetails,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            _meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();
            _allMeetingRows = Array.Empty<MeetingListRow>();
            MeetingsDataGrid.ItemsSource = _allMeetingRows;
            MeetingCleanupRecommendationsDataGrid.ItemsSource = Array.Empty<MeetingCleanupRecommendationRow>();
            MeetingCleanupRecommendationsStatusTextBlock.Text = "Cleanup suggestions are unavailable because the meeting list failed to load.";
            MeetingCleanupReviewBannerBorder.Visibility = Visibility.Collapsed;
            MeetingCleanupReviewBannerTextBlock.Text = string.Empty;
            UpdateMeetingsPresetPresentation(ResolveMeetingsViewPresetState());
            UpdateSelectedMeetingEditor(null);
            UpdateMeetingsRefreshStateText("Meeting details unavailable. Refresh to retry.");
        }
        finally
        {
            Interlocked.Decrement(ref _meetingBaselineRefreshOperations);
            UpdateMeetingsRefreshStateText();
            UpdateMeetingActionState();
            TryDispatchPendingMeetingCleanupWork();
        }
    }

    private void StartMeetingCleanupRecommendationRefresh(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        MeetingRefreshMode refreshMode,
        CancellationToken cancellationToken,
        Guid cancellationIdentity)
    {
        Interlocked.Increment(ref _meetingCleanupRefreshOperations);
        UpdateMeetingsRefreshStateText();
        _ = RunMeetingCleanupRecommendationRefreshAsync(
            records,
            refreshVersion,
            refreshMode,
            cancellationToken,
            cancellationIdentity);
    }

    private async Task RunMeetingCleanupRecommendationRefreshAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        MeetingRefreshMode refreshMode,
        CancellationToken cancellationToken,
        Guid cancellationIdentity)
    {
        try
        {
            var inspections = await BuildMeetingInspectionsAsync(records, cancellationToken);
            var visibleRecommendations = (await BuildVisibleMeetingCleanupRecommendationsAsync(inspections, cancellationToken)).ToArray();
            if (refreshVersion != Volatile.Read(ref _meetingRefreshVersion) || cancellationToken.IsCancellationRequested || IsShutdownRequested)
            {
                return;
            }

            await ReconcileLegacyCleanupLedgerAsync(records, visibleRecommendations, cancellationToken);
            _meetingCleanupRecommendations = visibleRecommendations;
            _meetingCleanupSchedulerFailureBackoffUntilUtc = null;
            ApplyMeetingRowsUpdate(records, _meetingCleanupRecommendations, preserveEditorDrafts: true);
            UpdateTeamsPlaybackCleanupStatus(inspections, visibleRecommendations);
            var automationSnapshot = AutomationCatalogSnapshot.Create(
                refreshVersion,
                ToAutomationCatalogRefreshMode(refreshMode),
                DateTimeOffset.UtcNow,
                records.Select(record => record.Stem),
                visibleRecommendations.Select(recommendation => recommendation.Fingerprint),
                AutomationPolicyRevision,
                cancellationIdentity);
            RequestPendingMeetingCleanupWorkDispatch(
                visibleRecommendations,
                records,
                refreshVersion,
                cancellationToken,
                automationSnapshot);
        }
        catch (OperationCanceledException)
        {
            // Ignore superseded or shutdown background work.
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting cleanup recommendation refresh {refreshVersion} failed: {exception}");
            _meetingCleanupSchedulerFailureBackoffUntilUtc = DateTimeOffset.UtcNow +
                MeetingCleanupAutoApplyPlanner.AutomaticBatchCooldown;
        }
        finally
        {
            Interlocked.Decrement(ref _meetingCleanupRefreshOperations);
            TryMarkFullMeetingsRefreshCompleted(refreshVersion);
            UpdateMeetingsRefreshStateText();
            UpdateMeetingActionState();
        }
    }

    private void RequestPendingMeetingCleanupWorkDispatch(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        CancellationToken cancellationToken,
        AutomationCatalogSnapshot automationSnapshot)
    {
        _pendingCleanupSchedulerDispatch = new CleanupSchedulerDispatchRequest(
            recommendations,
            records,
            refreshVersion,
            cancellationToken,
            automationSnapshot);
        TryDispatchPendingMeetingCleanupWork();
    }

    private void TryDispatchPendingMeetingCleanupWork()
    {
        if (_pendingCleanupSchedulerDispatch is null ||
            _isDispatchingPendingMeetingCleanupWork ||
            Volatile.Read(ref _meetingBaselineRefreshOperations) > 0 ||
            IsShutdownRequested)
        {
            return;
        }

        var dispatch = _pendingCleanupSchedulerDispatch;
        _pendingCleanupSchedulerDispatch = null;
        _isDispatchingPendingMeetingCleanupWork = true;
        _ = DispatchPendingMeetingCleanupWorkAsync(dispatch);
    }

    private async Task DispatchPendingMeetingCleanupWorkAsync(CleanupSchedulerDispatchRequest dispatch)
    {
        try
        {
            await TryAutoApplyMeetingCleanupSafeFixesAsync(
                dispatch.Recommendations,
                dispatch.Records,
                dispatch.RefreshVersion,
                dispatch.CancellationToken,
                dispatch.AutomationSnapshot);
        }
        finally
        {
            _isDispatchingPendingMeetingCleanupWork = false;
            UpdateMeetingCleanupReviewBanner();
            TryDispatchPendingMeetingCleanupWork();
        }
    }

    private async Task ReconcileLegacyCleanupLedgerAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        CancellationToken cancellationToken)
    {
        var recommendationsByFingerprint = recommendations.ToDictionary(
            recommendation => recommendation.Fingerprint,
            StringComparer.Ordinal);
        var recordsByStem = records.ToDictionary(record => record.Stem, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _meetingCleanupWorkLedgerService.GetEntries()
                     .Where(entry => entry.State == CleanupWorkState.ManualReview &&
                         string.Equals(entry.Detail, "Legacy queued work needs reconciliation.", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!recommendationsByFingerprint.TryGetValue(entry.Fingerprint, out var recommendation))
            {
                // Recommendation disappeared after refresh, which is the durable published-output completion signal.
                _meetingCleanupWorkLedgerService.Record(entry.Fingerprint, CleanupWorkState.Completed);
                continue;
            }

            if (!MeetingCleanupAutoApplyPlanner.ShouldSuppressSuccessfulAutomaticApply(recommendation.Action) ||
                !recordsByStem.TryGetValue(recommendation.PrimaryStem, out var record) ||
                string.IsNullOrWhiteSpace(record.ManifestPath) ||
                !File.Exists(record.ManifestPath))
            {
                continue;
            }

            try
            {
                var manifest = await _manifestStore.LoadAsync(record.ManifestPath, cancellationToken);
                if (manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing)
                {
                    _meetingCleanupWorkLedgerService.Record(
                        entry.Fingerprint,
                        CleanupWorkState.Queued,
                        record.ManifestPath,
                        "Reconciled legacy queued work.");
                }
            }
            catch (IOException)
            {
                // Keep manual review when a legacy work manifest cannot be read safely.
            }
            catch (JsonException)
            {
                // Keep manual review when a legacy work manifest cannot be read safely.
            }
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
        _lastSuccessfulMeetingsRefreshUtc = DateTimeOffset.UtcNow;
        _hasMeetingsRefreshFailure = false;
        MeetingsDataGrid.ItemsSource = _allMeetingRows;
        ApplyMeetingsWorkspaceView(selectedStems, preserveEditorDrafts);
        UpdateProcessingQueueStatusUi();
        RefreshOpenMeetingDetailWindow();
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

        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var snapshotIsStale = _hasMeetingsRefreshFailure ||
            _lastSuccessfulMeetingsRefreshUtc is null ||
            evaluatedAtUtc - _lastSuccessfulMeetingsRefreshUtc > TimeSpan.FromMinutes(2);
        var localTranscriptionSetup = HasReadyTranscriptionModel()
            ? MeetingRecommendationAvailability.Available
            : MeetingRecommendationAvailability.Missing;

        return records
            .Select(record =>
            {
                var recordRecommendations = recommendationsByStem.TryGetValue(record.Stem, out var resolvedRecommendations)
                    ? resolvedRecommendations
                    : Array.Empty<MeetingCleanupRecommendation>();
                var primaryRecommendation = _meetingRecommendationResolver.Resolve(
                    BuildMeetingRecommendationInput(
                        record,
                        recordRecommendations,
                        localTranscriptionSetup,
                        snapshotIsStale),
                    evaluatedAtUtc);
                var row = new MeetingListRow(record, recordRecommendations, primaryRecommendation);
                if (string.Equals(
                        _latestProcessingQueueStatusSnapshot.RushRequest?.ManifestPath,
                        record.ManifestPath,
                        StringComparison.Ordinal))
                {
                    row.SetAsapStatus(_latestProcessingQueueStatusSnapshot.RushRequest?.LifecycleText);
                }

                return row;
            })
            .ToArray();
    }

    private MeetingRecommendationInput BuildMeetingRecommendationInput(
        MeetingOutputRecord record,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        MeetingRecommendationAvailability localTranscriptionSetup,
        bool snapshotIsStale)
    {
        var isProcessing = record.ManifestState is SessionState.Queued or SessionState.Processing or SessionState.Finalizing;
        var transcriptAvailable = !string.IsNullOrWhiteSpace(record.MarkdownPath) || !string.IsNullOrWhiteSpace(record.JsonPath);
        var transcriptState = isProcessing
            ? MeetingRecommendationAvailability.Available
            : transcriptAvailable
                ? MeetingRecommendationAvailability.Available
                : MeetingRecommendationAvailability.Missing;
        var sourceState = !string.IsNullOrWhiteSpace(record.AudioPath) || !string.IsNullOrWhiteSpace(record.ManifestPath)
            ? MeetingRecommendationAvailability.Available
            : MeetingRecommendationAvailability.Missing;
        var speakerRepair = _currentDiarizationAssetStatus is null
            ? MeetingRecommendationAvailability.Unknown
            : _currentDiarizationAssetStatus.IsReady
                ? MeetingRecommendationAvailability.Available
                : MeetingRecommendationAvailability.Missing;
        var processing = record.ManifestState switch
        {
            SessionState.Queued => MeetingRecommendationProcessingState.Queued,
            SessionState.Processing or SessionState.Finalizing => MeetingRecommendationProcessingState.Processing,
            null => MeetingRecommendationProcessingState.Idle,
            _ => MeetingRecommendationProcessingState.Idle,
        };
        var summaryRetryAvailable = recommendations.Any(recommendation =>
            recommendation.Action == MeetingCleanupAction.GenerateSummary &&
            recommendation.ReasonCode.StartsWith("retry-", StringComparison.OrdinalIgnoreCase));
        var metadataPolishAvailable = recommendations.Any(recommendation =>
            recommendation.Action == MeetingCleanupAction.Rename);
        var dismissals = _liveConfig.Current.DismissedMeetingRecommendations
            .Select(dismissal => new MeetingRecommendationDismissal(
                dismissal.Fingerprint,
                dismissal.RecommendationVersion,
                dismissal.DismissedAtUtc))
            .ToArray();

        return new MeetingRecommendationInput(
            record.Stem,
            SnapshotVersion: Volatile.Read(ref _meetingRefreshVersion),
            SnapshotObservedAtUtc: _lastSuccessfulMeetingsRefreshUtc,
            IsSnapshotStale: snapshotIsStale,
            record.ManifestState,
            sourceState,
            localTranscriptionSetup,
            transcriptState,
            transcriptState,
            speakerRepair,
            record.HasSuspiciousSpeakerLabels,
            processing,
            summaryRetryAvailable,
            metadataPolishAvailable,
            recommendations,
            dismissals);
    }

    private CancellationToken CreateMeetingBackgroundWorkToken()
    {
        CancelMeetingBackgroundWork();
        _meetingBackgroundWorkCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _meetingBackgroundWorkCancellationIdentity = Guid.NewGuid();
        return _meetingBackgroundWorkCts.Token;
    }

    private static AutomationCatalogRefreshMode ToAutomationCatalogRefreshMode(MeetingRefreshMode refreshMode) =>
        refreshMode == MeetingRefreshMode.Full
            ? AutomationCatalogRefreshMode.Full
            : AutomationCatalogRefreshMode.Fast;

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

    private async Task HandleMeetingsWorkspacePreferenceChangedAsync(bool customized = false)
    {
        if (_isUpdatingMeetingsWorkspaceControls || !_isUiReady)
        {
            return;
        }

        if (customized)
        {
            _isUpdatingMeetingsWorkspaceControls = true;
            try
            {
                MeetingsPresetComboBox.SelectedValue = MeetingsViewPreset.Custom;
            }
            finally
            {
                _isUpdatingMeetingsWorkspaceControls = false;
            }
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
                MeetingsViewPreset = GetSelectedMeetingsViewPreset(),
                MeetingsViewPresetInitialized = true,
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
            _logger.Log($"Meeting view preference save failed: {exception}");
            AppendActivity("Meeting view preferences were not saved.");
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

        EnsureInitialMeetingsViewPreset();
        var presetState = ResolveMeetingsViewPresetState();
        var selectedViewMode = presetState.ViewMode;
        var selectedGroupKey = presetState.GroupKey;
        var selectedStems = preferredSelectedStems?.Count > 0
            ? preferredSelectedStems
            : GetSelectedMeetingRows().Select(row => row.Source.Stem).ToArray();
        var visibleRows = _allMeetingRows
            .Where(row => presetState.ScopedMeetingIds.Contains(row.Source.Stem, StringComparer.OrdinalIgnoreCase))
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
                    MainWindowInteractionLogic.GetMeetingWorkspaceSortPropertyName(presetState.SortKey),
                    presetState.SortDescending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending));
        }

        ReselectMeetingRows(selectedStems);
        UpdateMeetingsWorkspaceControlState(presetState);
        UpdateMeetingsPresetPresentation(presetState);
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
        if (ResolveMeetingsViewPresetState().ViewMode != MeetingsViewMode.Grouped)
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

    private void UpdateMeetingsWorkspaceControlState(MeetingViewPresetState? state = null)
    {
        state ??= ResolveMeetingsViewPresetState();
        var showCustomControls = state.ShowCustomControls;
        var isGroupedView = showCustomControls && state.ViewMode == MeetingsViewMode.Grouped;
        MeetingsCustomViewExpander.Visibility = showCustomControls ? Visibility.Visible : Visibility.Collapsed;
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

    private MeetingsViewPreset GetSelectedMeetingsViewPreset()
    {
        return MeetingsPresetComboBox.SelectedValue is MeetingsViewPreset value && Enum.IsDefined(value)
            ? value
            : MeetingsViewPreset.Recent;
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
            MeetingsPresetComboBox.SelectedValue = config.MeetingsViewPreset;
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

    private MeetingViewPresetState ResolveMeetingsViewPresetState()
    {
        return _meetingViewPresetResolver.Resolve(new MeetingViewPresetInput(
            GetSelectedMeetingsViewPreset(),
            new MeetingsCustomViewState(
                GetSelectedMeetingsViewMode(),
                GetSelectedMeetingsSortKey(),
                GetSelectedMeetingsSortDescending(),
                GetSelectedMeetingsGroupKey()),
            _allMeetingRows.Select(BuildMeetingViewPresetItem).ToArray(),
            MeetingsSearchTextBox.Text,
            ArchiveCatalogAvailable: false));
    }

    private static MeetingViewPresetItem BuildMeetingViewPresetItem(MeetingListRow row)
    {
        var workState = row.Source.ManifestState switch
        {
            SessionState.Queued => MeetingViewWorkState.Queued,
            SessionState.Processing or SessionState.Finalizing => MeetingViewWorkState.Processing,
            SessionState.Failed => MeetingViewWorkState.Failed,
            _ => row.Status switch
            {
                nameof(SessionState.Queued) => MeetingViewWorkState.Queued,
                nameof(SessionState.Processing) or nameof(SessionState.Finalizing) => MeetingViewWorkState.Processing,
                nameof(SessionState.Failed) => MeetingViewWorkState.Failed,
                nameof(SessionState.Published) or "Transcript files present" => MeetingViewWorkState.Complete,
                _ => MeetingViewWorkState.Unknown,
            },
        };
        var searchText = string.Join(
            " ",
            new[]
            {
                row.Title,
                row.ProjectName,
                row.Platform,
                row.Status,
                string.Join(" ", row.Source.Attendees.Select(attendee => attendee.Name)),
                string.Join(" ", row.Source.KeyAttendees ?? Array.Empty<string>()),
            });
        return new MeetingViewPresetItem(
            row.Source.Stem,
            row.Source.StartedAtUtc,
            workState,
            row.CanApplyRecommendedAction,
            IsArchived: false,
            SearchText: searchText);
    }

    private void EnsureInitialMeetingsViewPreset()
    {
        var config = _liveConfig.Current;
        if (config.MeetingsViewPresetInitialized || _isPersistingInitialMeetingsViewPreset)
        {
            return;
        }

        var initialPreset = _meetingViewPresetResolver.SelectInitialPreset(
            _allMeetingRows.Select(BuildMeetingViewPresetItem).ToArray());
        _isUpdatingMeetingsWorkspaceControls = true;
        try
        {
            MeetingsPresetComboBox.SelectedValue = initialPreset;
        }
        finally
        {
            _isUpdatingMeetingsWorkspaceControls = false;
        }

        _isPersistingInitialMeetingsViewPreset = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() => _ = PersistInitialMeetingsViewPresetAsync(initialPreset)),
            DispatcherPriority.Background);
    }

    private async Task PersistInitialMeetingsViewPresetAsync(MeetingsViewPreset initialPreset)
    {
        try
        {
            await _liveConfig.SaveAsync(
                _liveConfig.Current with
                {
                    MeetingsViewPreset = initialPreset,
                    MeetingsViewPresetInitialized = true,
                },
                _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"Initial meeting view preset save failed: {exception}");
            AppendActivity("Initial meeting view preset was not saved.");
        }
        finally
        {
            _isPersistingInitialMeetingsViewPreset = false;
        }
    }

    private void UpdateMeetingsPresetPresentation(MeetingViewPresetState state)
    {
        if (_hasMeetingsRefreshFailure)
        {
            MeetingsPresetStatusTextBlock.Text = "Last refresh failed. Refresh the list to retry.";
            return;
        }

        var lastRefreshText = _lastSuccessfulMeetingsRefreshUtc is { } lastSuccessfulRefreshUtc
            ? $" Last refreshed {TimeZoneInfo.ConvertTime(lastSuccessfulRefreshUtc, TimeZoneInfo.Local):g}."
            : " Refresh has not completed yet.";
        MeetingsPresetStatusTextBlock.Text = state.StatusSummary + lastRefreshText;
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

        var inspectorState = MainWindowInteractionLogic.BuildMeetingInspectorState(
            row.Source,
            row.Recommendations,
            primaryRecommendation: row.PrimaryRecommendation);
        var selectedCount = GetSelectedMeetingRows().Length;
        MeetingWorkspaceStatusTextBlock.Text = selectedCount <= 1
            ? $"Selected '{row.Title}'. Open Details for transcript review and focused maintenance."
            : $"{selectedCount} meetings selected. Bulk actions stay available from the meeting list context menu.";
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

    private void OpenMeetingDetailsForSelection()
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length != 1)
        {
            MeetingWorkspaceStatusTextBlock.Text = selectedMeetings.Length == 0
                ? "Select one meeting before opening details."
                : "Open Details works with one meeting at a time. Reduce the selection to one meeting first.";
            return;
        }

        OpenMeetingDetails(selectedMeetings[0]);
    }

    private void OpenMeetingDetails(MeetingListRow row)
    {
        _openMeetingDetailStem = row.Source.Stem;
        if (_meetingDetailWindow is null)
        {
            _meetingDetailWindow = new MeetingDetailWindow
            {
                Owner = this,
            };
            _meetingDetailWindow.Closed += (_, _) =>
            {
                _meetingDetailWindow = null;
                _openMeetingDetailStem = null;
            };
            _meetingDetailWindow.OpenTranscriptRequested += (_, _) => OpenMeetingDetailTranscript();
            _meetingDetailWindow.OpenAudioRequested += (_, _) => OpenMeetingDetailAudio();
            _meetingDetailWindow.OpenFolderRequested += (_, _) => OpenMeetingDetailFolder();
            _meetingDetailWindow.RenameRequested += async (_, args) => await RenameOpenMeetingDetailAsync(args.Text);
            _meetingDetailWindow.SuggestTitleRequested += async (_, _) => await SuggestOpenMeetingDetailTitleAsync();
            _meetingDetailWindow.ApplyProjectRequested += async (_, args) => await UpdateOpenMeetingDetailProjectAsync(args.Text, clearProject: false);
            _meetingDetailWindow.ClearProjectRequested += async (_, _) => await UpdateOpenMeetingDetailProjectAsync(projectName: null, clearProject: true);
            _meetingDetailWindow.RetryTranscriptRequested += async (_, _) => await RetryOpenMeetingDetailTranscriptAsync();
            _meetingDetailWindow.ReTranscribeRequested += (_, _) => OpenMeetingDetailTranscriptionSetup();
            _meetingDetailWindow.ConfigureSummariesRequested += (_, _) => OpenMeetingDetailSummarySettings();
            _meetingDetailWindow.GenerateSummaryRequested += async (_, _) => await GenerateOpenMeetingDetailSummaryAsync();
            _meetingDetailWindow.RetrySummaryRequested += async (_, _) => await GenerateOpenMeetingDetailSummaryAsync();
            _meetingDetailWindow.AddSpeakerLabelsRequested += async (_, _) => await AddSpeakerLabelsForOpenMeetingDetailAsync();
            _meetingDetailWindow.ProcessAsapRequested += async (_, _) => await UpdateRushProcessingForOpenMeetingDetailAsync();
            _meetingDetailWindow.SplitRequested += async (_, args) => await SplitOpenMeetingDetailAsync(args.Text);
            _meetingDetailWindow.ApplySpeakerNamesRequested += async (_, args) => await ApplyOpenMeetingDetailSpeakerNamesAsync(args.Rows);
            _meetingDetailWindow.RefreshSpeakerNamesRequested += async (_, _) => await RefreshOpenMeetingDetailSpeakerNamesAsync();
            _meetingDetailWindow.UndoSpeakerNameRecognitionRequested += async (_, _) => await UndoOpenMeetingDetailSpeakerNameRecognitionAsync();
            _meetingDetailWindow.ArchiveRequested += async (_, _) => await ArchiveOpenMeetingDetailAsync();
            _meetingDetailWindow.DeleteRequested += async (_, _) => await DeleteOpenMeetingDetailAsync();
        }

        _ = ApplyMeetingDetailWindowStateAsync(row);
        _meetingDetailWindow.Show();
        _meetingDetailWindow.Activate();
    }

    private void RefreshOpenMeetingDetailWindow()
    {
        _ = RefreshOpenMeetingDetailWindowAsync();
    }

    private async Task RefreshOpenMeetingDetailWindowAsync()
    {
        if (_meetingDetailWindow is null || string.IsNullOrWhiteSpace(_openMeetingDetailStem))
        {
            return;
        }

        var row = FindMeetingRowByStem(_openMeetingDetailStem);
        if (row is null)
        {
            _meetingDetailWindow.SetMaintenanceStatus("This meeting is no longer in the current library view.");
            return;
        }

        await ApplyMeetingDetailWindowStateAsync(row);
    }

    private async Task ApplyMeetingDetailWindowStateAsync(MeetingListRow row)
    {
        if (_meetingDetailWindow is null)
        {
            return;
        }

        try
        {
            var targetStem = row.Source.Stem;
            var summaryProviderConfiguration = await BuildSummaryProviderConfigurationStateAsync(_lifetimeCts.Token);
            if (_meetingDetailWindow is null ||
                !string.Equals(_openMeetingDetailStem, targetStem, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var transcript = MeetingTranscriptDocumentReader.Read(row.Source.JsonPath, row.Source.MarkdownPath);
            var speakerExperience = ResolveSpeakerExperienceForMeeting(row);
            var canQueueSpeakerLabels = speakerExperience.Permits(SpeakerExperienceAction.AddSpeakerLabels) ||
                speakerExperience.Permits(SpeakerExperienceAction.RepairSpeakerLabels);
            var state = MainWindowInteractionLogic.BuildMeetingDetailWindowState(
                row.Source,
                row.Recommendations,
                transcript,
                row.CanOpenAudioArtifact,
                row.CanOpenTranscriptArtifact,
                row.CanRegenerateTranscript,
                canQueueSpeakerLabels,
                CanChangeRushProcessing(row),
                IsMeetingMarkedAsap(row),
                summaryProviderConfiguration: summaryProviderConfiguration,
                isGeneratingSummary: _isGeneratingMeetingSummary,
                primaryRecommendation: row.PrimaryRecommendation) with
            {
                SpeakerLabelState = speakerExperience.Explanation,
                SpeakerLabelActionLabel = speakerExperience.Permits(SpeakerExperienceAction.RepairSpeakerLabels)
                    ? "Repair Speaker Labels"
                    : "Add Speaker Labels",
            };
            var speakerArtifactRevision = MeetingOutputCatalogService.GetSpeakerArtifactRevision(row.Source);
            var speakerLabels = _meetingOutputCatalogService.ListSpeakerLabelDetails(row.Source);
            var evidenceSpeakerIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(row.Source.ManifestPath) && File.Exists(row.Source.ManifestPath))
            {
                try
                {
                    var manifest = await _manifestStore.LoadAsync(row.Source.ManifestPath, _lifetimeCts.Token);
                    foreach (var sample in manifest.ProcessingMetadata?.SpeakerVoiceSamples ?? Array.Empty<SpeakerVoiceSample>())
                    {
                        if (!string.IsNullOrWhiteSpace(sample.SpeakerId))
                        {
                            evidenceSpeakerIds.Add(sample.SpeakerId);
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.Log($"Meeting speaker review evidence state could not be loaded: {exception}");
                }
            }

            var speakerReview = SpeakerReviewSnapshotResolver.Resolve(new SpeakerReviewSnapshotInput(
                row.Source.Stem,
                speakerArtifactRevision,
                speakerLabels.Select((label, index) => new SpeakerIdentity(
                    label.SpeakerId ?? $"legacy_{index}",
                    label.DisplayName,
                    label.NameSource == SpeakerNameSource.UserEdited,
                    label.ProfileId,
                    label.NameSource,
                    label.Confidence,
                    label.SuggestedDisplayName,
                    label.DecisionReason)).ToArray(),
                evidenceSpeakerIds,
                _liveConfig.Current.SpeakerNameLearningMode,
                IsLocalProfileStoreAvailable: true,
                IsStale: false,
                HasRepairWarning: row.Source.HasSuspiciousSpeakerLabels));
            var speakerRows = speakerLabels
                .Select((label, index) => new MeetingDetailSpeakerLabelEditorRow(
                    label.DisplayName,
                    BuildSpeakerNameProvenanceText(label),
                    label.SuggestedDisplayName,
                    label.SpeakerId,
                    label.ProfileId,
                    HasProfileSpeakerNameAttribution(label),
                    speakerArtifactRevision,
                    label.NameSource,
                    speakerReview.Rows[index]))
                .ToArray();
            var detailTaskCenter = MeetingDetailTaskCenterResolver.Resolve(new MeetingDetailTaskCenterInput(
                new MeetingDetailSnapshotRevision(
                    row.Source.Stem,
                    CatalogRevision: Volatile.Read(ref _meetingRefreshVersion),
                    ArtifactRevision: (row.CanOpenAudioArtifact ? 1 : 0) + (row.CanOpenTranscriptArtifact ? 2 : 0),
                    RecommendationRevision: row.PrimaryRecommendation.RecommendationVersion,
                    IsFresh: !_hasMeetingsRefreshFailure && _lastSuccessfulMeetingsRefreshUtc is not null),
                row.PrimaryRecommendation,
                transcript.HasTranscript,
                transcript.StatusText,
                row.CanOpenAudioArtifact,
                row.CanOpenTranscriptArtifact,
                ResolveMeetingActionCatalogState([row], row, IsMeetingActionInProgress())));

            if (_meetingDetailWindow.GetRefreshDisposition(detailTaskCenter) ==
                MeetingDetailRefreshDisposition.KeepDraftsAndOfferReload)
            {
                _meetingDetailWindow.ApplyTaskCenterState(detailTaskCenter, markAsApplied: false);
                _meetingDetailWindow.SetMaintenanceStatus(
                    "Meeting details changed in the background. Your unsaved drafts are preserved; finish them before reopening or refreshing this detail view.");
                return;
            }

            _meetingDetailWindow.ApplyState(state, GetRecentMeetingProjectNames(), speakerRows);
            _meetingDetailWindow.ApplyTaskCenterState(detailTaskCenter);
            _meetingDetailWindow.SetMaintenanceBusy(IsMeetingActionInProgress());
            if (IsMeetingMarkedAsap(row) &&
                _latestProcessingQueueStatusSnapshot.RushRequest is { LifecycleText: { Length: > 0 } lifecycleText })
            {
                _meetingDetailWindow.SetMaintenanceStatus(
                    $"{lifecycleText}. Clear ASAP releases only this meeting's future priority; it does not cancel current work.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log($"Meeting detail refresh failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshMeetingDetails,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
    }

    private async Task<SummaryProviderConfigurationState> BuildSummaryProviderConfigurationStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var hasOpenAiKey = await _summarySecretStore.HasSecretAsync(SummarySecretKind.OpenAi, cancellationToken);
            return new SummaryProviderConfigurationState(
                _liveConfig.Current.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled,
                _liveConfig.Current.SummaryProviderPreference,
                hasOpenAiKey,
                _liveConfig.Current.SummaryHostedRouteConsentVersion);
        }
        catch
        {
            return new SummaryProviderConfigurationState(
                _liveConfig.Current.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled,
                _liveConfig.Current.SummaryProviderPreference,
                HasOpenAiKey: false,
                HostedConsentVersion: _liveConfig.Current.SummaryHostedRouteConsentVersion);
        }
    }

    private MeetingListRow? GetOpenMeetingDetailRow()
    {
        return string.IsNullOrWhiteSpace(_openMeetingDetailStem)
            ? null
            : FindMeetingRowByStem(_openMeetingDetailStem);
    }

    private MeetingListRow? FindMeetingRowByStem(string? stem)
    {
        return string.IsNullOrWhiteSpace(stem)
            ? null
            : _allMeetingRows.FirstOrDefault(row =>
                string.Equals(row.Source.Stem, stem, StringComparison.OrdinalIgnoreCase));
    }

    private string[] GetRecentMeetingProjectNames()
    {
        return _allMeetingRows
            .Select(row => row.Source.ProjectName)
            .Where(projectName => !string.IsNullOrWhiteSpace(projectName))
            .Select(projectName => projectName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(projectName => projectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void OpenMeetingDetailTranscript()
    {
        if (GetOpenMeetingDetailRow() is { } row)
        {
            OpenPath(row.PrimaryTranscriptPath ?? string.Empty);
        }
    }

    private void OpenMeetingDetailAudio()
    {
        if (GetOpenMeetingDetailRow() is { } row)
        {
            OpenPath(row.Source.AudioPath ?? string.Empty);
        }
    }

    private void OpenMeetingDetailFolder()
    {
        if (GetOpenMeetingDetailRow() is { } row)
        {
            OpenContainingFolder(GetPreferredMeetingFolderPath(row));
        }
    }

    private async Task RenameOpenMeetingDetailAsync(string newTitle)
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            _meetingDetailWindow?.SetMaintenanceStatus("Enter a meeting title before renaming.");
            return;
        }

        _isRenamingMeeting = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus($"Renaming '{row.Title}'...");
        try
        {
            var renamed = await _meetingOutputCatalogService.RenameMeetingAsync(
                _liveConfig.Current.AudioOutputDir,
                _liveConfig.Current.TranscriptOutputDir,
                row.Source.Stem,
                newTitle,
                _liveConfig.Current.WorkDir,
                _lifetimeCts.Token);
            _openMeetingDetailStem = renamed.Stem;
            await RefreshMeetingListAsync(renamed.Stem);
            _meetingDetailWindow?.SetMaintenanceStatus($"Renamed meeting to '{renamed.Title}'.");
            AppendActivity($"Renamed published meeting '{row.Title}' to '{renamed.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting rename failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.RenameMeeting,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Meeting rename did not finish.");
        }
        finally
        {
            _isRenamingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private async Task SuggestOpenMeetingDetailTitleAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        _isSuggestingMeetingTitle = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus($"Looking for a better title for '{row.Title}'...");
        try
        {
            var suggestion = await TryGetMeetingTitleSuggestionAsync(row, _lifetimeCts.Token);
            if (suggestion is null)
            {
                _meetingDetailWindow?.SetMaintenanceStatus($"No better Outlook or Teams title history match was found for '{row.Title}'.");
                return;
            }

            _meetingDetailWindow?.SetTitleDraft(
                suggestion.Title,
                $"Suggested '{suggestion.Title}' from {suggestion.Source}. Review it, then click Rename.");
            AppendActivity($"Suggested title '{suggestion.Title}' from {suggestion.Source} for '{row.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting title suggestion failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.SuggestMeetingTitle,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Meeting title suggestion did not finish.");
        }
        finally
        {
            _isSuggestingMeetingTitle = false;
            UpdateMeetingActionState();
        }
    }

    private async Task UpdateOpenMeetingDetailProjectAsync(string? projectName, bool clearProject)
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        var normalizedProjectName = clearProject ? null : projectName?.Trim();
        if (!clearProject && string.IsNullOrWhiteSpace(normalizedProjectName))
        {
            _meetingDetailWindow?.SetMaintenanceStatus("Enter a project name, or use Clear Project.");
            return;
        }

        _isUpdatingMeetingProject = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus(clearProject
            ? $"Clearing project metadata for '{row.Title}'..."
            : $"Applying project '{normalizedProjectName}' to '{row.Title}'...");
        try
        {
            await _meetingOutputCatalogService.UpdateMeetingProjectAsync(
                _liveConfig.Current.AudioOutputDir,
                _liveConfig.Current.TranscriptOutputDir,
                row.Source.Stem,
                normalizedProjectName,
                _liveConfig.Current.WorkDir,
                _lifetimeCts.Token);
            await RefreshMeetingListAsync(row.Source.Stem);
            _meetingDetailWindow?.SetMaintenanceStatus(clearProject
                ? "Cleared project metadata."
                : $"Applied project '{normalizedProjectName}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting project update failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateMeetingProject,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Meeting project update did not finish.");
        }
        finally
        {
            _isUpdatingMeetingProject = false;
            UpdateMeetingActionState();
        }
    }

    private async Task RetryOpenMeetingDetailTranscriptAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null || !row.CanRegenerateTranscript)
        {
            return;
        }

        _isRetryingMeeting = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus($"Queueing transcript re-generation for '{row.Title}'...");
        try
        {
            await QueueTranscriptRegenerationAsync(row.Source, _lifetimeCts.Token);
            await RefreshMeetingListAsync(row.Source.Stem);
            _meetingDetailWindow?.SetMaintenanceStatus("Transcript re-generation is queued.");
            AppendActivity($"Re-generated transcript requested for '{row.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Transcript regeneration failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.RegenerateTranscript,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Transcript regeneration did not finish.");
        }
        finally
        {
            _isRetryingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private void OpenMeetingDetailTranscriptionSetup()
    {
        OpenSetupWindow(
            SetupWindowSection.Transcription,
            SettingsTranscriptionSetupSectionBorder,
            _currentTranscriptionSetupState,
            ModelActionStatusTextBlock);
        AppendActivity("Open Setup to choose a different Whisper model, then re-generate the transcript for the focused meeting.");
    }

    private void OpenMeetingDetailSummarySettings()
    {
        OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigSummaryGenerationEnabledCheckBox"));
        AppendActivity("Opened Settings > Summaries for AI summary configuration.");
    }

    private async Task GenerateOpenMeetingDetailSummaryAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        _isGeneratingMeetingSummary = true;
        UpdateMeetingActionState();
        RefreshOpenMeetingDetailWindow();
        var actionCopy = UserActionCopyResolver.Resolve(UserActionIntent.GenerateSummary);
        _meetingDetailWindow?.SetMaintenanceStatus(actionCopy.ProgressText);
        string? completionStatusText = null;
        try
        {
            var blockedReason = await ResolveSummaryGenerationBlockedReasonAsync(row, _lifetimeCts.Token);
            if (blockedReason != UserActionBlockedReasonKind.None)
            {
                completionStatusText = UserActionCopyResolver.Resolve(
                    UserActionIntent.GenerateSummary,
                    blockedReason).BlockedText;
                _meetingDetailWindow?.SetMaintenanceStatus(completionStatusText);
                return;
            }

            var result = await _publishedMeetingSummaryService.GenerateAsync(
                row.Source,
                _liveConfig.Current,
                _lifetimeCts.Token);
            await RefreshMeetingListAsync(row.Source.Stem);
            completionStatusText = BuildSummaryGenerationStatusText(result);
            _meetingDetailWindow?.SetMaintenanceStatus(completionStatusText);
            AppendActivity($"Summary generation for '{row.Title}' finished: {completionStatusText}");
        }
        catch (OperationCanceledException)
        {
            completionStatusText = "Summary generation canceled.";
            _meetingDetailWindow?.SetMaintenanceStatus(completionStatusText);
        }
        catch (Exception exception)
        {
            _logger.Log($"Summary generation failed: {exception.GetType().Name}");
            completionStatusText = UserActionCopyResolver.Resolve(
                UserActionIntent.GenerateSummary,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            _meetingDetailWindow?.SetMaintenanceStatus(completionStatusText);
            AppendActivity("Summary generation did not finish for the focused meeting.");
        }
        finally
        {
            _isGeneratingMeetingSummary = false;
            UpdateMeetingActionState();
            await RefreshOpenMeetingDetailWindowAsync();
            if (!string.IsNullOrWhiteSpace(completionStatusText))
            {
                _meetingDetailWindow?.SetMaintenanceStatus(completionStatusText);
            }
        }
    }

    private async Task<UserActionBlockedReasonKind> ResolveSummaryGenerationBlockedReasonAsync(
        MeetingListRow row,
        CancellationToken cancellationToken)
    {
        var providerConfiguration = await BuildSummaryProviderConfigurationStateAsync(cancellationToken);
        var summaryExperience = SummaryExperienceResolver.Resolve(new SummaryExperienceInput(
            providerConfiguration.IsEnabled
                ? MeetingSummaryGenerationMode.Enabled
                : MeetingSummaryGenerationMode.Disabled,
            providerConfiguration.Preference,
            providerConfiguration.HasOpenAiKey,
            providerConfiguration.HostedConsentVersion));

        if (!providerConfiguration.IsEnabled)
        {
            return UserActionBlockedReasonKind.SummaryDisabled;
        }

        if (!summaryExperience.CanGenerate)
        {
            return summaryExperience.RequiresHostedConsent
                ? UserActionBlockedReasonKind.HostedSummaryConsentRequired
                : UserActionBlockedReasonKind.SummaryProviderNotConfigured;
        }

        var transcript = MeetingTranscriptDocumentReader.Read(row.Source.JsonPath, row.Source.MarkdownPath);
        return !transcript.HasStructuredJson || transcript.StructuredSegments.Count == 0
            ? UserActionBlockedReasonKind.TranscriptUnavailable
            : UserActionBlockedReasonKind.None;
    }

    private static string BuildSummaryGenerationStatusText(PublishedMeetingSummaryUpdateResult result)
    {
        return result.Status.State switch
        {
            StageExecutionState.Succeeded => UserActionCopyResolver.Resolve(UserActionIntent.GenerateSummary).SuccessText,
            StageExecutionState.Skipped => "Summary generation did not run. Review the current summary settings and meeting transcript.",
            StageExecutionState.Failed => UserActionCopyResolver.Resolve(
                UserActionIntent.GenerateSummary,
                UserActionBlockedReasonKind.OperationFailed).BlockedText,
            _ => "Summary generation finished. Refresh the meeting to review the current state.",
        };
    }

    private async Task AddSpeakerLabelsForOpenMeetingDetailAsync()
    {
        if (GetOpenMeetingDetailRow() is { } row)
        {
            await QueueSpeakerLabelsForMeetingsAsync([row], "detail-speaker-labels");
            _meetingDetailWindow?.SetMaintenanceStatus("Speaker labeling is queued.");
        }
    }

    private async Task UpdateRushProcessingForOpenMeetingDetailAsync()
    {
        if (GetOpenMeetingDetailRow() is not { } row)
        {
            return;
        }

        SelectMeetingsByStem([row.Source.Stem]);
        await UpdateRushProcessingForSelectionAsync(this);
        RefreshOpenMeetingDetailWindow();
    }

    private async Task SplitOpenMeetingDetailAsync(string splitPointText)
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        if (!MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                splitPointText,
                row.Source.Duration,
                out var splitPoint,
                out var errorMessage))
        {
            _meetingDetailWindow?.SetMaintenanceStatus(errorMessage);
            return;
        }

        _isSplittingMeeting = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus($"Splitting '{row.Title}' at {MainWindowInteractionLogic.FormatMeetingSplitPoint(splitPoint)}...");
        try
        {
            var splitResult = await QueueSplitMeetingAsync(row.Source, splitPoint, _lifetimeCts.Token);
            await RefreshMeetingListAsync();
            _meetingDetailWindow?.SetMaintenanceStatus(
                $"Queued '{splitResult.FirstTitle}' and '{splitResult.SecondTitle}'. They will appear after processing finishes.");
            AppendActivity($"Queued split meetings '{splitResult.FirstTitle}' and '{splitResult.SecondTitle}' from '{row.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting split failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.SplitMeeting,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Meeting split did not finish.");
        }
        finally
        {
            _isSplittingMeeting = false;
            UpdateMeetingActionState();
        }
    }

    private async Task ApplyOpenMeetingDetailSpeakerNamesAsync(IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> rows)
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        var request = BuildSpeakerNameReviewRequest(rows.Select(labelRow => new SpeakerNameReviewRow(
            labelRow.SpeakerId,
            labelRow.OriginalLabel,
            labelRow.EditedLabel,
            labelRow.ProfileId,
            labelRow.ExpectedNameSource,
            labelRow.IsSuggestionRejected,
            labelRow.ArtifactRevision,
            labelRow.SuggestedDisplayName)));
        if (request is null)
        {
            _meetingDetailWindow?.SetMaintenanceStatus("No Meeting Display Name changes are pending, or meeting speaker data needs reload.");
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
            UserActionIntent.ApplyMeetingDisplayNames).ProgressText);
        try
        {
            var correctionResult = await _speakerNameCorrectionService.ApplyReviewAsync(
                row.Source,
                request,
                _liveConfig.Current.SpeakerNameLearningMode,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            if (correctionResult.RequiresReload)
            {
                _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                    UserActionIntent.ApplyMeetingDisplayNames,
                    UserActionBlockedReasonKind.DataStale).BlockedText);
                AppendActivity($"Preserved Meeting Display Name drafts for '{row.Title}' because speaker data changed.");
                return;
            }

            await RefreshMeetingListAsync(row.Source.Stem);
            await RefreshVoiceProfileSettingsAsync();
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyMeetingDisplayNames).SuccessText);
            AppendActivity($"Applied Meeting Display Name changes for '{row.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting display-name update failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyMeetingDisplayNames,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Meeting display-name update did not finish for the focused meeting.");
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private async Task RefreshOpenMeetingDetailSpeakerNamesAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
            UserActionIntent.RefreshLocalNameSuggestions).ProgressText);
        try
        {
            await _speakerNameCorrectionService.RefreshSpeakerNameAttributionAsync(
                row.Source,
                _liveConfig.Current.SpeakerNameLearningMode,
                BuildSpeakerNameRecognitionOptions(_liveConfig.Current),
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            await RefreshMeetingListAsync(row.Source.Stem);
            RefreshOpenMeetingDetailWindow();
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshLocalNameSuggestions).SuccessText);
        }
        catch (Exception exception)
        {
            _logger.Log($"Local speaker-name suggestion refresh failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshLocalNameSuggestions,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText);
            AppendActivity("Local speaker-name suggestions were not refreshed.");
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private async Task UndoOpenMeetingDetailSpeakerNameRecognitionAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null)
        {
            return;
        }

        _isApplyingSpeakerNames = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
            UserActionIntent.UndoProfileNames).ProgressText);
        try
        {
            await _speakerNameCorrectionService.UndoProfileSpeakerNameRecognitionAsync(
                row.Source,
                DateTimeOffset.UtcNow,
                _lifetimeCts.Token);
            await RefreshMeetingListAsync(row.Source.Stem);
            await RefreshVoiceProfileSettingsAsync();
            RefreshOpenMeetingDetailWindow();
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.UndoProfileNames).SuccessText);
            AppendActivity($"Profile-applied names were removed for '{row.Title}'.");
        }
        catch (Exception exception)
        {
            _logger.Log($"Profile-applied speaker-name undo failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.UndoProfileNames,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Profile-applied speaker names were not removed.");
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private static SpeakerNameRecognitionOptions BuildSpeakerNameRecognitionOptions(AppConfig config)
    {
        return new SpeakerNameRecognitionOptions(
            config.SpeakerNameAutoApplyConfidenceThreshold,
            config.SpeakerNameSuggestionConfidenceThreshold,
            config.SpeakerNameMatchMarginThreshold);
    }

    private static SpeakerNameReviewRequest? BuildSpeakerNameReviewRequest(
        IEnumerable<SpeakerNameReviewRow> rows)
    {
        var pendingRows = rows
            .Where(row =>
                row.RejectSuggestion ||
                !string.Equals(row.OriginalLabel.Trim(), row.EditedLabel.Trim(), StringComparison.Ordinal))
            .ToArray();
        if (pendingRows.Length == 0 ||
            pendingRows.Any(row =>
                string.IsNullOrWhiteSpace(row.SpeakerId) ||
                string.IsNullOrWhiteSpace(row.ArtifactRevision)))
        {
            return null;
        }

        var revisions = pendingRows
            .Select(row => row.ArtifactRevision)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (revisions.Length != 1)
        {
            return null;
        }

        return new SpeakerNameReviewRequest(
            revisions[0],
            pendingRows.Select(row => new SpeakerNameCorrectionDraft(
                row.SpeakerId!,
                row.OriginalLabel,
                row.EditedLabel,
                row.ProfileId,
                row.ExpectedNameSource,
                row.RejectSuggestion,
                row.ExpectedSuggestedDisplayName)).ToArray());
    }

    private static bool HasProfileSpeakerNameAttribution(SpeakerLabelInfo label)
    {
        return !string.IsNullOrWhiteSpace(label.ProfileId) &&
            label.NameSource is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile;
    }

    private async Task ArchiveOpenMeetingDetailAsync()
    {
        if (GetOpenMeetingDetailRow() is { } row)
        {
            var archived = await ArchiveMeetingsAsync([row], "detail-archive");
            if (archived)
            {
                _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                    UserActionIntent.ArchiveMeetings).SuccessText);
            }
        }
    }

    private async Task DeleteOpenMeetingDetailAsync()
    {
        var row = GetOpenMeetingDetailRow();
        if (row is null || !TryConfirmPermanentDelete([row]))
        {
            _meetingDetailWindow?.SetMaintenanceStatus("Permanent delete cancelled.");
            return;
        }

        _isDeletingMeetings = true;
        UpdateMeetingActionState();
        _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
            UserActionIntent.DeleteMeetings).ProgressText);
        try
        {
            await _meetingCleanupExecutionService.DeleteMeetingPermanentlyAsync(row.Source, _lifetimeCts.Token);
            AppendActivity($"Permanently deleted published meeting '{row.Title}'.");
            _openMeetingDetailStem = null;
            await RefreshMeetingListAsync();
            _meetingDetailWindow?.Close();
        }
        catch (Exception exception)
        {
            _logger.Log($"Permanent meeting deletion from detail failed: {exception}");
            _meetingDetailWindow?.SetMaintenanceStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.DeleteMeetings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Permanent meeting deletion did not finish.");
        }
        finally
        {
            _isDeletingMeetings = false;
            UpdateMeetingActionState();
        }
    }

    private void UpdateMeetingProjectEditor(IReadOnlyList<MeetingListRow> selectedMeetings, bool preserveDraftInputs = false)
    {
        var recentProjects = GetRecentMeetingProjectNames();
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
            _logger.Log($"Meeting project update failed: {exception}");
            SelectedMeetingProjectStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateMeetingProject,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting project update did not finish.");
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

        var labels = _meetingOutputCatalogService.ListSpeakerLabelDetails(row.Source);
        var speakerArtifactRevision = MeetingOutputCatalogService.GetSpeakerArtifactRevision(row.Source);
        var labelRows = labels
            .Select(label => new SpeakerLabelEditorRow(label, speakerArtifactRevision, UpdateMeetingActionState))
            .ToArray();

        SpeakerLabelsEditorDataGrid.ItemsSource = labelRows;
        SpeakerNamesStatusTextBlock.Text = labelRows.Length == 0
            ? "This transcript does not currently contain Diarization Labels."
            : "Edit Meeting Display Names below. These changes are separate from local Name Suggestions and speaker-label repair.";
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
        var catalogState = ResolveMeetingActionCatalogState(
            selectedMeetings,
            singleSelectedMeeting,
            isMeetingActionInProgress);
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
        var selectionCommandState = MainWindowInteractionLogic.BuildMeetingSelectionCommandState(
            selectedMeetings.Length,
            singleSelectedMeeting is not null,
            singleSelectedMeeting?.CanOpenAudioArtifact == true,
            singleSelectedMeeting?.CanOpenTranscriptArtifact == true,
            visibleCleanupRecommendationRows,
            isMeetingActionInProgress);

        RefreshMeetingsButton.Content = Volatile.Read(ref _meetingBaselineRefreshOperations) > 0
            ? "Refreshing..."
            : "Refresh List";
        MeetingSelectionSummaryTextBlock.Text = selectionCommandState.SummaryText;
        MeetingSelectionBulkHintTextBlock.Visibility = selectionCommandState.ShowBulkMeetingCommands
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenMeetingDetailsButton.IsEnabled = catalogState[MeetingActionId.OpenDetails].IsEligible;
        OpenSelectedTranscriptLibraryButton.IsEnabled = catalogState[MeetingActionId.OpenTranscript].IsEligible;
        OpenSelectedAudioLibraryButton.IsEnabled = catalogState[MeetingActionId.OpenAudio].IsEligible;
        OpenSelectedFolderLibraryButton.IsEnabled = catalogState[MeetingActionId.OpenContainingFolder].IsEligible;
        ReviewCleanupSuggestionsLibraryButton.IsEnabled = selectionCommandState.CanReviewCleanup;
        RenameSelectedMeetingButton.Content = _isRenamingMeeting ? "Renaming..." : "Rename Meeting";
        ApplySpeakerNamesButton.Content = _isApplyingSpeakerNames ? "Applying..." : "Apply Name Changes";
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
        LegacyMeetingActionDraftsBorder.Visibility = Visibility.Collapsed;
        LegacyMeetingActionDraftsExpander.IsExpanded = false;
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
        RenameMeetingActionButton.IsEnabled = catalogState[MeetingActionId.Rename].IsEligible;
        SuggestMeetingTitleActionButton.IsEnabled = catalogState[MeetingActionId.SuggestTitle].IsEligible;
        RetryTranscriptActionButton.IsEnabled = catalogState[MeetingActionId.RetryTranscript].IsEligible;
        var isSelectedMeetingAsap = IsMeetingMarkedAsap(singleSelectedMeeting);
        ProcessAsapActionButton.Content = isSelectedMeetingAsap
            ? "Clear ASAP"
            : _isUpdatingRushProcessing
                ? "Updating..."
                : "Process This ASAP...";
        ProcessAsapActionButton.Visibility = catalogState[MeetingActionId.ProcessAsap].IsEligible ||
            catalogState[MeetingActionId.ClearAsap].IsEligible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProcessAsapActionButton.IsEnabled = catalogState[MeetingActionId.ProcessAsap].IsEligible ||
            catalogState[MeetingActionId.ClearAsap].IsEligible;
        SplitMeetingActionButton.IsEnabled = catalogState[MeetingActionId.Split].IsEligible && canEditSplitPoint;
        MergeMeetingsActionButton.IsEnabled = catalogState[MeetingActionId.MergeSelected].IsEligible;
        SelectedMeetingTitleTextBox.IsEnabled = catalogState[MeetingActionId.Rename].IsEligible;
        SelectedMeetingProjectComboBox.IsEnabled = catalogState[MeetingActionId.EditProject].IsEligible;
        RenameSelectedMeetingButton.IsEnabled = singleSelectedMeeting is not null &&
            catalogState[MeetingActionId.Rename].IsEligible &&
            MainWindowInteractionLogic.HasPendingMeetingRename(singleSelectedMeeting.Title, SelectedMeetingTitleTextBox.Text);
        ApplyMeetingProjectButton.Content = _isUpdatingMeetingProject ? "Applying..." : "Apply Project";
        ClearMeetingProjectButton.Content = _isUpdatingMeetingProject ? "Clearing..." : "Clear";
        ApplyMeetingProjectButton.IsEnabled = catalogState[MeetingActionId.EditProject].IsEligible &&
            !string.IsNullOrWhiteSpace(SelectedMeetingProjectComboBox.Text);
        ClearMeetingProjectButton.IsEnabled = catalogState[MeetingActionId.EditProject].IsEligible &&
            selectedMeetings.Any(row => !string.IsNullOrWhiteSpace(row.Source.ProjectName));
        ApplySpeakerNamesButton.IsEnabled = singleSelectedMeeting is not null &&
            !isMeetingActionInProgress &&
            HasPendingSpeakerLabelChanges();
        SplitSelectedMeetingPointTextBox.IsEnabled = canEditSplitPoint;
        SplitSelectedMeetingSlider.IsEnabled = canEditSplitPoint;
        SplitSelectedMeetingButton.IsEnabled = splitMeeting is not null &&
            catalogState[MeetingActionId.Split].IsEligible &&
            canEditSplitPoint &&
            MainWindowInteractionLogic.TryParseMeetingSplitPoint(
                SplitSelectedMeetingPointTextBox.Text,
                splitMeeting.Source.Duration,
                out _,
                out _);
        MergeSelectedMeetingsTitleTextBox.IsEnabled = catalogState[MeetingActionId.MergeSelected].IsEligible;
        MergeSelectedMeetingsButton.IsEnabled = catalogState[MeetingActionId.MergeSelected].IsEligible &&
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
        _meetingDetailWindow?.SetMaintenanceBusy(isMeetingActionInProgress);
        UpdateMeetingsContextMenuState();
        UpdateExternalAudioImportReviewState();
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
               _isGeneratingMeetingSummary ||
               _isMergingMeetings ||
               _isSplittingMeeting ||
               _isArchivingMeetings ||
               _isUpdatingRushProcessing ||
               _isDeletingMeetings ||
               _isApplyingMeetingCleanupRecommendations ||
               _isDismissingMeetingCleanupRecommendations ||
               _isApplyingSafeMeetingCleanupFixes ||
               _isRushingBacklog ||
               _isQueueingExternalAudioImports;
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

    private static string BuildSpeakerNameProvenanceText(SpeakerLabelInfo label)
    {
        var confidenceText = label.Confidence is { } confidence
            ? $" {confidence:P0}"
            : string.Empty;
        var reasonText = label.DecisionReason is { } reason && reason != SpeakerNameDecisionReason.None
            ? $" - {FormatSpeakerNameDecisionReason(reason)}"
            : string.Empty;

        return label.NameSource switch
        {
            SpeakerNameSource.UserEdited => "User-edited Meeting Display Name",
            SpeakerNameSource.AutoAppliedVoiceProfile => $"Auto-applied local Voice Profile{confidenceText}{reasonText}",
            SpeakerNameSource.SuggestedVoiceProfile when !string.IsNullOrWhiteSpace(label.SuggestedDisplayName) =>
                $"Suggested local Voice Profile: {label.SuggestedDisplayName}{confidenceText}{reasonText}",
            SpeakerNameSource.SuggestedVoiceProfile => $"Suggested local Voice Profile{confidenceText}{reasonText}",
            _ => "Anonymous Diarization Label",
        };
    }

    private static string FormatSpeakerNameDecisionReason(SpeakerNameDecisionReason reason)
    {
        return reason switch
        {
            SpeakerNameDecisionReason.AutoAppliedHighConfidence => "high confidence",
            SpeakerNameDecisionReason.SuggestedBelowAutoApplyThreshold => "below auto-apply threshold",
            SpeakerNameDecisionReason.SuggestedAmbiguousProfileMargin => "ambiguous profile margin",
            SpeakerNameDecisionReason.SuggestedProfileNeedsMoreSamples => "needs more confirmed samples",
            SpeakerNameDecisionReason.SuggestedSampleTooShort => "sample too short",
            SpeakerNameDecisionReason.SuggestedDuplicateProfileCandidate => "same profile already matched another speaker",
            _ => "review needed",
        };
    }

    private void LoadConfigEditorValues(AppConfig config)
    {
        ConfigAudioOutputDirTextBox.Text = config.AudioOutputDir;
        ConfigTranscriptOutputDirTextBox.Text = config.TranscriptOutputDir;
        ConfigWorkDirTextBox.Text = config.WorkDir;
        ConfigImportInboxDirTextBox.Text = config.ImportInboxDir;
        ConfigImportInboxEnabledCheckBox.IsChecked = config.ImportInboxEnabled;
        ConfigImportInboxArchiveAfterQueueEnabledCheckBox.IsChecked = config.ImportInboxArchiveAfterQueueEnabled;
        ConfigImportInboxMoveBlockedToErrorEnabledCheckBox.IsChecked = config.ImportInboxMoveBlockedToErrorEnabled;
        UpdateImportInboxStatus(config);
        ConfigModelStorageSummaryTextBlock.Text = config.ModelCacheDir;
        ConfigTranscriptionStorageTextBlock.Text = config.TranscriptionModelPath;
        ConfigSpeakerLabelingStorageTextBlock.Text = config.DiarizationAssetPath;
        ConfigDiarizationGpuAccelerationCheckBox.IsChecked = config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto;
        ConfigAutoDetectThresholdTextBox.Text = config.AutoDetectAudioPeakThreshold.ToString("0.###", CultureInfo.InvariantCulture);
        ConfigMeetingStopTimeoutTextBox.Text = config.MeetingStopTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ConfigMicCaptureCheckBox.IsChecked = config.MicCaptureEnabled;
        ConfigSpeakerNameLearningCheckBox.IsChecked = config.SpeakerNameLearningMode == SpeakerNameLearningMode.LocalAutoLearn;
        ConfigLaunchOnLoginCheckBox.IsChecked = config.LaunchOnLoginEnabled;
        ConfigAutoDetectCheckBox.IsChecked = config.AutoDetectEnabled;
        ConfigCalendarTitleFallbackCheckBox.IsChecked = config.CalendarTitleFallbackEnabled;
        ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked = config.MeetingAttendeeEnrichmentEnabled;
        ConfigUpdateCheckEnabledCheckBox.IsChecked = config.UpdateCheckEnabled;
        ConfigAutoInstallUpdatesCheckBox.IsChecked = config.AutoInstallUpdatesEnabled;
        ConfigUpdateFeedUrlTextBox.Text = config.UpdateFeedUrl;
        ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue = config.PreferredTeamsIntegrationMode;
        ConfigBacklogAccelerationProfileComboBox.SelectedValue = config.BacklogAccelerationProfile;
        ConfigInitialProcessingStrategyComboBox.SelectedValue = config.InitialProcessingStrategy;
        ConfigOvernightInitialProcessingStrategyComboBox.SelectedValue = config.OvernightInitialProcessingStrategy;
        ApplyIncrementalWorkPlanToEditor(config.IncrementalWorkPlan);
        ConfigOvernightDrainStartTextBox.Text = config.OvernightDrainStartLocal;
        ConfigOvernightDrainEndTextBox.Text = config.OvernightDrainEndLocal;
        ConfigTranscriptionProviderPreferenceComboBox.SelectedValue = config.TranscriptionProviderPreference;
        ConfigTranscriptionCliPathTextBox.Text = config.TranscriptionCliPath;
        ConfigTranscriptionCliArgumentsTextBox.Text = config.TranscriptionCliArguments;
        ConfigDiarizationProviderPreferenceComboBox.SelectedValue = config.DiarizationProviderPreference;
        ConfigDiarizationCliPathTextBox.Text = config.DiarizationCliPath;
        ConfigDiarizationCliArgumentsTextBox.Text = config.DiarizationCliArguments;
        ConfigBackgroundProcessingModeComboBox.SelectedValue = config.BackgroundProcessingMode;
        UpdateProcessingSchedulePresentation(config);
        ConfigSummaryGenerationEnabledCheckBox.IsChecked = config.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled;
        ConfigSummaryProviderPreferenceComboBox.SelectedValue = config.SummaryProviderPreference;
        ConfigSummaryModelProxyBaseUrlTextBox.Text = config.SummaryModelProxyBaseUrl;
        ConfigSummaryModelProxyModelTextBox.Text = config.SummaryModelProxyModel;
        ConfigSummaryOpenAiModelTextBox.Text = config.SummaryOpenAiModel;
        PopulateSummaryReasoningEffortChoices(config.SummaryReasoningEffort);
        PopulateSummaryModelChoices(
            ConfigSummaryModelProxyModelComboBox,
            config.SummaryModelProxyModel,
            _modelProxySummaryModels,
            _modelProxySummaryDefaultModel);
        PopulateSummaryModelChoices(
            ConfigSummaryOpenAiModelComboBox,
            config.SummaryOpenAiModel,
            _openAiSummaryModels,
            _openAiSummaryDefaultModel);
        ConfigSummaryRequestTimeoutTextBox.Text = config.SummaryRequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ConfigSummaryTranscriptChunkTargetTextBox.Text = config.SummaryTranscriptChunkTokenTarget.ToString(CultureInfo.InvariantCulture);
        ConfigSummaryTranscriptChunkOverlapTextBox.Text = config.SummaryTranscriptChunkOverlapTokens.ToString(CultureInfo.InvariantCulture);
        SetSpeakerLabelingModeSelectors(config.BackgroundSpeakerLabelingMode);
        ConfigInitialProcessingStrategyHelpTextBlock.Text =
            MainWindowInteractionLogic.BuildInitialProcessingStrategyHelpText(config.InitialProcessingStrategy);
        UpdateExternalProviderStatusText(config);
        UpdateBackgroundProcessingModeHelpText(config.BackgroundProcessingMode);
        UpdateDiarizationAccelerationStatusText(config, _currentDiarizationAssetStatus);
        UpdateTeamsIntegrationProbePresentation(config);
        _ = RefreshSummaryProviderSecretStatusAsync();
        _ = RefreshVoiceProfileSettingsAsync();
    }

    private void UpdateImportInboxStatus(AppConfig config)
    {
        if (!config.ImportInboxEnabled)
        {
            ConfigImportInboxStatusTextBlock.Text =
                "Paused. Save enabled Inbox settings before any Inbox audio is scanned.";
            return;
        }

        var pathValidation = ImportInboxPathPolicy.Validate(
            config.ImportInboxDir,
            [config.AudioOutputDir, config.TranscriptOutputDir, config.WorkDir]);
        if (!pathValidation.IsValid)
        {
            ConfigImportInboxStatusTextBlock.Text = pathValidation.Message;
            return;
        }

        var health = ImportInboxPathPolicy.CheckStorageHealth(config.ImportInboxDir, requiredBytes: 0);
        ConfigImportInboxStatusTextBlock.Text = health.IsReady
            ? $"Ready. Scans top-level audio every {config.ImportInboxScanIntervalSeconds} seconds; original files stay in the Inbox."
            : health.Message;
    }

    private async void RescanImportInboxButton_OnClick(object sender, RoutedEventArgs e)
    {
        var config = _liveConfig.Current;
        if (!config.ImportInboxEnabled)
        {
            ConfigImportInboxStatusTextBlock.Text =
                "Save enabled Inbox settings before requesting a scan.";
            return;
        }

        if (!HasReadyTranscriptionModel())
        {
            ConfigImportInboxStatusTextBlock.Text =
                "Finish transcription setup before Inbox audio can be queued.";
            return;
        }

        RescanImportInboxButton.IsEnabled = false;
        try
        {
            _lastImportInboxReconciliationUtc = null;
            await RunExternalAudioImportCycleAsync("manual Inbox rescan", _lifetimeCts.Token);
            UpdateImportInboxStatus(_liveConfig.Current);
        }
        finally
        {
            RescanImportInboxButton.IsEnabled = true;
        }
    }

    private void OpenImportInboxButton_OnClick(object sender, RoutedEventArgs e)
    {
        var config = _liveConfig.Current;
        var pathValidation = ImportInboxPathPolicy.Validate(
            config.ImportInboxDir,
            [config.AudioOutputDir, config.TranscriptOutputDir, config.WorkDir]);
        if (!pathValidation.IsValid)
        {
            ConfigImportInboxStatusTextBlock.Text = pathValidation.Message;
            return;
        }

        try
        {
            Directory.CreateDirectory(config.ImportInboxDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = config.ImportInboxDir,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            _logger.Log("Opening the Import Inbox failed.");
            ConfigImportInboxStatusTextBlock.Text =
                "Meeting Recorder could not open the Import Inbox folder.";
        }
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
        ConfigBackgroundProcessingModeComboBox.ItemsSource = MainWindowInteractionLogic
            .BuildBackgroundProcessingModeOptions(Environment.ProcessorCount)
            .Select(option => new SelectionOption<BackgroundProcessingMode>(option.Value, option.Label))
            .ToArray();

        ConfigBacklogAccelerationProfileComboBox.DisplayMemberPath = nameof(SelectionOption<BacklogAccelerationProfile>.Label);
        ConfigBacklogAccelerationProfileComboBox.SelectedValuePath = nameof(SelectionOption<BacklogAccelerationProfile>.Value);
        ConfigBacklogAccelerationProfileComboBox.ItemsSource = BacklogAccelerationProfileResolver
            .GetOptions()
            .Select(option => new SelectionOption<BacklogAccelerationProfile>(option.Value, option.Label))
            .ToArray();

        foreach (var comboBox in new[]
                 {
                     ConfigInitialProcessingStrategyComboBox,
                     ConfigOvernightInitialProcessingStrategyComboBox,
                 })
        {
            comboBox.DisplayMemberPath = nameof(SelectionOption<InitialProcessingStrategy>.Label);
            comboBox.SelectedValuePath = nameof(SelectionOption<InitialProcessingStrategy>.Value);
            comboBox.ItemsSource = MainWindowInteractionLogic
                .BuildInitialProcessingStrategyOptions()
                .Select(option => new SelectionOption<InitialProcessingStrategy>(option.Value, option.Label))
                .ToArray();
        }

        ConfigTranscriptionProviderPreferenceComboBox.DisplayMemberPath = nameof(SelectionOption<TranscriptionProviderPreference>.Label);
        ConfigTranscriptionProviderPreferenceComboBox.SelectedValuePath = nameof(SelectionOption<TranscriptionProviderPreference>.Value);
        ConfigTranscriptionProviderPreferenceComboBox.ItemsSource = MainWindowInteractionLogic
            .BuildTranscriptionProviderOptions()
            .Select(option => new SelectionOption<TranscriptionProviderPreference>(option.Value, option.Label))
            .ToArray();

        ConfigDiarizationProviderPreferenceComboBox.DisplayMemberPath = nameof(SelectionOption<DiarizationProviderPreference>.Label);
        ConfigDiarizationProviderPreferenceComboBox.SelectedValuePath = nameof(SelectionOption<DiarizationProviderPreference>.Value);
        ConfigDiarizationProviderPreferenceComboBox.ItemsSource = MainWindowInteractionLogic
            .BuildDiarizationProviderOptions()
            .Select(option => new SelectionOption<DiarizationProviderPreference>(option.Value, option.Label))
            .ToArray();

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

        ConfigSummaryProviderPreferenceComboBox.DisplayMemberPath = nameof(SelectionOption<MeetingSummaryProviderPreference>.Label);
        ConfigSummaryProviderPreferenceComboBox.SelectedValuePath = nameof(SelectionOption<MeetingSummaryProviderPreference>.Value);
        ConfigSummaryProviderPreferenceComboBox.ItemsSource = new[]
        {
            new SelectionOption<MeetingSummaryProviderPreference>(MeetingSummaryProviderPreference.LocalThenOpenAi, "Local then OpenAI"),
            new SelectionOption<MeetingSummaryProviderPreference>(MeetingSummaryProviderPreference.LocalOnly, "Local only"),
            new SelectionOption<MeetingSummaryProviderPreference>(MeetingSummaryProviderPreference.OpenAiOnly, "OpenAI only"),
        };

        ConfigSummaryReasoningEffortComboBox.DisplayMemberPath = nameof(SelectionOption<SummaryReasoningEffort>.Label);
        ConfigSummaryReasoningEffortComboBox.SelectedValuePath = nameof(SelectionOption<SummaryReasoningEffort>.Value);
        ConfigSummaryReasoningEffortComboBox.ItemsSource = new[]
        {
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.ProviderDefault, "Provider default"),
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.Minimal, "Minimal"),
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.Low, "Low"),
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.Medium, "Medium (recommended)"),
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.High, "High"),
            new SelectionOption<SummaryReasoningEffort>(SummaryReasoningEffort.XHigh, "Extra high"),
        };
    }

    private void RegisterConfigEditorChangeHandlers()
    {
        foreach (var textBox in new[]
                 {
                     ConfigAudioOutputDirTextBox,
                     ConfigTranscriptOutputDirTextBox,
                     ConfigWorkDirTextBox,
                     ConfigImportInboxDirTextBox,
                     ConfigAutoDetectThresholdTextBox,
                     ConfigMeetingStopTimeoutTextBox,
                     ConfigUpdateFeedUrlTextBox,
                     ConfigOvernightDrainStartTextBox,
                     ConfigOvernightDrainEndTextBox,
                     ConfigTranscriptionCliPathTextBox,
                     ConfigTranscriptionCliArgumentsTextBox,
                     ConfigDiarizationCliPathTextBox,
                     ConfigDiarizationCliArgumentsTextBox,
                     ConfigSummaryModelProxyBaseUrlTextBox,
                     ConfigSummaryModelProxyModelTextBox,
                     ConfigSummaryOpenAiModelTextBox,
                     ConfigSummaryRequestTimeoutTextBox,
                     ConfigSummaryTranscriptChunkTargetTextBox,
                     ConfigSummaryTranscriptChunkOverlapTextBox,
                 })
        {
            textBox.TextChanged += ConfigEditorValueChanged;
        }

        foreach (var checkBox in new[]
                 {
                     ConfigMicCaptureCheckBox,
                     ConfigDiarizationGpuAccelerationCheckBox,
                     ConfigSpeakerNameLearningCheckBox,
                     ConfigLaunchOnLoginCheckBox,
                     ConfigAutoDetectCheckBox,
                     ConfigCalendarTitleFallbackCheckBox,
                     ConfigMeetingAttendeeEnrichmentCheckBox,
                     ConfigUpdateCheckEnabledCheckBox,
                     ConfigAutoInstallUpdatesCheckBox,
                     ConfigImportInboxEnabledCheckBox,
                     ConfigImportInboxArchiveAfterQueueEnabledCheckBox,
                     ConfigImportInboxMoveBlockedToErrorEnabledCheckBox,
                     ConfigSummaryGenerationEnabledCheckBox,
                     ConfigIncrementalQueuedRecordingsCheckBox,
                     ConfigIncrementalSpeakerLabelsCheckBox,
                     ConfigIncrementalAiSummariesCheckBox,
                     ConfigIncrementalSafeCleanupCheckBox,
                 })
        {
            checkBox.Checked += ConfigEditorValueChanged;
            checkBox.Unchecked += ConfigEditorValueChanged;
        }

        ConfigPreferredTeamsIntegrationModeComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigBacklogAccelerationProfileComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigInitialProcessingStrategyComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigOvernightInitialProcessingStrategyComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigTranscriptionProviderPreferenceComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigDiarizationProviderPreferenceComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigBackgroundProcessingModeComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigBackgroundSpeakerLabelingModeComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigSummaryProviderPreferenceComboBox.SelectionChanged += ConfigEditorValueChanged;
        ConfigSummaryReasoningEffortComboBox.SelectionChanged += ConfigEditorValueChanged;
    }

    private void ConfigEditorValueChanged(object sender, RoutedEventArgs e)
    {
        UpdateConfigActionState();
    }

    private void ConfigEditorValueChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingSummaryModelSelectors)
        {
            return;
        }

        UpdateConfigModeHelpTextFromSelection();
        if (ReferenceEquals(sender, ConfigSummaryProviderPreferenceComboBox))
        {
            PopulateSummaryReasoningEffortChoices(
                ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort selectedEffort
                    ? selectedEffort
                    : _liveConfig.Current.SummaryReasoningEffort);
        }
        if (ReferenceEquals(sender, ConfigSummaryReasoningEffortComboBox))
        {
            MarkSummaryProviderValidationStale();
        }
        UpdateConfigActionState();
    }

    private void ConfigEditorValueChanged(object sender, TextChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ConfigSummaryModelProxyBaseUrlTextBox) ||
            ReferenceEquals(sender, ConfigSummaryModelProxyModelTextBox) ||
            ReferenceEquals(sender, ConfigSummaryOpenAiModelTextBox))
        {
            MarkSummaryProviderValidationStale();
        }
        UpdateConfigActionState();
    }

    private void UpdateConfigActionState()
    {
        UpdateConfigDependencyState();
        UpdateConfigModeHelpTextFromSelection();
        UpdateIncrementalWorkAvailability();
        UpdateProcessingSchedulePresentation(_liveConfig.Current with
        {
            InitialProcessingStrategy = ConfigInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy initialStrategy
                ? initialStrategy
                : _liveConfig.Current.InitialProcessingStrategy,
            OvernightInitialProcessingStrategy = ConfigOvernightInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy overnightStrategy
                ? overnightStrategy
                : _liveConfig.Current.OvernightInitialProcessingStrategy,
            IncrementalWorkPlan = BuildIncrementalWorkPlanFromEditor(),
            OvernightDrainStartLocal = ConfigOvernightDrainStartTextBox.Text.Trim(),
            OvernightDrainEndLocal = ConfigOvernightDrainEndTextBox.Text.Trim(),
        });
        UpdateExternalProviderStatusTextFromEditor();
        var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(
            _liveConfig.Current,
            ReadConfigEditorSnapshot());

        _settingsWindow?.SetSaveActionState(
            _isSavingConfig ? "Saving..." : "Save Changes",
            !_isSavingConfig && hasPendingChanges);
        UpdateTeamsIntegrationProbeActionState();
        UpdateDiarizationGpuTestActionState();
        UpdateExternalProviderTestActionState();
        UpdateSummaryProviderValidationActionState();
    }

    private void UpdateDiarizationGpuTestActionState()
    {
        var isReady = _currentDiarizationAssetStatus?.IsReady ??
            _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady;
        TestDiarizationGpuAccelerationButton.Content = _isTestingDiarizationGpuAcceleration
            ? "Testing GPU..."
            : "Test GPU";
        TestDiarizationGpuAccelerationButton.IsEnabled = !_isTestingDiarizationGpuAcceleration && isReady;
    }

    private void UpdateExternalProviderTestActionState()
    {
        TestTranscriptionCliProviderButton.Content = _isTestingTranscriptionCliProvider ? "Testing..." : "Test";
        TestDiarizationCliProviderButton.Content = _isTestingDiarizationCliProvider ? "Testing..." : "Test";
        TestTranscriptionCliProviderButton.IsEnabled = !_isTestingTranscriptionCliProvider && !_isSavingConfig;
        TestDiarizationCliProviderButton.IsEnabled = !_isTestingDiarizationCliProvider && !_isSavingConfig;
    }

    private void SetConfigSaveStatus(string statusText)
    {
        ConfigSaveStatusTextBlock.Text = statusText;
        _settingsWindow?.SetFooterStatus(statusText);
    }

    private async Task SavePendingSummaryProviderSecretsAsync(CancellationToken cancellationToken)
    {
        var openAiSecret = ConfigSummaryOpenAiKeyPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(openAiSecret))
        {
            await _summarySecretStore.SaveAsync(SummarySecretKind.OpenAi, openAiSecret, cancellationToken);
            ConfigSummaryOpenAiKeyPasswordBox.Clear();
        }

        await RefreshSummaryProviderSecretStatusAsync();
    }

    private async Task RefreshSummaryProviderSecretStatusAsync()
    {
        try
        {
            var hasOpenAiKey = await _summarySecretStore.HasSecretAsync(SummarySecretKind.OpenAi, _lifetimeCts.Token);
            ConfigSummaryOpenAiKeyStatusTextBlock.Text = hasOpenAiKey
                ? "OpenAI key: saved."
                : "OpenAI key: not saved.";
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"Summary credential status check failed: {exception.GetType().Name}");
            ConfigSummaryProviderStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ConfigureSummary,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
        }
    }

    private async Task RefreshVoiceProfileSettingsAsync()
    {
        try
        {
            var document = await _voiceProfileStore.LoadOrCreateAsync(_lifetimeCts.Token);
            var rows = document.Profiles
                .Select(profile => new VoiceProfileSettingsRow(profile))
                .ToArray();
            VoiceProfilesDataGrid.ItemsSource = rows;
            var experience = SpeakerExperienceResolver.Resolve(new SpeakerExperienceInput(
                SpeakerExperienceSurface.Settings,
                HasMeetingManifest: false,
                HasTranscript: false,
                HasDiarizationLabels: false,
                IsLabelingQueued: false,
                IsLabelingRunning: false,
                HasSuspiciousLabels: false,
                IsRepairEligible: false,
                HasVoiceSamples: false,
                IsLocalProfileStoreAvailable: true,
                ActiveVoiceProfileCount: document.Profiles.Count(profile => profile.Status == VoiceProfileStatus.Active),
                LearningMode: _liveConfig.Current.SpeakerNameLearningMode,
                NameSuggestionCount: 0,
                HasProfileAttribution: false,
                RequiresRefresh: false));
            ConfigSpeakerNameLearningStatusTextBlock.Text = rows.Length == 0
                ? $"No local Voice Profiles have been taught yet. {experience.Explanation}"
                : $"Stored {rows.Length} local Voice Profile(s). {experience.Explanation}";
            UpdateVoiceProfileActionState();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception)
        {
            ConfigSpeakerNameLearningStatusTextBlock.Text = SpeakerExperienceResolver.Resolve(new SpeakerExperienceInput(
                SpeakerExperienceSurface.Settings,
                HasMeetingManifest: false,
                HasTranscript: false,
                HasDiarizationLabels: false,
                IsLabelingQueued: false,
                IsLabelingRunning: false,
                HasSuspiciousLabels: false,
                IsRepairEligible: false,
                HasVoiceSamples: false,
                IsLocalProfileStoreAvailable: false,
                ActiveVoiceProfileCount: 0,
                LearningMode: _liveConfig.Current.SpeakerNameLearningMode,
                NameSuggestionCount: 0,
                HasProfileAttribution: false,
                RequiresRefresh: false)).Explanation;
            UpdateVoiceProfileActionState();
        }
    }

    private async void EnableVoiceProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (VoiceProfilesDataGrid.SelectedItem is not VoiceProfileSettingsRow row)
        {
            return;
        }

        try
        {
            await _voiceProfileStore.EnableProfileAsync(row.ProfileId, _lifetimeCts.Token);
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.EnableVoiceProfile).SuccessText;
            await RefreshVoiceProfileSettingsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log($"Enable local Voice Profile failed: {exception}");
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.EnableVoiceProfile,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
        }
    }

    private async void DisableVoiceProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (VoiceProfilesDataGrid.SelectedItem is not VoiceProfileSettingsRow row)
        {
            return;
        }

        try
        {
            await _voiceProfileStore.DisableProfileAsync(row.ProfileId, _lifetimeCts.Token);
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DisableVoiceProfile).SuccessText;
            await RefreshVoiceProfileSettingsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log($"Disable local Voice Profile failed: {exception}");
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DisableVoiceProfile,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
        }
    }

    private async void DeleteVoiceProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (VoiceProfilesDataGrid.SelectedItem is not VoiceProfileSettingsRow row)
        {
            return;
        }

        try
        {
            await _voiceProfileStore.DeleteProfileAsync(row.ProfileId, _lifetimeCts.Token);
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DeleteVoiceProfile).SuccessText;
            await RefreshVoiceProfileSettingsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log($"Delete local Voice Profile failed: {exception}");
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DeleteVoiceProfile,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
        }
    }

    private async void DeleteAllVoiceProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var copy = UserActionCopyResolver.Resolve(UserActionIntent.DeleteAllVoiceProfiles);
        var confirmed = MessageBox.Show(
            this,
            $"{copy.HelperText} {copy.ConfirmationText}",
            copy.Label,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _voiceProfileStore.DeleteAllAsync(_lifetimeCts.Token);
            ConfigSpeakerNameLearningStatusTextBlock.Text = copy.SuccessText;
            await RefreshVoiceProfileSettingsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Log($"Delete all local Voice Profiles failed: {exception}");
            ConfigSpeakerNameLearningStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.DeleteAllVoiceProfiles,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
        }
    }

    private void VoiceProfilesDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVoiceProfileActionState();
    }

    private void UpdateVoiceProfileActionState()
    {
        var selectedProfile = VoiceProfilesDataGrid.SelectedItem as VoiceProfileSettingsRow;
        ApplyVoiceProfileActionState(
            EnableVoiceProfileButton,
            UserActionIntent.EnableVoiceProfile,
            selectedProfile?.Status == VoiceProfileStatus.Disabled.ToString(),
            selectedProfile is null
                ? UserActionBlockedReasonKind.SelectionRequired
                : UserActionBlockedReasonKind.ActionNotEligible);
        ApplyVoiceProfileActionState(
            DisableVoiceProfileButton,
            UserActionIntent.DisableVoiceProfile,
            selectedProfile?.Status == VoiceProfileStatus.Active.ToString(),
            selectedProfile is null
                ? UserActionBlockedReasonKind.SelectionRequired
                : UserActionBlockedReasonKind.ActionNotEligible);
        ApplyVoiceProfileActionState(
            DeleteVoiceProfileButton,
            UserActionIntent.DeleteVoiceProfile,
            selectedProfile is not null,
            UserActionBlockedReasonKind.SelectionRequired);
        ApplyVoiceProfileActionState(
            DeleteAllVoiceProfilesButton,
            UserActionIntent.DeleteAllVoiceProfiles,
            VoiceProfilesDataGrid.ItemsSource is IEnumerable<VoiceProfileSettingsRow> rows && rows.Any(),
            UserActionBlockedReasonKind.NoEligibleItems);
    }

    private static void ApplyVoiceProfileActionState(
        Button button,
        UserActionIntent intent,
        bool isEnabled,
        UserActionBlockedReasonKind blockedReason)
    {
        var availableCopy = UserActionCopyResolver.Resolve(intent);
        var copy = isEnabled
            ? availableCopy
            : UserActionCopyResolver.Resolve(intent, blockedReason);
        button.Content = availableCopy.Label;
        button.IsEnabled = isEnabled;
        button.ToolTip = isEnabled ? availableCopy.HelperText : copy.BlockedText;
        AutomationProperties.SetHelpText(button, isEnabled ? availableCopy.HelperText : copy.BlockedText);
    }

    private void ConfigSummaryProviderPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ConfigSummaryOpenAiKeyPasswordBox))
        {
            ConfigSummaryOpenAiKeyStatusTextBlock.Text =
                string.IsNullOrWhiteSpace(ConfigSummaryOpenAiKeyPasswordBox.Password)
                    ? ConfigSummaryOpenAiKeyStatusTextBlock.Text
                    : "OpenAI key: new key pending save.";
            MarkSummaryProviderValidationStale();
        }

        UpdateConfigActionState();
    }

    private async void TestDiarizationGpuAccelerationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isTestingDiarizationGpuAcceleration)
        {
            return;
        }

        _isTestingDiarizationGpuAcceleration = true;
        UpdateConfigActionState();
        ConfigDiarizationAccelerationStatusTextBlock.Text = "Testing DirectML GPU initialization with speaker-labeling assets. No transcript or meeting content is sent.";
        SetConfigSaveStatus("Testing speaker-labeling GPU acceleration...");

        try
        {
            var probeMessage = await RunDiarizationDirectMlProbeAsync(_lifetimeCts.Token);
            await EnableDiarizationGpuAccelerationAfterSuccessfulProbeAsync(_lifetimeCts.Token);
            SetConfigSaveStatus("Speaker-labeling GPU test finished.");
            var status = _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath);
            ApplyDiarizationAssetStatus(status);
            ConfigDiarizationAccelerationStatusTextBlock.Text =
                BuildDiarizationGpuProbeSuccessStatusText(_liveConfig.Current, status, probeMessage);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"DirectML speaker-labeling probe failed safely: {exception.GetType().Name}");
            ConfigDiarizationAccelerationStatusTextBlock.Text = IsSafeDirectMlRuntimeUnavailableMessage(exception.Message)
                ? exception.Message
                : "DirectML GPU test failed safely. Speaker labeling will continue to use CPU fallback unless a later test succeeds.";
            SetConfigSaveStatus("Speaker-labeling GPU test failed safely.");
        }
        finally
        {
            _isTestingDiarizationGpuAcceleration = false;
            UpdateConfigActionState();
        }
    }

    private async void TestTranscriptionCliProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        await TestExternalCliProviderAsync(
            isTranscription: true,
            "--probe-transcription-cli",
            value => _isTestingTranscriptionCliProvider = value);
    }

    private async void TestDiarizationCliProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        await TestExternalCliProviderAsync(
            isTranscription: false,
            "--probe-diarization-cli",
            value => _isTestingDiarizationCliProvider = value);
    }

    private async Task TestExternalCliProviderAsync(
        bool isTranscription,
        string probeArgument,
        Action<bool> setTesting)
    {
        setTesting(true);
        UpdateConfigActionState();
        var label = isTranscription ? "transcription" : "speaker-labeling";
        SetConfigSaveStatus($"Testing external {label} provider...");

        try
        {
            await SaveExternalProviderEditorSettingsAsync(isTranscription, _lifetimeCts.Token);
            var message = await RunExternalCliProviderProbeAsync(probeArgument, _lifetimeCts.Token);
            await _liveConfig.ReloadIfChangedAsync(_lifetimeCts.Token);
            UpdateExternalProviderStatusText(_liveConfig.Current);
            SetConfigSaveStatus($"External {label} provider test finished: {message}");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"External {label} provider probe failed safely: {exception.GetType().Name}");
            await _liveConfig.ReloadIfChangedAsync(_lifetimeCts.Token);
            UpdateExternalProviderStatusText(_liveConfig.Current);
            SetConfigSaveStatus($"External {label} provider test failed safely.");
        }
        finally
        {
            setTesting(false);
            UpdateConfigActionState();
        }
    }

    private async Task SaveExternalProviderEditorSettingsAsync(
        bool isTranscription,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        var editorSnapshot = ReadConfigEditorSnapshot();
        var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(currentConfig, editorSnapshot);
        _pendingConfigEditorSnapshotRestore = hasPendingChanges ? editorSnapshot : null;

        var nextConfig = isTranscription
            ? currentConfig with
            {
                TranscriptionProviderPreference = editorSnapshot.TranscriptionProviderPreference,
                TranscriptionCliPath = editorSnapshot.TranscriptionCliPath.Trim(),
                TranscriptionCliArguments = editorSnapshot.TranscriptionCliArguments.Trim(),
            }
            : currentConfig with
            {
                DiarizationProviderPreference = editorSnapshot.DiarizationProviderPreference,
                DiarizationCliPath = editorSnapshot.DiarizationCliPath.Trim(),
                DiarizationCliArguments = editorSnapshot.DiarizationCliArguments.Trim(),
            };

        await _liveConfig.SaveAsync(nextConfig, cancellationToken);
    }

    private async Task<string> RunExternalCliProviderProbeAsync(
        string probeArgument,
        CancellationToken cancellationToken)
    {
        var launch = WorkerLocator.Resolve();
        var arguments = $"{launch.ArgumentPrefix} {probeArgument} --config \"{_liveConfig.ConfigPath}\"".Trim();
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the processing worker for the external provider test.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();
        var message = string.IsNullOrWhiteSpace(standardOutput) ? standardError : standardOutput;
        return string.IsNullOrWhiteSpace(message)
            ? process.ExitCode == 0 ? "Probe succeeded." : "Probe failed."
            : message;
    }

    private async Task EnableDiarizationGpuAccelerationAfterSuccessfulProbeAsync(CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (currentConfig.DiarizationAccelerationPreference != InferenceAccelerationPreference.Auto)
        {
            var editorSnapshot = ReadConfigEditorSnapshot();
            var hasPendingChanges = MainWindowInteractionLogic.HasPendingConfigChanges(currentConfig, editorSnapshot);
            _pendingConfigEditorSnapshotRestore = hasPendingChanges
                ? editorSnapshot with { UseGpuAcceleration = true }
                : null;

            await _liveConfig.SaveAsync(
                currentConfig with
                {
                    DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
                    DiarizationAccelerationSecurityPromptMigrationApplied = true,
                },
                cancellationToken);
        }
        else
        {
            _pendingConfigEditorSnapshotRestore = null;
        }

        ConfigDiarizationGpuAccelerationCheckBox.IsChecked = true;
        UpdateConfigActionState();
    }

    private async Task<string> RunDiarizationDirectMlProbeAsync(CancellationToken cancellationToken)
    {
        var status = _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath);
        if (!status.IsReady)
        {
            throw new InvalidOperationException("Install speaker-labeling assets before testing GPU acceleration.");
        }

        var launch = WorkerLocator.Resolve();
        var arguments = $"{launch.ArgumentPrefix} --probe-directml --config \"{_liveConfig.ConfigPath}\"".Trim();
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the processing worker for the GPU test.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode == 0)
        {
            var workerMessage = string.IsNullOrWhiteSpace(standardOutput)
                ? "DirectML probe succeeded."
                : standardOutput.Trim();
            return workerMessage;
        }

        var workerError = standardError.Trim();
        if (IsSafeDirectMlRuntimeUnavailableMessage(workerError))
        {
            throw new InvalidOperationException(workerError);
        }

        throw new InvalidOperationException("DirectML probe failed.");
    }

    private async void ValidateModelProxySummaryProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isValidatingModelProxySummaryProvider)
        {
            return;
        }

        _isValidatingModelProxySummaryProvider = true;
        UpdateSummaryProviderValidationActionState();
        ConfigModelProxyValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ValidateLocalSummaryProvider).ProgressText;

        try
        {
            var config = BuildSummaryValidationConfigFromEditor(_liveConfig.Current);
            var legacyApiKey = await _summarySecretStore.LoadAsync(SummarySecretKind.ModelProxy, _lifetimeCts.Token);
            var result = await _summaryProviderValidationService.ValidateModelProxyAsync(
                config,
                legacyApiKey,
                _lifetimeCts.Token);
            _summaryProviderValidationIsCurrent = result.Success;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            ConfigModelProxyValidationStatusTextBlock.Text = FormatSummaryProviderValidationStatus(
                result,
                UserActionIntent.ValidateLocalSummaryProvider);
            _logger.Log($"ModelProxy summary provider validation: {result.StatusText}");
            AppendActivity(result.Success
                ? "ModelProxy summary provider validation finished."
                : "ModelProxy summary provider validation did not finish.");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            _summaryProviderValidationIsCurrent = false;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            ConfigModelProxyValidationStatusTextBlock.Text = "Canceled: ModelProxy validation stopped during app shutdown.";
        }
        catch (Exception exception)
        {
            _summaryProviderValidationIsCurrent = false;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            _logger.Log($"ModelProxy summary provider validation failed: {exception.GetType().Name}");
            ConfigModelProxyValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ValidateLocalSummaryProvider,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("ModelProxy summary provider validation did not finish.");
        }
        finally
        {
            _isValidatingModelProxySummaryProvider = false;
            UpdateSummaryProviderValidationActionState();
            UpdateDashboardReadiness();
        }
    }

    private async void ValidateOpenAiSummaryProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isValidatingOpenAiSummaryProvider)
        {
            return;
        }

        _isValidatingOpenAiSummaryProvider = true;
        UpdateSummaryProviderValidationActionState();
        ConfigOpenAiValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ValidateHostedSummaryProvider).ProgressText;

        try
        {
            var config = BuildSummaryValidationConfigFromEditor(_liveConfig.Current);
            var apiKey = !string.IsNullOrWhiteSpace(ConfigSummaryOpenAiKeyPasswordBox.Password)
                ? ConfigSummaryOpenAiKeyPasswordBox.Password
                : await _summarySecretStore.LoadAsync(SummarySecretKind.OpenAi, _lifetimeCts.Token);
            var result = await _summaryProviderValidationService.ValidateOpenAiAsync(
                config,
                apiKey,
                _lifetimeCts.Token);
            _summaryProviderValidationIsCurrent = result.Success;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            ConfigOpenAiValidationStatusTextBlock.Text = FormatSummaryProviderValidationStatus(
                result,
                UserActionIntent.ValidateHostedSummaryProvider);
            _logger.Log($"OpenAI summary provider validation: {result.StatusText}");
            AppendActivity(result.Success
                ? "OpenAI summary provider validation finished."
                : "OpenAI summary provider validation did not finish.");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            _summaryProviderValidationIsCurrent = false;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            ConfigOpenAiValidationStatusTextBlock.Text = "Canceled: OpenAI validation stopped during app shutdown.";
        }
        catch (Exception exception)
        {
            _summaryProviderValidationIsCurrent = false;
            _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
            _logger.Log($"OpenAI summary provider validation failed: {exception.GetType().Name}");
            ConfigOpenAiValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ValidateHostedSummaryProvider,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("OpenAI summary provider validation did not finish.");
        }
        finally
        {
            _isValidatingOpenAiSummaryProvider = false;
            UpdateSummaryProviderValidationActionState();
            UpdateDashboardReadiness();
        }
    }

    private async void ClearOpenAiSummaryKeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ClearSummaryProviderSecretAsync(
            SummarySecretKind.OpenAi,
            ConfigSummaryOpenAiKeyPasswordBox,
            "OpenAI key cleared.");
    }

    private async Task ClearSummaryProviderSecretAsync(
        SummarySecretKind kind,
        PasswordBox passwordBox,
        string statusText)
    {
        try
        {
            await _summarySecretStore.DeleteAsync(kind, _lifetimeCts.Token);
            passwordBox.Clear();
            await RefreshSummaryProviderSecretStatusAsync();
            ConfigSummaryProviderStatusTextBlock.Text = statusText;
            AppendActivity(statusText);
            UpdateConfigActionState();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"Summary provider key clear failed: {exception.GetType().Name}");
            ConfigSummaryProviderStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.SaveSummarySettings,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
            AppendActivity("Summary provider key was not cleared.");
        }
    }

    private void UpdateSummaryProviderValidationActionState()
    {
        var refreshCopy = UserActionCopyResolver.Resolve(UserActionIntent.RefreshSummaryProviderModels);
        ValidateModelProxySummaryProviderButton.Content = _isValidatingModelProxySummaryProvider
            ? "Validating..."
            : UserActionCopyResolver.Resolve(UserActionIntent.ValidateLocalSummaryProvider).Label;
        ValidateOpenAiSummaryProviderButton.Content = _isValidatingOpenAiSummaryProvider
            ? "Validating..."
            : UserActionCopyResolver.Resolve(UserActionIntent.ValidateHostedSummaryProvider).Label;
        ValidateModelProxySummaryProviderButton.IsEnabled = !_isValidatingModelProxySummaryProvider;
        ValidateOpenAiSummaryProviderButton.IsEnabled = !_isValidatingOpenAiSummaryProvider;
        ClearOpenAiSummaryKeyButton.IsEnabled = !_isValidatingOpenAiSummaryProvider;
        RefreshModelProxySummaryModelsButton.IsEnabled = !_isRefreshingModelProxySummaryModels;
        RefreshOpenAiSummaryModelsButton.IsEnabled = !_isRefreshingOpenAiSummaryModels;
        RefreshModelProxySummaryModelsButton.Content = _isRefreshingModelProxySummaryModels
            ? "Refreshing..."
            : refreshCopy.Label;
        RefreshOpenAiSummaryModelsButton.Content = _isRefreshingOpenAiSummaryModels
            ? "Refreshing..."
            : refreshCopy.Label;
    }

    private async void RefreshModelProxySummaryModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshModelProxySummaryModelsAsync(manual: true, _lifetimeCts.Token);
    }

    private async Task RefreshModelProxySummaryModelsAsync(bool manual, CancellationToken cancellationToken)
    {
        if (_isRefreshingModelProxySummaryModels)
        {
            return;
        }

        _isRefreshingModelProxySummaryModels = true;
        ConfigModelProxyValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.RefreshSummaryProviderModels).ProgressText;
        UpdateSummaryProviderValidationActionState();
        try
        {
            var config = BuildSummaryValidationConfigFromEditor(_liveConfig.Current);
            var legacyApiKey = await _summarySecretStore.LoadAsync(SummarySecretKind.ModelProxy, cancellationToken);
            var options = SummaryChatProviderOptions.ForModelProxy(
                string.IsNullOrWhiteSpace(legacyApiKey) ? MeetingSummaryDefaults.ModelProxyLocalApiKey : legacyApiKey,
                config.SummaryModelProxyBaseUrl);
            var catalog = await _summaryModelCatalogClient.GetModelsAsync(options, cancellationToken);
            _modelProxySummaryModels = catalog.Models;
            _modelProxySummaryDefaultModel = catalog.ResolveModel();
            _modelProxySummaryCatalogState = catalog.CatalogState;
            PopulateSummaryModelChoices(
                ConfigSummaryModelProxyModelComboBox,
                ConfigSummaryModelProxyModelTextBox.Text,
                _modelProxySummaryModels,
                _modelProxySummaryDefaultModel);
            PopulateSummaryReasoningEffortChoices(
                ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort effort
                    ? effort
                    : _liveConfig.Current.SummaryReasoningEffort);
            var state = string.Equals(catalog.CatalogState, "fallback", StringComparison.OrdinalIgnoreCase)
                ? "Degraded fallback catalog"
                : "Live catalog";
            ConfigModelProxyValidationStatusTextBlock.Text =
                $"{state}: loaded {_modelProxySummaryModels.Count} text model(s); default is {_modelProxySummaryDefaultModel}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConfigModelProxyValidationStatusTextBlock.Text = "Canceled: the app is closing before model refresh completed.";
        }
        catch (Exception exception)
        {
            _logger.Log($"Summary model refresh failed: {exception}");
            _modelProxySummaryModels = Array.Empty<ModelProxyModelInfo>();
            _modelProxySummaryDefaultModel = null;
            _modelProxySummaryCatalogState = null;
            PopulateSummaryReasoningEffortChoices(SummaryReasoningEffort.ProviderDefault);
            ConfigModelProxyValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshSummaryProviderModels,
                UserActionBlockedReasonKind.NetworkUnavailable).BlockedText;
        }
        finally
        {
            _isRefreshingModelProxySummaryModels = false;
            UpdateSummaryProviderValidationActionState();
        }
    }

    private async void RefreshOpenAiSummaryModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingOpenAiSummaryModels)
        {
            return;
        }

        _isRefreshingOpenAiSummaryModels = true;
        ConfigOpenAiValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.RefreshSummaryProviderModels).ProgressText;
        UpdateSummaryProviderValidationActionState();
        try
        {
            var apiKey = !string.IsNullOrWhiteSpace(ConfigSummaryOpenAiKeyPasswordBox.Password)
                ? ConfigSummaryOpenAiKeyPasswordBox.Password
                : await _summarySecretStore.LoadAsync(SummarySecretKind.OpenAi, _lifetimeCts.Token);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ConfigOpenAiValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                    UserActionIntent.RefreshSummaryProviderModels,
                    UserActionBlockedReasonKind.SummaryProviderNotConfigured).BlockedText;
                return;
            }

            var catalog = await _summaryModelCatalogClient.GetModelsAsync(
                SummaryChatProviderOptions.ForOpenAi(apiKey),
                _lifetimeCts.Token);
            _openAiSummaryModels = catalog.Models;
            _openAiSummaryDefaultModel = catalog.DefaultModel;
            PopulateSummaryModelChoices(
                ConfigSummaryOpenAiModelComboBox,
                ConfigSummaryOpenAiModelTextBox.Text,
                _openAiSummaryModels,
                _openAiSummaryDefaultModel);
            ConfigOpenAiValidationStatusTextBlock.Text = $"Ready: loaded {_openAiSummaryModels.Count} OpenAI model(s).";
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            ConfigOpenAiValidationStatusTextBlock.Text = "Canceled: the app is closing before model refresh completed.";
        }
        catch (Exception exception)
        {
            _logger.Log($"Hosted summary model refresh failed: {exception}");
            ConfigOpenAiValidationStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.RefreshSummaryProviderModels,
                UserActionBlockedReasonKind.NetworkUnavailable).BlockedText;
        }
        finally
        {
            _isRefreshingOpenAiSummaryModels = false;
            UpdateSummaryProviderValidationActionState();
        }
    }

    private void ConfigSummaryModelProxyModelComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySummaryModelSelection(ConfigSummaryModelProxyModelComboBox, ConfigSummaryModelProxyModelTextBox);
    }

    private void ConfigSummaryOpenAiModelComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySummaryModelSelection(ConfigSummaryOpenAiModelComboBox, ConfigSummaryOpenAiModelTextBox);
    }

    private void PopulateSummaryModelChoices(
        ComboBox comboBox,
        string configuredModel,
        IReadOnlyList<ModelProxyModelInfo> models,
        string? defaultModel)
    {
        _isSynchronizingSummaryModelSelectors = true;
        try
        {
            var isModelProxy = ReferenceEquals(comboBox, ConfigSummaryModelProxyModelComboBox);
            var normalizedConfiguredModel = configuredModel.Trim();
            var options = models
                .OrderByDescending(model => model.IsDefault || string.Equals(model.Id, defaultModel, StringComparison.OrdinalIgnoreCase))
                .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(model => new SelectionOption<string>(
                    model.Id,
                    model.IsDefault || string.Equals(model.Id, defaultModel, StringComparison.OrdinalIgnoreCase)
                        ? $"{model.Id} (default)"
                        : model.Id))
                .ToList();
            if (isModelProxy)
            {
                var defaultLabel = string.IsNullOrWhiteSpace(defaultModel)
                    ? "Use ModelProxy default (loading catalog...)"
                    : $"Use ModelProxy default ({defaultModel})";
                options.Insert(0, new SelectionOption<string>(ModelProxyProviderDefaultSelection, defaultLabel));
            }
            if (!string.IsNullOrWhiteSpace(normalizedConfiguredModel) &&
                !options.Any(option => string.Equals(option.Value, normalizedConfiguredModel, StringComparison.OrdinalIgnoreCase)))
            {
                // Keep an unrefreshed or no-longer-advertised choice visible without treating it as a new value.
                options.Insert(0, new SelectionOption<string>(normalizedConfiguredModel, $"Current: {normalizedConfiguredModel}"));
            }
            options.Add(new SelectionOption<string>("__custom_summary_model__", "Custom model..."));
            comboBox.DisplayMemberPath = nameof(SelectionOption<string>.Label);
            comboBox.SelectedValuePath = nameof(SelectionOption<string>.Value);
            comboBox.ItemsSource = options;
            comboBox.SelectedValue = isModelProxy && string.IsNullOrWhiteSpace(normalizedConfiguredModel)
                ? ModelProxyProviderDefaultSelection
                : options.Any(option => string.Equals(option.Value, normalizedConfiguredModel, StringComparison.OrdinalIgnoreCase))
                    ? normalizedConfiguredModel
                    : "__custom_summary_model__";
            var customTextBox = isModelProxy
                ? ConfigSummaryModelProxyModelTextBox
                : ConfigSummaryOpenAiModelTextBox;
            customTextBox.Visibility = string.Equals(comboBox.SelectedValue as string, "__custom_summary_model__", StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _isSynchronizingSummaryModelSelectors = false;
        }
    }

    private void ApplySummaryModelSelection(ComboBox comboBox, TextBox customTextBox)
    {
        if (_isSynchronizingSummaryModelSelectors)
        {
            return;
        }

        var selectedModel = comboBox.SelectedValue as string;
        var isProviderDefault = ReferenceEquals(comboBox, ConfigSummaryModelProxyModelComboBox) &&
                                string.Equals(selectedModel, ModelProxyProviderDefaultSelection, StringComparison.Ordinal);
        var isCustom = string.Equals(selectedModel, "__custom_summary_model__", StringComparison.Ordinal);
        customTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (isProviderDefault)
        {
            customTextBox.Text = string.Empty;
        }
        else if (!isCustom && !string.IsNullOrWhiteSpace(selectedModel))
        {
            customTextBox.Text = selectedModel;
        }

        PopulateSummaryReasoningEffortChoices(
            ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort effort
                ? effort
                : _liveConfig.Current.SummaryReasoningEffort);
        MarkSummaryProviderValidationStale();
        UpdateConfigActionState();
    }

    private void MarkSummaryProviderValidationStale()
    {
        _summaryProviderValidationIsCurrent = false;
        _summaryProviderValidationObservedAtUtc = DateTimeOffset.UtcNow;
        if (!_isValidatingModelProxySummaryProvider)
        {
            ConfigModelProxyValidationStatusTextBlock.Text = "Configuration changed. Validate ModelProxy before using this setup.";
        }

        if (!_isValidatingOpenAiSummaryProvider)
        {
            ConfigOpenAiValidationStatusTextBlock.Text = "Configuration changed. Validate OpenAI before using this setup.";
        }

        if (_isUiReady)
        {
            UpdateDashboardReadiness();
        }
    }

    private void PopulateSummaryReasoningEffortChoices(SummaryReasoningEffort selectedEffort)
    {
        var openAiOnly = ConfigSummaryProviderPreferenceComboBox.SelectedValue is MeetingSummaryProviderPreference preference &&
                         preference == MeetingSummaryProviderPreference.OpenAiOnly;
        var supportedEfforts = openAiOnly
            ? new[]
            {
                SummaryReasoningEffort.ProviderDefault,
                SummaryReasoningEffort.Minimal,
                SummaryReasoningEffort.Low,
                SummaryReasoningEffort.Medium,
                SummaryReasoningEffort.High,
                SummaryReasoningEffort.XHigh,
            }
            : GetSelectedModelProxyReasoningEfforts();
        var labels = new Dictionary<SummaryReasoningEffort, string>
        {
            [SummaryReasoningEffort.ProviderDefault] = "Provider default",
            [SummaryReasoningEffort.Minimal] = "Minimal",
            [SummaryReasoningEffort.Low] = "Low",
            [SummaryReasoningEffort.Medium] = "Medium (recommended)",
            [SummaryReasoningEffort.High] = "High",
            [SummaryReasoningEffort.XHigh] = "Extra high",
        };

        _isSynchronizingSummaryModelSelectors = true;
        try
        {
            var options = supportedEfforts
                .Select(effort => new SelectionOption<SummaryReasoningEffort>(effort, labels[effort]))
                .ToList();
            if (!supportedEfforts.Contains(selectedEffort))
            {
                options.Add(new SelectionOption<SummaryReasoningEffort>(
                    selectedEffort,
                    $"{labels[selectedEffort]} (unavailable for the selected ModelProxy model)",
                    IsEnabled: false));
            }

            ConfigSummaryReasoningEffortComboBox.ItemsSource = options;
            ConfigSummaryReasoningEffortComboBox.SelectedValue = selectedEffort;
        }
        finally
        {
            _isSynchronizingSummaryModelSelectors = false;
        }
    }

    private IReadOnlyList<SummaryReasoningEffort> GetSelectedModelProxyReasoningEfforts()
    {
        var selectedModel = ConfigSummaryModelProxyModelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedModel))
        {
            selectedModel = _modelProxySummaryDefaultModel ?? string.Empty;
        }

        var supported = _modelProxySummaryModels.FirstOrDefault(model =>
            string.Equals(model.Id, selectedModel, StringComparison.OrdinalIgnoreCase))?.SupportedReasoningEfforts;
        return supported is { Count: > 0 }
            ? [SummaryReasoningEffort.ProviderDefault, .. supported]
            : [SummaryReasoningEffort.ProviderDefault];
    }

    private static string FormatSummaryProviderValidationStatus(
        SummaryProviderValidationResult result,
        UserActionIntent intent)
    {
        return result.Success
            ? UserActionCopyResolver.Resolve(intent).SuccessText
            : UserActionCopyResolver.Resolve(intent, UserActionBlockedReasonKind.OperationFailed).BlockedText;
    }

    private AppConfig BuildSummaryValidationConfigFromEditor(AppConfig currentConfig)
    {
        var timeoutSeconds = int.TryParse(
            ConfigSummaryRequestTimeoutTextBox.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedTimeoutSeconds)
                ? parsedTimeoutSeconds
                : currentConfig.SummaryRequestTimeoutSeconds;

        return currentConfig with
        {
            SummaryModelProxyBaseUrl = ConfigSummaryModelProxyBaseUrlTextBox.Text.Trim(),
            SummaryModelProxyModel = ConfigSummaryModelProxyModelTextBox.Text.Trim(),
            SummaryOpenAiModel = ConfigSummaryOpenAiModelTextBox.Text.Trim(),
            SummaryReasoningEffort = ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort summaryReasoningEffort
                ? summaryReasoningEffort
                : currentConfig.SummaryReasoningEffort,
            SummaryRequestTimeoutSeconds = timeoutSeconds,
        };
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
            ConfigSpeakerNameLearningCheckBox.IsChecked == true
                ? SpeakerNameLearningMode.LocalAutoLearn
                : SpeakerNameLearningMode.Disabled,
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
                : _liveConfig.Current.BackgroundSpeakerLabelingMode,
            ConfigInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy initialProcessingStrategy
                ? initialProcessingStrategy
                : _liveConfig.Current.InitialProcessingStrategy,
            ConfigOvernightInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy overnightInitialProcessingStrategy
                ? overnightInitialProcessingStrategy
                : _liveConfig.Current.OvernightInitialProcessingStrategy,
            BuildIncrementalWorkPlanFromEditor(),
            ConfigOvernightDrainStartTextBox.Text,
            ConfigOvernightDrainEndTextBox.Text,
            ConfigTranscriptionProviderPreferenceComboBox.SelectedValue is TranscriptionProviderPreference transcriptionProviderPreference
                ? transcriptionProviderPreference
                : _liveConfig.Current.TranscriptionProviderPreference,
            ConfigTranscriptionCliPathTextBox.Text,
            ConfigTranscriptionCliArgumentsTextBox.Text,
            ConfigDiarizationProviderPreferenceComboBox.SelectedValue is DiarizationProviderPreference diarizationProviderPreference
                ? diarizationProviderPreference
                : _liveConfig.Current.DiarizationProviderPreference,
            ConfigDiarizationCliPathTextBox.Text,
            ConfigDiarizationCliArgumentsTextBox.Text,
            ConfigSummaryGenerationEnabledCheckBox.IsChecked == true
                ? MeetingSummaryGenerationMode.Enabled
                : MeetingSummaryGenerationMode.Disabled,
            ConfigSummaryProviderPreferenceComboBox.SelectedValue is MeetingSummaryProviderPreference summaryProviderPreference
                ? summaryProviderPreference
                : _liveConfig.Current.SummaryProviderPreference,
            ConfigSummaryModelProxyBaseUrlTextBox.Text,
            ConfigSummaryModelProxyModelTextBox.Text,
            ConfigSummaryOpenAiModelTextBox.Text,
            ConfigSummaryRequestTimeoutTextBox.Text,
            ConfigSummaryTranscriptChunkTargetTextBox.Text,
            ConfigSummaryTranscriptChunkOverlapTextBox.Text,
            !string.IsNullOrWhiteSpace(ConfigSummaryOpenAiKeyPasswordBox.Password),
            ConfigSummaryReasoningEffortComboBox.SelectedValue is SummaryReasoningEffort summaryReasoningEffort
                ? summaryReasoningEffort
                : _liveConfig.Current.SummaryReasoningEffort,
            ConfigImportInboxEnabledCheckBox.IsChecked == true,
            ConfigImportInboxDirTextBox.Text,
            ConfigImportInboxArchiveAfterQueueEnabledCheckBox.IsChecked == true,
            ConfigImportInboxMoveBlockedToErrorEnabledCheckBox.IsChecked == true,
            ConfigBacklogAccelerationProfileComboBox.SelectedValue is BacklogAccelerationProfile backlogAccelerationProfile
                ? backlogAccelerationProfile
                : _liveConfig.Current.BacklogAccelerationProfile);
    }

    private void ApplyConfigEditorSnapshot(ConfigEditorSnapshot snapshot)
    {
        ConfigAudioOutputDirTextBox.Text = snapshot.AudioOutputDir;
        ConfigTranscriptOutputDirTextBox.Text = snapshot.TranscriptOutputDir;
        ConfigWorkDirTextBox.Text = snapshot.WorkDir;
        ConfigImportInboxEnabledCheckBox.IsChecked = snapshot.ImportInboxEnabled;
        ConfigImportInboxDirTextBox.Text = snapshot.ImportInboxDir;
        ConfigImportInboxArchiveAfterQueueEnabledCheckBox.IsChecked = snapshot.ImportInboxArchiveAfterQueueEnabled;
        ConfigImportInboxMoveBlockedToErrorEnabledCheckBox.IsChecked = snapshot.ImportInboxMoveBlockedToErrorEnabled;
        ConfigDiarizationGpuAccelerationCheckBox.IsChecked = snapshot.UseGpuAcceleration;
        ConfigAutoDetectThresholdTextBox.Text = snapshot.AutoDetectThresholdText;
        ConfigMeetingStopTimeoutTextBox.Text = snapshot.MeetingStopTimeoutText;
        ConfigMicCaptureCheckBox.IsChecked = snapshot.MicCaptureEnabled;
        ConfigSpeakerNameLearningCheckBox.IsChecked = snapshot.SpeakerNameLearningMode == SpeakerNameLearningMode.LocalAutoLearn;
        ConfigLaunchOnLoginCheckBox.IsChecked = snapshot.LaunchOnLoginEnabled;
        ConfigAutoDetectCheckBox.IsChecked = snapshot.AutoDetectEnabled;
        ConfigCalendarTitleFallbackCheckBox.IsChecked = snapshot.CalendarTitleFallbackEnabled;
        ConfigMeetingAttendeeEnrichmentCheckBox.IsChecked = snapshot.MeetingAttendeeEnrichmentEnabled;
        ConfigUpdateCheckEnabledCheckBox.IsChecked = snapshot.UpdateCheckEnabled;
        ConfigAutoInstallUpdatesCheckBox.IsChecked = snapshot.AutoInstallUpdatesEnabled;
        ConfigUpdateFeedUrlTextBox.Text = snapshot.UpdateFeedUrl;
        ConfigPreferredTeamsIntegrationModeComboBox.SelectedValue = snapshot.PreferredTeamsIntegrationMode;
        ConfigBacklogAccelerationProfileComboBox.SelectedValue = snapshot.BacklogAccelerationProfile;
        ConfigInitialProcessingStrategyComboBox.SelectedValue = snapshot.InitialProcessingStrategy;
        ConfigOvernightInitialProcessingStrategyComboBox.SelectedValue = snapshot.OvernightInitialProcessingStrategy;
        ApplyIncrementalWorkPlanToEditor(snapshot.IncrementalWorkPlan);
        ConfigOvernightDrainStartTextBox.Text = snapshot.OvernightDrainStartLocal;
        ConfigOvernightDrainEndTextBox.Text = snapshot.OvernightDrainEndLocal;
        ConfigTranscriptionProviderPreferenceComboBox.SelectedValue = snapshot.TranscriptionProviderPreference;
        ConfigTranscriptionCliPathTextBox.Text = snapshot.TranscriptionCliPath;
        ConfigTranscriptionCliArgumentsTextBox.Text = snapshot.TranscriptionCliArguments;
        ConfigDiarizationProviderPreferenceComboBox.SelectedValue = snapshot.DiarizationProviderPreference;
        ConfigDiarizationCliPathTextBox.Text = snapshot.DiarizationCliPath;
        ConfigDiarizationCliArgumentsTextBox.Text = snapshot.DiarizationCliArguments;
        ConfigBackgroundProcessingModeComboBox.SelectedValue = snapshot.BackgroundProcessingMode;
        ConfigSummaryGenerationEnabledCheckBox.IsChecked =
            snapshot.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled;
        ConfigSummaryProviderPreferenceComboBox.SelectedValue = snapshot.SummaryProviderPreference;
        ConfigSummaryModelProxyBaseUrlTextBox.Text = snapshot.SummaryModelProxyBaseUrl;
        ConfigSummaryModelProxyModelTextBox.Text = snapshot.SummaryModelProxyModel;
        ConfigSummaryOpenAiModelTextBox.Text = snapshot.SummaryOpenAiModel;
        PopulateSummaryReasoningEffortChoices(snapshot.SummaryReasoningEffort);
        PopulateSummaryModelChoices(
            ConfigSummaryModelProxyModelComboBox,
            snapshot.SummaryModelProxyModel,
            _modelProxySummaryModels,
            _modelProxySummaryDefaultModel);
        PopulateSummaryModelChoices(
            ConfigSummaryOpenAiModelComboBox,
            snapshot.SummaryOpenAiModel,
            _openAiSummaryModels,
            _openAiSummaryDefaultModel);
        ConfigSummaryRequestTimeoutTextBox.Text = snapshot.SummaryRequestTimeoutSecondsText;
        ConfigSummaryTranscriptChunkTargetTextBox.Text = snapshot.SummaryTranscriptChunkTokenTargetText;
        ConfigSummaryTranscriptChunkOverlapTextBox.Text = snapshot.SummaryTranscriptChunkOverlapTokensText;
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
        ConfigBackgroundSpeakerLabelingModeHelpTextBlock.Text = MainWindowInteractionLogic.BuildSpeakerLabelingModeHelpText(mode);
    }

    private static string BuildSpeakerLabelingRunModeHelpText(BackgroundSpeakerLabelingMode mode)
    {
        return MainWindowInteractionLogic.BuildSpeakerLabelingModeHelpText(mode);
    }

    private void UpdateConfigModeHelpTextFromSelection()
    {
        if (ConfigBacklogAccelerationProfileComboBox.SelectedValue is BacklogAccelerationProfile backlogProfile)
        {
            ConfigBacklogAccelerationProfileHelpTextBlock.Text = BacklogAccelerationProfileResolver
                .GetOptions()
                .First(option => option.Value == backlogProfile)
                .Detail;
        }

        if (ConfigBackgroundProcessingModeComboBox.SelectedValue is BackgroundProcessingMode backgroundProcessingMode)
        {
            UpdateBackgroundProcessingModeHelpText(backgroundProcessingMode);
        }

        if (ConfigInitialProcessingStrategyComboBox.SelectedValue is InitialProcessingStrategy initialProcessingStrategy)
        {
            ConfigInitialProcessingStrategyHelpTextBlock.Text =
                MainWindowInteractionLogic.BuildInitialProcessingStrategyHelpText(initialProcessingStrategy);
        }

        if (ConfigBackgroundSpeakerLabelingModeComboBox.SelectedValue is BackgroundSpeakerLabelingMode speakerLabelingMode)
        {
            ConfigBackgroundSpeakerLabelingModeHelpTextBlock.Text =
                MainWindowInteractionLogic.BuildSpeakerLabelingModeHelpText(speakerLabelingMode);
        }
    }

    private void UpdateBackgroundProcessingModeHelpText(BackgroundProcessingMode mode)
    {
        ConfigBackgroundProcessingModeHelpTextBlock.Text =
            MainWindowInteractionLogic.BuildBackgroundProcessingModeHelpText(mode, Environment.ProcessorCount);
    }

    private IncrementalWorkPlan BuildIncrementalWorkPlanFromEditor()
    {
        var plan = IncrementalWorkPlan.None;
        if (ConfigIncrementalQueuedRecordingsCheckBox.IsChecked == true)
        {
            plan |= IncrementalWorkPlan.QueuedRecordings;
        }
        if (ConfigIncrementalSpeakerLabelsCheckBox.IsChecked == true)
        {
            plan |= IncrementalWorkPlan.DeferredSpeakerLabels;
        }
        if (ConfigIncrementalAiSummariesCheckBox.IsChecked == true)
        {
            plan |= IncrementalWorkPlan.MissingAiSummaries;
        }
        if (ConfigIncrementalSafeCleanupCheckBox.IsChecked == true)
        {
            plan |= IncrementalWorkPlan.SafeCleanup;
        }
        return plan;
    }

    private void UpdateIncrementalWorkAvailability()
    {
        var summariesEnabled = ConfigSummaryGenerationEnabledCheckBox.IsChecked == true;
        // Keep an unavailable saved choice clearable rather than trapping it checked.
        ConfigIncrementalAiSummariesCheckBox.IsEnabled = summariesEnabled ||
            ConfigIncrementalAiSummariesCheckBox.IsChecked == true;
        ConfigIncrementalAiSummariesCheckBox.ToolTip = summariesEnabled
            ? _summaryProviderValidationIsCurrent
                ? "Uses the validated provider, model, and reasoning effort configured below."
                : "Validate the configured summary provider before scheduled summaries can run."
            : "Enable AI summaries before scheduled summaries can run.";

        var speakerLabelsAvailable = !string.IsNullOrWhiteSpace(_liveConfig.Current.DiarizationAssetPath) &&
            Directory.Exists(_liveConfig.Current.DiarizationAssetPath);
        ConfigIncrementalSpeakerLabelsCheckBox.IsEnabled = speakerLabelsAvailable ||
            ConfigIncrementalSpeakerLabelsCheckBox.IsChecked == true;
        ConfigIncrementalSpeakerLabelsCheckBox.ToolTip = speakerLabelsAvailable
            ? "Queues only published transcripts that still need speaker labels."
            : "Install or configure the speaker-labeling bundle before enabling this work.";
    }

    private void ApplyIncrementalWorkPlanToEditor(IncrementalWorkPlan plan)
    {
        ConfigIncrementalQueuedRecordingsCheckBox.IsChecked = plan.HasFlag(IncrementalWorkPlan.QueuedRecordings);
        ConfigIncrementalSpeakerLabelsCheckBox.IsChecked = plan.HasFlag(IncrementalWorkPlan.DeferredSpeakerLabels);
        ConfigIncrementalAiSummariesCheckBox.IsChecked = plan.HasFlag(IncrementalWorkPlan.MissingAiSummaries);
        ConfigIncrementalSafeCleanupCheckBox.IsChecked = plan.HasFlag(IncrementalWorkPlan.SafeCleanup);
    }

    private void UpdateProcessingSchedulePresentation(AppConfig config)
    {
        var work = new List<string>();
        if (config.IncrementalWorkPlan.HasFlag(IncrementalWorkPlan.QueuedRecordings)) work.Add("queued recordings");
        if (config.IncrementalWorkPlan.HasFlag(IncrementalWorkPlan.DeferredSpeakerLabels)) work.Add("speaker labels");
        if (config.IncrementalWorkPlan.HasFlag(IncrementalWorkPlan.MissingAiSummaries)) work.Add("AI summaries");
        if (config.IncrementalWorkPlan.HasFlag(IncrementalWorkPlan.SafeCleanup)) work.Add("safe cleanup");
        var overnightSummary = config.OvernightInitialProcessingStrategy == InitialProcessingStrategy.TranscriptFirst
            ? "transcript-first mode keeps speaker labels and summaries deferred"
            : "overnight acceleration drains transcripts (up to 3 workers), then speaker labels and summaries (one worker each); new accelerated work pauses while recording";
        ConfigProcessingScheduleSummaryTextBlock.Text =
            $"Idle: {(work.Count == 0 ? "manual only" : string.Join(", ", work))}. " +
            $"Overnight {config.OvernightDrainStartLocal}-{config.OvernightDrainEndLocal}: {overnightSummary}.";
    }

    private void UpdateExternalProviderStatusText(AppConfig config)
    {
        ConfigTranscriptionCliProviderStatusTextBlock.Text = BuildExternalProviderStatusText(
            config.TranscriptionProviderPreference == TranscriptionProviderPreference.LocalCli,
            config.TranscriptionCliPath,
            config.TranscriptionCliProviderProbe,
            "External transcription");
        ConfigDiarizationCliProviderStatusTextBlock.Text = BuildExternalProviderStatusText(
            config.DiarizationProviderPreference == DiarizationProviderPreference.LocalCli,
            config.DiarizationCliPath,
            config.DiarizationCliProviderProbe,
            "External speaker-labeling");
    }

    private void UpdateExternalProviderStatusTextFromEditor()
    {
        ConfigTranscriptionCliProviderStatusTextBlock.Text = BuildExternalProviderStatusText(
            ConfigTranscriptionProviderPreferenceComboBox.SelectedValue is TranscriptionProviderPreference.LocalCli,
            ConfigTranscriptionCliPathTextBox.Text.Trim(),
            _liveConfig.Current.TranscriptionCliProviderProbe,
            "External transcription");
        ConfigDiarizationCliProviderStatusTextBlock.Text = BuildExternalProviderStatusText(
            ConfigDiarizationProviderPreferenceComboBox.SelectedValue is DiarizationProviderPreference.LocalCli,
            ConfigDiarizationCliPathTextBox.Text.Trim(),
            _liveConfig.Current.DiarizationCliProviderProbe,
            "External speaker-labeling");
    }

    private static string BuildExternalProviderStatusText(
        bool selected,
        string configuredPath,
        ExternalProviderProbeSnapshot? probe,
        string label)
    {
        if (!selected)
        {
            return $"{label} provider is off. Built-in processing will be used.";
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return $"{label} provider is selected, but no CLI path is configured.";
        }

        var probeMatches = probe?.Succeeded == true &&
            !string.IsNullOrWhiteSpace(probe.ExecutablePath) &&
            PathsEqualSafe(configuredPath, probe.ExecutablePath);
        var lastProbe = probe?.LastProbeUtc is { } atUtc
            ? atUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "not run";
        var message = string.IsNullOrWhiteSpace(probe?.Message)
            ? "No probe result."
            : probe!.Message;

        return probeMatches
            ? $"{label} provider probe passed. Last probe: {lastProbe}. {message}"
            : $"{label} provider will fall back to built-in processing until Test passes for this path. Last probe: {lastProbe}. {message}";
    }

    private static bool PathsEqualSafe(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
                SpeakerLabelingSecurityPromptMigrationApplied = true,
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
            _logger.Log($"Speaker-labeling mode update failed from {source}: {exception}");
            var copy = UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateSpeakerLabelingMode,
                UserActionBlockedReasonKind.OperationFailed);
            SetConfigSaveStatus(copy.BlockedText);
            DiarizationActionStatusTextBlock.Text = copy.BlockedText;
            AppendActivity("Speaker-labeling mode update did not finish.");
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
        var capability = RecordingReadinessService.ProjectTeamsIntegration(snapshot.Status);
        ConfigTeamsIntegrationStatusTextBlock.Text = capability.Summary;
        ConfigTeamsIntegrationDetailTextBlock.Text = BuildTeamsCapabilityNextStep(snapshot.Status);
        ConfigTeamsIntegrationAdvancedDetailTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.Detail)
            ? "No detailed probe result is saved yet. Run the probe when you need troubleshooting detail."
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

    private void ReportCuratedTranscriptionDownloadProgress(FileDownloadProgress progress)
    {
        _modelDownloadProgressIsIndeterminate = progress.TotalBytes is not > 0;
        _modelDownloadProgressPercent = progress.TotalBytes is > 0
            ? Math.Clamp(progress.BytesDownloaded / (double)progress.TotalBytes.Value * 100d, 0d, 100d)
            : 0d;
        var sizeText = progress.TotalBytes is > 0
            ? $"{FormatBytes(progress.BytesDownloaded)} of {FormatBytes(progress.TotalBytes.Value)}"
            : $"{FormatBytes(progress.BytesDownloaded)} downloaded";
        ModelActionStatusTextBlock.Text =
            $"Downloading the approved local transcription model ({sizeText}). You can cancel without changing optional settings.";
    }

    private static string BuildTeamsCapabilityNextStep(TeamsCapabilityStatus status)
    {
        return status switch
        {
            TeamsCapabilityStatus.FallbackOnly =>
                "Local Teams detection remains available. Run the probe only when you want to assess an official integration.",
            TeamsCapabilityStatus.ThirdPartyApiUsable or
                TeamsCapabilityStatus.CalendarBacked or
                TeamsCapabilityStatus.CalendarAndOnlineMeeting =>
                "The supported integration is available. Local detection remains the fallback when it is not usable.",
            TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly =>
                "Use local detection, or run the probe again after the integration capabilities change.",
            TeamsCapabilityStatus.BlockedByPolicyOrConsent or
                TeamsCapabilityStatus.SignedOut =>
                "Use local detection, or review Advanced probe diagnostics before changing an integration setting.",
            _ => "Local detection remains available. Run the probe when you need an updated capability check.",
        };
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
            TryScheduleMeetingCleanupAutomaticBatchRefill(DateTimeOffset.UtcNow);
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
            _logger.Log($"Launch-on-login update failed from {source}: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateLaunchOnLogin,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Scheduled update cycle failed from {source}: {exception}");
            AppendActivity("Scheduled update check did not finish.");
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
            var nowUtc = DateTimeOffset.UtcNow;
            var config = _liveConfig.Current;
            var hasReadyTranscriptionModel = HasReadyTranscriptionModel();
            if (!hasReadyTranscriptionModel)
            {
                // Discovery is safe without a model and keeps Inbox-owned files
                // visible for Setup recovery. Worker admission remains blocked.
                if (ShouldReconcileImportInbox(config, nowUtc))
                {
                    _lastImportInboxReconciliationUtc = nowUtc;
                    var reconciliation = await _importInboxReconciliationService.ReconcileAsync(
                        config,
                        nowUtc,
                        cancellationToken);
                    ConfigImportInboxStatusTextBlock.Text = reconciliation.Message;
                }

                ApplyExternalAudioImportSetupState();
                return;
            }

            var imported = (await _externalAudioImportService.ImportPendingAudioFilesAsync(
                config,
                nowUtc,
                cancellationToken)).ToList();

            if (ShouldReconcileImportInbox(config, nowUtc))
            {
                _lastImportInboxReconciliationUtc = nowUtc;
                var reconciliation = await _importInboxReconciliationService.ReconcileAsync(
                    config,
                    nowUtc,
                    cancellationToken);
                if (reconciliation.Status == ImportInboxReconciliationStatus.Ready &&
                    ImportInboxLifecyclePolicy.MayQueueDiscoveredWork(config, hasReadyTranscriptionModel))
                {
                    var intake = await _importInboxIntakeService.TryQueueNextAsync(
                        config,
                        _importInboxLeaseOwnerId,
                        nowUtc,
                        cancellationToken);
                    if (intake.Status == ImportInboxIntakeStatus.Queued &&
                        !string.IsNullOrWhiteSpace(intake.ManifestPath))
                    {
                        var inboxManifest = await _manifestStore.LoadAsync(intake.ManifestPath, cancellationToken);
                        imported.Add(new ImportedExternalAudioResult(
                            intake.ManifestPath,
                            OriginalSourcePath: string.Empty,
                            inboxManifest.DetectedTitle));
                    }
                    else if (intake.Status == ImportInboxIntakeStatus.Blocked)
                    {
                        ConfigImportInboxStatusTextBlock.Text = intake.Message;
                    }
                }
                else
                {
                    ConfigImportInboxStatusTextBlock.Text = reconciliation.Message;
                }
            }

            if (imported.Count == 0)
            {
                return;
            }

            foreach (var item in imported)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AppendActivity($"Queued imported audio '{item.Title}' for automatic transcription.");
                await _processingQueue.EnqueueAsync(item.ManifestPath, cancellationToken);

                var manifest = await _manifestStore.LoadAsync(item.ManifestPath, cancellationToken);
                if (manifest.State == SessionState.Failed)
                {
                    var errorSummary = string.IsNullOrWhiteSpace(manifest.ErrorSummary)
                        ? manifest.TranscriptionStatus.Message ?? "See app.log for details."
                        : manifest.ErrorSummary;
                    AppendActivity($"Imported audio '{item.Title}' failed to transcribe: {errorSummary}");
                }
                else if (manifest.State == SessionState.Published)
                {
                    AppendActivity($"Finished transcript build for imported audio '{item.Title}'.");
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
            _logger.Log($"External audio import from {source} failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.ImportAudio,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
        finally
        {
            _externalAudioImportGate.Release();
        }
    }

    private bool ShouldReconcileImportInbox(AppConfig config, DateTimeOffset nowUtc)
    {
        return ImportInboxLifecyclePolicy.ShouldReconcile(
            config,
            _lastImportInboxReconciliationUtc,
            nowUtc);
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
            _logger.Log($"Update check failed from {source}: {exception}");
            UpdateCheckStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.CheckForUpdates,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Update check did not finish.");
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
        if (result.Status == AppUpdateStatusKind.UpToDate &&
            InstalledProvenanceRepairService.TryBackfillInstalledReleaseMetadata(
                _liveConfig.ConfigPath,
                AppBranding.Version,
                result.LatestPublishedAtUtc,
                result.LatestAssetSizeBytes,
                Environment.ProcessPath,
                AppContext.BaseDirectory))
        {
            AppendActivity("Recovered installed package metadata from a successful GitHub update check.");
        }

        await PersistLastUpdateCheckUtcAsync(checkedAtUtc, cancellationToken);
        _lastUpdateCheckResult = result;
        ApplyUpdateCheckResult(result, manual);

        if (result.Status == AppUpdateStatusKind.UpdateAvailable)
        {
            AppendActivity($"Update check from {source}: version {result.LatestVersion} is available.");
        }
        else if (manual ||
                 result.Status == AppUpdateStatusKind.Error ||
                 result.Status == AppUpdateStatusKind.RequiresInstallerReset)
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

        await InstallAvailableUpdateAsync(
            source,
            manual: false,
            allowProcessingOverride: false,
            queueWhenProcessingBlocked: false,
            cancellationToken);
    }

    private async Task<bool> TryInstallPendingDownloadedUpdateIfIdleAsync(string source, CancellationToken cancellationToken)
    {
        var config = _liveConfig.Current;
        var localUpdateState = BuildLocalUpdateState(config);
        if (!_appUpdateInstallPolicy.ShouldRetryPendingInstall(
                config.PendingUpdateZipPath,
                config.PendingUpdateVersion,
                AppBranding.Version,
                config.PendingUpdateInstallWhenIdleRequested,
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
        if (config.PendingUpdateInstallWhenIdleRequested)
        {
            await PersistPendingDownloadedUpdateAsync(
                config.PendingUpdateZipPath,
                pendingResult,
                queueInstallWhenIdleRequested: false,
                cancellationToken);
        }

        await InstallDownloadedUpdateAsync(
            $"{source} restart retry",
            config.PendingUpdateZipPath,
            pendingResult,
            allowProcessingOverride: false,
            cancellationToken);
        return true;
    }

    private async Task InstallAvailableUpdateAsync(
        string source,
        bool manual,
        bool allowProcessingOverride,
        bool queueWhenProcessingBlocked,
        CancellationToken cancellationToken)
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
            UpdateCheckStatusTextBlock.Text = allowProcessingOverride
                ? "Preparing the queued update..."
                : "Preparing the latest update...";
        }

        try
        {
            var currentConfig = _liveConfig.Current;
            if (allowProcessingOverride &&
                currentConfig.PendingUpdateInstallWhenIdleRequested &&
                HasPendingDownloadedUpdate(currentConfig))
            {
                var pendingResult = BuildPendingDownloadedUpdateResult(currentConfig);
                await PersistPendingDownloadedUpdateAsync(
                    currentConfig.PendingUpdateZipPath,
                    pendingResult,
                    queueInstallWhenIdleRequested: false,
                    cancellationToken);
                await InstallDownloadedUpdateWithGateHeldAsync(
                    source,
                    currentConfig.PendingUpdateZipPath,
                    pendingResult,
                    allowProcessingOverride: true,
                    $"Installing queued update {FormatVersionLabel(pendingResult.LatestVersion)} and interrupting background processing for the installer handoff...",
                    $"Installing queued update {FormatVersionLabel(pendingResult.LatestVersion)} immediately from {source} by stopping background processing.",
                    cancellationToken);
                return;
            }

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

            var blockKind = _appUpdateInstallPolicy.GetInstallBlockKind(
                _recordingCoordinator.IsRecording,
                _processingQueue.IsProcessingInProgress,
                _isUpdateInstallInProgress,
                allowCurrentInstallInProgress: false,
                allowProcessingOverride: allowProcessingOverride);
            if (blockKind != AppUpdateInstallBlockKind.None)
            {
                var blockReason = _appUpdateInstallPolicy.GetInstallBlockReason(
                    _recordingCoordinator.IsRecording,
                    _processingQueue.IsProcessingInProgress,
                    _isUpdateInstallInProgress,
                    allowCurrentInstallInProgress: false,
                    allowProcessingOverride: allowProcessingOverride) ?? "The app is currently busy.";
                if (queueWhenProcessingBlocked && blockKind == AppUpdateInstallBlockKind.Processing)
                {
                    UpdateCheckStatusTextBlock.Text = $"Downloading update {FormatVersionLabel(available.LatestVersion)} so it can install automatically once background processing is idle...";
                    var queuedDownloadedPath = await EnsurePendingDownloadedUpdateAsync(
                        available,
                        queueInstallWhenIdleRequested: true,
                        cancellationToken);
                    UpdateCheckStatusTextBlock.Text = MainWindowInteractionLogic.BuildQueuedUpdateInstallMessage(
                        FormatVersionLabel(available.LatestVersion),
                        queuedDownloadedPath);
                    AppendActivity($"Queued update {FormatVersionLabel(available.LatestVersion)} from {source} for automatic install once background processing is idle.");
                    OpenContainingFolder(queuedDownloadedPath);
                    return;
                }

                if (manual)
                {
                    UpdateCheckStatusTextBlock.Text = blockReason;
                }

                return;
            }

            var statusText = allowProcessingOverride
                ? $"Downloading update {FormatVersionLabel(available.LatestVersion)} and interrupting background processing for the installer handoff..."
                : $"Downloading update {FormatVersionLabel(available.LatestVersion)} and preparing the installer handoff...";
            UpdateCheckStatusTextBlock.Text = statusText;
            var downloadedPath = await EnsurePendingDownloadedUpdateAsync(
                available,
                queueInstallWhenIdleRequested: false,
                cancellationToken);
            await InstallDownloadedUpdateWithGateHeldAsync(
                source,
                downloadedPath,
                available,
                allowProcessingOverride,
                statusText,
                allowProcessingOverride
                    ? $"Starting override update install for {FormatVersionLabel(available.LatestVersion)} from {source} by stopping background processing."
                    : $"Starting {(manual ? "manual" : "automatic")} update install for {FormatVersionLabel(available.LatestVersion)} from {source}.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"Update installation failed from {source}: {exception}");
            UpdateCheckStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.InstallUpdates,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Update installation did not finish.");
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
        bool allowProcessingOverride,
        CancellationToken cancellationToken)
    {
        if (!await _updateOperationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        _isPreparingUpdateInstall = true;

        try
        {
            await InstallDownloadedUpdateWithGateHeldAsync(
                source,
                downloadedPath,
                updateResult,
                allowProcessingOverride,
                allowProcessingOverride
                    ? $"Installing queued update {FormatVersionLabel(updateResult.LatestVersion)} and interrupting background processing for the installer handoff..."
                    : $"Retrying previously downloaded update {FormatVersionLabel(updateResult.LatestVersion)} after restart...",
                allowProcessingOverride
                    ? $"Installing queued update {FormatVersionLabel(updateResult.LatestVersion)} immediately from {source} by stopping background processing."
                    : $"Retrying pending downloaded update {FormatVersionLabel(updateResult.LatestVersion)} from {source}.",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations during shutdown.
        }
        catch (Exception exception)
        {
            _logger.Log($"Pending update installation failed from {source}: {exception}");
            UpdateCheckStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.InstallUpdates,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Pending update installation did not finish.");
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

    private async Task InstallDownloadedUpdateWithGateHeldAsync(
        string source,
        string downloadedPath,
        AppUpdateCheckResult updateResult,
        bool allowProcessingOverride,
        string statusText,
        string activityText,
        CancellationToken cancellationToken)
    {
        _isUpdateInstallInProgress = true;
        UpdateUpdateActionButtons();
        _detectionTimer.Stop();
        _updateTimer.Stop();
        UpdateUi($"Installing update {FormatVersionLabel(updateResult.LatestVersion)}...", DetectionTextBlock.Text);
        UpdateCheckStatusTextBlock.Text = statusText;
        AppendActivity(activityText);
        await InstallDownloadedUpdateCoreAsync(
            source,
            downloadedPath,
            updateResult,
            allowProcessingOverride,
            cancellationToken);
    }

    private async Task InstallDownloadedUpdateCoreAsync(
        string source,
        string downloadedPath,
        AppUpdateCheckResult updateResult,
        bool allowProcessingOverride,
        CancellationToken cancellationToken)
    {
        var blockKind = _appUpdateInstallPolicy.GetInstallBlockKind(
            _recordingCoordinator.IsRecording,
            _processingQueue.IsProcessingInProgress,
            _isUpdateInstallInProgress,
            allowCurrentInstallInProgress: true,
            allowProcessingOverride: allowProcessingOverride);
        if (blockKind != AppUpdateInstallBlockKind.None)
        {
            var blockReason = _appUpdateInstallPolicy.GetInstallBlockReason(
                _recordingCoordinator.IsRecording,
                _processingQueue.IsProcessingInProgress,
                _isUpdateInstallInProgress,
                allowCurrentInstallInProgress: true,
                allowProcessingOverride: allowProcessingOverride) ?? "The app became busy before the installer handoff could finish.";
            var queueInstallWhenIdleRequested = blockKind == AppUpdateInstallBlockKind.Processing;
            await PersistPendingDownloadedUpdateAsync(
                downloadedPath,
                updateResult,
                queueInstallWhenIdleRequested,
                cancellationToken);
            UpdateCheckStatusTextBlock.Text = queueInstallWhenIdleRequested
                ? MainWindowInteractionLogic.BuildQueuedUpdateInstallMessage(
                    FormatVersionLabel(updateResult.LatestVersion),
                    downloadedPath)
                : MainWindowInteractionLogic.BuildDeferredUpdateInstallMessage(
                    blockReason,
                    downloadedPath);
            AppendActivity(queueInstallWhenIdleRequested
                ? $"Queued update install for {FormatVersionLabel(updateResult.LatestVersion)} because background processing resumed before the installer handoff finished."
                : $"Deferred update install for {FormatVersionLabel(updateResult.LatestVersion)} because the app became busy after download.");
            OpenContainingFolder(downloadedPath);
            return;
        }

        await PersistPendingDownloadedUpdateAsync(
            downloadedPath,
            updateResult,
            queueInstallWhenIdleRequested: false,
            cancellationToken);
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
        bool queueInstallWhenIdleRequested,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (string.Equals(currentConfig.PendingUpdateZipPath, downloadedPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentConfig.PendingUpdateVersion, updateResult.LatestVersion, StringComparison.Ordinal) &&
            currentConfig.PendingUpdatePublishedAtUtc == updateResult.LatestPublishedAtUtc &&
            currentConfig.PendingUpdateAssetSizeBytes == updateResult.LatestAssetSizeBytes &&
            currentConfig.PendingUpdateInstallWhenIdleRequested == queueInstallWhenIdleRequested)
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            PendingUpdateZipPath = downloadedPath,
            PendingUpdateVersion = updateResult.LatestVersion,
            PendingUpdatePublishedAtUtc = updateResult.LatestPublishedAtUtc,
            PendingUpdateAssetSizeBytes = updateResult.LatestAssetSizeBytes,
            PendingUpdateInstallWhenIdleRequested = queueInstallWhenIdleRequested,
        }, cancellationToken);
    }

    private async Task ClearPendingDownloadedUpdateAsync(CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (string.IsNullOrWhiteSpace(currentConfig.PendingUpdateZipPath) &&
            string.IsNullOrWhiteSpace(currentConfig.PendingUpdateVersion) &&
            !currentConfig.PendingUpdatePublishedAtUtc.HasValue &&
            !currentConfig.PendingUpdateAssetSizeBytes.HasValue &&
            !currentConfig.PendingUpdateInstallWhenIdleRequested)
        {
            return;
        }

        await _liveConfig.SaveAsync(currentConfig with
        {
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
            PendingUpdateInstallWhenIdleRequested = false,
        }, cancellationToken);
    }

    private async Task<string> EnsurePendingDownloadedUpdateAsync(
        AppUpdateCheckResult updateResult,
        bool queueInstallWhenIdleRequested,
        CancellationToken cancellationToken)
    {
        var currentConfig = _liveConfig.Current;
        if (DoesPendingDownloadedUpdateMatch(currentConfig, updateResult))
        {
            await PersistPendingDownloadedUpdateAsync(
                currentConfig.PendingUpdateZipPath,
                updateResult,
                queueInstallWhenIdleRequested,
                cancellationToken);
            return currentConfig.PendingUpdateZipPath;
        }

        var downloadedPath = await _appUpdateService.DownloadUpdateAsync(
            updateResult.DownloadUrl!,
            updateResult.LatestVersion,
            updateResult.LatestAssetSizeBytes,
            cancellationToken);
        await PersistPendingDownloadedUpdateAsync(
            downloadedPath,
            updateResult,
            queueInstallWhenIdleRequested,
            cancellationToken);
        return downloadedPath;
    }

    private static bool DoesPendingDownloadedUpdateMatch(AppConfig config, AppUpdateCheckResult updateResult)
    {
        if (!HasPendingDownloadedUpdate(config) ||
            string.IsNullOrWhiteSpace(config.PendingUpdateZipPath) ||
            !string.Equals(config.PendingUpdateVersion, updateResult.LatestVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (config.PendingUpdatePublishedAtUtc.HasValue &&
            updateResult.LatestPublishedAtUtc.HasValue &&
            config.PendingUpdatePublishedAtUtc.Value != updateResult.LatestPublishedAtUtc.Value)
        {
            return false;
        }

        if (config.PendingUpdateAssetSizeBytes.HasValue &&
            updateResult.LatestAssetSizeBytes.HasValue &&
            config.PendingUpdateAssetSizeBytes.Value != updateResult.LatestAssetSizeBytes.Value)
        {
            return false;
        }

        return true;
    }

    private static bool HasPendingDownloadedUpdate(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.PendingUpdateZipPath) &&
               File.Exists(config.PendingUpdateZipPath);
    }

    private AppUpdateCheckResult BuildPendingDownloadedUpdateResult(AppConfig config)
    {
        var version = string.IsNullOrWhiteSpace(config.PendingUpdateVersion)
            ? AppBranding.Version
            : config.PendingUpdateVersion;
        var message = config.PendingUpdateInstallWhenIdleRequested
            ? $"A previously downloaded update {FormatVersionLabel(version)} is queued and will install automatically once background processing is idle."
            : $"A previously downloaded update {FormatVersionLabel(version)} is ready to install.";

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
            message);
    }

    private AppUpdateLocalState BuildLocalUpdateState(AppConfig config)
    {
        var installedDiagnostics = InstalledApplicationDiagnosticsService.Inspect(
            Environment.ProcessPath,
            AppContext.BaseDirectory);
        var installRoot = UpdateInstallerLaunchBuilder.ResolveInstalledAppRoot(
            Environment.ProcessPath,
            AppContext.BaseDirectory);

        return new AppUpdateLocalState(
            AppBranding.Version,
            string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
                ? AppBranding.Version
                : config.InstalledReleaseVersion,
            installedDiagnostics.InstalledReleasePublishedAtUtc,
            installedDiagnostics.InstalledReleaseAssetSizeBytes,
            UpdateInstallerLaunchBuilder.SupportsStableAppHostInAppUpdates(installRoot));
    }

    private bool TryGetUpdateInstallBlockReason(
        out string reason,
        bool allowCurrentInstallInProgress = false,
        bool allowProcessingOverride = false)
    {
        reason = _appUpdateInstallPolicy.GetInstallBlockReason(
            _recordingCoordinator.IsRecording,
            _processingQueue.IsProcessingInProgress,
            _isUpdateInstallInProgress,
            allowCurrentInstallInProgress,
            allowProcessingOverride) ?? string.Empty;
        return string.IsNullOrEmpty(reason);
    }

    private void ApplyUpdateCheckResult(AppUpdateCheckResult? result, bool manual)
    {
        var currentConfig = _liveConfig.Current;
        RefreshUpdateMetadataDisplay(currentConfig, result);

        if (result is null)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            UpdateBannerTextBlock.Text = string.Empty;
            UpdateCheckStatusTextBlock.Text = HasPendingDownloadedUpdate(currentConfig)
                ? BuildPendingDownloadedUpdateResult(currentConfig).Message
                : $"Current version: {FormatVersionLabel(AppBranding.Version)}. No update check has run yet.";
            UpdateUpdateActionButtons();
            return;
        }

        var hasMatchingPendingDownloadedUpdate =
            result.Status == AppUpdateStatusKind.UpdateAvailable &&
            HasPendingDownloadedUpdate(currentConfig) &&
            !string.IsNullOrWhiteSpace(currentConfig.PendingUpdateVersion) &&
            string.Equals(currentConfig.PendingUpdateVersion, result.LatestVersion, StringComparison.Ordinal);
        UpdateCheckStatusTextBlock.Text = hasMatchingPendingDownloadedUpdate
            ? BuildPendingDownloadedUpdateResult(currentConfig).Message
            : result.Message;

        if (result.Status == AppUpdateStatusKind.UpdateAvailable)
        {
            var installMessage =
                hasMatchingPendingDownloadedUpdate && currentConfig.PendingUpdateInstallWhenIdleRequested
                ? "The ZIP is already downloaded and queued to install automatically once background processing is idle. Use Install Now Anyway if you want to interrupt processing and install immediately."
                : hasMatchingPendingDownloadedUpdate
                ? "The ZIP is already downloaded. Install it from this tab when the app is idle."
                : currentConfig.AutoInstallUpdatesEnabled
                ? "If auto-install is enabled, the app will install it automatically the next time no recording or background processing is active."
                : "Use the Updates tab to install it manually when the app is idle.";
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerTextBlock.Text =
                $"A newer GitHub release is available: {FormatVersionLabel(result.LatestVersion)}. " +
                installMessage;
        }
        else if (result.Status == AppUpdateStatusKind.RequiresInstallerReset)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerTextBlock.Text =
                $"A newer GitHub release is available: {FormatVersionLabel(result.LatestVersion)}. " +
                "This install needs a one-time MSI or bootstrapper reset before in-app updates can resume.";
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
        var installedReleasePublishedAtUtc = installedDiagnostics.InstalledReleasePublishedAtUtc;
        var installedReleaseAssetSizeBytes = installedDiagnostics.InstalledReleaseAssetSizeBytes;
        if (result is { Status: AppUpdateStatusKind.UpToDate })
        {
            installedReleasePublishedAtUtc ??= result?.LatestPublishedAtUtc;
            installedReleaseAssetSizeBytes ??= result?.LatestAssetSizeBytes;
        }

        InstalledAppVersionTextBlock.Text = FormatVersionLabel(AppBranding.Version);
        InstalledReleaseVersionTextBlock.Text = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
            ? FormatVersionLabel(AppBranding.Version)
            : FormatVersionLabel(config.InstalledReleaseVersion);
        InstalledOnTextBlock.Text = FormatUpdateTimestamp(installedDiagnostics.InstalledAtUtc);
        InstalledFootprintTextBlock.Text = FormatUpdateSize(installedDiagnostics.InstallFootprintBytes);
        InstalledReleasePublishedAtTextBlock.Text = FormatUpdateTimestamp(
            installedReleasePublishedAtUtc);
        InstalledReleaseAssetSizeTextBlock.Text = FormatUpdateSize(
            installedReleaseAssetSizeBytes);
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
        if (config.PendingUpdateInstallWhenIdleRequested && HasPendingDownloadedUpdate(config))
        {
            return "A downloaded update is queued and will install automatically once background processing is idle. Use Install Now Anyway in Updates if you want to interrupt processing and install immediately.";
        }

        if (HasPendingDownloadedUpdate(config))
        {
            return "A downloaded update is ready to install from the Updates tab the next time the app is idle.";
        }

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
        var currentConfig = _liveConfig.Current;
        var isCheckingForUpdates = Volatile.Read(ref _updateCheckOperations) > 0;
        var isAnyUpdateActionBusy = isCheckingForUpdates || _isDownloadingUpdate || _isPreparingUpdateInstall || _isUpdateInstallInProgress;
        var hasDownloadableUpdate =
            _lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpdateAvailable } available &&
            !string.IsNullOrWhiteSpace(available.DownloadUrl);
        var hasPendingDownloadedUpdate = HasPendingDownloadedUpdate(currentConfig);
        var installActionState = MainWindowInteractionLogic.BuildUpdateInstallActionState(
            hasDownloadableUpdate || hasPendingDownloadedUpdate,
            isAnyUpdateActionBusy,
            _recordingCoordinator.IsRecording,
            _processingQueue.IsProcessingInProgress,
            currentConfig.PendingUpdateInstallWhenIdleRequested && hasPendingDownloadedUpdate);
        var canDownloadUpdate = hasDownloadableUpdate && !isAnyUpdateActionBusy;

        CheckForUpdatesButton.Content = isCheckingForUpdates ? "Checking..." : "Check for Updates";
        InstallLatestUpdateButton.Content = _isUpdateInstallInProgress
            ? "Installing..."
            : _isPreparingUpdateInstall
                ? "Preparing..."
                : installActionState.PrimaryButtonText;
        DownloadLatestUpdateButton.Content = _isDownloadingUpdate ? "Downloading..." : "Download Latest ZIP";
        InstallQueuedUpdateNowButton.Content = installActionState.OverrideButtonText;

        CheckForUpdatesButton.IsEnabled = !isAnyUpdateActionBusy;
        InstallLatestUpdateButton.IsEnabled = installActionState.PrimaryButtonEnabled;
        DownloadLatestUpdateButton.IsEnabled = canDownloadUpdate;
        OpenLatestReleasePageButton.IsEnabled = !_isUpdateInstallInProgress;
        InstallQueuedUpdateNowButton.IsEnabled = installActionState.OverrideButtonEnabled;
        InstallQueuedUpdateNowButton.Visibility = installActionState.ShowOverrideButton
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateOperationProgressBar.Visibility = isAnyUpdateActionBusy ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstallAvailabilityTextBlock.Text =
            _lastUpdateCheckResult is { Status: AppUpdateStatusKind.UpToDate } && !hasPendingDownloadedUpdate
                ? "No newer GitHub release is currently available."
                : installActionState.AvailabilityText;
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
        var readiness = BuildRecordingReadinessSnapshot();
        UpdateSetupRecordingReadinessPresentation(readiness);
        ApplyHomeCommandCenterState(
            _homeCommandCenterResolver.Resolve(BuildHomeCommandCenterInput(readiness, DateTimeOffset.UtcNow)));
    }

    private RecordingReadinessSnapshot BuildRecordingReadinessSnapshot()
    {
        var config = _liveConfig.Current;
        return new RecordingReadinessService().Project(
            new RecordingReadinessInput(
                HasLocalTranscription: HasReadyTranscriptionModel(),
                HasWritableOutput: HasConfiguredOutputDirectories(config),
                RecordingPermission: RecordingPermissionReadiness.Unknown,
                HasOptionalSpeakerLabeling: _currentDiarizationAssetStatus?.IsReady == true,
                IsMicrophoneCaptureEnabled: config.MicCaptureEnabled,
                IsAutoDetectionEnabled: config.AutoDetectEnabled,
                TeamsCapabilityStatus: config.TeamsCapabilitySnapshot?.Status ?? TeamsCapabilityStatus.FallbackOnly,
                HasAdvancedProviderConfiguration:
                    config.TranscriptionProviderPreference == TranscriptionProviderPreference.LocalCli ||
                    config.DiarizationProviderPreference == DiarizationProviderPreference.LocalCli));
    }

    private void UpdateSetupRecordingReadinessPresentation(RecordingReadinessSnapshot readiness)
    {

        SettingsSetupReadinessPrimaryTextBlock.Text = readiness.PrimaryBlocker?.Summary ??
            "Ready to record. Local transcription and configured meeting output locations are ready.";
        SettingsSetupReadinessNoticesTextBlock.Text = readiness.Notices.Count == 0
            ? "No optional setup action needs attention."
            : string.Join(Environment.NewLine, readiness.Notices.Select(notice => notice.Summary));
        var remediation = readiness.PrimaryBlocker?.RemediationTarget ?? RecordingReadinessRemediationTarget.None;
        SettingsSetupReadinessActionButton.Tag = remediation;
        SettingsSetupReadinessActionButton.Content = GetRecordingReadinessActionLabel(remediation);
        SettingsSetupReadinessActionButton.Visibility = remediation == RecordingReadinessRemediationTarget.None
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private HomeCommandCenterInput BuildHomeCommandCenterInput(
        RecordingReadinessSnapshot readiness,
        DateTimeOffset nowUtc)
    {
        var config = _liveConfig.Current;
        var loopbackStatus = _recordingCoordinator.GetLoopbackCaptureStatusSnapshot();
        var captureTruth = GetHomeCaptureTruth(loopbackStatus);
        var persistedBacklog = BuildPersistedProcessingBacklogState();
        var actionableQueueCount = Math.Max(
            _latestProcessingQueueStatusSnapshot.TotalRemainingCount,
            persistedBacklog?.TotalRemainingCount ?? 0);
        var updateActionable = _lastUpdateCheckResult?.Status is
            AppUpdateStatusKind.UpdateAvailable or AppUpdateStatusKind.RequiresInstallerReset;
        var providerNeedsAttention = config.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
            !_summaryProviderValidationIsCurrent;

        return new HomeCommandCenterInput(
            new HomeRecordingState(_recordingCoordinator.IsRecording, nowUtc),
            new HomeReadinessState(readiness, nowUtc),
            new HomeCaptureState(captureTruth, nowUtc),
            new HomeRecoveryState(
                _shellStatusOverride is not null,
                MapShellStatusTarget(_shellStatusOverride?.Target ?? ShellStatusTarget.None),
                nowUtc),
            new HomeCurrentTaskState(false, HomeCommandCenterTarget.None, null),
            new HomeQueueState(actionableQueueCount, _latestProcessingQueueStatusSnapshot.LastUpdatedAtUtc),
            new HomeUpdateState(updateActionable, config.LastUpdateCheckUtc),
            new HomeProviderState(providerNeedsAttention, _summaryProviderValidationObservedAtUtc),
            nowUtc);
    }

    private static HomeCommandCenterTarget MapShellStatusTarget(ShellStatusTarget target)
    {
        return target switch
        {
            ShellStatusTarget.SettingsSetup => HomeCommandCenterTarget.SettingsSetup,
            ShellStatusTarget.SettingsUpdates => HomeCommandCenterTarget.SettingsUpdates,
            ShellStatusTarget.SettingsGeneral => HomeCommandCenterTarget.SettingsRecording,
            _ => HomeCommandCenterTarget.None,
        };
    }

    private void ApplyHomeCommandCenterState(HomeCommandCenterState state)
    {
        HeaderShellStatusLabelTextBlock.Text = state.Headline;
        HeaderShellStatusDetailTextBlock.Text = state.Reason;
        HeaderShellStatusActionButton.Tag = state.Target;
        HeaderShellStatusActionButton.Content = state.ActionLabel ?? string.Empty;
        HeaderShellStatusActionButton.Visibility = string.IsNullOrWhiteSpace(state.ActionLabel)
            ? Visibility.Hidden
            : Visibility.Visible;

        HomeNextBestActionHeadlineTextBlock.Text = state.Headline;
        HomeNextBestActionReasonTextBlock.Text = state.Reason;
        HomeNextBestActionButton.Tag = state.Target;
        HomeNextBestActionButton.Content = state.ActionLabel ?? string.Empty;
        HomeNextBestActionButton.Visibility = string.IsNullOrWhiteSpace(state.ActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Legacy hidden bindings remain populated while product UI uses one command-center state.
        DashboardPrimaryActionHeadlineTextBlock.Text = state.Headline;
        DashboardPrimaryActionBodyTextBlock.Text = state.Reason;
        DashboardPrimaryActionButton.Tag = state.Target;
        DashboardPrimaryActionButton.Content = state.ActionLabel ?? string.Empty;
        DashboardPrimaryActionButton.Visibility = HeaderShellStatusActionButton.Visibility;

        ApplyShellStatusChrome(state);
    }

    private static bool HasConfiguredOutputDirectories(AppConfig config)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(config.AudioOutputDir) &&
                   !string.IsNullOrWhiteSpace(config.TranscriptOutputDir) &&
                   Directory.Exists(config.AudioOutputDir) &&
                   Directory.Exists(config.TranscriptOutputDir);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string GetRecordingReadinessActionLabel(RecordingReadinessRemediationTarget target)
    {
        return target switch
        {
            RecordingReadinessRemediationTarget.SettingsSetup => "Open transcription setup",
            RecordingReadinessRemediationTarget.SettingsFilesAndUpdates => "Open output locations",
            RecordingReadinessRemediationTarget.WindowsRecordingPrivacy => "Open Windows recording privacy",
            RecordingReadinessRemediationTarget.SettingsRecording => "Open recording settings",
            RecordingReadinessRemediationTarget.SettingsAdvanced => "Open advanced settings",
            _ => string.Empty,
        };
    }

    private void SettingsSetupReadinessActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RecordingReadinessRemediationTarget target })
        {
            return;
        }

        switch (target)
        {
            case RecordingReadinessRemediationTarget.SettingsSetup:
                OpenSettingsSurface(SettingsWindowSection.Setup);
                break;
            case RecordingReadinessRemediationTarget.SettingsFilesAndUpdates:
                OpenSettingsSurface(SettingsInformationArchitecture.ResolveControl("ConfigAudioOutputDirTextBox"));
                break;
            case RecordingReadinessRemediationTarget.WindowsRecordingPrivacy:
                OpenExternalUrl("ms-settings:privacy-microphone");
                break;
            case RecordingReadinessRemediationTarget.SettingsRecording:
                OpenSettingsSurface(SettingsWindowSection.Recording);
                break;
            case RecordingReadinessRemediationTarget.SettingsAdvanced:
                OpenSettingsSurface(SettingsWindowSection.Advanced);
                break;
        }
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

    private void ApplyShellStatusChrome(HomeCommandCenterState state)
    {
        switch (state.Severity)
        {
            case HomeCommandCenterSeverity.Danger:
                SetShellStatusBrushes("AppDangerBackgroundBrush", "AppDangerOutlineBrush", "AppDangerBrush", "AppTextBrush");
                break;
            case HomeCommandCenterSeverity.Warning:
                SetShellStatusBrushes("AppWarningBackgroundBrush", "AppWarningOutlineBrush", "AppWarningBrush", "AppTextBrush");
                break;
            case HomeCommandCenterSeverity.Information:
                SetShellStatusBrushes("AppMutedCardBrush", "AppSecondaryBrush", "AppSecondaryBrush", "AppTextBrush");
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
            _logger.Log($"{settingName} quick setting update failed: {exception}");
            SetConfigSaveStatus(UserActionCopyResolver.Resolve(
                UserActionIntent.SaveSettings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
            AppendActivity("Quick setting update did not finish.");
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
            _logger.Log($"Live microphone capture update failed from {source}: {exception}");
            SetConfigSaveStatus("Microphone capture setting was saved. Active recording keeps its current capture setup.");
            AppendActivity("Active microphone capture was not updated.");
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
        var summaryConfig = _liveConfig.Current;
        var hasOpenAiKey = summaryConfig.SummaryProviderPreference != MeetingSummaryProviderPreference.OpenAiOnly ||
            !string.IsNullOrWhiteSpace(await _summarySecretStore.LoadAsync(SummarySecretKind.OpenAi, cancellationToken));
        var canGenerateSummary = summaryConfig.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled && hasOpenAiKey;

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

                var isTeamsPlaybackMergeReady = manifest?.TeamsRecordingPlayback is { } playback &&
                    DateTimeOffset.UtcNow - playback.LastObservedAtUtc >= TeamsRecordingPlaybackAbsentBeforeMerge;
                var transcript = canGenerateSummary
                    ? MeetingTranscriptDocumentReader.Read(record.JsonPath, record.MarkdownPath)
                    : null;
                var hasPublishedSummary =
                    (transcript?.SummarizationStatus?.State == StageExecutionState.Succeeded && transcript.Summary is not null) ||
                    (manifest?.SummarizationStatus.State == StageExecutionState.Succeeded && manifest.Summary is not null);
                inspections.Add(new MeetingInspectionRecord(
                    record,
                    manifest,
                    suggestedTitle,
                    suggestedTitleSource,
                    isDiarizationReady,
                    isTeamsPlaybackMergeReady,
                    canGenerateSummary,
                    transcript?.HasStructuredJson == true && transcript.StructuredSegments.Count > 0,
                    hasPublishedSummary));
            }

            return (IReadOnlyList<MeetingInspectionRecord>)inspections;
        }, cancellationToken);
    }

    private static TeamsRecordingPlaybackProvenance? TryCreateTeamsRecordingPlaybackProvenance(
        DetectionDecision? decision,
        DateTimeOffset observedAtUtc)
    {
        if (decision?.Platform != MeetingPlatform.Teams)
        {
            return null;
        }

        var recordingId = decision.Signals
            .FirstOrDefault(signal => string.Equals(
                signal.Source,
                TeamsRecordingPlaybackProvenance.DetectionSignalSource,
                StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!Guid.TryParse(recordingId, out var parsedRecordingId))
        {
            return null;
        }

        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(decision.SessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle) ||
            normalizedTitle is "microsoft teams" or "teams" or "ms teams" or "meeting" or "detected meeting")
        {
            return null;
        }

        return new TeamsRecordingPlaybackProvenance(
            parsedRecordingId.ToString("D"),
            normalizedTitle,
            observedAtUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            observedAtUtc);
    }

    private void UpdateTeamsPlaybackCleanupStatus(
        IReadOnlyList<MeetingInspectionRecord> inspections,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations)
    {
        var groups = inspections
            .Where(inspection => inspection.Manifest?.TeamsRecordingPlayback is not null)
            .GroupBy(inspection => inspection.Manifest!.TeamsRecordingPlayback!.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray())
            .Where(group => group.Length >= 2)
            .ToArray();
        if (groups.Length == 0 || IsMeetingActionInProgress())
        {
            return;
        }

        var group = groups[0];
        var stems = group.Select(inspection => inspection.Meeting.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergeRecommendation = recommendations.FirstOrDefault(recommendation =>
            string.Equals(recommendation.ReasonCode, "merge-teams-recording-playback-fragments", StringComparison.Ordinal) &&
            recommendation.RelatedStems.All(stems.Contains));
        if (mergeRecommendation is not null)
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Teams recording playback group ready to merge ({group.Length} fragments).";
            return;
        }

        if (group.Any(inspection => inspection.Meeting.ManifestState is SessionState.Queued or SessionState.Processing or SessionState.Finalizing))
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Teams recording playback group detected; waiting for {group.Length} fragments to finish processing.";
            return;
        }

        if (group.Any(inspection =>
                string.IsNullOrWhiteSpace(inspection.Meeting.AudioPath) ||
                string.IsNullOrWhiteSpace(inspection.Meeting.MarkdownPath) ||
                !File.Exists(inspection.Meeting.AudioPath) ||
                !File.Exists(inspection.Meeting.MarkdownPath)))
        {
            MeetingCleanupRecommendationsStatusTextBlock.Text =
                $"Teams recording playback group blocked; one or more fragments are missing audio or transcript output.";
            return;
        }

        MeetingCleanupRecommendationsStatusTextBlock.Text =
            "Teams recording playback group detected; waiting for the player to be absent for two minutes.";
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
            MeetingCleanupReviewBannerBorder.Visibility = Visibility.Hidden;
            MeetingCleanupReviewBannerTextBlock.Text = string.Empty;
            return;
        }

        var schedulerStatus = GetCleanupSchedulerStatus();
        var impactedMeetingCount = _meetingCleanupRecommendations
            .SelectMany(recommendation => recommendation.RelatedStems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        MeetingCleanupReviewBannerBorder.Visibility = Visibility.Visible;
        var overnight = BackgroundProcessingPolicy.IsOvernightDrainWindowActive(_liveConfig.Current);
        var schedulerDetail = _recordingCoordinator.IsRecording
            ? "Automatic cleanup is paused by the live recording."
            : !string.IsNullOrWhiteSpace(GetAutomaticCleanupSchedulerBlockerReason())
                ? GetAutomaticCleanupSchedulerBlockerReason()!
            : schedulerStatus.ProcessingCount > 0
                ? "Automatic cleanup resumes after the current cleanup worker completes."
                : schedulerStatus.QueuedCount > 0
                    ? $"Automatic cleanup is queued behind processing. {(overnight ? "Overnight batches allow up to 5 items." : "Daytime runs one item after normal work.")}"
                    : !string.IsNullOrWhiteSpace(schedulerStatus.PrimaryBlocker)
                        ? schedulerStatus.PrimaryBlocker
                        : !string.IsNullOrWhiteSpace(_lastCleanupSchedulerDispatchDetail)
                            ? _lastCleanupSchedulerDispatchDetail
                        : overnight
                        ? "Eligible work can run in overnight batches of up to 5 items."
                        : "Eligible work runs one item after normal work.";
        LogCleanupSchedulerStatus(schedulerStatus, schedulerDetail);
        MeetingCleanupReviewBannerTextBlock.Text = MainWindowInteractionLogic.BuildMeetingCleanupSchedulerBannerText(
            _meetingCleanupRecommendations.Length,
            impactedMeetingCount,
            schedulerStatus.SafeFixCount,
            schedulerStatus.EligibleNowCount,
            schedulerStatus.QueuedCount,
            schedulerStatus.ProcessingCount,
            schedulerStatus.DisabledSpeakerLabelCount,
            schedulerStatus.BlockedSummaryCount,
            schedulerStatus.DisabledCleanupCount,
            schedulerStatus.ManualReviewCount,
            schedulerDetail);
    }

    private CleanupSchedulerStatus GetCleanupSchedulerStatus()
    {
        return MeetingCleanupAutoApplyPlanner.BuildSchedulerStatus(
            _meetingCleanupRecommendations,
            _meetingCleanupWorkLedgerService,
            IsScheduledIncrementalRecommendation,
            IsSummaryBlockedBySchedule);
    }

    private bool IsSummaryBlockedBySchedule(MeetingCleanupRecommendation recommendation)
    {
        return recommendation.Action == MeetingCleanupAction.GenerateSummary &&
               !IsScheduledIncrementalRecommendation(recommendation);
    }

    private void LogCleanupSchedulerStatus(CleanupSchedulerStatus status, string schedulerDetail)
    {
        var message =
            $"Cleanup scheduler status changed. eligible={status.EligibleNowCount}, queued={status.QueuedCount}, " +
            $"processing={status.ProcessingCount}, disabledLabels={status.DisabledSpeakerLabelCount}, " +
            $"blockedSummaries={status.BlockedSummaryCount}, disabledCleanup={status.DisabledCleanupCount}, " +
            $"manualReview={status.ManualReviewCount}. detail='{schedulerDetail}'";
        if (string.Equals(message, _lastCleanupSchedulerStatusLog, StringComparison.Ordinal))
        {
            return;
        }

        _lastCleanupSchedulerStatusLog = message;
        _logger.Log(message);
    }

    private async Task TryAutoApplyMeetingCleanupSafeFixesAsync(
        IReadOnlyList<MeetingCleanupRecommendation> visibleRecommendations,
        IReadOnlyList<MeetingOutputRecord> records,
        int refreshVersion,
        CancellationToken cancellationToken,
        AutomationCatalogSnapshot automationSnapshot)
    {
        var maximumOutstanding = BackgroundProcessingPolicy.IsOvernightDrainWindowActive(_liveConfig.Current)
            ? MeetingCleanupAutoApplyPlanner.MaxAutomaticFixesPerBatch
            : 1;
        var outstandingCleanupCount = _meetingCleanupWorkLedgerService.GetEntries()
            .Count(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing);
        var queueIsIdle = _processingQueue.GetStatusSnapshot().RunState == ProcessingQueueRunState.Idle;
        var eligibleRecommendations = MeetingCleanupAutoApplyPlanner.GetNextScheduledBatch(
            visibleRecommendations,
            _meetingCleanupWorkLedgerService,
            recommendation => IsScheduledIncrementalRecommendation(recommendation) &&
                (queueIsIdle || recommendation.Action != MeetingCleanupAction.GenerateSummary),
            maximumBatchSize: Math.Max(0, maximumOutstanding - outstandingCleanupCount));
        var coordinatorDecision = AutomationSchedulerCoordinator.Resolve(new AutomationCoordinatorInput(
            IsScanInProgress: false,
            HasSnapshot: true,
            IsFullSnapshot: automationSnapshot.RefreshMode == AutomationCatalogRefreshMode.Full,
            IsSnapshotCurrent: IsAutomationCatalogSnapshotCurrent(automationSnapshot, refreshVersion),
            IsSnapshotCancelled: cancellationToken.IsCancellationRequested,
            IsShutdownRequested: IsShutdownRequested,
            IsRecording: _recordingCoordinator.IsRecording,
            IsUserActionInProgress: IsUserMeetingMaintenanceInProgress(),
            IsQueueUnderPressure: outstandingCleanupCount >= maximumOutstanding,
            IsProviderAvailable: true,
            IsBackoffActive: _meetingCleanupSchedulerFailureBackoffUntilUtc is { } backoffUntil && DateTimeOffset.UtcNow < backoffUntil,
            HasInFlightWorkerWork: _isApplyingSafeMeetingCleanupFixes,
            EligibleRecommendationCount: eligibleRecommendations.Count));
        if (!coordinatorDecision.CanDispatch)
        {
            RecordCleanupSchedulerDispatchDetail(outstandingCleanupCount >= maximumOutstanding
                ? $"Automatic cleanup is using its {maximumOutstanding}-item {(maximumOutstanding == 1 ? "daytime" : "overnight")} worker allowance."
                : coordinatorDecision.StatusText);
            return;
        }

        _isApplyingSafeMeetingCleanupFixes = true;
        RecordCleanupSchedulerDispatchDetail(
            $"Dispatched {eligibleRecommendations.Count} scheduled cleanup item(s) after the meeting refresh completed.");
        UpdateMeetingActionState();
        MeetingCleanupRecommendationsStatusTextBlock.Text =
            $"Running {eligibleRecommendations.Count} scheduled incremental work item(s)...";

        try
        {
            var meetingsByStem = records.ToDictionary(
                record => record.Stem,
                StringComparer.OrdinalIgnoreCase);
            var archiveRoot = MeetingCleanupExecutionService.GetArchiveRoot(_liveConfig.Current.AudioOutputDir);
            var archiveDirectory = MeetingCleanupExecutionService.CreateExecutionArchiveDirectory(archiveRoot, "auto-safe-fixes");
            var batchResult = await MeetingCleanupRecommendationBatchRunner.ExecuteAsync(
                eligibleRecommendations,
                (recommendation, batchCancellationToken) =>
                    ExecuteAutomaticMeetingCleanupRecommendationAsync(
                        recommendation,
                        archiveDirectory,
                        meetingsByStem,
                        automationSnapshot,
                        batchCancellationToken),
                continueOnError: true,
                cancellationToken);

            await RefreshMeetingListAsync();

            var remainingRecommendationCount = _meetingCleanupRecommendations.Length;
            MeetingCleanupRecommendationsStatusTextBlock.Text = BuildAutomaticMeetingCleanupApplyStatusText(
                batchResult.SucceededCount,
                batchResult.SkippedCount,
                remainingRecommendationCount);
            AppendActivity(BuildAutomaticMeetingCleanupApplyActivityText(
                batchResult.SucceededCount,
                batchResult.FailedCount,
                batchResult.SkippedCount,
                remainingRecommendationCount));
        }
        finally
        {
            _isApplyingSafeMeetingCleanupFixes = false;
            UpdateMeetingActionState();
        }
    }

    private IReadOnlyList<MeetingCleanupRecommendation> GetEligibleScheduledIncrementalRecommendations()
    {
        return MeetingCleanupAutoApplyPlanner.GetEligibleScheduledRecommendations(
            _meetingCleanupRecommendations,
            _meetingCleanupWorkLedgerService,
            IsScheduledIncrementalRecommendation);
    }

    private bool IsScheduledIncrementalRecommendation(MeetingCleanupRecommendation recommendation)
    {
        var plan = _liveConfig.Current.IncrementalWorkPlan;
        return recommendation.Action switch
        {
            MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels =>
                plan.HasFlag(IncrementalWorkPlan.DeferredSpeakerLabels),
            MeetingCleanupAction.GenerateSummary =>
                plan.HasFlag(IncrementalWorkPlan.MissingAiSummaries) &&
                _summaryProviderValidationIsCurrent,
            _ => plan.HasFlag(IncrementalWorkPlan.SafeCleanup) &&
                MainWindowInteractionLogic.IsSafeMeetingCleanupRecommendation(recommendation),
        };
    }

    private bool CanExecuteAutomaticCleanupRecommendation(
        MeetingCleanupRecommendation recommendation,
        AutomationCatalogSnapshot automationSnapshot,
        CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               IsAutomationCatalogSnapshotCurrent(automationSnapshot, automationSnapshot.RefreshVersion) &&
               !IsShutdownRequested &&
               !_recordingCoordinator.IsRecording &&
               !IsUserMeetingMaintenanceInProgress() &&
               !(_meetingCleanupSchedulerFailureBackoffUntilUtc is { } backoffUntil && DateTimeOffset.UtcNow < backoffUntil) &&
               IsScheduledIncrementalRecommendation(recommendation) &&
               _meetingCleanupWorkLedgerService.IsEligibleForAutomaticApply(recommendation.Fingerprint) &&
               _meetingCleanupRecommendations.Any(current =>
                   string.Equals(current.Fingerprint, recommendation.Fingerprint, StringComparison.Ordinal));
    }

    private bool IsAutomationCatalogSnapshotCurrent(
        AutomationCatalogSnapshot automationSnapshot,
        int refreshVersion)
    {
        return automationSnapshot.RefreshMode == AutomationCatalogRefreshMode.Full &&
               automationSnapshot.PolicyRevision == AutomationPolicyRevision &&
               automationSnapshot.RefreshVersion == refreshVersion &&
               refreshVersion == Volatile.Read(ref _meetingRefreshVersion) &&
               automationSnapshot.CancellationIdentity == _meetingBackgroundWorkCancellationIdentity;
    }

    private async Task ExecuteAutomaticMeetingCleanupRecommendationAsync(
        MeetingCleanupRecommendation recommendation,
        string archiveDirectory,
        IReadOnlyDictionary<string, MeetingOutputRecord> meetingsByStem,
        AutomationCatalogSnapshot automationSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!CanExecuteAutomaticCleanupRecommendation(recommendation, automationSnapshot, cancellationToken))
            {
                throw new AutomationDispatchSkippedException("The meeting catalog or automatic-work policy changed before this item could run.");
            }

            if (MeetingCleanupAutoApplyPlanner.ShouldSuppressSuccessfulAutomaticApply(recommendation.Action))
            {
                // Persist intent before enqueueing. Queue acceptance is not
                // completion, but a restart can now reconcile this request.
                _meetingCleanupWorkLedgerService.Record(
                    recommendation.Fingerprint,
                    CleanupWorkState.Queued,
                    action: recommendation.Action,
                    affectedStems: recommendation.RelatedStems,
                    inputRevision: automationSnapshot.InputRevision);
            }

            await ExecuteMeetingCleanupRecommendationAsync(
                recommendation,
                archiveDirectory,
                meetingsByStem,
                cancellationToken,
                ProcessingWorkPriority.Cleanup,
                recommendation.Fingerprint);
            if (!MeetingCleanupAutoApplyPlanner.ShouldSuppressSuccessfulAutomaticApply(recommendation.Action))
            {
                _meetingCleanupWorkLedgerService.Record(
                    recommendation.Fingerprint,
                    CleanupWorkState.Completed,
                    action: recommendation.Action,
                    affectedStems: recommendation.RelatedStems,
                    inputRevision: automationSnapshot.InputRevision);
            }
            else
            {
                _meetingCleanupWorkLedgerService.Record(
                    recommendation.Fingerprint,
                    CleanupWorkState.Queued,
                    action: recommendation.Action,
                    affectedStems: recommendation.RelatedStems,
                    inputRevision: automationSnapshot.InputRevision);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log($"Automatic meeting cleanup action failed: {exception}");
            _meetingCleanupWorkLedgerService.Record(
                recommendation.Fingerprint,
                CleanupWorkState.Failed,
                detail: UserActionCopyResolver.Resolve(
                    UserActionIntent.ApplyCleanupRecommendations,
                    UserActionBlockedReasonKind.OperationFailed).BlockedText,
                action: recommendation.Action,
                affectedStems: recommendation.RelatedStems,
                inputRevision: automationSnapshot.InputRevision);
            throw;
        }
    }

    private static string BuildAutomaticMeetingCleanupApplyStatusText(
        int appliedCount,
        int skippedCount,
        int remainingRecommendationCount)
    {
        var appliedText = appliedCount == 0
            ? "Automatic safe cleanup did not apply any fixes."
            : $"Automatically applied {appliedCount} safe cleanup fix(es).";
        var skippedText = skippedCount == 0
            ? string.Empty
            : $" {skippedCount} item(s) were skipped because the current catalog or policy changed.";
        return remainingRecommendationCount == 0
            ? appliedText + skippedText
            : $"{appliedText}{skippedText} {remainingRecommendationCount} recommendation(s) still need manual review.";
    }

    private static string BuildAutomaticMeetingCleanupApplyActivityText(
        int appliedCount,
        int failedCount,
        int skippedCount,
        int remainingRecommendationCount)
    {
        var statusText = BuildAutomaticMeetingCleanupApplyStatusText(appliedCount, skippedCount, remainingRecommendationCount);
        return failedCount == 0
            ? statusText
            : $"{statusText} Suppressed {failedCount} failed automatic retry attempt(s) until the recommendation changes.";
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
            await ExecuteMeetingCleanupRecommendationAsync(
                recommendation,
                archiveDirectory,
                meetingsByStem: null,
                cancellationToken);
        }
    }

    private async Task ExecuteMeetingCleanupRecommendationAsync(
        MeetingCleanupRecommendation recommendation,
        string archiveDirectory,
        IReadOnlyDictionary<string, MeetingOutputRecord>? meetingsByStem,
        CancellationToken cancellationToken,
        ProcessingWorkPriority processingPriority = ProcessingWorkPriority.Normal,
        string? cleanupFingerprint = null)
    {
        if (meetingsByStem is null)
        {
            var config = _liveConfig.Current;
            meetingsByStem = await Task.Run(
                () => _meetingOutputCatalogService.ListMeetings(
                        config.AudioOutputDir,
                        config.TranscriptOutputDir,
                        config.WorkDir)
                    .ToDictionary(record => record.Stem, StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        switch (recommendation.Action)
        {
            case MeetingCleanupAction.Archive:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var archiveMeeting))
                {
                    return;
                }

                await _meetingCleanupExecutionService.ArchiveMeetingAsync(
                    archiveMeeting,
                    archiveDirectory,
                    ResolveMeetingCleanupArchiveCategory(recommendation),
                    cancellationToken);
                return;

            case MeetingCleanupAction.Merge:
                if (recommendation.RelatedStems.Count < 2)
                {
                    return;
                }

                var mergeMeetings = recommendation.RelatedStems
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(stem => meetingsByStem.TryGetValue(stem, out var meeting) ? meeting : null)
                    .ToArray();
                if (mergeMeetings.Length != recommendation.RelatedStems.Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
                    mergeMeetings.Any(meeting => meeting is null))
                {
                    return;
                }

                var orderedMergeMeetings = mergeMeetings.Select(meeting => meeting!).ToArray();

                await _meetingCleanupExecutionService.MergeMeetingsAsync(
                    orderedMergeMeetings,
                    recommendation.SuggestedTitle ?? orderedMergeMeetings[0].Title,
                    _liveConfig.Current.AudioOutputDir,
                    _liveConfig.Current.TranscriptOutputDir,
                    archiveDirectory,
                    cancellationToken);
                return;

            case MeetingCleanupAction.Rename:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var renameMeeting) ||
                    string.IsNullOrWhiteSpace(recommendation.SuggestedTitle))
                {
                    return;
                }

                await _meetingCleanupExecutionService.RenameMeetingAsync(
                    _liveConfig.Current.AudioOutputDir,
                    _liveConfig.Current.TranscriptOutputDir,
                    _liveConfig.Current.WorkDir,
                    renameMeeting,
                    recommendation.SuggestedTitle,
                    cancellationToken);
                return;

            case MeetingCleanupAction.RegenerateTranscript:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var regenerateMeeting))
                {
                    return;
                }

                await QueueTranscriptRegenerationAsync(regenerateMeeting, cancellationToken, processingPriority, cleanupFingerprint);
                return;

            case MeetingCleanupAction.GenerateSpeakerLabels:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var speakerLabelMeeting))
                {
                    return;
                }

                await QueueSpeakerLabelGenerationAsync(speakerLabelMeeting, cancellationToken, processingPriority, cleanupFingerprint);
                return;

            case MeetingCleanupAction.RepairSpeakerLabels:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var repairSpeakerLabelMeeting))
                {
                    return;
                }

                await QueueSpeakerLabelRepairAsync(repairSpeakerLabelMeeting, cancellationToken, processingPriority, cleanupFingerprint);
                return;

            case MeetingCleanupAction.GenerateSummary:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var summaryMeeting))
                {
                    return;
                }

                var summaryResult = await _publishedMeetingSummaryService.GenerateAsync(
                    summaryMeeting,
                    _liveConfig.Current,
                    cancellationToken);
                if (summaryResult.Status.State != StageExecutionState.Succeeded)
                {
                    throw new InvalidOperationException(
                        summaryResult.Status.Message ?? "AI summary generation did not produce a usable summary.");
                }

                return;

            case MeetingCleanupAction.Split:
                if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var splitMeeting) ||
                    recommendation.SuggestedSplitPoint is not { } suggestedSplitPoint)
                {
                    return;
                }

                await QueueSplitMeetingAsync(splitMeeting, suggestedSplitPoint, cancellationToken);
                return;
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

    private async Task ApplyPendingCurrentMetadataAsync(
        CancellationToken cancellationToken,
        bool applyDeferredReclassification = true)
    {
        var activeSession = _recordingCoordinator.ActiveSession;
        if (activeSession is null)
        {
            return;
        }

        if (applyDeferredReclassification)
        {
            await TryApplyDeferredMeetingReclassificationAsync(activeSession, cancellationToken);
            activeSession = _recordingCoordinator.ActiveSession;
            if (activeSession is null)
            {
                return;
            }
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
            _logger.Log($"Live meeting metadata save failed: {exception}");
            AppendActivity("Live meeting metadata was not saved.");
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
            _logger.Log($"Whisper model status refresh failed: {exception}");
            UpdateModelCatalog(Array.Empty<WhisperModelCatalogItem>());
            ApplyWhisperModelStatusDisplayState(WhisperModelStatusDisplayStateFactory.CreateError(
                UserActionCopyResolver.Resolve(
                    UserActionIntent.ManageModelAssets,
                    UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText));
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
            _logger.Log($"Speaker-labeling asset status refresh failed: {exception}");
            DiarizationAssetStatusTextBlock.Text = "Unable to inspect diarization assets.";
            DiarizationAssetStatusTextBlock.Foreground = UnhealthyModelStatusBrush;
            DiarizationAssetDetailsTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ManageModelAssets,
                UserActionBlockedReasonKind.LocalStorageUnavailable).BlockedText;
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
                ModelActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                    UserActionIntent.ManageModelAssets,
                    UserActionBlockedReasonKind.NetworkUnavailable).BlockedText;
            }

            _logger.Log($"Downloadable Whisper model catalog load failed: {exception}");
            AppendActivity("Downloadable Whisper model catalog was not loaded.");
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
                DiarizationActionStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                    UserActionIntent.ManageModelAssets,
                    UserActionBlockedReasonKind.NetworkUnavailable).BlockedText;
            }

            _logger.Log($"Downloadable speaker-labeling asset catalog load failed: {exception}");
            AppendActivity("Downloadable speaker-labeling asset catalog was not loaded.");
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
        UpdateRecordingControlState();
        UpdateExternalAudioImportReviewState();
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
        var preferenceText = config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto
            ? "GPU acceleration is enabled. Speaker labeling tries DirectML only when the bundled runtime supports it, then falls back to CPU if GPU initialization or a DirectML run fails."
            : "GPU acceleration is off. Speaker labeling uses CPU unless you opt into DirectML.";
        var accelerationDiagnostic = GetSafeAccelerationDiagnostic(status?.DiagnosticMessage);

        var lastRunText = status?.EffectiveExecutionProvider switch
        {
            DiarizationExecutionProvider.Directml => "The last speaker-labeling run used DirectML.",
            DiarizationExecutionProvider.Cpu when status.GpuAccelerationAvailable == false &&
                !string.IsNullOrWhiteSpace(accelerationDiagnostic)
                    => $"{accelerationDiagnostic} CPU fallback was used.",
            DiarizationExecutionProvider.Cpu => "The last speaker-labeling run used CPU.",
            _ => status?.LastDirectMlProbeSucceeded is not null
                ? "No speaker-labeling run has reported DirectML or CPU yet."
                : config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto
                ? "Use Test GPU to check DirectML before the next speaker-labeling run."
                : "DirectML is not tested while GPU acceleration is off.",
        };
        var probeText = BuildLastDirectMlProbeStatusText(status);
        var deferredModeText = config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto &&
            config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred
                ? "DirectML will apply to manual speaker labeling or future Throttled/Inline runs."
                : null;

        ConfigDiarizationAccelerationStatusTextBlock.Text = string.Join(
            " ",
            new[] { preferenceText, lastRunText, probeText, deferredModeText }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildDiarizationGpuProbeSuccessStatusText(
        AppConfig config,
        DiarizationAssetInstallStatus? status,
        string probeMessage)
    {
        var modeText = config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred
            ? " DirectML will apply to manual speaker labeling or future Throttled/Inline runs."
            : string.Empty;
        var lastRunText = status?.EffectiveExecutionProvider switch
        {
            DiarizationExecutionProvider.Directml => " The last speaker-labeling run used DirectML.",
            DiarizationExecutionProvider.Cpu => " The last speaker-labeling run used CPU.",
            _ => string.Empty,
        };

        return $"{probeMessage} GPU acceleration is now enabled. Speaker labeling will try GPU first and fall back to CPU if a DirectML run fails.{modeText}{lastRunText}";
    }

    private static string? BuildLastDirectMlProbeStatusText(DiarizationAssetInstallStatus? status)
    {
        var probeDiagnostic = GetSafeAccelerationDiagnostic(status?.LastDirectMlProbeMessage);
        return status?.LastDirectMlProbeSucceeded switch
        {
            true => "Last GPU test succeeded.",
            false when !string.IsNullOrWhiteSpace(probeDiagnostic) => $"Last GPU test failed: {probeDiagnostic}",
            false => "Last GPU test failed.",
            _ => null,
        };
    }

    private static string? GetSafeAccelerationDiagnostic(string? diagnosticMessage)
    {
        if (string.IsNullOrWhiteSpace(diagnosticMessage))
        {
            return null;
        }

        var trimmed = diagnosticMessage.Trim();
        return trimmed.Contains("DirectML", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("GPU", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    private static bool IsSafeDirectMlRuntimeUnavailableMessage(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            message.Contains("DirectML-enabled speaker-labeling runtime", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDiarizationAccelerationDetails(DiarizationAssetInstallStatus status)
    {
        var providerText = status.EffectiveExecutionProvider switch
        {
            DiarizationExecutionProvider.Directml => "Last effective diarization provider: DirectML.",
            DiarizationExecutionProvider.Cpu => "Last effective diarization provider: CPU.",
            _ => "No diarization run has reported an effective provider yet.",
        };

        var runtimeAvailabilityText = status.GpuAccelerationAvailable switch
        {
            true => "Last speaker-labeling run reported DirectML available.",
            false => "Last speaker-labeling run reported CPU fallback available.",
            _ => "No speaker-labeling run has reported GPU availability yet.",
        };
        var lastProbeText = BuildLastDirectMlProbeStatusText(status) ?? "No GPU test has been recorded yet.";

        if (string.IsNullOrWhiteSpace(status.DiagnosticMessage))
        {
            return string.Join(" ", new[] { providerText, runtimeAvailabilityText, lastProbeText }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        return string.Join(
            " ",
            new[] { providerText, runtimeAvailabilityText, lastProbeText, $"Diagnostic: {status.DiagnosticMessage}" }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
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
        UseStandardTranscriptionProfileButton.Content = _isActivatingModel
            ? "Downloading..."
            : "Use recommended";
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
        CancelRecommendedTranscriptionSetupButton.Visibility = _modelProvisioningCts is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        CancelRecommendedTranscriptionSetupButton.IsEnabled = _modelProvisioningCts is { IsCancellationRequested: false };
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
        if (_currentWhisperModelDisplayState is not null)
        {
            return _currentWhisperModelDisplayState.IsHealthy;
        }

        try
        {
            return _whisperModelService.Inspect(_liveConfig.Current.TranscriptionModelPath).Kind == WhisperModelStatusKind.Valid;
        }
        catch
        {
            return false;
        }
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
        _activityLogLines.Enqueue($"{DateTimeOffset.Now:t} {message}");
        TrimActivityLogLines();
        ActivityTextBox.Text = string.Concat(_activityLogLines.Select(line => line + Environment.NewLine));
        if (ActivityTextBox.IsVisible)
        {
            ActivityTextBox.ScrollToEnd();
        }
    }

    private void TrimActivityLogLines()
    {
        while (_activityLogLines.Count > MaxActivityLogLines)
        {
            _activityLogLines.Dequeue();
        }
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
            _logger.Log($"Open artifact path failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.OpenArtifact,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Open artifact folder failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.OpenArtifact,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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
            _logger.Log($"Open external URL failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.OpenArtifact,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
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

        item.Focus();
        e.Handled = true;
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
        var hasRecommendationInSelection = selectedMeetings.Any(row => row.PrimaryRecommendation.HasPrimaryAction);
        var canAddSpeakerLabels = selectedMeetings.Any(CanQueueSpeakerLabelsForMeeting);
        var canProcessAsap = focusedMeeting is not null && CanChangeRushProcessing(focusedMeeting);
        var isSelectedMeetingAsap = IsMeetingMarkedAsap(focusedMeeting);
        var catalogState = ResolveMeetingActionCatalogState(selectedMeetings, focusedMeeting, IsMeetingActionInProgress());
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

        OpenMeetingDetailsMenuItem.IsEnabled = catalogState[MeetingActionId.OpenDetails].IsEligible;
        OpenMeetingTranscriptMenuItem.IsEnabled = catalogState[MeetingActionId.OpenTranscript].IsEligible;
        OpenMeetingAudioMenuItem.IsEnabled = catalogState[MeetingActionId.OpenAudio].IsEligible;
        OpenMeetingContainingFolderMenuItem.IsEnabled = catalogState[MeetingActionId.OpenContainingFolder].IsEligible;
        CopyMeetingTranscriptPathMenuItem.IsEnabled = catalogState[MeetingActionId.CopyTranscriptPath].IsEligible;
        CopyMeetingAudioPathMenuItem.IsEnabled = catalogState[MeetingActionId.CopyAudioPath].IsEligible;
        OpenMeetingDetailsMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingTranscriptMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingAudioMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingContainingFolderMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        CopyMeetingTranscriptPathMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        CopyMeetingAudioPathMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OpenMeetingActionsFamilyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        FixMeetingActionsFamilyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        OrganizeMeetingActionsFamilyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        DangerMeetingActionsFamilyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyMeetingRecommendedActionMenuItem.IsEnabled = catalogState[MeetingActionId.ReviewRecommendation].IsEligible;
        ApplyMeetingRecommendedActionMenuItem.Header = focusedMeeting?.PrimaryRecommendation.HasPrimaryAction == true
            ? focusedMeeting.PrimaryRecommendation.Label
            : "Recommended action unavailable";
        RenameMeetingContextMenuItem.IsEnabled = catalogState[MeetingActionId.Rename].IsEligible;
        SuggestMeetingTitleContextMenuItem.IsEnabled = catalogState[MeetingActionId.SuggestTitle].IsEligible;
        RetryMeetingTranscriptContextMenuItem.IsEnabled = catalogState[MeetingActionId.RetryTranscript].IsEligible;
        ReTranscribeMeetingWithDifferentModelMenuItem.IsEnabled = catalogState[MeetingActionId.ReTranscribeWithDifferentModel].IsEligible;
        AddSpeakerLabelsContextMenuItem.IsEnabled = catalogState[MeetingActionId.AddSpeakerLabels].IsEligible;
        ProcessAsapContextMenuItem.IsEnabled = catalogState[MeetingActionId.ProcessAsap].IsEligible || catalogState[MeetingActionId.ClearAsap].IsEligible;
        ProcessAsapContextMenuItem.Header = catalogState[MeetingActionId.ClearAsap].IsEligible ? "Clear ASAP" : "Process This ASAP...";
        SplitMeetingContextMenuItem.IsEnabled = catalogState[MeetingActionId.Split].IsEligible;
        ArchiveMeetingContextMenuItem.IsEnabled = catalogState[MeetingActionId.Archive].IsEligible;
        DeleteMeetingPermanentlyMenuItem.IsEnabled = catalogState[MeetingActionId.DeletePermanently].IsEligible;
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
        ProcessingMeetingActionsFamilyMenuItem.Visibility = ProcessAsapContextMenuItem.Visibility;
        SplitMeetingContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ArchiveMeetingContextMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteMeetingPermanentlyMenuItem.Visibility = contextState.ShowSingleMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyRecommendationsForSelectedMeetingsMenuItem.IsEnabled = catalogState[MeetingActionId.ApplyRecommendations].IsEligible;
        MergeSelectedMeetingsContextMenuItem.IsEnabled = catalogState[MeetingActionId.MergeSelected].IsEligible;
        ReTranscribeSelectedMeetingsWithModelMenuItem.IsEnabled = catalogState[MeetingActionId.ReTranscribeSelectedWithModel].IsEligible;
        AddSpeakerLabelsToSelectedMeetingsMenuItem.IsEnabled = catalogState[MeetingActionId.AddSpeakerLabelsToSelected].IsEligible;
        ArchiveSelectedMeetingsMenuItem.IsEnabled = catalogState[MeetingActionId.ArchiveSelected].IsEligible;
        DeleteSelectedMeetingsPermanentlyMenuItem.IsEnabled = catalogState[MeetingActionId.DeleteSelectedPermanently].IsEligible;
        ApplyMeetingActionCatalogPresentation(OpenMeetingDetailsMenuItem, catalogState[MeetingActionId.OpenDetails]);
        ApplyMeetingActionCatalogPresentation(OpenMeetingTranscriptMenuItem, catalogState[MeetingActionId.OpenTranscript]);
        ApplyMeetingActionCatalogPresentation(OpenMeetingAudioMenuItem, catalogState[MeetingActionId.OpenAudio]);
        ApplyMeetingActionCatalogPresentation(OpenMeetingContainingFolderMenuItem, catalogState[MeetingActionId.OpenContainingFolder]);
        ApplyMeetingActionCatalogPresentation(CopyMeetingTranscriptPathMenuItem, catalogState[MeetingActionId.CopyTranscriptPath]);
        ApplyMeetingActionCatalogPresentation(CopyMeetingAudioPathMenuItem, catalogState[MeetingActionId.CopyAudioPath]);
        ApplyMeetingActionCatalogPresentation(ApplyMeetingRecommendedActionMenuItem, catalogState[MeetingActionId.ReviewRecommendation]);
        ApplyMeetingActionCatalogPresentation(RenameMeetingContextMenuItem, catalogState[MeetingActionId.Rename]);
        ApplyMeetingActionCatalogPresentation(SuggestMeetingTitleContextMenuItem, catalogState[MeetingActionId.SuggestTitle]);
        ApplyMeetingActionCatalogPresentation(RetryMeetingTranscriptContextMenuItem, catalogState[MeetingActionId.RetryTranscript]);
        ApplyMeetingActionCatalogPresentation(ReTranscribeMeetingWithDifferentModelMenuItem, catalogState[MeetingActionId.ReTranscribeWithDifferentModel]);
        ApplyMeetingActionCatalogPresentation(AddSpeakerLabelsContextMenuItem, catalogState[MeetingActionId.AddSpeakerLabels]);
        ApplyMeetingActionCatalogPresentation(ProcessAsapContextMenuItem, isSelectedMeetingAsap
            ? catalogState[MeetingActionId.ClearAsap]
            : catalogState[MeetingActionId.ProcessAsap]);
        ApplyMeetingActionCatalogPresentation(SplitMeetingContextMenuItem, catalogState[MeetingActionId.Split]);
        ApplyMeetingActionCatalogPresentation(ArchiveMeetingContextMenuItem, catalogState[MeetingActionId.Archive]);
        ApplyMeetingActionCatalogPresentation(DeleteMeetingPermanentlyMenuItem, catalogState[MeetingActionId.DeletePermanently]);
        ApplyMeetingActionCatalogPresentation(ApplyRecommendationsForSelectedMeetingsMenuItem, catalogState[MeetingActionId.ApplyRecommendations], includeCounts: true);
        ApplyMeetingActionCatalogPresentation(MergeSelectedMeetingsContextMenuItem, catalogState[MeetingActionId.MergeSelected], includeCounts: true);
        ApplyMeetingActionCatalogPresentation(ReTranscribeSelectedMeetingsWithModelMenuItem, catalogState[MeetingActionId.ReTranscribeSelectedWithModel], includeCounts: true);
        ApplyMeetingActionCatalogPresentation(AddSpeakerLabelsToSelectedMeetingsMenuItem, catalogState[MeetingActionId.AddSpeakerLabelsToSelected], includeCounts: true);
        ApplyMeetingActionCatalogPresentation(ArchiveSelectedMeetingsMenuItem, catalogState[MeetingActionId.ArchiveSelected], includeCounts: true);
        ApplyMeetingActionCatalogPresentation(DeleteSelectedMeetingsPermanentlyMenuItem, catalogState[MeetingActionId.DeleteSelectedPermanently], includeCounts: true);
        BulkMeetingContextMenuSeparator.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        BulkMeetingActionsFamilyMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ApplyRecommendationsForSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        MergeSelectedMeetingsContextMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ReTranscribeSelectedMeetingsWithModelMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        AddSpeakerLabelsToSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        ArchiveSelectedMeetingsMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteSelectedMeetingsPermanentlyMenuItem.Visibility = contextState.ShowBulkMeetingActionGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private static MeetingActionSelectionAvailability BuildMeetingActionSelectionAvailability(
        IReadOnlyList<MeetingListRow> selectedMeetings,
        Func<MeetingListRow, bool> isEligible,
        string blockedReason)
    {
        var eligibleCount = selectedMeetings.Count(isEligible);
        var blockedCount = selectedMeetings.Count - eligibleCount;
        return new MeetingActionSelectionAvailability(
            eligibleCount,
            blockedCount,
            blockedCount == 0 ? null : blockedReason);
    }

    private MeetingActionCatalogState ResolveMeetingActionCatalogState(
        IReadOnlyList<MeetingListRow> selectedMeetings,
        MeetingListRow? focusedMeeting,
        bool isBusy)
    {
        var matchingSelectedMeetingRecommendations = _meetingCleanupRecommendations
            .Where(recommendation => recommendation.RelatedStems.All(stem =>
                selectedMeetings.Any(row => string.Equals(row.Source.Stem, stem, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var perActionAvailability = new Dictionary<MeetingActionId, MeetingActionSelectionAvailability>
        {
            [MeetingActionId.ApplyRecommendations] = new(
                matchingSelectedMeetingRecommendations.Length > 0 ? selectedMeetings.Count : 0,
                matchingSelectedMeetingRecommendations.Length > 0 ? 0 : selectedMeetings.Count,
                "No cleanup recommendations match the selected meetings."),
            [MeetingActionId.ReTranscribeSelectedWithModel] = BuildMeetingActionSelectionAvailability(
                selectedMeetings,
                row => row.CanRegenerateTranscript,
                "One or more selected meetings have no source audio for transcript recovery."),
            [MeetingActionId.AddSpeakerLabelsToSelected] = BuildMeetingActionSelectionAvailability(
                selectedMeetings,
                CanQueueSpeakerLabelsForMeeting,
                "One or more selected meetings are not eligible for speaker labeling."),
            [MeetingActionId.ArchiveSelected] = new(selectedMeetings.Count, 0, null),
            [MeetingActionId.DeleteSelectedPermanently] = new(selectedMeetings.Count, 0, null),
            [MeetingActionId.MergeSelected] = new(selectedMeetings.Count >= 2 ? selectedMeetings.Count : 0, 0, null),
        };
        return _meetingActionCatalog.Resolve(new MeetingActionCatalogInput(
            selectedMeetings.Count,
            isBusy,
            focusedMeeting is not null,
            focusedMeeting?.CanOpenAudioArtifact == true,
            focusedMeeting?.CanOpenTranscriptArtifact == true,
            selectedMeetings.Any(row => row.PrimaryRecommendation.HasPrimaryAction),
            focusedMeeting?.CanRegenerateTranscript == true,
            focusedMeeting?.Source.Duration is { } duration && duration > TimeSpan.FromSeconds(2),
            selectedMeetings.Any(CanQueueSpeakerLabelsForMeeting),
            focusedMeeting is not null && CanChangeRushProcessing(focusedMeeting),
            IsMeetingMarkedAsap(focusedMeeting),
            matchingSelectedMeetingRecommendations.Length > 0,
            CanMergeSelected: selectedMeetings.Count >= 2,
            CanReTranscribeSelected: selectedMeetings.Any(row => row.CanRegenerateTranscript),
            CanAddSpeakerLabelsToSelected: selectedMeetings.Any(CanQueueSpeakerLabelsForMeeting),
            CanArchiveSelected: selectedMeetings.Count > 0,
            CanDeleteSelectedPermanently: selectedMeetings.Count > 0,
            HasProcessingBacklog: _latestProcessingQueueStatusSnapshot.TotalRemainingCount > 0 || BuildPersistedProcessingBacklogState() is not null,
            SelectionAvailability: perActionAvailability));
    }

    private static void ApplyMeetingActionCatalogPresentation(
        MenuItem menuItem,
        MeetingActionEligibility action,
        bool includeCounts = false)
    {
        menuItem.IsEnabled = action.IsEligible;
        menuItem.ToolTip = action.IsEligible
            ? BuildMeetingActionAvailabilityText(action)
            : action.BlockReason ?? "This action is unavailable.";

        if (includeCounts && action.SelectedCount > 0)
        {
            menuItem.Header = $"{action.Entry.AccessibleLabel} ({action.EligibleCount}/{action.SelectedCount} eligible)";
        }
    }

    private static string BuildMeetingActionAvailabilityText(MeetingActionEligibility action)
    {
        if (action.SelectedCount == 0)
        {
            return action.Entry.AccessibleLabel;
        }

        var counts = $"{action.EligibleCount} of {action.SelectedCount} selected meeting(s) eligible";
        return action.BlockedCount > 0 && !string.IsNullOrWhiteSpace(action.BlockReason)
            ? $"{counts}. {action.BlockReason}"
            : counts + ".";
    }

    private void OpenMeetingDetailsButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenMeetingDetailsForSelection();
    }

    private void OpenMeetingDetailsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var targetMeetings = GetMeetingRowsForContextMenuAction(sender);
        if (targetMeetings.Length == 1)
        {
            OpenMeetingDetails(targetMeetings[0]);
            return;
        }

        OpenMeetingDetailsForSelection();
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

    private void OpenSelectedFolderLibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedMeetings = GetSelectedMeetingRows();
        if (selectedMeetings.Length == 1)
        {
            OpenContainingFolder(GetPreferredMeetingFolderPath(selectedMeetings[0]));
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
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow row)
        {
            return;
        }

        await OpenMeetingRecommendationAsync(row);
    }

    private void RenameMeetingContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var targetMeetings = GetMeetingRowsForContextMenuAction(sender);
        if (targetMeetings.Length == 1)
        {
            OpenMeetingDetails(targetMeetings[0]);
            _meetingDetailWindow?.SetTitleDraft(
                targetMeetings[0].Title,
                "Edit the meeting title, then click Rename to apply it.");
        }
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
        var targetMeetings = GetMeetingRowsForContextMenuAction(sender);
        if (targetMeetings.Length == 1)
        {
            OpenMeetingDetails(targetMeetings[0]);
            _meetingDetailWindow?.SetMaintenanceStatus("Choose a split point, then click Split.");
        }
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

        MeetingsDataGrid.SelectedItem = selectedMeetings[0];
        ReviewMeetingCleanupSuggestionsButton_OnClick(this, new RoutedEventArgs());
        MeetingCleanupRecommendationsStatusTextBlock.Text =
            "Review the selected meetings' cleanup suggestions before applying any action.";
        await Task.CompletedTask;
    }

    private async void MergeSelectedMeetingsContextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
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

    private async Task OpenMeetingRecommendationAsync(MeetingListRow row)
    {
        if (IsMeetingActionInProgress() || !row.PrimaryRecommendation.HasPrimaryAction)
        {
            return;
        }

        switch (row.PrimaryRecommendation.ActionTarget)
        {
            case MeetingRecommendationActionTarget.SettingsSetup:
                OpenSettingsSurface(SettingsWindowSection.Setup);
                break;
            case MeetingRecommendationActionTarget.MeetingDetails:
                OpenMeetingDetails(row);
                break;
            case MeetingRecommendationActionTarget.CleanupReview:
                MeetingsDataGrid.SelectedItem = row;
                ReviewMeetingCleanupSuggestionsButton_OnClick(this, new RoutedEventArgs());
                break;
            case MeetingRecommendationActionTarget.CheckAgain:
                MeetingCleanupRecommendationsStatusTextBlock.Text = "Refreshing meeting status...";
                await RefreshMeetingListAsync();
                break;
        }
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
        MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
            UserActionIntent.ApplyCleanupRecommendations).ProgressText;
        try
        {
            await ExecuteMeetingCleanupRecommendationsAsync(recommendations, archiveLabel, _lifetimeCts.Token);
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting cleanup action failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ApplyCleanupRecommendations,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting cleanup action did not finish.");
        }
        finally
        {
            _isApplyingMeetingCleanupRecommendations = false;
            UpdateMeetingActionState();
        }
    }

    private async Task<bool> ArchiveMeetingsAsync(IReadOnlyList<MeetingListRow> meetings, string archiveLabel)
    {
        if (meetings.Count == 0 || IsMeetingActionInProgress())
        {
            return false;
        }

        _isArchivingMeetings = true;
        UpdateMeetingActionState();
        var archiveCopy = UserActionCopyResolver.Resolve(UserActionIntent.ArchiveMeetings);
        MeetingCleanupRecommendationsStatusTextBlock.Text = archiveCopy.ProgressText;
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

            MeetingCleanupRecommendationsStatusTextBlock.Text = archiveCopy.SuccessText;
            await RefreshMeetingListAsync();
            return true;
        }
        catch (Exception exception)
        {
            _logger.Log($"Meeting archive failed: {exception}");
            MeetingCleanupRecommendationsStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.ArchiveMeetings,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Meeting archive did not finish.");
            return false;
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
            var requestedCount = 0;
            foreach (var meeting in meetings)
            {
                var speakerState = ResolveSpeakerExperienceForMeeting(meeting);
                if (speakerState.Permits(SpeakerExperienceAction.AddSpeakerLabels))
                {
                    await QueueSpeakerLabelGenerationAsync(meeting.Source, _lifetimeCts.Token);
                    requestedCount++;
                }
                else if (speakerState.Permits(SpeakerExperienceAction.RepairSpeakerLabels))
                {
                    await QueueSpeakerLabelRepairAsync(meeting.Source, _lifetimeCts.Token);
                    requestedCount++;
                }
            }

            if (requestedCount == 0)
            {
                SpeakerNamesStatusTextBlock.Text = ResolveSpeakerExperienceForMeeting(meetings[0]).Explanation;
                return;
            }

            AppendActivity($"Requested Diarization Label processing from {activityLabel}. Queue acceptance is not completion.");
            await RefreshMeetingListAsync();
        }
        catch (Exception exception)
        {
            SpeakerNamesStatusTextBlock.Text = "Unable to request Diarization Label processing. Check local speaker-labeling setup and retry.";
            _logger.Log($"Speaker-labeling request from {activityLabel} failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.AddSpeakerLabels,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
        finally
        {
            _isApplyingSpeakerNames = false;
            UpdateMeetingActionState();
        }
    }

    private bool CanQueueSpeakerLabelsForMeeting(MeetingListRow row)
    {
        var state = ResolveSpeakerExperienceForMeeting(row);
        return state.Permits(SpeakerExperienceAction.AddSpeakerLabels) ||
               state.Permits(SpeakerExperienceAction.RepairSpeakerLabels);
    }

    private SpeakerExperienceStateResult ResolveSpeakerExperienceForMeeting(MeetingListRow row)
    {
        var diarizationReady = _currentDiarizationAssetStatus?.IsReady
            ?? _diarizationAssetCatalogService.InspectInstalledAssets(_liveConfig.Current.DiarizationAssetPath).IsReady;
        return SpeakerExperienceResolver.Resolve(new SpeakerExperienceInput(
            SpeakerExperienceSurface.Meeting,
            HasMeetingManifest: !string.IsNullOrWhiteSpace(row.Source.ManifestPath) && File.Exists(row.Source.ManifestPath),
            HasTranscript: row.CanOpenTranscriptArtifact,
            HasDiarizationLabels: row.Source.HasSpeakerLabels,
            IsLabelingQueued: row.Source.ManifestState == SessionState.Queued,
            IsLabelingRunning: row.Source.ManifestState is SessionState.Processing or SessionState.Finalizing,
            HasSuspiciousLabels: row.Source.HasSuspiciousSpeakerLabels,
            IsRepairEligible: diarizationReady && row.CanRegenerateTranscript,
            HasVoiceSamples: false,
            IsLocalProfileStoreAvailable: true,
            ActiveVoiceProfileCount: 0,
            LearningMode: _liveConfig.Current.SpeakerNameLearningMode,
            NameSuggestionCount: 0,
            HasProfileAttribution: false,
            RequiresRefresh: false));
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
            _logger.Log($"Copy artifact path failed: {exception}");
            AppendActivity(UserActionCopyResolver.Resolve(
                UserActionIntent.CopyArtifactPath,
                UserActionBlockedReasonKind.OperationFailed).BlockedText);
        }
    }

    private static string GetPreferredMeetingFolderPath(MeetingListRow row)
    {
        return row.PrimaryTranscriptPath
            ?? row.Source.AudioPath
            ?? row.Source.ManifestPath
            ?? string.Empty;
    }

    private async void RushBacklogButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRushingBacklog || _latestProcessingQueueStatusSnapshot.TotalRemainingCount == 0 && BuildPersistedProcessingBacklogState() is null)
        {
            return;
        }

        var choice = PromptBacklogRushChoice();
        if (choice is null)
        {
            MeetingWorkspaceStatusTextBlock.Text = "Rush backlog canceled.";
            return;
        }

        _isRushingBacklog = true;
        UpdateProcessingQueueStatusUi();
        UpdateMeetingActionState();

        try
        {
            var result = await _processingQueue.RushBacklogAsync(
                choice == BacklogRushChoice.ThisAndFutureMeetings,
                _lifetimeCts.Token);
            var futureText = result.FutureMeetingsDeferred
                ? " Future meetings will also publish transcripts before speaker labels."
                : string.Empty;
            var interruptedText = result.InterruptedCurrentDiarization
                ? " Current speaker labeling was interrupted and requeued for transcript-first publishing."
                : string.Empty;
            var status = result.DeferredMeetingCount == 0
                ? "No queued backlog items needed speaker labeling deferral."
                : $"Rush Backlog deferred speaker labeling for {result.DeferredMeetingCount} backlog item(s).";

            MeetingWorkspaceStatusTextBlock.Text = $"{status}{futureText}{interruptedText}";
            AppendActivity(MeetingWorkspaceStatusTextBlock.Text);
            RequestMeetingRefreshForCurrentContext(MeetingRefreshMode.Fast, "rush backlog");
        }
        catch (Exception exception)
        {
            _logger.Log($"Rush backlog update failed: {exception}");
            MeetingWorkspaceStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateRushProcessing,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
            AppendActivity("Rush backlog update did not finish.");
        }
        finally
        {
            _isRushingBacklog = false;
            UpdateProcessingQueueStatusUi();
            UpdateMeetingActionState();
        }
    }

    private BacklogRushChoice? PromptBacklogRushChoice()
    {
        var selectionWindow = new Window
        {
            Title = "Rush Backlog",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 520,
            Background = Brushes.White,
        };

        BacklogRushChoice? result = null;
        var thisBacklogButton = new Button
        {
            Content = "This backlog — defer labels",
            Width = 200,
            Height = 34,
            IsDefault = true,
        };
        thisBacklogButton.Click += (_, _) =>
        {
            result = BacklogRushChoice.ThisBacklogOnly;
            selectionWindow.DialogResult = true;
            selectionWindow.Close();
        };

        var futureButton = new Button
        {
            Content = "This + future — defer labels",
            Width = 220,
            Height = 34,
            Margin = new Thickness(12, 0, 0, 0),
        };
        futureButton.Click += (_, _) =>
        {
            result = BacklogRushChoice.ThisAndFutureMeetings;
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
                        Text = "Rush current backlog",
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Margin = new Thickness(0, 10, 0, 0),
                        Text = "Rush Backlog publishes transcripts sooner by deferring speaker labels for the selected scope. It never interrupts live recording or transcription. When no recording is active, only the app-owned speaker-labeling stage may be stopped; safe transcript output is reused and labels can run later.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Margin = new Thickness(0, 16, 0, 0),
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            thisBacklogButton,
                            futureButton,
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
                SelectedMeetingStatusTextBlock.Text = $"Cleared future ASAP priority for '{meeting.Title}'. Current work was not canceled.";
                AppendActivity($"Cleared future ASAP priority for '{meeting.Title}' without canceling current work.");
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
            var lifecycleText = _processingQueue.GetStatusSnapshot().RushRequest?.LifecycleText ?? "ASAP: transcript";
            SelectedMeetingStatusTextBlock.Text = $"Marked '{meeting.Title}' — {lifecycleText} ({behaviorLabel}).";
            AppendActivity($"Marked '{meeting.Title}' — {lifecycleText} ({behaviorLabel}).");
        }
        catch (Exception exception)
        {
            _logger.Log($"ASAP processing update failed: {exception}");
            SelectedMeetingStatusTextBlock.Text = UserActionCopyResolver.Resolve(
                UserActionIntent.UpdateRushProcessing,
                UserActionBlockedReasonKind.OperationFailed).BlockedText;
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
            Content = "Run this meeting next",
            Width = 170,
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
            Content = "Run next during recording",
            Width = 210,
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
                        Text = "Move this meeting ahead of ordinary queued work through transcript publication and any eligible speaker-labeling pass. Clear ASAP releases only this meeting's future priority; it does not cancel current work. The recording option bypasses the Responsive pause for this one run; it never stops or interrupts active capture. Existing transcription is never preempted; when no recording is active, only an app-owned speaker-labeling pass may be safely restarted.",
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

    private sealed record SelectionOption<TValue>(TValue Value, string Label, bool IsEnabled = true);

    private sealed record SpeakerNameReviewRow(
        string? SpeakerId,
        string OriginalLabel,
        string EditedLabel,
        string? ProfileId,
        SpeakerNameSource ExpectedNameSource,
        bool RejectSuggestion,
        string ArtifactRevision,
        string? ExpectedSuggestedDisplayName);

    private sealed class MeetingListRow
    {
        public MeetingListRow(
            MeetingOutputRecord source,
            IReadOnlyList<MeetingCleanupRecommendation> recommendations,
            MeetingPrimaryRecommendation primaryRecommendation)
        {
            Source = source;
            Recommendations = recommendations;
            PrimaryRecommendation = primaryRecommendation;
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
            PrimaryCleanupRecommendation = MainWindowInteractionLogic.GetPrimaryMeetingCleanupRecommendation(recommendations);
            RecommendationCount = recommendations.Count;
            Recommended = primaryRecommendation.IsDismissed
                ? "Recommendation dismissed"
                : primaryRecommendation.HasPrimaryAction
                    ? primaryRecommendation.Reason
                    : primaryRecommendation.Label;
            RecommendedActionLabel = !primaryRecommendation.HasPrimaryAction
                ? string.Empty
                : primaryRecommendation.Label;
            RecommendedActionToolTip = !primaryRecommendation.HasPrimaryAction
                ? string.Empty
                : string.IsNullOrWhiteSpace(primaryRecommendation.BlockReason)
                    ? primaryRecommendation.Reason
                    : $"{primaryRecommendation.Reason} {primaryRecommendation.BlockReason}";
            CanApplyRecommendedAction = primaryRecommendation.HasPrimaryAction;
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

        public void SetAsapStatus(string? lifecycleText)
        {
            if (!string.IsNullOrWhiteSpace(lifecycleText))
            {
                Recommended = lifecycleText;
            }
        }

        public MeetingPrimaryRecommendation PrimaryRecommendation { get; }

        public MeetingCleanupRecommendation? PrimaryCleanupRecommendation { get; }

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

    private sealed record CleanupSchedulerDispatchRequest(
        IReadOnlyList<MeetingCleanupRecommendation> Recommendations,
        IReadOnlyList<MeetingOutputRecord> Records,
        int RefreshVersion,
        CancellationToken CancellationToken,
        AutomationCatalogSnapshot AutomationSnapshot);

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
                MeetingCleanupAction.RepairSpeakerLabels => "Repair Speaker Labels",
                MeetingCleanupAction.GenerateSummary => "Generate AI Summary",
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

    private sealed class ExternalAudioImportReviewRow
    {
        private ExternalAudioImportCandidate _source;
        private string _editableTitle;
        private string _startedAtInputText;
        private DateTimeOffset _startedAtLocal;
        private string? _validationMessage;
        private string? _queueErrorMessage;
        private string? _setupBlockedMessage;

        public ExternalAudioImportReviewRow(ExternalAudioImportCandidate source)
        {
            _source = source;
            _editableTitle = source.Title;
            _startedAtLocal = source.StartedAtUtc.ToLocalTime();
            _startedAtInputText = _startedAtLocal.ToString("g", CultureInfo.CurrentCulture);
            ProjectName = string.Empty;
            RecomputeValidation();
        }

        public ExternalAudioImportCandidate Source => _source;

        public string SourcePath => Source.SourcePath;

        public string SourceDisplayName => ReviewProjection.SourceDisplayName;

        public ExternalAudioImportMethod ImportMethod => Source.ImportMethod;

        public long SourceSizeBytes => Source.SourceSizeBytes;

        public DateTimeOffset SourceLastWriteUtc => Source.SourceLastWriteUtc;

        public TimeSpan? ProbedDuration => Source.Preflight.Duration;

        public ExternalAudioImportPreflightResult Preflight => Source.Preflight;

        public string SourceIdentityKey => ReviewProjection.Revision;

        public string ImportMethodLabel => ReviewProjection.SourceMethodLabel;

        public string EditableTitle => _editableTitle;

        public string StartedAtInputText => _startedAtInputText;

        public string StartedAtDisplayText => _startedAtInputText;

        public string ProjectName { get; private set; }

        public bool IsSetupBlocked { get; private set; }

        public string DurationDisplayText => ProbedDuration is null
            ? "Unknown"
            : FormatDuration(ProbedDuration.Value);

        public string StatusText => ReviewProjection.StatusText;

        public string RetentionText => ReviewProjection.RetentionText;

        public string RecoveryText => ReviewProjection.RecoveryText;

        public string AccessibleName =>
            $"{SourceDisplayName}. {ImportMethodLabel}. {StatusText}. {RetentionText} {RecoveryText}";

        public bool CanRetry => ReviewProjection.CanRetry;

        public bool CanSkipDuplicate => ReviewProjection.CanSkipDuplicate;

        public ExternalAudioImportReviewRowProjection ReviewProjection =>
            ExternalAudioImportReviewProjection.CreateRow(
                Source,
                IsSetupBlocked,
                !string.IsNullOrWhiteSpace(_validationMessage),
                !string.IsNullOrWhiteSpace(_queueErrorMessage));

        public string DetailStatusText
        {
            get
            {
                var projection = ReviewProjection;
                return $"{projection.StatusText} {projection.RecoveryText}";
            }
        }

        public bool CanQueue => ReviewProjection.CanQueue;

        public bool CanStageForSetup => Preflight.IsSuccess &&
                                        string.IsNullOrWhiteSpace(_validationMessage) &&
                                        string.IsNullOrWhiteSpace(_queueErrorMessage);

        public void UpdateTitle(string title)
        {
            _editableTitle = title.Trim();
            RecomputeValidation();
        }

        public void UpdateStartedAtInput(string text)
        {
            _startedAtInputText = text;
            RecomputeValidation();
        }

        public void UpdateProjectName(string projectName)
        {
            ProjectName = projectName.Trim();
        }

        public void SetSetupBlocked(bool isSetupBlocked, string? setupBlockedMessage)
        {
            IsSetupBlocked = isSetupBlocked;
            _setupBlockedMessage = isSetupBlocked ? setupBlockedMessage : null;
        }

        public void SetQueueError(string? message)
        {
            _queueErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown queue failure." : message.Trim();
        }

        public void ClearQueueError()
        {
            _queueErrorMessage = null;
        }

        public void RefreshCandidate(ExternalAudioImportCandidate source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _queueErrorMessage = null;
            RecomputeValidation();
        }

        public bool TryBuildRequest(out ExternalAudioImportRequest request, out string validationMessage)
        {
            if (!TryGetStartedAtUtc(out var startedAtUtc, out validationMessage))
            {
                request = default!;
                return false;
            }

            if (string.IsNullOrWhiteSpace(_editableTitle))
            {
                validationMessage = "Enter a meeting title before queueing this import.";
                request = default!;
                return false;
            }

            request = new ExternalAudioImportRequest(
                SourcePath,
                SourceDisplayName,
                SourceSizeBytes,
                SourceLastWriteUtc,
                ImportMethod,
                _editableTitle,
                startedAtUtc,
                string.IsNullOrWhiteSpace(ProjectName) ? null : ProjectName,
                ProbedDuration,
                SourceRetained: true);
            validationMessage = string.Empty;
            return true;
        }

        private void RecomputeValidation()
        {
            if (string.IsNullOrWhiteSpace(_editableTitle))
            {
                _validationMessage = "Enter a meeting title before queueing this import.";
                return;
            }

            if (!TryParseStartedAtLocal(_startedAtInputText, out _startedAtLocal))
            {
                _validationMessage = "Enter a valid local start date and time before queueing this import.";
                return;
            }

            _validationMessage = null;
        }

        private bool TryGetStartedAtUtc(out DateTimeOffset startedAtUtc, out string validationMessage)
        {
            if (!TryParseStartedAtLocal(_startedAtInputText, out var localStartedAt))
            {
                startedAtUtc = default;
                validationMessage = "Enter a valid local start date and time before queueing this import.";
                return false;
            }

            startedAtUtc = localStartedAt.ToUniversalTime();
            validationMessage = string.Empty;
            return true;
        }

        private static bool TryParseStartedAtLocal(string text, out DateTimeOffset startedAtLocal)
        {
            if (DateTime.TryParse(
                    text,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                    out var localDateTime))
            {
                startedAtLocal = new DateTimeOffset(DateTime.SpecifyKind(localDateTime, DateTimeKind.Local));
                return true;
            }

            startedAtLocal = default;
            return false;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1d
                ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
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
            (Source.FileSizeBytes.HasValue ? $" | {FormatBytes(Source.FileSizeBytes.Value)}" : string.Empty);
    }

    private sealed class VoiceProfileSettingsRow
    {
        public VoiceProfileSettingsRow(VoiceProfile profile)
        {
            ProfileId = profile.ProfileId;
            DisplayName = profile.DisplayName;
            SampleCount = profile.SampleCount;
            Status = profile.Status.ToString();
            LastMatchedText = profile.LastMatchedAtUtc is { } lastMatched
                ? lastMatched.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "Never";
        }

        public string ProfileId { get; }

        public string DisplayName { get; }

        public int SampleCount { get; }

        public string Status { get; }

        public string LastMatchedText { get; }
    }

    private sealed class SpeakerLabelEditorRow
    {
        private readonly Action _onEditedLabelChanged;
        private string _editedLabel;

        public SpeakerLabelEditorRow(
            SpeakerLabelInfo label,
            string artifactRevision,
            Action onEditedLabelChanged)
        {
            OriginalLabel = label.DisplayName;
            Provenance = BuildSpeakerNameProvenanceText(label);
            SpeakerId = label.SpeakerId;
            ProfileId = label.ProfileId;
            ExpectedNameSource = label.NameSource;
            SuggestedDisplayName = label.SuggestedDisplayName;
            ArtifactRevision = artifactRevision;
            _editedLabel = label.DisplayName;
            _onEditedLabelChanged = onEditedLabelChanged;
        }

        public string OriginalLabel { get; }

        public string Provenance { get; }

        public string? SpeakerId { get; }

        public string? ProfileId { get; }

        public SpeakerNameSource ExpectedNameSource { get; }

        public string? SuggestedDisplayName { get; }

        public string ArtifactRevision { get; }

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
