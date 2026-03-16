using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MeetingRecorder.App;

public partial class MainWindow : Window
{
    private readonly LiveAppConfig _liveConfig;
    private readonly FileLogWriter _logger;
    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;
    private readonly MeetingOutputCatalogService _meetingOutputCatalogService;
    private readonly RecordingSessionCoordinator _recordingCoordinator;
    private readonly WindowMeetingDetector _meetingDetector;
    private readonly ProcessingQueueService _processingQueue;
    private readonly DispatcherTimer _detectionTimer;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private DateTimeOffset? _lastPositiveDetectionUtc;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private string? _lastDetectionFingerprint;
    private string? _lastAutoStopFingerprint;

    public MainWindow(LiveAppConfig liveConfig, FileLogWriter logger)
    {
        InitializeComponent();
        _liveConfig = liveConfig;
        _logger = logger;

        _pathBuilder = new ArtifactPathBuilder();
        _manifestStore = new SessionManifestStore(_pathBuilder);
        _meetingOutputCatalogService = new MeetingOutputCatalogService(_pathBuilder);
        _recordingCoordinator = new RecordingSessionCoordinator(liveConfig, _manifestStore, _pathBuilder, logger);
        _meetingDetector = new WindowMeetingDetector(
            liveConfig,
            new MeetingDetectionEvaluator(),
            new SystemAudioActivityProbe());
        _processingQueue = new ProcessingQueueService(liveConfig, _manifestStore, logger);
        _detectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _detectionTimer.Tick += DetectionTimer_OnTick;
        _liveConfig.Changed += LiveConfig_OnChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;

        ApplyConfigToUi(_liveConfig.Current, "Initial config loaded.");
        UpdateCurrentMeetingEditor();
        UpdateSelectedMeetingEditor(null);
        UpdateUi("Ready to record.", "No meeting detected.");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppendActivity("App started.");
            _detectionTimer.Start();
            RefreshMeetingList();
            await _processingQueue.ResumePendingSessionsAsync(_lifetimeCts.Token);
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
        _liveConfig.Changed -= LiveConfig_OnChanged;
        _lifetimeCts.Dispose();
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _recordingCoordinator.StartAsync(
                MeetingPlatform.Manual,
                $"Manual session {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
                Array.Empty<DetectionSignal>(),
                autoStarted: false);

            UpdateCurrentMeetingEditor();
            UpdateUi("Recording in progress.", "Manual recording started.");
            AppendActivity("Manual recording started.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to start recording: {exception.Message}");
            UpdateUi("Unable to start recording.", DetectionTextBlock.Text);
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StopCurrentRecordingAsync("Manual stop requested.");
    }

    private async void RenameCurrentButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var renamed = await _recordingCoordinator.RenameActiveSessionAsync(CurrentMeetingTitleTextBox.Text, _lifetimeCts.Token);
            if (!renamed)
            {
                AppendActivity("No active recording is available to rename.");
                return;
            }

