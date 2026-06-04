using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.App.Services;

internal sealed class ProcessingQueueService
{
    private static readonly TimeSpan RecoverablePendingSessionStalenessThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultPublishTailEstimate = TimeSpan.FromSeconds(45);
    private const long MinimumRecoverableWaveChunkBytes = 44;
    private const double DefaultTranscriptionSecondsPerAudioSecond = 0.55d;
    private const double DefaultDiarizationSecondsPerAudioSecond = 0.35d;
    private readonly LiveAppConfig _config;
    private readonly SessionManifestStore _manifestStore;
    private readonly IMeetingMetadataEnricher _meetingMetadataEnricher;
    private readonly FileLogWriter _logger;
    private readonly Func<WorkerLaunch> _workerLaunchResolver;
    private readonly IWorkerProcessFactory _workerProcessFactory;
    private readonly Func<bool> _isRecordingProvider;
    private readonly ProcessingTempCleanupService _tempCleanupService;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _pendingManifestSignal = new(0);
    private readonly object _processSyncRoot = new();
    private readonly Task _drainTask;
    private IWorkerProcess? _currentWorker;
    private string? _currentManifestPath;
    private bool _isBackgroundWorkPausedForRecording;
    private readonly List<QueuedManifestStatusEntry> _queuedManifestEntries = [];
    private readonly Dictionary<string, StageTimingAverage> _stageTimingAverages = new(StringComparer.OrdinalIgnoreCase);
    private ActiveQueueItemState? _currentItemState;
    private ProcessingQueueStatusSnapshot _statusSnapshot;
    private CancellationTokenSource? _currentManifestMonitorCts;
    private Task? _currentManifestMonitorTask;
    private string? _preemptedManifestPath;

    internal event Action<ProcessingQueueStatusSnapshot>? StatusChanged;

    public ProcessingQueueService(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        FileLogWriter logger,
        IMeetingMetadataEnricher? meetingMetadataEnricher = null,
        Func<WorkerLaunch>? workerLaunchResolver = null,
        IWorkerProcessFactory? workerProcessFactory = null,
        Func<bool>? isRecordingProvider = null,
        ProcessingTempCleanupService? tempCleanupService = null)
    {
        _config = config;
        _manifestStore = manifestStore;
        _meetingMetadataEnricher = meetingMetadataEnricher ?? new PassthroughMeetingMetadataEnricher();
        _logger = logger;
        _workerLaunchResolver = workerLaunchResolver ?? WorkerLocator.Resolve;
        _workerProcessFactory = workerProcessFactory ?? new SystemWorkerProcessFactory();
        _isRecordingProvider = isRecordingProvider ?? (() => false);
        _tempCleanupService = tempCleanupService ?? new ProcessingTempCleanupService(
            AppDataPaths.GetAppRoot(),
            Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization"),
            Path.Combine(Path.GetTempPath(), "MeetingRecorderTranscription"),
            logger);
        _statusSnapshot = new ProcessingQueueStatusSnapshot(
            ProcessingQueueRunState.Idle,
            ProcessingQueuePauseReason.None,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
        _drainTask = Task.Run(() => DrainQueueAsync(_shutdownCts.Token));
    }

    public bool IsProcessingInProgress
    {
        get
        {
            lock (_processSyncRoot)
            {
                return _currentWorker is { HasExited: false };
            }
        }
    }

    public async Task ResumePendingSessionsAsync(CancellationToken cancellationToken = default)
    {
        await NormalizeRushProcessingRequestAsync(cancellationToken);
        await _tempCleanupService.RunStartupCleanupAsync(cancellationToken);
        var publishedWorkCleanupResult = await PublishedSessionWorkCleanupService.PrunePublishedSessionsAsync(
            _manifestStore,
            _config.Current.WorkDir,
            _config.Current.AudioOutputDir,
            cancellationToken);
        if (publishedWorkCleanupResult.SessionsPruned > 0)
        {
            _logger.Log(
                $"Pruned published work artifacts from {publishedWorkCleanupResult.SessionsPruned} published session(s), reclaiming {publishedWorkCleanupResult.BytesReclaimed} bytes.");
        }

        var interruptedRecordingRecoveryCount = await RecoverInterruptedRecordingSessionsAsync(_config.Current.WorkDir, cancellationToken);
        if (interruptedRecordingRecoveryCount > 0)
        {
            _logger.Log($"Recovered {interruptedRecordingRecoveryCount} interrupted recording session(s) from preserved raw chunks.");
        }

        var deferredRepairCount = await ApplyDeferredSpeakerLabelingBacklogOverridesAsync(_config.Current.WorkDir, cancellationToken);
        if (deferredRepairCount > 0)
        {
            _logger.Log($"Deferred speaker labeling for {deferredRepairCount} queued or interrupted sessions so publish can drain without blocking on background diarization.");
        }

        var repairedCount = await RepairRecoverablePendingSessionsAsync(_config.Current.WorkDir, cancellationToken);
        if (repairedCount > 0)
        {
            _logger.Log($"Recovered {repairedCount} queued or interrupted sessions by retrying them without optional speaker labeling.");
        }

        var archivedSupersededImportedCount = await ArchiveSupersededImportedSourceWorkAsync(_config.Current.WorkDir, cancellationToken);
        if (archivedSupersededImportedCount > 0)
        {
            _logger.Log($"Archived {archivedSupersededImportedCount} superseded imported-source reprocessing session(s) because published transcript artifacts already exist.");
        }

        var pending = (await _manifestStore.FindPendingManifestPathsAsync(_config.Current.WorkDir, cancellationToken)).ToList();
        if (_config.Current.RushProcessingRequest is { } rushRequest)
        {
            var rushIndex = pending.FindIndex(path => string.Equals(path, rushRequest.ManifestPath, StringComparison.Ordinal));
            if (rushIndex > 0)
            {
                var rushPath = pending[rushIndex];
                pending.RemoveAt(rushIndex);
                pending.Insert(0, rushPath);
            }
        }

        var excludedSupersededImportedCount = 0;
        foreach (var manifestPath in pending)
        {
            if (await ShouldExcludeSupersededImportedManifestAsync(manifestPath, cancellationToken))
            {
                excludedSupersededImportedCount++;
                continue;
            }

            await EnqueueAsync(manifestPath, cancellationToken);
        }

        if (excludedSupersededImportedCount > 0)
        {
            _logger.Log($"Excluded {excludedSupersededImportedCount} superseded imported-source manifest(s) from the active processing queue because published transcript artifacts already exist.");
        }
    }

    public ProcessingQueueStatusSnapshot GetStatusSnapshot()
    {
        lock (_processSyncRoot)
        {
            return _statusSnapshot;
        }
    }

    public async Task EnqueueAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_shutdownCts.IsCancellationRequested)
        {
            _logger.Log($"Skipping processing enqueue for '{manifestPath}' because shutdown is in progress.");
            return;
        }