            UpdateCurrentMeetingEditor();
            AppendActivity($"Renamed active recording to '{CurrentMeetingTitleTextBox.Text.Trim()}'.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to rename active recording: {exception.Message}");
        }
    }

    private async void DetectionTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            await TryReloadConfigAsync();
            var decision = _meetingDetector.DetectBestCandidate();
            LogDetectionChange(decision);
            DetectionTextBlock.Text = decision is null
                ? "No meeting detected."
                : $"Detected {decision.Platform} meeting '{decision.SessionTitle}' with confidence {decision.Confidence:P0}.";

            if (decision is { ShouldStart: true })
            {
                _lastPositiveDetectionUtc = DateTimeOffset.UtcNow;
                _lastAutoStopFingerprint = null;
                if (!_recordingCoordinator.IsRecording && _liveConfig.Current.AutoDetectEnabled)
                {
                    await _recordingCoordinator.StartAsync(
                        decision.Platform,
                        decision.SessionTitle,
                        decision.Signals,
                        autoStarted: true);
                    UpdateCurrentMeetingEditor();
                    UpdateUi("Recording in progress.", DetectionTextBlock.Text);
                    AppendActivity($"Auto-started recording for '{decision.SessionTitle}'.");
                }
            }
            else if (_recordingCoordinator.IsRecording &&
                     _recordingCoordinator.ActiveSession?.AutoStarted == true)
            {
                var elapsedSincePositive = _lastPositiveDetectionUtc.HasValue
                    ? DateTimeOffset.UtcNow - _lastPositiveDetectionUtc.Value
                    : (TimeSpan?)null;
                var lastPositiveUtc = _lastPositiveDetectionUtc;

                if (elapsedSincePositive.HasValue && lastPositiveUtc.HasValue)
                {
                    var remaining = TimeSpan.FromSeconds(_liveConfig.Current.MeetingStopTimeoutSeconds) - elapsedSincePositive.Value;
                    if (remaining <= TimeSpan.Zero)
                    {
                        AppendAutoStopStatus($"Auto-stop triggered after {_liveConfig.Current.MeetingStopTimeoutSeconds} seconds without a qualifying meeting signal.");
                        await StopCurrentRecordingAsync("Meeting signals expired after the configured timeout.");
                        _lastPositiveDetectionUtc = null;
                        _lastAutoStopFingerprint = null;
                    }
                    else
                    {
                        AppendAutoStopStatus(
                            $"Auto-stop countdown active: {Math.Ceiling(remaining.TotalSeconds)} seconds remaining. Last qualifying signal was at {lastPositiveUtc.Value:O}.");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppendActivity($"Detection error: {exception.Message}");
        }
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
        _ = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            AppendActivity("Application shutdown requested.");
            _detectionTimer.Stop();

            if (_recordingCoordinator.IsRecording)
            {
                await StopCurrentRecordingAsync("Application closing.", enqueueForProcessing: false, CancellationToken.None);
            }

            await _processingQueue.StopAsync(CancellationToken.None);
            _lifetimeCts.Cancel();
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
            var manifestPath = await _recordingCoordinator.StopAsync(reason, cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                if (enqueueForProcessing)
                {
                    await _processingQueue.EnqueueAsync(manifestPath, cancellationToken);
                    AppendActivity($"Queued session for processing: {manifestPath}");
                }
                else
                {
                    AppendActivity($"Deferred processing until next launch: {manifestPath}");
                }
            }

            RefreshMeetingList();
            UpdateCurrentMeetingEditor();
            UpdateUi("Ready to record.", "No meeting detected.");
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

    private void RefreshMeetingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshMeetingList();
        AppendActivity("Refreshed published meeting list.");
    }

    private async void RenameSelectedMeetingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MeetingsDataGrid.SelectedItem is not MeetingListRow selectedMeeting)
        {
            AppendActivity("Select a published meeting before renaming it.");
            return;
        }

        try
        {
            var renamed = await _meetingOutputCatalogService.RenameMeetingAsync(
                _liveConfig.Current.AudioOutputDir,
                _liveConfig.Current.TranscriptOutputDir,
                selectedMeeting.Source.Stem,
                SelectedMeetingTitleTextBox.Text,
                _lifetimeCts.Token);

            RefreshMeetingList(renamed.Stem);
            AppendActivity($"Renamed published meeting '{selectedMeeting.Title}' to '{renamed.Title}'.");
        }
        catch (Exception exception)
        {
            AppendActivity($"Failed to rename published meeting: {exception.Message}");
        }
    }

    private void MeetingsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMeetingEditor(MeetingsDataGrid.SelectedItem as MeetingListRow);
    }

    private async void SaveConfigButton_OnClick(object sender, RoutedEventArgs e)
    {
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
                AutoDetectEnabled = ConfigAutoDetectCheckBox.IsChecked == true,
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

    private void UpdateUi(string status, string detection)
    {
        StatusTextBlock.Text = status;
        DetectionTextBlock.Text = detection;
        StartButton.IsEnabled = !_recordingCoordinator.IsRecording;
        StopButton.IsEnabled = _recordingCoordinator.IsRecording;
        RenameCurrentButton.IsEnabled = _recordingCoordinator.IsRecording;
        CurrentMeetingTitleTextBox.IsEnabled = _recordingCoordinator.IsRecording;
    }

    private void UpdateCurrentMeetingEditor()
    {
        CurrentMeetingTitleTextBox.Text = _recordingCoordinator.ActiveSession?.Manifest.DetectedTitle ?? string.Empty;
        RenameCurrentButton.IsEnabled = _recordingCoordinator.IsRecording;
        CurrentMeetingTitleTextBox.IsEnabled = _recordingCoordinator.IsRecording;
    }

    private void RefreshMeetingList(string? selectedStem = null)
    {
        var rows = _meetingOutputCatalogService
            .ListMeetings(_liveConfig.Current.AudioOutputDir, _liveConfig.Current.TranscriptOutputDir)
            .Select(record => new MeetingListRow(record))
            .ToArray();

        MeetingsDataGrid.ItemsSource = rows;

        MeetingListRow? selectedRow = null;
        if (!string.IsNullOrWhiteSpace(selectedStem))
        {
            selectedRow = rows.SingleOrDefault(row => string.Equals(row.Source.Stem, selectedStem, StringComparison.OrdinalIgnoreCase));
        }

        MeetingsDataGrid.SelectedItem = selectedRow;
        UpdateSelectedMeetingEditor(selectedRow);
    }

    private void UpdateSelectedMeetingEditor(MeetingListRow? row)
    {
        SelectedMeetingTitleTextBox.Text = row?.Title ?? string.Empty;
        RenameSelectedMeetingButton.IsEnabled = row is not null;
        SelectedMeetingTitleTextBox.IsEnabled = row is not null;
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
        ConfigAutoDetectCheckBox.IsChecked = config.AutoDetectEnabled;
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
            ApplyConfigToUi(e.CurrentConfig, $"Config {e.Source.ToString().ToLowerInvariant()} applied without restart.");
            RefreshMeetingList();
        });
    }

    private void ApplyConfigToUi(AppConfig config, string? statusMessage)
    {
        AudioPathRun.Text = config.AudioOutputDir;
        TranscriptPathRun.Text = config.TranscriptOutputDir;
        ConfigPathRun.Text = _liveConfig.ConfigPath;
        AutoDetectTextBlock.Text = BuildRuntimeSummary(config);
        LoadConfigEditorValues(config);

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            AppendActivity(statusMessage);
        }
    }

    private static string BuildRuntimeSummary(AppConfig config)
    {
        return $"Auto-detect: {(config.AutoDetectEnabled ? "enabled" : "disabled")} | Mic capture: {(config.MicCaptureEnabled ? "enabled" : "disabled")} | Audio threshold: {config.AutoDetectAudioPeakThreshold:0.000}";
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
            : $"{decision.Platform}|{decision.ShouldStart}|{decision.SessionTitle}|{decision.Reason}";

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
            $"Detection candidate: platform={decision.Platform}; title='{decision.SessionTitle}'; confidence={decision.Confidence:P0}; shouldStart={decision.ShouldStart}; reason='{decision.Reason}'; signals={signals}");
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

    private sealed class MeetingListRow
    {
        public MeetingListRow(MeetingOutputRecord source)
        {
            Source = source;
        }

        public MeetingOutputRecord Source { get; }

        public string Title => Source.Title;

        public string StartedAtUtc => Source.StartedAtUtc == DateTimeOffset.MinValue
            ? "Unknown"
            : Source.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public string Platform => Source.Platform.ToString();

        public string AudioFileName => Path.GetFileName(Source.AudioPath) ?? "Missing";

        public string TranscriptFileName => Path.GetFileName(Source.MarkdownPath ?? Source.JsonPath) ?? "Missing";
    }
}