        var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken);
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        var shouldSignal = false;
        lock (_processSyncRoot)
        {
            shouldSignal = UpsertQueuedEntryLocked(queueEntry, preferFront: false, markPreempted: false);
            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        if (shouldSignal)
        {
            _pendingManifestSignal.Release();
        }

        PublishStatusSnapshot(snapshotToPublish);
    }

    public async Task RequestRushProcessingAsync(
        string manifestPath,
        RushProcessingBehavior behavior,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        if (!IsRushEligible(manifest))
        {
            throw new InvalidOperationException("Only queued or in-progress meetings can be marked for ASAP processing.");
        }

        var queueEntry = CreateQueueEntry(manifest, manifestPath);
        var request = new RushProcessingRequest(manifestPath, behavior, DateTimeOffset.UtcNow);
        await _config.SaveAsync(_config.Current with { RushProcessingRequest = request }, cancellationToken);

        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        IWorkerProcess? workerToKill = null;
        var shouldSignal = false;
        lock (_processSyncRoot)
        {
            shouldSignal = UpsertQueuedEntryLocked(queueEntry, preferFront: true, markPreempted: false);
            if (_currentWorker is { HasExited: false } currentWorker &&
                !string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal))
            {
                _preemptedManifestPath = _currentManifestPath;
                workerToKill = currentWorker;
            }

            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        if (shouldSignal)
        {
            _pendingManifestSignal.Release();
        }

        PublishStatusSnapshot(snapshotToPublish);

        if (workerToKill is not null)
        {
            _logger.Log($"Preempting '{_preemptedManifestPath}' so ASAP processing can start for '{manifestPath}'.");
            KillWorkerProcess(workerToKill);
        }
    }

    public async Task ClearRushProcessingAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentRequest = _config.Current.RushProcessingRequest;
        if (currentRequest is null ||
            !string.Equals(currentRequest.ManifestPath, manifestPath, StringComparison.Ordinal))
        {
            return;
        }

        await _config.SaveAsync(_config.Current with { RushProcessingRequest = null }, cancellationToken);
        PublishStatusSnapshot(UpdateStatusSnapshot());
    }

    public async Task<BacklogRushResult> RushBacklogAsync(
        bool deferFutureMeetings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (deferFutureMeetings && _config.Current.BackgroundSpeakerLabelingMode != BackgroundSpeakerLabelingMode.Deferred)
        {
            await _config.SaveAsync(
                _config.Current with
                {
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                    SpeakerLabelingSecurityPromptMigrationApplied = true,
                },
                cancellationToken);
        }

        List<string> queuedManifestPaths;
        string? currentDiarizationManifestPath = null;
        IWorkerProcess? workerToKill = null;
        lock (_processSyncRoot)
        {
            queuedManifestPaths = _queuedManifestEntries
                .Select(entry => entry.ManifestPath)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (_currentWorker is { HasExited: false } currentWorker &&
                _currentItemState is { } currentItem &&
                currentItem.Summary.TranscriptionStatus.State == StageExecutionState.Succeeded &&
                currentItem.Summary.DiarizationStatus.State == StageExecutionState.Running)
            {
                currentDiarizationManifestPath = currentItem.Summary.ManifestPath;
                _preemptedManifestPath = currentDiarizationManifestPath;
                workerToKill = currentWorker;
            }
        }

        var deferredCount = 0;
        foreach (var manifestPath in queuedManifestPaths)
        {
            if (await TryApplyBacklogRushSpeakerLabelingOverrideAsync(
                    manifestPath,
                    resetSessionStateToQueued: false,
                    cancellationToken))
            {
                deferredCount++;
                var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken);
                lock (_processSyncRoot)
                {
                    ReplaceQueuedEntryLocked(queueEntry);
                }
            }
        }

        var interruptedCurrentDiarization = false;
        if (!string.IsNullOrWhiteSpace(currentDiarizationManifestPath) &&
            await TryApplyBacklogRushSpeakerLabelingOverrideAsync(
                currentDiarizationManifestPath,
                resetSessionStateToQueued: true,
                cancellationToken))
        {
            deferredCount++;
            interruptedCurrentDiarization = true;
        }

        PublishStatusSnapshot(UpdateStatusSnapshot());

        if (workerToKill is not null && interruptedCurrentDiarization)
        {
            _logger.Log($"Interrupting diarization for '{currentDiarizationManifestPath}' so the rushed backlog can publish transcripts without speaker labels.");
            KillWorkerProcess(workerToKill);
        }

        return new BacklogRushResult(
            deferredCount,
            _config.Current.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred && deferFutureMeetings,
            interruptedCurrentDiarization);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _shutdownCts.Cancel();
        _pendingManifestSignal.Release();

        IWorkerProcess? worker;
        string? manifestPath;
        lock (_processSyncRoot)
        {
            worker = _currentWorker;
            manifestPath = _currentManifestPath;
        }

        if (worker is not null)
        {
            try
            {
                if (!worker.HasExited)
                {
                    _logger.Log($"Stopping worker for '{manifestPath}' because the application is shutting down.");
                    KillWorkerProcess(worker);
                }

                await worker.WaitForExitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // The worker may already have exited.
            }
        }

        try
        {
            await _drainTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (_shutdownCts.IsCancellationRequested)
        {
            _logger.Log($"Ignoring queued processing failure during shutdown: {exception.Message}");
        }
    }

    private async Task DrainQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _pendingManifestSignal.WaitAsync(cancellationToken);
                while (TryDequeueNextManifestPath(out var manifestPath))
                {
                    try
                    {
                        await ProcessManifestAsync(manifestPath, cancellationToken);
                    }
                    catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.Log($"Queued processing failed unexpectedly for '{manifestPath}': {exception}");
                        await MarkManifestFailedAfterQueueExceptionAsync(manifestPath, exception, cancellationToken);
                        RemoveQueuedEntryAfterFailure(manifestPath);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var selectedManifestPath = await WaitForBackgroundProcessingPermitAsync(manifestPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedManifestPath))
        {
            return;
        }

        manifestPath = selectedManifestPath;
        await ApplyDeferredSpeakerLabelingIfConfiguredAsync(manifestPath, cancellationToken);
        await TryEnrichManifestAsync(manifestPath, cancellationToken);
        var workerResult = await RunWorkerAsync(manifestPath, AppDataPaths.GetConfigPath(), cancellationToken);
        if (await TryHandlePreemptedManifestAsync(manifestPath, cancellationToken))
        {
            return;
        }

        if (workerResult.ExitCode == 0)
        {
            await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
            return;
        }

        if (await TryRecoverFromDiarizationWorkerCrashAsync(manifestPath, workerResult, cancellationToken))
        {
            await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
            return;
        }

        LogWorkerFailure(manifestPath, workerResult);
        await MarkManifestFailedAfterWorkerFailureAsync(manifestPath, workerResult, cancellationToken);
        await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
    }

    private async Task TryEnrichManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!_config.Current.MeetingAttendeeEnrichmentEnabled)
        {
            return;
        }

        try
        {
            var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            await _meetingMetadataEnricher.TryEnrichAsync(manifest, manifestPath, cancellationToken);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Log($"Attendee enrichment failed for '{manifestPath}': {exception.Message}");
        }
    }

    private async Task<WorkerRunResult> RunWorkerAsync(
        string manifestPath,
        string configPath,
        CancellationToken cancellationToken,
        AppConfig? launchConfigOverride = null)
    {
        await _tempCleanupService.RunRecurringCleanupAsync(cancellationToken);
        var launch = _workerLaunchResolver();
        var arguments = $"{launch.ArgumentPrefix} --manifest \"{manifestPath}\" --config \"{configPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var currentConfig = launchConfigOverride ?? _config.Current;
        var launchSnapshot = new WorkerLaunchConfigSnapshot(
            currentConfig.DiarizationAccelerationPreference,
            currentConfig.BackgroundSpeakerLabelingMode,
            currentConfig.DiarizationAssetPath);
        var priority = BackgroundProcessingPolicy.GetWorkerPriority(currentConfig);
        var transcriptionThreads = BackgroundProcessingPolicy.GetTranscriptionThreadCount(currentConfig, Environment.ProcessorCount);
        var diarizationThreads = BackgroundProcessingPolicy.GetDiarizationThreadCount(currentConfig, Environment.ProcessorCount);
        _logger.Log(
            $"Launching worker for '{manifestPath}'. FileName='{startInfo.FileName}'. Arguments='{startInfo.Arguments}'. " +
            $"Mode={currentConfig.BackgroundProcessingMode}. SpeakerLabelingMode={currentConfig.BackgroundSpeakerLabelingMode}. " +
            $"DiarizationAcceleration={currentConfig.DiarizationAccelerationPreference}. " +
            $"Priority={priority}. TranscriptionThreads={transcriptionThreads}. DiarizationThreads={diarizationThreads}.");
        var process = _workerProcessFactory.Start(startInfo);
        SetCurrentWorker(process, manifestPath);

        try
        {
            TryApplyWorkerPriority(process, priority, manifestPath);
            await MarkManifestProcessingStartedAsync(manifestPath, cancellationToken);

            var standardOutputTask = process.ReadStandardOutputToEndAsync(cancellationToken);
            var standardErrorTask = process.ReadStandardErrorToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);

            var result = new WorkerRunResult(
                process.ExitCode,
                standardOutputTask.Result,
                standardErrorTask.Result,
                launchSnapshot);
            if (result.ExitCode == 0)
            {
                _logger.Log($"Worker completed for '{manifestPath}': {result.StandardOutput.Trim()}");
            }

            return result;
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                KillWorkerProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            _logger.Log($"Worker canceled during shutdown for '{manifestPath}'.");
            throw;
        }
        finally
        {
            await StopCurrentManifestMonitorAsync();
            ClearCurrentWorker(process);
            process.Dispose();
        }
    }

    private async Task<bool> TryRecoverFromDiarizationWorkerCrashAsync(
        string manifestPath,
        WorkerRunResult workerResult,
        CancellationToken cancellationToken)
    {
        if (!IsDiarizationCrash(workerResult.StandardError))
        {
            return false;
        }

        if (workerResult.LaunchConfig.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto)
        {
            return await TryRecoverDirectMlDiarizationCrashWithCpuAsync(
                manifestPath,
                cancellationToken);
        }

        return await TryRecoverDiarizationCrashWithoutSpeakerLabelingAsync(
            manifestPath,
            "Recovered from an earlier worker crash by retrying without optional speaker labeling.",
            cancellationToken);
    }

    private async Task<bool> TryRecoverDirectMlDiarizationCrashWithCpuAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var recoveryConfigPath = string.Empty;
        try
        {
            var cpuOnlyConfig = await SaveCpuOnlyDiarizationPreferenceAfterDirectMlCrashAsync(cancellationToken);
            var recoveryConfig = await CreateCpuOnlyDiarizationConfigAsync(manifestPath, cpuOnlyConfig, cancellationToken);
            recoveryConfigPath = recoveryConfig.ConfigPath;
            _logger.Log($"Retrying '{manifestPath}' once with CPU speaker labeling because the worker crashed during DirectML diarization.");

            var retryResult = await RunWorkerAsync(
                manifestPath,
                recoveryConfig.ConfigPath,
                cancellationToken,
                recoveryConfig.Config);
            if (retryResult.ExitCode == 0)
            {
                _logger.Log($"Recovered '{manifestPath}' by retrying speaker labeling on CPU after the DirectML worker crash.");
                return true;
            }

            if (IsDiarizationCrash(retryResult.StandardError))
            {
                _logger.Log($"CPU speaker-labeling retry also crashed for '{manifestPath}'. Retrying once without optional speaker labeling.");
                return await TryRecoverDiarizationCrashWithoutSpeakerLabelingAsync(
                    manifestPath,
                    "Recovered from repeated speaker-labeling crashes by retrying without optional speaker labeling.",
                    cancellationToken);
            }

            LogWorkerFailure(manifestPath, retryResult);
            return false;
        }
        finally
        {
            TryDeleteRecoveryConfig(recoveryConfigPath);
        }
    }

    private async Task<AppConfig> SaveCpuOnlyDiarizationPreferenceAfterDirectMlCrashAsync(CancellationToken cancellationToken)
    {
        if (_config.Current.DiarizationAccelerationPreference == InferenceAccelerationPreference.CpuOnly)
        {
            return _config.Current;
        }

        var savedConfig = await _config.SaveAsync(
            _config.Current with
            {
                DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
                DiarizationAccelerationSecurityPromptMigrationApplied = true,
            },
            cancellationToken);
        _logger.Log(
            "Set future speaker-labeling GPU acceleration to CPU-only after a DirectML worker crash while preserving the speaker-labeling run mode.");
        PublishStatusSnapshot(UpdateStatusSnapshot());
        return savedConfig;
    }

    private async Task<bool> TryRecoverDiarizationCrashWithoutSpeakerLabelingAsync(
        string manifestPath,
        string reason,
        CancellationToken cancellationToken)
    {
        var recoveryConfigPath = string.Empty;
        try
        {
            await ApplySkipSpeakerLabelingOverrideAsync(
                manifestPath,
                reason,
                resetSessionStateToQueued: false,
                cancellationToken);
            await DeferFutureSpeakerLabelingAfterWorkerCrashAsync(cancellationToken);
            var recoveryConfig = await CreateDiarizationDisabledConfigAsync(manifestPath, cancellationToken);
            recoveryConfigPath = recoveryConfig.ConfigPath;
            _logger.Log($"Retrying '{manifestPath}' once without speaker labeling because the worker crashed during optional diarization.");
            var retryResult = await RunWorkerAsync(
                manifestPath,
                recoveryConfig.ConfigPath,
                cancellationToken,
                recoveryConfig.Config);
            if (retryResult.ExitCode == 0)
            {
                _logger.Log($"Recovered '{manifestPath}' by retrying without speaker labeling after the initial worker crash.");
                return true;
            }

            LogWorkerFailure(manifestPath, retryResult);
            return false;
        }
        finally
        {
            TryDeleteRecoveryConfig(recoveryConfigPath);
        }
    }

    private async Task DeferFutureSpeakerLabelingAfterWorkerCrashAsync(CancellationToken cancellationToken)
    {
        if (_config.Current.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred)
        {
            return;
        }

        await _config.SaveAsync(
            _config.Current with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                SpeakerLabelingSecurityPromptMigrationApplied = true,
            },
            cancellationToken);
        _logger.Log("Set future speaker labeling to Deferred after a worker crash in optional diarization to avoid repeated endpoint-protection prompts.");
        PublishStatusSnapshot(UpdateStatusSnapshot());
    }

    private async Task<int> RecoverInterruptedRecordingSessionsAsync(
        string workDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workDir) || _isRecordingProvider())
        {
            return 0;
        }

        var recoveredCount = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var (manifestPath, manifest) in await LoadReadableManifestsAsync(workDir, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recoveredChunkManifest = RecoverCaptureChunksFromRawDirectory(manifestPath, manifest);

            if (!TryResolveInterruptedRecordingEndTime(recoveredChunkManifest, now, out var endedAtUtc))
            {
                continue;
            }

            var closedManifest = CloseOpenCaptureSegments(recoveredChunkManifest, endedAtUtc);
            var updatedManifest = closedManifest with
            {
                State = SessionState.Queued,
                EndedAtUtc = endedAtUtc,
                TranscriptionStatus = new ProcessingStageStatus(
                    "transcription",
                    StageExecutionState.Queued,
                    now,
                    "Recovered from an interrupted recording after the app restarted."),
                DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
                ErrorSummary = null,
            };
            await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
            recoveredCount++;
        }

        return recoveredCount;
    }

    private async Task<int> RepairRecoverablePendingSessionsAsync(
        string workDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workDir))
        {
            return 0;
        }

        var repairedCount = 0;
        foreach (var (manifestPath, manifest) in await LoadReadableManifestsAsync(workDir, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ShouldRepairRecoverableDiarizationCrash(manifest, manifestPath))
            {
                continue;
            }

            await ApplySkipSpeakerLabelingOverrideAsync(
                manifestPath,
                "Recovered from an earlier speaker-labeling crash and requeued without optional speaker labeling.",
                resetSessionStateToQueued: true,
                cancellationToken);
            repairedCount++;
        }

        return repairedCount;
    }

    private async Task<int> ArchiveSupersededImportedSourceWorkAsync(
        string workDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workDir))
        {
            return 0;
        }

        var archivedCount = 0;
        var manifestPaths = Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories).ToArray();
        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MeetingSessionManifest manifest;
            try
            {
                manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (!IsSupersededImportedReprocessManifest(manifest))
            {
                continue;
            }

            var sessionRoot = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(sessionRoot) || !Directory.Exists(sessionRoot))
            {
                continue;
            }

            var archivePath = BuildArchivedSessionRootPath(manifest, sessionRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? throw new InvalidOperationException("Archive path must include a parent directory."));
            Directory.Move(sessionRoot, archivePath);
            archivedCount++;
            _logger.Log($"Archived superseded imported-source work '{manifestPath}' to '{archivePath}'.");
        }

        return archivedCount;
    }

    private async Task<int> ApplyDeferredSpeakerLabelingBacklogOverridesAsync(
        string workDir,
        CancellationToken cancellationToken)
    {
        if (!BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(_config.Current) || !Directory.Exists(workDir))
        {
            return 0;
        }

        var repairedCount = 0;
        foreach (var (manifestPath, manifest) in await LoadReadableManifestsAsync(workDir, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldApplyDeferredSpeakerLabelingOverride(manifest))
            {
                continue;
            }

            await ApplySkipSpeakerLabelingOverrideAsync(
                manifestPath,
                "Speaker labeling deferred by the responsive background processing mode.",
                resetSessionStateToQueued: manifest.State is SessionState.Processing or SessionState.Finalizing,
                cancellationToken);
            repairedCount++;
        }

        return repairedCount;
    }

    private async Task<IReadOnlyList<(string Path, MeetingSessionManifest Manifest)>> LoadReadableManifestsAsync(
        string workDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workDir))
        {
            return Array.Empty<(string, MeetingSessionManifest)>();
        }

        var manifests = new List<(string Path, MeetingSessionManifest Manifest)>();
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
                manifests.Add((manifestPath, manifest));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Log($"Skipped unreadable session manifest '{manifestPath}' during startup maintenance: {exception.Message}");
            }
        }

        return manifests;
    }

    private async Task<WorkerRecoveryConfig> CreateCpuOnlyDiarizationConfigAsync(
        string manifestPath,
        AppConfig cpuOnlyConfig,
        CancellationToken cancellationToken)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath) ?? _config.Current.WorkDir;
        var recoveryRoot = Path.Combine(sessionRoot, "processing", "recovery");
        Directory.CreateDirectory(recoveryRoot);

        var configPath = Path.Combine(recoveryRoot, $"appsettings-cpu-diarization-{Guid.NewGuid():N}.json");
        var configStore = new AppConfigStore(configPath);
        var savedConfig = await configStore.SaveAsync(
            cpuOnlyConfig with
            {
                DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
                DiarizationAccelerationSecurityPromptMigrationApplied = true,
            },
            cancellationToken);
        return new WorkerRecoveryConfig(configPath, savedConfig);
    }

    private async Task<WorkerRecoveryConfig> CreateDiarizationDisabledConfigAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath) ?? _config.Current.WorkDir;
        var recoveryRoot = Path.Combine(sessionRoot, "processing", "recovery");
        Directory.CreateDirectory(recoveryRoot);

        var configPath = Path.Combine(recoveryRoot, $"appsettings-no-diarization-{Guid.NewGuid():N}.json");
        var disabledDiarizationRoot = Path.Combine(recoveryRoot, $"diarization-disabled-{Guid.NewGuid():N}");
        var configStore = new AppConfigStore(configPath);
        var savedConfig = await configStore.SaveAsync(
            _config.Current with
            {
                DiarizationAssetPath = disabledDiarizationRoot,
                DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
            },
            cancellationToken);
        return new WorkerRecoveryConfig(configPath, savedConfig);
    }

    private async Task ApplySkipSpeakerLabelingOverrideAsync(
        string manifestPath,
        string reason,
        bool resetSessionStateToQueued,
        CancellationToken cancellationToken)
    {
        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        var existingOverrides = manifest.ProcessingOverrides ?? new MeetingProcessingOverrides(null, null);
        if (existingOverrides.SkipSpeakerLabeling && !resetSessionStateToQueued)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var updatedManifest = manifest with
        {
            ProcessingOverrides = existingOverrides with
            {
                SkipSpeakerLabeling = true,
            },
            ErrorSummary = null,
        };

        if (resetSessionStateToQueued)
        {
            updatedManifest = updatedManifest with
            {
                State = SessionState.Queued,
                DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Queued, now, reason),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
            };
        }

        await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
    }

    private static MeetingSessionManifest RecoverCaptureChunksFromRawDirectory(
        string manifestPath,
        MeetingSessionManifest manifest)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot))
        {
            return manifest;
        }

        var rawDir = Path.Combine(sessionRoot, "raw");
        if (!Directory.Exists(rawDir))
        {
            return manifest;
        }

        var loopbackGroups = DiscoverCapturedChunkGroups(rawDir, "loopback");
        var microphoneGroups = DiscoverCapturedChunkGroups(rawDir, "microphone");
        if (loopbackGroups.Count == 0 && microphoneGroups.Count == 0)
        {
            return manifest;
        }

        var loopbackCaptureSegments = loopbackGroups.Count > 0
            ? RebuildLoopbackCaptureSegments(manifest, loopbackGroups)
            : manifest.LoopbackCaptureSegments;
        var microphoneCaptureSegments = microphoneGroups.Count > 0
            ? RebuildMicrophoneCaptureSegments(manifest, microphoneGroups)
            : manifest.MicrophoneCaptureSegments;

        return manifest with
        {
            LoopbackCaptureSegments = loopbackCaptureSegments,
            RawChunkPaths = loopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            MicrophoneCaptureSegments = microphoneCaptureSegments,
            MicrophoneChunkPaths = microphoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
        };
    }

    private static bool TryResolveInterruptedRecordingEndTime(
        MeetingSessionManifest manifest,
        DateTimeOffset now,
        out DateTimeOffset endedAtUtc)
    {
        endedAtUtc = default;
        if (manifest.State is not (SessionState.Recording or SessionState.Queued) ||
            (manifest.EndedAtUtc is not null &&
             !HasStaleRecoveredEndTime(manifest, out _)))
        {
            return false;
        }

        if (!TryGetLatestRecoverableChunkWriteTime(manifest, out var latestChunkWriteUtc))
        {
            return false;
        }

        if (latestChunkWriteUtc > now || latestChunkWriteUtc <= manifest.StartedAtUtc)
        {
            return false;
        }

        if (manifest.State == SessionState.Recording &&
            manifest.EndedAtUtc is null &&
            now - latestChunkWriteUtc < RecoverablePendingSessionStalenessThreshold)
        {
            return false;
        }

        endedAtUtc = latestChunkWriteUtc;
        return true;
    }

    private static bool HasStaleRecoveredEndTime(MeetingSessionManifest manifest, out DateTimeOffset latestChunkWriteUtc)
    {
        latestChunkWriteUtc = default;
        if (manifest.EndedAtUtc is not { } endedAtUtc)
        {
            return false;
        }

        return TryGetLatestRecoverableChunkWriteTime(manifest, out latestChunkWriteUtc) &&
               latestChunkWriteUtc > endedAtUtc.AddSeconds(1);
    }

    private static IReadOnlyList<LoopbackCaptureSegment> RebuildLoopbackCaptureSegments(
        MeetingSessionManifest manifest,
        IReadOnlyList<CapturedChunkGroup> discoveredGroups)
    {
        var existingSegments = manifest.LoopbackCaptureSegments
            .Select(segment => new
            {
                Prefix = TryGetCaptureChunkPrefix(segment.ChunkPaths.FirstOrDefault(), "loopback", out var prefix)
                    ? prefix
                    : null,
                Segment = segment,
            })
            .Where(candidate => candidate.Prefix is not null)
            .GroupBy(candidate => candidate.Prefix!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Segment, StringComparer.OrdinalIgnoreCase);

        return discoveredGroups
            .Select(group => existingSegments.TryGetValue(group.Prefix, out var existingSegment)
                ? existingSegment with
                {
                    ChunkPaths = group.ChunkPaths,
                }
                : new LoopbackCaptureSegment(
                    InferCaptureSegmentStartTime(group.ChunkPaths, manifest.StartedAtUtc),
                    null,
                    group.ChunkPaths,
                    string.Empty,
                    "Unknown endpoint",
                    "Unknown"))
            .ToArray();
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> RebuildMicrophoneCaptureSegments(
        MeetingSessionManifest manifest,
        IReadOnlyList<CapturedChunkGroup> discoveredGroups)
    {
        var existingSegments = manifest.MicrophoneCaptureSegments
            .Select(segment => new
            {
                Prefix = TryGetCaptureChunkPrefix(segment.ChunkPaths.FirstOrDefault(), "microphone", out var prefix)
                    ? prefix
                    : null,
                Segment = segment,
            })
            .Where(candidate => candidate.Prefix is not null)
            .GroupBy(candidate => candidate.Prefix!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Segment, StringComparer.OrdinalIgnoreCase);

        return discoveredGroups
            .Select(group => existingSegments.TryGetValue(group.Prefix, out var existingSegment)
                ? existingSegment with
                {
                    ChunkPaths = group.ChunkPaths,
                }
                : new MicrophoneCaptureSegment(
                    InferCaptureSegmentStartTime(group.ChunkPaths, manifest.StartedAtUtc),
                    null,
                    group.ChunkPaths))
            .ToArray();
    }

    private static IReadOnlyList<CapturedChunkGroup> DiscoverCapturedChunkGroups(
        string rawDir,
        string captureKind)
    {
        return Directory.EnumerateFiles(rawDir, $"{captureKind}-*-chunk-*.wav", SearchOption.TopDirectoryOnly)
            .Where(path => new FileInfo(path) is { Exists: true, Length: > MinimumRecoverableWaveChunkBytes })
            .Select(path => new
            {
                Path = path,
                HasPrefix = TryGetCaptureChunkPrefix(path, captureKind, out var prefix),
                Prefix = prefix,
            })
            .Where(candidate => candidate.HasPrefix)
            .GroupBy(candidate => candidate.Prefix, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CapturedChunkGroup(
                group.Key,
                group
                    .Select(candidate => candidate.Path)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static bool TryGetCaptureChunkPrefix(
        string? chunkPath,
        string captureKind,
        out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrWhiteSpace(chunkPath))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(chunkPath);
        var expectedStart = $"{captureKind}-";
        if (!fileName.StartsWith(expectedStart, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var chunkMarkerIndex = fileName.IndexOf("-chunk-", StringComparison.OrdinalIgnoreCase);
        if (chunkMarkerIndex <= expectedStart.Length)
        {
            return false;
        }

        prefix = fileName[..chunkMarkerIndex];
        return true;
    }

    private static DateTimeOffset InferCaptureSegmentStartTime(
        IReadOnlyList<string> chunkPaths,
        DateTimeOffset fallbackStartedAtUtc)
    {
        var firstChunkPath = chunkPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstChunkPath))
        {
            return fallbackStartedAtUtc;
        }

        try
        {
            var firstChunk = new FileInfo(firstChunkPath);
            if (!firstChunk.Exists || firstChunk.Length <= MinimumRecoverableWaveChunkBytes)
            {
                return fallbackStartedAtUtc;
            }

            using var reader = new AudioFileReader(firstChunkPath);
            var inferredStartedAtUtc = new DateTimeOffset(firstChunk.LastWriteTimeUtc) - reader.TotalTime;
            return inferredStartedAtUtc < fallbackStartedAtUtc ? fallbackStartedAtUtc : inferredStartedAtUtc;
        }
        catch
        {
            return fallbackStartedAtUtc;
        }
    }

    private static MeetingSessionManifest CloseOpenCaptureSegments(
        MeetingSessionManifest manifest,
        DateTimeOffset endedAtUtc)
    {
        var loopbackCaptureSegments = manifest.LoopbackCaptureSegments
            .Select(segment => segment.EndedAtUtc is null
                ? segment with
                {
                    EndedAtUtc = ResolveCaptureSegmentEndTime(
                        segment.StartedAtUtc,
                        segment.ChunkPaths,
                        endedAtUtc),
                }
                : segment)
            .ToArray();
        var microphoneCaptureSegments = manifest.MicrophoneCaptureSegments
            .Select(segment => segment.EndedAtUtc is null
                ? segment with
                {
                    EndedAtUtc = ResolveCaptureSegmentEndTime(
                        segment.StartedAtUtc,
                        segment.ChunkPaths,
                        endedAtUtc),
                }
                : segment)
            .ToArray();
        var captureTimeline = manifest.CaptureTimeline.ToList();
        if (captureTimeline.Count > 0 && captureTimeline[^1].Kind == CaptureTimelineEventKind.Stopped)
        {
            captureTimeline[^1] = captureTimeline[^1] with
            {
                OccurredAtUtc = endedAtUtc,
                Summary = "Recovered interrupted recording stop from preserved raw chunks.",
                Detail = "The app sealed an open capture manifest during startup recovery.",
            };
        }
        else
        {
            captureTimeline.Add(
                new CaptureTimelineEntry(
                    endedAtUtc,
                    CaptureTimelineEventKind.Stopped,
                    "Recovered interrupted recording stop from preserved raw chunks.",
                    "The app sealed an open capture manifest during startup recovery."));
        }

        return manifest with
        {
            LoopbackCaptureSegments = loopbackCaptureSegments,
            RawChunkPaths = loopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            MicrophoneCaptureSegments = microphoneCaptureSegments,
            MicrophoneChunkPaths = microphoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            CaptureTimeline = captureTimeline.ToArray(),
        };
    }

    private static DateTimeOffset ResolveCaptureSegmentEndTime(
        DateTimeOffset segmentStartedAtUtc,
        IReadOnlyList<string> chunkPaths,
        DateTimeOffset fallbackEndedAtUtc)
    {
        return TryGetLatestRecoverableChunkWriteTime(chunkPaths, out var latestChunkWriteUtc) &&
               latestChunkWriteUtc > segmentStartedAtUtc
            ? latestChunkWriteUtc
            : fallbackEndedAtUtc;
    }

    private static bool TryGetLatestRecoverableChunkWriteTime(
        MeetingSessionManifest manifest,
        out DateTimeOffset latestChunkWriteUtc)
    {
        return TryGetLatestRecoverableChunkWriteTime(
            EnumerateCapturedAudioChunkPaths(manifest),
            out latestChunkWriteUtc);
    }

    private static bool TryGetLatestRecoverableChunkWriteTime(
        IEnumerable<string> chunkPaths,
        out DateTimeOffset latestChunkWriteUtc)
    {
        latestChunkWriteUtc = default;
        var foundRecoverableChunk = false;
        foreach (var chunkPath in chunkPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var chunk = new FileInfo(chunkPath);
            if (!chunk.Exists || chunk.Length <= MinimumRecoverableWaveChunkBytes)
            {
                continue;
            }

            var chunkWriteUtc = new DateTimeOffset(chunk.LastWriteTimeUtc);
            if (!foundRecoverableChunk || chunkWriteUtc > latestChunkWriteUtc)
            {
                latestChunkWriteUtc = chunkWriteUtc;
                foundRecoverableChunk = true;
            }
        }

        return foundRecoverableChunk;
    }

    private static IEnumerable<string> EnumerateCapturedAudioChunkPaths(MeetingSessionManifest manifest)
    {
        return manifest.RawChunkPaths
            .Concat(manifest.MicrophoneChunkPaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> TryApplyBacklogRushSpeakerLabelingOverrideAsync(
        string manifestPath,
        bool resetSessionStateToQueued,
        CancellationToken cancellationToken)
    {
        MeetingSessionManifest manifest;
        try
        {
            manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            _logger.Log($"Unable to rush backlog item '{manifestPath}': {exception.Message}");
            return false;
        }

        if (!resetSessionStateToQueued && IsCurrentManifestPath(manifestPath))
        {
            return false;
        }

        if (!ShouldApplyDeferredSpeakerLabelingOverride(manifest))
        {
            return false;
        }

        await ApplySkipSpeakerLabelingOverrideAsync(
            manifestPath,
            "Speaker labeling deferred by Rush Backlog so transcripts can publish sooner.",
            resetSessionStateToQueued,
            cancellationToken);
        return true;
    }

    private async Task ApplyDeferredSpeakerLabelingIfConfiguredAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(_config.Current))
        {
            return;
        }

        await ApplySkipSpeakerLabelingOverrideAsync(
            manifestPath,
            "Speaker labeling deferred by the responsive background processing mode.",
            resetSessionStateToQueued: false,
            cancellationToken);
    }

    private void LogWorkerFailure(string manifestPath, WorkerRunResult workerResult)
    {
        var sessionLogPath = Path.Combine(
            Path.GetDirectoryName(manifestPath) ?? _config.Current.WorkDir,
            "logs",
            "processing.log");
        _logger.Log($"Worker failed for '{manifestPath}' with exit code {workerResult.ExitCode}: {workerResult.StandardError} See '{sessionLogPath}' for per-session diagnostics.");
    }

    private async Task MarkManifestFailedAfterWorkerFailureAsync(
        string manifestPath,
        WorkerRunResult workerResult,
        CancellationToken cancellationToken)
    {
        var failureSummary = string.IsNullOrWhiteSpace(workerResult.StandardError)
            ? $"Processing worker exited with code {workerResult.ExitCode} before it could finish the manifest."
            : workerResult.StandardError.Trim();
        await TryMarkManifestFailedAsync(manifestPath, failureSummary, cancellationToken);
    }

    private async Task MarkManifestFailedAfterQueueExceptionAsync(
        string manifestPath,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var failureSummary = string.IsNullOrWhiteSpace(exception.Message)
            ? "Queued processing failed before the worker could finish the manifest."
            : exception.Message;
        await TryMarkManifestFailedAsync(manifestPath, failureSummary, cancellationToken);
    }

    private async Task TryMarkManifestFailedAsync(
        string manifestPath,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        MeetingSessionManifest manifest;
        try
        {
            manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            _logger.Log($"Unable to mark failed manifest '{manifestPath}': {exception.Message}");
            return;
        }

        if (manifest.State is SessionState.Published or SessionState.Failed)
        {
            return;
        }

        var updatedManifest = BuildWorkerFailedManifest(manifest, failureSummary, DateTimeOffset.UtcNow);
        await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
        _logger.Log($"Marked manifest '{manifestPath}' failed after worker failure: {failureSummary}");
    }

    private static MeetingSessionManifest BuildWorkerFailedManifest(
        MeetingSessionManifest manifest,
        string failureSummary,
        DateTimeOffset nowUtc)
    {
        var transcriptionStatus = manifest.TranscriptionStatus;
        var diarizationStatus = manifest.DiarizationStatus;
        var publishStatus = manifest.PublishStatus;

        if (!IsSuccessfulOrSkipped(transcriptionStatus.State))
        {
            transcriptionStatus = new ProcessingStageStatus(
                transcriptionStatus.StageName,
                StageExecutionState.Failed,
                nowUtc,
                failureSummary);
            diarizationStatus = SkipIncompleteStage(diarizationStatus, nowUtc, "Skipped because source audio processing failed.");
            publishStatus = SkipIncompleteStage(publishStatus, nowUtc, "Skipped because source audio processing failed.");
        }
        else if (manifest.ProcessingOverrides?.SkipSpeakerLabeling != true &&
                 !IsSuccessfulOrSkipped(diarizationStatus.State))
        {
            diarizationStatus = new ProcessingStageStatus(
                diarizationStatus.StageName,
                StageExecutionState.Failed,
                nowUtc,
                failureSummary);
            publishStatus = SkipIncompleteStage(publishStatus, nowUtc, "Skipped because speaker labeling failed before publish.");
        }
        else if (publishStatus.State != StageExecutionState.Succeeded)
        {
            publishStatus = new ProcessingStageStatus(
                publishStatus.StageName,
                StageExecutionState.Failed,
                nowUtc,
                failureSummary);
        }

        return manifest with
        {
            State = SessionState.Failed,
            ErrorSummary = failureSummary,
            TranscriptionStatus = transcriptionStatus,
            DiarizationStatus = diarizationStatus,
            PublishStatus = publishStatus,
        };
    }

    private static bool IsSuccessfulOrSkipped(StageExecutionState state)
    {
        return state is StageExecutionState.Succeeded or StageExecutionState.Skipped;
    }

    private static ProcessingStageStatus SkipIncompleteStage(
        ProcessingStageStatus stageStatus,
        DateTimeOffset nowUtc,
        string message)
    {
        return IsSuccessfulOrSkipped(stageStatus.State)
            ? stageStatus
            : new ProcessingStageStatus(stageStatus.StageName, StageExecutionState.Skipped, nowUtc, message);
    }

    private static bool IsDiarizationCrash(string standardError)
    {
        return standardError.Contains("OfflineSpeakerDiarization", StringComparison.OrdinalIgnoreCase) ||
               standardError.Contains("LocalSpeakerDiarizationProvider", StringComparison.OrdinalIgnoreCase) ||
               standardError.Contains("ApplySpeakerLabelsAsync", StringComparison.OrdinalIgnoreCase) ||
               standardError.Contains("AccessViolationException", StringComparison.OrdinalIgnoreCase) ||
               standardError.Contains("speaker labeling", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRepairRecoverableDiarizationCrash(
        MeetingSessionManifest manifest,
        string manifestPath)
    {
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true)
        {
            return false;
        }

        if (manifest.State is not (SessionState.Queued or SessionState.Processing or SessionState.Finalizing or SessionState.Failed))
        {
            return false;
        }

        if (manifest.TranscriptionStatus.State != StageExecutionState.Succeeded)
        {
            return false;
        }

        if (manifest.DiarizationStatus.State is not (StageExecutionState.Running or StageExecutionState.Failed))
        {
            return false;
        }

        if (manifest.PublishStatus.State != StageExecutionState.NotStarted)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.MergedAudioPath) || !File.Exists(manifest.MergedAudioPath))
        {
            return false;
        }

        var sessionLogPath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, "logs", "processing.log");
        try
        {
            if (File.Exists(sessionLogPath))
            {
                var sessionLog = File.ReadAllText(sessionLogPath);
                if (IsDiarizationCrash(sessionLog))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return IsStaleRecoverablePendingSession(manifest);
    }

    private static bool IsStaleRecoverablePendingSession(MeetingSessionManifest manifest)
    {
        var latestStageUpdateUtc = manifest.DiarizationStatus.UpdatedAtUtc;
        if (manifest.TranscriptionStatus.UpdatedAtUtc > latestStageUpdateUtc)
        {
            latestStageUpdateUtc = manifest.TranscriptionStatus.UpdatedAtUtc;
        }

        if (manifest.PublishStatus.UpdatedAtUtc > latestStageUpdateUtc)
        {
            latestStageUpdateUtc = manifest.PublishStatus.UpdatedAtUtc;
        }

        return DateTimeOffset.UtcNow - latestStageUpdateUtc >= RecoverablePendingSessionStalenessThreshold;
    }

    private static bool ShouldApplyDeferredSpeakerLabelingOverride(MeetingSessionManifest manifest)
    {
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true)
        {
            return false;
        }

        if (manifest.PublishStatus.State == StageExecutionState.Succeeded || manifest.State == SessionState.Published)
        {
            return false;
        }

        return manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing;
    }

    private async Task<bool> ShouldExcludeSupersededImportedManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            return IsSupersededImportedReprocessManifest(manifest);
        }
        catch
        {
            return false;
        }
    }

    private bool IsSupersededImportedReprocessManifest(MeetingSessionManifest manifest)
    {
        return manifest.ImportedSourceAudio is not null &&
            manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing &&
            File.Exists(manifest.ImportedSourceAudio.OriginalPath) &&
            HasPublishedTranscriptArtifacts(manifest.ImportedSourceAudio.OriginalPath);
    }

    private bool HasPublishedTranscriptArtifacts(string sourceAudioPath)
    {
        var transcriptOutputDir = _config.Current.TranscriptOutputDir;
        if (string.IsNullOrWhiteSpace(transcriptOutputDir) || !Directory.Exists(transcriptOutputDir))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(sourceAudioPath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        var transcriptSidecarRoot = ArtifactPathBuilder.BuildTranscriptSidecarRoot(transcriptOutputDir);
        return File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.md")) ||
               File.Exists(Path.Combine(transcriptSidecarRoot, $"{stem}.json")) ||
               File.Exists(Path.Combine(transcriptSidecarRoot, $"{stem}.ready")) ||
               File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.json")) ||
               File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.ready"));
    }

    private string BuildArchivedSessionRootPath(MeetingSessionManifest manifest, string sessionRoot)
    {
        var archiveRoot = GetMaintenanceArchiveRoot();
        var sessionName = string.IsNullOrWhiteSpace(manifest.SessionId)
            ? Path.GetFileName(sessionRoot)
            : manifest.SessionId;
        var archivePath = Path.Combine(archiveRoot, sessionName);
        if (!Directory.Exists(archivePath) && !File.Exists(archivePath))
        {
            return archivePath;
        }

        return Path.Combine(archiveRoot, $"{sessionName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
    }

    private async Task NormalizeRushProcessingRequestAsync(CancellationToken cancellationToken)
    {
        var rushRequest = _config.Current.RushProcessingRequest;
        if (rushRequest is null)
        {
            return;
        }

        if (!File.Exists(rushRequest.ManifestPath))
        {
            await _config.SaveAsync(_config.Current with { RushProcessingRequest = null }, cancellationToken);
            return;
        }

        try
        {
            var manifest = await _manifestStore.LoadAsync(rushRequest.ManifestPath, cancellationToken);
            if (!IsRushEligible(manifest))
            {
                await _config.SaveAsync(_config.Current with { RushProcessingRequest = null }, cancellationToken);
            }
        }
        catch
        {
            await _config.SaveAsync(_config.Current with { RushProcessingRequest = null }, cancellationToken);
        }
    }

    private async Task ClearRushProcessingRequestIfCompletedAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var rushRequest = _config.Current.RushProcessingRequest;
        if (rushRequest is null ||
            !string.Equals(rushRequest.ManifestPath, manifestPath, StringComparison.Ordinal))
        {
            return;
        }

        await NormalizeRushProcessingRequestAsync(cancellationToken);
        PublishStatusSnapshot(UpdateStatusSnapshot());
    }

    private async Task<bool> TryHandlePreemptedManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!string.Equals(_preemptedManifestPath, manifestPath, StringComparison.Ordinal))
        {
            return false;
        }

        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        _preemptedManifestPath = null;
        if (!IsRushEligible(manifest))
        {
            return false;
        }

        var updatedManifest = await ResetManifestForRushPreemptionAsync(manifestPath, manifest, cancellationToken);
        var queueEntry = CreateQueueEntry(updatedManifest, manifestPath);
        ProcessingQueueStatusSnapshot? snapshotToPublish;
        lock (_processSyncRoot)
        {
            UpsertQueuedEntryLocked(queueEntry, preferFront: false, markPreempted: true);
            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        PublishStatusSnapshot(snapshotToPublish);
        return true;
    }

    private async Task<MeetingSessionManifest> ResetManifestForRushPreemptionAsync(
        string manifestPath,
        MeetingSessionManifest manifest,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var speakerLabelingSkipped = manifest.ProcessingOverrides?.SkipSpeakerLabeling == true;
        var transcriptionStatus = ResetRunningStage(manifest.TranscriptionStatus, now);
        var diarizationStatus = ResetRunningStage(manifest.DiarizationStatus, now);
        var publishStatus = ResetRunningStage(manifest.PublishStatus, now);
        if (transcriptionStatus.State == StageExecutionState.NotStarted)
        {
            transcriptionStatus = QueueInterruptedStage(transcriptionStatus, now);
        }
        else if (transcriptionStatus.State == StageExecutionState.Succeeded &&
                 diarizationStatus.State == StageExecutionState.NotStarted &&
                 publishStatus.State != StageExecutionState.Succeeded)
        {
            diarizationStatus = QueueInterruptedStage(diarizationStatus, now);
        }
        else if (transcriptionStatus.State == StageExecutionState.Succeeded &&
                 (speakerLabelingSkipped || diarizationStatus.State == StageExecutionState.Succeeded) &&
                 publishStatus.State == StageExecutionState.NotStarted)
        {
            publishStatus = QueueInterruptedStage(publishStatus, now);
        }

        var updatedManifest = manifest with
        {
            State = SessionState.Queued,
            ErrorSummary = null,
            TranscriptionStatus = transcriptionStatus,
            DiarizationStatus = diarizationStatus,
            PublishStatus = publishStatus,
        };

        await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
        return updatedManifest;
    }

    private async Task<MeetingSessionManifest> ResetManifestForRushPreemptionAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        return await ResetManifestForRushPreemptionAsync(manifestPath, manifest, cancellationToken);
    }

    private static ProcessingStageStatus ResetRunningStage(ProcessingStageStatus stageStatus, DateTimeOffset now)
    {
        return stageStatus.State == StageExecutionState.Running
            ? new ProcessingStageStatus(
                stageStatus.StageName,
                StageExecutionState.Queued,
                now,
                "Interrupted so an ASAP meeting could run first.")
            : stageStatus;
    }

    private static ProcessingStageStatus QueueInterruptedStage(ProcessingStageStatus stageStatus, DateTimeOffset now)
    {
        return new ProcessingStageStatus(
            stageStatus.StageName,
            StageExecutionState.Queued,
            now,
            "Interrupted so an ASAP meeting could run first.");
    }

    private static bool IsRushEligible(MeetingSessionManifest manifest)
    {
        return manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing;
    }

    private string GetMaintenanceArchiveRoot()
    {
        var configDirectory = Path.GetDirectoryName(_config.ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var appRoot = Path.GetDirectoryName(configDirectory)
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");
        return Path.Combine(appRoot, "maintenance", "archived-imported-source-work");
    }

    private async Task<QueuedManifestStatusEntry> LoadQueueEntryAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            return CreateQueueEntry(manifest, manifestPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            _logger.Log($"Unable to load manifest queue metadata for '{manifestPath}': {exception.Message}");
            return new QueuedManifestStatusEntry(
                manifestPath,
                Path.GetFileNameWithoutExtension(Path.GetDirectoryName(manifestPath) ?? manifestPath),
                null,
                null,
                true,
                new ProcessingStageStatus("transcription", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
                new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
                new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null));
        }
    }

    private static QueuedManifestStatusEntry CreateQueueEntry(MeetingSessionManifest manifest, string manifestPath)
    {
        var recordingDuration = ResolveRecordingDuration(manifest);
        var expectsSpeakerLabeling = !manifest.ProcessingOverrides?.SkipSpeakerLabeling ?? true;

        return new QueuedManifestStatusEntry(
            manifestPath,
            string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? "Untitled meeting" : manifest.DetectedTitle,
            manifest.Platform,
            recordingDuration,
            expectsSpeakerLabeling,
            manifest.TranscriptionStatus,
            manifest.DiarizationStatus,
            manifest.PublishStatus);
    }

    private static TimeSpan? ResolveRecordingDuration(MeetingSessionManifest manifest)
    {
        if (manifest.EndedAtUtc is { } endedAtUtc && endedAtUtc > manifest.StartedAtUtc)
        {
            return endedAtUtc - manifest.StartedAtUtc;
        }

        if (string.IsNullOrWhiteSpace(manifest.MergedAudioPath) || !File.Exists(manifest.MergedAudioPath))
        {
            return null;
        }

        try
        {
            using var reader = new AudioFileReader(manifest.MergedAudioPath);
            return reader.TotalTime > TimeSpan.Zero ? reader.TotalTime : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task MarkManifestProcessingStartedAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken);
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        lock (_processSyncRoot)
        {
            RemoveQueuedEntryLocked(manifestPath);
            _currentItemState = new ActiveQueueItemState(queueEntry, DateTimeOffset.UtcNow);
            InitializeRunningStageTrackingLocked(_currentItemState);
            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        PublishStatusSnapshot(snapshotToPublish);
    }

    private void RemoveQueuedEntryLocked(string manifestPath)
    {
        var index = _queuedManifestEntries.FindIndex(entry => string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal));
        if (index >= 0)
        {
            _queuedManifestEntries.RemoveAt(index);
        }
    }

    private void RemoveQueuedEntryAfterFailure(string manifestPath)
    {
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        lock (_processSyncRoot)
        {
            RemoveQueuedEntryLocked(manifestPath);
            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        PublishStatusSnapshot(snapshotToPublish);
    }

    private bool UpsertQueuedEntryLocked(
        QueuedManifestStatusEntry queueEntry,
        bool preferFront,
        bool markPreempted)
    {
        if (_currentItemState is not null &&
            string.Equals(_currentItemState.Summary.ManifestPath, queueEntry.ManifestPath, StringComparison.Ordinal))
        {
            return false;
        }

        var existingIndex = _queuedManifestEntries.FindIndex(entry =>
            string.Equals(entry.ManifestPath, queueEntry.ManifestPath, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = _queuedManifestEntries[existingIndex];
            _queuedManifestEntries.RemoveAt(existingIndex);
            queueEntry = queueEntry with { WasPreempted = existing.WasPreempted || markPreempted };
        }
        else if (markPreempted)
        {
            queueEntry = queueEntry with { WasPreempted = true };
        }

        if (preferFront)
        {
            _queuedManifestEntries.Insert(0, queueEntry);
        }
        else if (markPreempted && _config.Current.RushProcessingRequest is { } rushRequest)
        {
            var rushIndex = _queuedManifestEntries.FindIndex(entry =>
                string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal));
            var insertIndex = rushIndex >= 0 ? rushIndex + 1 : 0;
            _queuedManifestEntries.Insert(insertIndex, queueEntry);
        }
        else
        {
            _queuedManifestEntries.Add(queueEntry);
        }

        return true;
    }

    private void ReplaceQueuedEntryLocked(QueuedManifestStatusEntry queueEntry)
    {
        var existingIndex = _queuedManifestEntries.FindIndex(entry =>
            string.Equals(entry.ManifestPath, queueEntry.ManifestPath, StringComparison.Ordinal));
        if (existingIndex < 0)
        {
            return;
        }

        _queuedManifestEntries[existingIndex] = queueEntry with { WasPreempted = _queuedManifestEntries[existingIndex].WasPreempted };
    }

    private bool TryDequeueNextManifestPath(out string manifestPath)
    {
        lock (_processSyncRoot)
        {
            if (_queuedManifestEntries.Count == 0)
            {
                manifestPath = string.Empty;
                return false;
            }

            var rushRequest = _config.Current.RushProcessingRequest;
            var dequeueIndex = rushRequest is null
                ? 0
                : _queuedManifestEntries.FindIndex(entry =>
                    string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal));
            if (dequeueIndex < 0)
            {
                dequeueIndex = 0;
            }

            manifestPath = _queuedManifestEntries[dequeueIndex].ManifestPath;
            return true;
        }
    }

    private void InitializeRunningStageTrackingLocked(ActiveQueueItemState activeItem)
    {
        foreach (var stageStatus in activeItem.Summary.GetStageStatuses())
        {
            if (stageStatus.State == StageExecutionState.Running)
            {
                activeItem.RunningStageStartedAtUtc[stageStatus.StageName] = stageStatus.UpdatedAtUtc;
            }
        }
    }

    private void StartCurrentManifestMonitorLocked(string manifestPath)
    {
        _currentManifestMonitorCts?.Cancel();
        _currentManifestMonitorCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _currentManifestMonitorCts = cts;
        _currentManifestMonitorTask = Task.Run(() => MonitorCurrentManifestAsync(manifestPath, cts.Token));
    }

    private async Task MonitorCurrentManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken);
                ProcessingQueueStatusSnapshot? snapshotToPublish = null;
                lock (_processSyncRoot)
                {
                    if (_currentItemState is null ||
                        !string.Equals(_currentItemState.Summary.ManifestPath, manifestPath, StringComparison.Ordinal))
                    {
                        return;
                    }

                    ApplyObservedStageTransitionsLocked(_currentItemState, queueEntry);
                    if (_currentItemState.Summary != queueEntry)
                    {
                        _currentItemState = _currentItemState with { Summary = queueEntry };
                        snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
                    }
                }

                PublishStatusSnapshot(snapshotToPublish);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ApplyObservedStageTransitionsLocked(ActiveQueueItemState activeItem, QueuedManifestStatusEntry updatedEntry)
    {
        foreach (var (previousStatus, currentStatus) in EnumerateStageTransitions(activeItem.Summary, updatedEntry))
        {
            if (previousStatus.State != StageExecutionState.Running &&
                currentStatus.State == StageExecutionState.Running)
            {
                activeItem.RunningStageStartedAtUtc[currentStatus.StageName] = currentStatus.UpdatedAtUtc;
                continue;
            }

            if (previousStatus.State == StageExecutionState.Running &&
                currentStatus.State != StageExecutionState.Running &&
                activeItem.RunningStageStartedAtUtc.Remove(currentStatus.StageName, out var stageStartedAtUtc) &&
                currentStatus.State == StageExecutionState.Succeeded &&
                updatedEntry.RecordingDuration is { } recordingDuration &&
                recordingDuration > TimeSpan.Zero)
            {
                ObserveStageDurationLocked(currentStatus.StageName, currentStatus.UpdatedAtUtc - stageStartedAtUtc, recordingDuration);
            }
        }
    }

    private static IEnumerable<(ProcessingStageStatus Previous, ProcessingStageStatus Current)> EnumerateStageTransitions(
        QueuedManifestStatusEntry previous,
        QueuedManifestStatusEntry current)
    {
        yield return (previous.TranscriptionStatus, current.TranscriptionStatus);
        yield return (previous.DiarizationStatus, current.DiarizationStatus);
        yield return (previous.PublishStatus, current.PublishStatus);
    }

    private void ObserveStageDurationLocked(string stageName, TimeSpan observedDuration, TimeSpan recordingDuration)
    {
        if (observedDuration <= TimeSpan.Zero || recordingDuration <= TimeSpan.Zero)
        {
            return;
        }

        var observedRatio = observedDuration.TotalSeconds / recordingDuration.TotalSeconds;
        if (_stageTimingAverages.TryGetValue(stageName, out var existing))
        {
            var observationCount = existing.ObservationCount + 1;
            var averageRatio = ((existing.AverageSecondsPerAudioSecond * existing.ObservationCount) + observedRatio) / observationCount;
            var averageDuration = TimeSpan.FromSeconds(((existing.AverageDuration.TotalSeconds * existing.ObservationCount) + observedDuration.TotalSeconds) / observationCount);
            _stageTimingAverages[stageName] = new StageTimingAverage(observationCount, averageRatio, averageDuration);
            return;
        }

        _stageTimingAverages[stageName] = new StageTimingAverage(1, observedRatio, observedDuration);
    }

    private async Task StopCurrentManifestMonitorAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_processSyncRoot)
        {
            cts = _currentManifestMonitorCts;
            task = _currentManifestMonitorTask;
            _currentManifestMonitorCts = null;
            _currentManifestMonitorTask = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (task is not null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private ProcessingQueueStatusSnapshot? UpdateStatusSnapshot()
    {
        lock (_processSyncRoot)
        {
            return UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }
    }

    private ProcessingQueueStatusSnapshot? UpdateStatusSnapshotLocked(DateTimeOffset nowUtc)
    {
        var candidate = BuildStatusSnapshotLocked(nowUtc);
        if (AreEquivalentIgnoringLastUpdated(_statusSnapshot, candidate))
        {
            return null;
        }

        _statusSnapshot = candidate;
        return candidate;
    }

    private ProcessingQueueStatusSnapshot BuildStatusSnapshotLocked(DateTimeOffset nowUtc)
    {
        var queuedCount = _queuedManifestEntries.Count;
        var totalRemainingCount = queuedCount + (_currentItemState is null ? 0 : 1);
        var rushRequest = BuildRushedProcessingStateLocked();
        var runState = _currentItemState is not null
            ? ProcessingQueueRunState.Processing
            : _isBackgroundWorkPausedForRecording && queuedCount > 0
                ? ProcessingQueueRunState.Paused
                : queuedCount > 0
                    ? ProcessingQueueRunState.Queued
                    : ProcessingQueueRunState.Idle;
        var pauseReason = runState == ProcessingQueueRunState.Paused
            ? ProcessingQueuePauseReason.LiveRecordingResponsiveMode
            : ProcessingQueuePauseReason.None;
        var currentItemEstimatedRemaining = _currentItemState is null
            ? null
            : EstimateRemainingLocked(_currentItemState.Summary, nowUtc);
        var queuedRemaining = EstimateQueuedRemainingLocked(nowUtc);
        var overallEstimatedRemaining = currentItemEstimatedRemaining is { } currentRemaining && queuedRemaining is { } queuedRemainingEstimate
            ? currentRemaining + queuedRemainingEstimate
            : _currentItemState is null
                ? queuedRemaining
                : null;
        var currentStageStatus = _currentItemState is null
            ? null
            : GetCurrentStageStatus(_currentItemState.Summary);

        return new ProcessingQueueStatusSnapshot(
            runState,
            pauseReason,
            queuedCount,
            totalRemainingCount,
            _currentItemState?.Summary.ManifestPath,
            _currentItemState?.Summary.Title,
            _currentItemState?.Summary.Platform,
            currentStageStatus?.StageName,
            currentStageStatus?.State,
            currentStageStatus?.UpdatedAtUtc,
            _currentItemState?.ProcessingStartedAtUtc,
            currentItemEstimatedRemaining,
            overallEstimatedRemaining,
            nowUtc,
            rushRequest,
            IsRushPauseBypassActiveLocked(rushRequest),
            _queuedManifestEntries.Any(entry => entry.WasPreempted));
    }

    private TimeSpan? EstimateQueuedRemainingLocked(DateTimeOffset nowUtc)
    {
        TimeSpan total = TimeSpan.Zero;
        foreach (var queueEntry in _queuedManifestEntries)
        {
            var estimatedRemaining = EstimateRemainingLocked(queueEntry, nowUtc);
            if (estimatedRemaining is null)
            {
                return null;
            }

            total += estimatedRemaining.Value;
        }

        return total;
    }

    private TimeSpan? EstimateRemainingLocked(QueuedManifestStatusEntry queueEntry, DateTimeOffset nowUtc)
    {
        if (queueEntry.RecordingDuration is not { } recordingDuration || recordingDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var remaining = TimeSpan.Zero;
        foreach (var stageStatus in GetEstimatedStageSequence(queueEntry))
        {
            var stageEstimate = EstimateStageDurationLocked(stageStatus.StageName, recordingDuration);
            if (stageEstimate <= TimeSpan.Zero)
            {
                continue;
            }

            if (string.Equals(_currentItemState?.Summary.ManifestPath, queueEntry.ManifestPath, StringComparison.Ordinal) &&
                stageStatus.State == StageExecutionState.Running)
            {
                var stageElapsed = nowUtc - stageStatus.UpdatedAtUtc;
                var stageRemaining = stageEstimate - stageElapsed;
                if (stageRemaining > TimeSpan.Zero)
                {
                    remaining += stageRemaining;
                }

                continue;
            }

            remaining += stageStatus.State switch
            {
                StageExecutionState.Succeeded or StageExecutionState.Skipped => TimeSpan.Zero,
                StageExecutionState.Failed => TimeSpan.Zero,
                _ => stageEstimate,
            };
        }

        return remaining;
    }

    private IReadOnlyList<ProcessingStageStatus> GetEstimatedStageSequence(QueuedManifestStatusEntry queueEntry)
    {
        var stages = new List<ProcessingStageStatus>(3)
        {
            queueEntry.TranscriptionStatus,
        };

        if (queueEntry.ExpectsSpeakerLabeling)
        {
            stages.Add(queueEntry.DiarizationStatus);
        }

        stages.Add(queueEntry.PublishStatus);
        return stages;
    }

    private TimeSpan EstimateStageDurationLocked(string stageName, TimeSpan recordingDuration)
    {
        if (_stageTimingAverages.TryGetValue(stageName, out var observedAverage))
        {
            return stageName switch
            {
                "publish" => observedAverage.AverageDuration,
                _ => TimeSpan.FromSeconds(recordingDuration.TotalSeconds * observedAverage.AverageSecondsPerAudioSecond),
            };
        }

        return stageName switch
        {
            "transcription" => TimeSpan.FromSeconds(recordingDuration.TotalSeconds * DefaultTranscriptionSecondsPerAudioSecond),
            "diarization" => TimeSpan.FromSeconds(recordingDuration.TotalSeconds * DefaultDiarizationSecondsPerAudioSecond),
            "publish" => DefaultPublishTailEstimate,
            _ => TimeSpan.Zero,
        };
    }

    private static ProcessingStageStatus? GetCurrentStageStatus(QueuedManifestStatusEntry queueEntry)
    {
        if (queueEntry.TranscriptionStatus.State is StageExecutionState.Running or StageExecutionState.Queued or StageExecutionState.NotStarted)
        {
            return queueEntry.TranscriptionStatus;
        }

        if (queueEntry.ExpectsSpeakerLabeling &&
            queueEntry.DiarizationStatus.State is StageExecutionState.Running or StageExecutionState.Queued or StageExecutionState.NotStarted)
        {
            return queueEntry.DiarizationStatus;
        }

        if (queueEntry.PublishStatus.State is StageExecutionState.Running or StageExecutionState.Queued or StageExecutionState.NotStarted)
        {
            return queueEntry.PublishStatus;
        }

        return queueEntry.PublishStatus.State == StageExecutionState.Succeeded
            ? queueEntry.PublishStatus
            : null;
    }

    private bool IsCurrentManifestPath(string manifestPath)
    {
        lock (_processSyncRoot)
        {
            return string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal) ||
                   string.Equals(_currentItemState?.Summary.ManifestPath, manifestPath, StringComparison.Ordinal);
        }
    }

    private static bool AreEquivalentIgnoringLastUpdated(
        ProcessingQueueStatusSnapshot previous,
        ProcessingQueueStatusSnapshot current)
    {
        return previous with { LastUpdatedAtUtc = current.LastUpdatedAtUtc } == current;
    }

    private void PublishStatusSnapshot(ProcessingQueueStatusSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        try
        {
            StatusChanged?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            _logger.Log($"Processing queue status subscriber failed: {exception.Message}");
        }
    }

    private async Task<string?> WaitForBackgroundProcessingPermitAsync(string manifestPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manifestPath = GetNextManifestPathCandidate() ?? manifestPath;
            if (!ShouldPauseBackgroundProcessing(manifestPath))
            {
                break;
            }

            if (!_isBackgroundWorkPausedForRecording)
            {
                _isBackgroundWorkPausedForRecording = true;
                _logger.Log("Pausing new background processing because a live recording is active in responsive mode.");
                PublishStatusSnapshot(UpdateStatusSnapshot());
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (_isBackgroundWorkPausedForRecording)
        {
            _isBackgroundWorkPausedForRecording = false;
            _logger.Log("Resuming background processing because the live recording pause condition cleared.");
            PublishStatusSnapshot(UpdateStatusSnapshot());
        }

        return GetNextManifestPathCandidate() ?? manifestPath;
    }

    private bool ShouldPauseBackgroundProcessing(string manifestPath)
    {
        var shouldPause = BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(_config.Current, _isRecordingProvider());
        if (!shouldPause)
        {
            return false;
        }

        var rushRequest = _config.Current.RushProcessingRequest;
        return rushRequest is null ||
               rushRequest.Behavior != RushProcessingBehavior.RunNextIgnoreRecordingPause ||
               !string.Equals(rushRequest.ManifestPath, manifestPath, StringComparison.Ordinal);
    }

    private RushedProcessingQueueState? BuildRushedProcessingStateLocked()
    {
        var rushRequest = _config.Current.RushProcessingRequest;
        if (rushRequest is null)
        {
            return null;
        }

        var title = _currentItemState is not null &&
                    string.Equals(_currentItemState.Summary.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal)
            ? _currentItemState.Summary.Title
            : _queuedManifestEntries.FirstOrDefault(entry =>
                    string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal))?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(rushRequest.ManifestPath) ?? rushRequest.ManifestPath);
        }

        return new RushedProcessingQueueState(
            rushRequest.ManifestPath,
            title ?? "Queued meeting",
            rushRequest.Behavior,
            rushRequest.RequestedAtUtc);
    }

    private bool IsRushPauseBypassActiveLocked(RushedProcessingQueueState? rushRequest)
    {
        if (rushRequest is null ||
            rushRequest.Behavior != RushProcessingBehavior.RunNextIgnoreRecordingPause ||
            !_isRecordingProvider())
        {
            return false;
        }

        return string.Equals(_currentItemState?.Summary.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal);
    }

    private string? GetNextManifestPathCandidate()
    {
        lock (_processSyncRoot)
        {
            if (_queuedManifestEntries.Count == 0)
            {
                return null;
            }

            var rushRequest = _config.Current.RushProcessingRequest;
            if (rushRequest is not null)
            {
                var rushEntry = _queuedManifestEntries.FirstOrDefault(entry =>
                    string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal));
                if (rushEntry is not null)
                {
                    return rushEntry.ManifestPath;
                }
            }

            return _queuedManifestEntries[0].ManifestPath;
        }
    }

    private void TryApplyWorkerPriority(IWorkerProcess process, ProcessPriorityClass priority, string manifestPath)
    {
        try
        {
            process.SetPriority(priority);
        }
        catch (Exception exception)
        {
            _logger.Log($"Unable to set worker priority for '{manifestPath}' to {priority}: {exception.Message}");
        }
    }

    private static void KillWorkerProcess(IWorkerProcess process)
    {
        // Process-tree kills can inspect the worker's console host on managed Windows laptops.
        // Kill the app-owned worker directly and let Windows clean up its private conhost.exe.
        process.Kill(entireProcessTree: false);
    }

    private void TryDeleteRecoveryConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        try
        {
            var recoveryRoot = Path.GetDirectoryName(configPath);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            if (!string.IsNullOrWhiteSpace(recoveryRoot) && Directory.Exists(recoveryRoot))
            {
                Directory.Delete(recoveryRoot, recursive: true);
            }
        }
        catch (Exception exception)
        {
            _logger.Log($"Failed to clean up temporary no-diarization recovery config '{configPath}': {exception.Message}");
        }
    }

    private void SetCurrentWorker(IWorkerProcess process, string manifestPath)
    {
        lock (_processSyncRoot)
        {
            _currentWorker = process;
            _currentManifestPath = manifestPath;
            StartCurrentManifestMonitorLocked(manifestPath);
        }
    }

    private void ClearCurrentWorker(IWorkerProcess process)
    {
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        lock (_processSyncRoot)
        {
            if (ReferenceEquals(_currentWorker, process))
            {
                _currentWorker = null;
                _currentManifestPath = null;
                _currentItemState = null;
                snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
            }
        }

        PublishStatusSnapshot(snapshotToPublish);
    }
}

internal sealed record BacklogRushResult(
    int DeferredMeetingCount,
    bool FutureMeetingsDeferred,
    bool InterruptedCurrentDiarization);

internal sealed record WorkerLaunchConfigSnapshot(
    InferenceAccelerationPreference DiarizationAccelerationPreference,
    BackgroundSpeakerLabelingMode BackgroundSpeakerLabelingMode,
    string DiarizationAssetPath);

internal sealed record WorkerRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    WorkerLaunchConfigSnapshot LaunchConfig);

internal sealed record WorkerRecoveryConfig(string ConfigPath, AppConfig Config);

internal sealed record QueuedManifestStatusEntry(
    string ManifestPath,
    string Title,
    MeetingPlatform? Platform,
    TimeSpan? RecordingDuration,
    bool ExpectsSpeakerLabeling,
    ProcessingStageStatus TranscriptionStatus,
    ProcessingStageStatus DiarizationStatus,
    ProcessingStageStatus PublishStatus,
    bool WasPreempted = false)
{
    public IEnumerable<ProcessingStageStatus> GetStageStatuses()
    {
        yield return TranscriptionStatus;
        yield return DiarizationStatus;
        yield return PublishStatus;
    }
}

internal sealed record ActiveQueueItemState(
    QueuedManifestStatusEntry Summary,
    DateTimeOffset ProcessingStartedAtUtc)
{
    public Dictionary<string, DateTimeOffset> RunningStageStartedAtUtc { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record StageTimingAverage(
    int ObservationCount,
    double AverageSecondsPerAudioSecond,
    TimeSpan AverageDuration);

internal sealed record CapturedChunkGroup(
    string Prefix,
    IReadOnlyList<string> ChunkPaths);

internal interface IWorkerProcessFactory
{
    IWorkerProcess Start(ProcessStartInfo startInfo);
}

internal interface IWorkerProcess : IDisposable
{
    int ExitCode { get; }

    bool HasExited { get; }

    Task<string> ReadStandardOutputToEndAsync(CancellationToken cancellationToken);

    Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken);

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);

    void SetPriority(ProcessPriorityClass priorityClass);
}

internal sealed class SystemWorkerProcessFactory : IWorkerProcessFactory
{
    public IWorkerProcess Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the processing worker.");
        return new SystemWorkerProcess(process);
    }
}

internal sealed class SystemWorkerProcess : IWorkerProcess
{
    private readonly Process _process;

    public SystemWorkerProcess(Process process)
    {
        _process = process;
    }

    public int ExitCode => _process.ExitCode;

    public bool HasExited => _process.HasExited;

    public Task<string> ReadStandardOutputToEndAsync(CancellationToken cancellationToken)
    {
        return _process.StandardOutput.ReadToEndAsync(cancellationToken);
    }

    public Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken)
    {
        return _process.StandardError.ReadToEndAsync(cancellationToken);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return _process.WaitForExitAsync(cancellationToken);
    }

    public void Kill(bool entireProcessTree)
    {
        _process.Kill(entireProcessTree);
    }

    public void SetPriority(ProcessPriorityClass priorityClass)
    {
        _process.PriorityClass = priorityClass;
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
