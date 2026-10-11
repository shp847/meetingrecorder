using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.App.Services;

internal sealed class ProcessingQueueService : IDisposable
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
    private readonly Func<bool> _isSpeakerLabelingAvailableProvider;
    private readonly Func<DateTimeOffset> _localNowProvider;
    private readonly ProcessingTempCleanupService _tempCleanupService;
    private readonly AsapLifecycleResolver _asapLifecycleResolver = new();
    private readonly StagedBacklogWorkStore _stagedBacklogWorkStore;
    private readonly ResourceCapacityMonitor _resourceCapacityMonitor;
    private readonly GpuCapacityMonitor _gpuCapacityMonitor;
    private readonly Func<bool> _isGpuDiarizationReadyProvider;
    private readonly SemaphoreSlim _stagedBacklogWorkGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _pendingManifestSignal = new(0);
    private readonly object _processSyncRoot = new();
    private readonly Task _drainTask;
    private IWorkerProcess? _currentWorker;
    private string? _currentManifestPath;
    private bool _isBackgroundWorkPausedForRecording;
    private readonly List<QueuedManifestStatusEntry> _queuedManifestEntries = [];
    private readonly HashSet<string> _reservedManifestPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWorkerProcess> _activeWorkersByManifestPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveQueueItemState> _activeItemStatesByManifestPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StagedBacklogWorkItem> _activeStagedWorkByManifestPath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gpuDiarizationManifestPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StagedBacklogWorkItem> _reservedStagedWorkByManifestPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManifestMonitorState> _manifestMonitorsByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StageTimingAverage> _stageTimingAverages = new(StringComparer.OrdinalIgnoreCase);
    private List<StagedBacklogWorkItem> _stagedBacklogWorkItems = [];
    private bool _stagedBacklogWorkLoaded;
    private bool _stagedBacklogWorkUnavailable;
    private ActiveQueueItemState? _currentItemState;
    private ProcessingQueueStatusSnapshot _statusSnapshot;
    private string? _preemptedManifestPath;
    private int _normalJobsSinceCleanup;
    private int _overnightCleanupBurstCount;

    internal event Action<ProcessingQueueStatusSnapshot>? StatusChanged;
    internal event Action<ProcessingWorkCompletion>? WorkCompleted;

    public ProcessingQueueService(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        FileLogWriter logger,
        IMeetingMetadataEnricher? meetingMetadataEnricher = null,
        Func<WorkerLaunch>? workerLaunchResolver = null,
        IWorkerProcessFactory? workerProcessFactory = null,
        Func<bool>? isRecordingProvider = null,
        ProcessingTempCleanupService? tempCleanupService = null,
        Func<bool>? isSpeakerLabelingAvailableProvider = null,
        Func<DateTimeOffset>? localNowProvider = null,
        ResourceCapacityMonitor? resourceCapacityMonitor = null,
        GpuCapacityMonitor? gpuCapacityMonitor = null,
        Func<bool>? isGpuDiarizationReadyProvider = null)
    {
        _config = config;
        _manifestStore = manifestStore;
        _meetingMetadataEnricher = meetingMetadataEnricher ?? new PassthroughMeetingMetadataEnricher();
        _logger = logger;
        _workerLaunchResolver = workerLaunchResolver ?? WorkerLocator.Resolve;
        _workerProcessFactory = workerProcessFactory ?? new SystemWorkerProcessFactory();
        _isRecordingProvider = isRecordingProvider ?? (() => false);
        _isSpeakerLabelingAvailableProvider = isSpeakerLabelingAvailableProvider ?? (() =>
            new DiarizationAssetCatalogService().InspectInstalledAssets(_config.Current.DiarizationAssetPath).IsReady);
        _localNowProvider = localNowProvider ?? (() => DateTimeOffset.Now);
        _tempCleanupService = tempCleanupService ?? new ProcessingTempCleanupService(
            AppDataPaths.GetAppRoot(),
            Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization"),
            Path.Combine(Path.GetTempPath(), "MeetingRecorderTranscription"),
            logger);
        var configDirectory = Path.GetDirectoryName(_config.ConfigPath)
            ?? throw new InvalidOperationException("Configuration path must include a parent directory.");
        _stagedBacklogWorkStore = new StagedBacklogWorkStore(Path.Combine(configDirectory, "staged-backlog.json"));
        _resourceCapacityMonitor = resourceCapacityMonitor ?? new ResourceCapacityMonitor(
            _isRecordingProvider,
            HasEligibleStagedBacklogForCapacity);
        _gpuCapacityMonitor = gpuCapacityMonitor ?? new GpuCapacityMonitor(HasEligibleStagedBacklogForCapacity);
        _isGpuDiarizationReadyProvider = isGpuDiarizationReadyProvider ?? InspectDirectMlDiarizationReadiness;
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
                return _activeWorkersByManifestPath.Values.Any(process => !process.HasExited);
            }
        }
    }

    public async Task ResumePendingSessionsAsync(
        IReadOnlySet<string>? cleanupManifestPaths = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureStagedBacklogWorkLoadedAsync(cancellationToken);
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

        var recoveredImportJobCount = await new ExternalAudioImportJobRecoveryService(_manifestStore)
            .RecoverMissingCompanionJobsAsync(_config.Current.WorkDir, DateTimeOffset.UtcNow, cancellationToken);
        if (recoveredImportJobCount > 0)
        {
            _logger.Log($"Recovered {recoveredImportJobCount} imported-audio job record(s) from existing staged work.");
        }

        var importReconciliation = await new ExternalAudioImportStartupReconciliationService(_manifestStore)
            .ReconcileAsync(
                _config.Current.WorkDir,
                _config.Current.AudioOutputDir,
                _config.Current.TranscriptOutputDir,
                cancellationToken);
        var pending = importReconciliation.PendingManifestPaths.ToList();
        if (!_config.Current.IncrementalWorkPlan.HasFlag(IncrementalWorkPlan.QueuedRecordings))
        {
            _logger.Log("Left existing queued recordings pending because queued-recording background work is disabled.");
            PublishStatusSnapshot(UpdateStatusSnapshot());
            return;
        }

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

            await EnqueueAsync(
                manifestPath,
                cleanupManifestPaths is not null && cleanupManifestPaths.Contains(manifestPath)
                    ? ProcessingWorkPriority.Cleanup
                    : ProcessingWorkPriority.Normal,
                cancellationToken);
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
            return UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow) ?? _statusSnapshot;
        }
    }

    public Task EnqueueAsync(string manifestPath, CancellationToken cancellationToken)
    {
        return EnqueueAsync(manifestPath, ProcessingWorkPriority.Normal, cancellationToken);
    }

    public async Task EnqueueAsync(
        string manifestPath,
        ProcessingWorkPriority priority = ProcessingWorkPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_shutdownCts.IsCancellationRequested)
        {
            _logger.Log($"Skipping processing enqueue for '{manifestPath}' because shutdown is in progress.");
            return;
        }

        var queueEntry = await LoadBackgroundQueueEntryAsync(manifestPath, priority, cancellationToken);
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
        if (!CanRetainRushRequest(manifest))
        {
            throw new InvalidOperationException("Only queued or in-progress meetings can be marked for ASAP processing.");
        }

        // ASAP is a one-meeting lifecycle priority. It wins over a prior
        // transcript-first backlog override only for this manifest; clearing
        // ASAP releases that priority without changing the backlog policy.
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true)
        {
            manifest = manifest with
            {
                ProcessingOverrides = manifest.ProcessingOverrides with
                {
                    SkipSpeakerLabeling = false,
                },
            };
            await _manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
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
            if (!_isRecordingProvider() &&
                _currentWorker is { HasExited: false } currentWorker &&
                _currentItemState is { } currentItem &&
                !string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal) &&
                currentItem.Summary.TranscriptionStatus.State == StageExecutionState.Succeeded &&
                currentItem.Summary.DiarizationStatus.State == StageExecutionState.Running)
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
        ProcessingQueueStatusSnapshot? snapshotToPublish;
        lock (_processSyncRoot)
        {
            // A reserved item is already being admitted to the current pass.
            // Clearing ASAP must never cancel or reshuffle that work. A queued
            // item, however, returns to ordinary fairness after its priority is
            // released.
            if (!_reservedManifestPaths.Contains(manifestPath))
            {
                var queuedIndex = _queuedManifestEntries.FindIndex(entry =>
                    string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal));
                if (queuedIndex >= 0)
                {
                    var queuedEntry = _queuedManifestEntries[queuedIndex];
                    _queuedManifestEntries.RemoveAt(queuedIndex);
                    _queuedManifestEntries.Add(queuedEntry);
                }
            }

            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
        }

        PublishStatusSnapshot(snapshotToPublish);
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

            if (!_isRecordingProvider() &&
                _currentWorker is { HasExited: false } currentWorker &&
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
            var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken: cancellationToken);
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
        _resourceCapacityMonitor.Dispose();
        _gpuCapacityMonitor.Dispose();
        _shutdownCts.Cancel();
        _pendingManifestSignal.Release();

        List<(IWorkerProcess Worker, string ManifestPath)> workers;
        lock (_processSyncRoot)
        {
            workers = _activeWorkersByManifestPath
                .Select(pair => (pair.Value, pair.Key))
                .ToList();
        }

        foreach (var (worker, manifestPath) in workers)
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
            var activeTasks = new List<Task>();
            while (!cancellationToken.IsCancellationRequested)
            {
                while (activeTasks.Count < GetMaximumConcurrentWorkerCount() &&
                       TryDequeueNextManifestPath(out var manifestPath))
                {
                    activeTasks.Add(ProcessManifestSafelyAsync(manifestPath, cancellationToken));
                }

                if (activeTasks.Count == 0)
                {
                    await _pendingManifestSignal.WaitAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    continue;
                }

                var completedTask = await Task.WhenAny(activeTasks.Append(Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)));
                if (!activeTasks.Contains(completedTask))
                {
                    continue;
                }

                activeTasks.Remove(completedTask);
                await completedTask;
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessManifestSafelyAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var workPriority = GetWorkPriority(manifestPath);
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
            PublishWorkCompletion(manifestPath, workPriority, succeeded: false, detail: exception.Message);
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
        var workPriority = GetWorkPriority(manifestPath);
        var stagedWork = GetReservedStagedWork(manifestPath);
        if (stagedWork is not null)
        {
            stagedWork = await TryLeaseStagedWorkAsync(stagedWork, cancellationToken);
            if (stagedWork is null)
            {
                _logger.Log($"Skipped staged queue work for '{manifestPath}' because its durable lease is no longer current.");
                return;
            }
        }

        await ApplyDeferredSpeakerLabelingIfConfiguredAsync(manifestPath, cancellationToken);
        await TryEnrichManifestAsync(manifestPath, cancellationToken);
        WorkerRecoveryConfig? gpuCapacityCpuConfig = null;
        WorkerRunResult workerResult;
        try
        {
            if (ShouldForceCpuDiarizationForGpuCapacity(manifestPath, stagedWork))
            {
                gpuCapacityCpuConfig = await CreateCpuOnlyDiarizationConfigAsync(manifestPath, _config.Current, cancellationToken);
            }

            workerResult = await RunWorkerAsync(
                manifestPath,
                gpuCapacityCpuConfig?.ConfigPath ?? AppDataPaths.GetConfigPath(),
                cancellationToken,
                stagedWork,
                gpuCapacityCpuConfig?.Config);
        }
        finally
        {
            TryDeleteRecoveryConfig(gpuCapacityCpuConfig?.ConfigPath);
        }
        if (await TryHandlePreemptedManifestAsync(manifestPath, cancellationToken))
        {
            await CompleteStagedWorkAsync(
                stagedWork,
                StagedBacklogWorkState.Retryable,
                "The worker was preempted before its staged receipt could be recorded.",
                cancellationToken);
            return;
        }

        if (workerResult.ExitCode == 0)
        {
            if (stagedWork is not null && !HasMatchingStagedWorkReceipt(workerResult.Receipt, stagedWork))
            {
                await CompleteStagedWorkAsync(
                    stagedWork,
                    StagedBacklogWorkState.Retryable,
                    "The worker exited without a matching staged-work receipt.",
                    cancellationToken);
                PublishWorkCompletion(manifestPath, workPriority, succeeded: false, detail: "Staged worker receipt needs retry.");
                return;
            }

            await CompleteStagedWorkAsync(stagedWork, StagedBacklogWorkState.Succeeded, null, cancellationToken);
            await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
            PublishWorkCompletion(manifestPath, workPriority, succeeded: true, detail: null);
            await EnqueueNextStagedPassAfterSuccessAsync(manifestPath, stagedWork, cancellationToken);
            return;
        }

        if (await TryRecoverFromDiarizationWorkerCrashAsync(manifestPath, workerResult, cancellationToken))
        {
            await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
            PublishWorkCompletion(manifestPath, workPriority, succeeded: true, detail: "Recovered from worker crash.");
            return;
        }

        LogWorkerFailure(manifestPath, workerResult);
        await CompleteStagedWorkAsync(
            stagedWork,
            StagedBacklogWorkState.Retryable,
            "The worker failed before a successful staged receipt was recorded.",
            cancellationToken);
        await MarkManifestFailedAfterWorkerFailureAsync(manifestPath, workerResult, cancellationToken);
        await ClearRushProcessingRequestIfCompletedAsync(manifestPath, cancellationToken);
        PublishWorkCompletion(manifestPath, workPriority, succeeded: false, detail: workerResult.StandardError);
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
        StagedBacklogWorkItem? stagedWork = null,
        AppConfig? launchConfigOverride = null)
    {
        await _tempCleanupService.RunRecurringCleanupAsync(cancellationToken);
        var launch = _workerLaunchResolver();
        var stage = stagedWork is null ? SessionProcessingStage.FullPass : ToSessionProcessingStage(stagedWork.Stage);
        var workLease = stagedWork is null
            ? null
            : new SessionProcessingWorkLease(
                stagedWork.WorkId,
                stagedWork.InputRevision,
                stagedWork.LeaseToken ?? throw new InvalidOperationException("Staged work must have a durable lease before launch."));
        var arguments = BuildWorkerArguments(launch.ArgumentPrefix, manifestPath, configPath, stage, workLease);
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
            $"Launching worker for '{manifestPath}'. FileName='{startInfo.FileName}'. Arguments='{BuildWorkerLogArguments(launch.ArgumentPrefix, manifestPath, configPath, stage, workLease)}'. " +
            $"Mode={currentConfig.BackgroundProcessingMode}. SpeakerLabelingMode={currentConfig.BackgroundSpeakerLabelingMode}. " +
            $"DiarizationAcceleration={currentConfig.DiarizationAccelerationPreference}. " +
            $"Priority={priority}. TranscriptionThreads={transcriptionThreads}. DiarizationThreads={diarizationThreads}.");
        var process = _workerProcessFactory.Start(startInfo);
        SetCurrentWorker(process, manifestPath);

        try
        {
            TryApplyWorkerPriority(process, priority, manifestPath);
            await MarkStagedWorkRunningAsync(stagedWork, cancellationToken);
            await MarkManifestProcessingStartedAsync(manifestPath, cancellationToken);

            var standardOutputTask = process.ReadStandardOutputToEndAsync(cancellationToken);
            var standardErrorTask = process.ReadStandardErrorToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);

            var result = new WorkerRunResult(
                process.ExitCode,
                standardOutputTask.Result,
                standardErrorTask.Result,
                launchSnapshot,
                TryParseStagedWorkReceipt(standardOutputTask.Result));
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
            await ClearCurrentWorkerAsync(process);
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
                launchConfigOverride: recoveryConfig.Config);
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
                launchConfigOverride: recoveryConfig.Config);
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
            if (IsActiveRushRequestFor(manifestPath))
            {
                continue;
            }

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
                ForceSpeakerLabeling = false,
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

        if (IsActiveRushRequestFor(manifestPath))
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

        if (IsActiveRushRequestFor(manifestPath))
        {
            return;
        }

        var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
        if (!ShouldApplyDeferredSpeakerLabelingOverride(manifest))
        {
            return;
        }

        await ApplySkipSpeakerLabelingOverrideAsync(
            manifestPath,
            BackgroundProcessingPolicy.IsTranscriptOnlyDrainActive(_config.Current)
                ? "Speaker labeling skipped by transcript-only drain so transcripts can publish sooner."
                : "Speaker labeling deferred by the responsive background processing mode.",
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
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true ||
            manifest.ProcessingOverrides?.ForceSpeakerLabeling == true)
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
            if (!CanRetainRushRequest(manifest))
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
        if (!CanRetainRushRequest(manifest))
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

    private bool CanRetainRushRequest(MeetingSessionManifest manifest)
    {
        if (IsRushEligible(manifest))
        {
            return true;
        }

        return _asapLifecycleResolver.Resolve(
            manifest,
            IsSpeakerLabelingAvailableForAsap(),
            IsRecoverableAsapFailure(manifest)).RetainRequest;
    }

    private bool IsActiveRushRequestFor(string manifestPath) =>
        _config.Current.RushProcessingRequest is { } request &&
        string.Equals(request.ManifestPath, manifestPath, StringComparison.Ordinal);

    private bool IsSpeakerLabelingAvailableForAsap()
    {
        try
        {
            return _isSpeakerLabelingAvailableProvider();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRecoverableAsapFailure(MeetingSessionManifest manifest) =>
        manifest.State == SessionState.Failed &&
        manifest.TranscriptionStatus.State == StageExecutionState.Failed &&
        (!string.IsNullOrWhiteSpace(manifest.MergedAudioPath) ||
         manifest.RawChunkPaths.Count > 0 ||
         manifest.LoopbackCaptureSegments.Any(segment => segment.ChunkPaths.Count > 0) ||
         manifest.MicrophoneCaptureSegments.Any(segment => segment.ChunkPaths.Count > 0) ||
         !string.IsNullOrWhiteSpace(manifest.ImportedSourceAudio?.OriginalPath));

    private string GetMaintenanceArchiveRoot()
    {
        var configDirectory = Path.GetDirectoryName(_config.ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var appRoot = Path.GetDirectoryName(configDirectory)
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");
        return Path.Combine(appRoot, "maintenance", "archived-imported-source-work");
    }

    private async Task<QueuedManifestStatusEntry> LoadQueueEntryAsync(
        string manifestPath,
        ProcessingWorkPriority priority = ProcessingWorkPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            return CreateQueueEntry(manifest, manifestPath, priority);
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
                new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
                Priority: priority);
        }
    }

    private async Task<QueuedManifestStatusEntry> LoadBackgroundQueueEntryAsync(
        string manifestPath,
        ProcessingWorkPriority priority,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            var stagedWork = await GetOrCreateBackgroundStagedWorkAsync(manifest, manifestPath, cancellationToken);
            return CreateQueueEntry(manifest, manifestPath, priority) with { StagedWork = stagedWork };
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            // The legacy full-pass queue remains the safe recovery route when a
            // manifest cannot be read or the staged ledger is preserved for
            // repair. Never reinterpret unreadable user work as an empty queue.
            return await LoadQueueEntryAsync(manifestPath, priority, cancellationToken);
        }
    }

    private async Task<bool> EnsureStagedBacklogWorkLoadedAsync(CancellationToken cancellationToken)
    {
        if (_stagedBacklogWorkLoaded)
        {
            return !_stagedBacklogWorkUnavailable;
        }

        await _stagedBacklogWorkGate.WaitAsync(cancellationToken);
        try
        {
            if (_stagedBacklogWorkLoaded)
            {
                return !_stagedBacklogWorkUnavailable;
            }

            try
            {
                _stagedBacklogWorkItems = (await _stagedBacklogWorkStore.LoadAsync(cancellationToken)).ToList();
                var recovered = StagedBacklogWorkLeaseCoordinator.RecoverInterruptedLeases(
                    _stagedBacklogWorkItems,
                    DateTimeOffset.UtcNow);
                if (!recovered.SequenceEqual(_stagedBacklogWorkItems))
                {
                    _stagedBacklogWorkItems = recovered.ToList();
                    await _stagedBacklogWorkStore.SaveAsync(_stagedBacklogWorkItems, cancellationToken);
                    _logger.Log("Recovered interrupted staged backlog lease(s) as retryable local work.");
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                // The untouched store is the recovery source of truth. Fall
                // back to the existing full-pass queue instead of overwriting
                // it, silently dropping records, or fabricating staged state.
                _stagedBacklogWorkUnavailable = true;
                _logger.Log($"Staged backlog ledger is unavailable and was left unchanged for repair: {exception.Message}");
            }
            finally
            {
                _stagedBacklogWorkLoaded = true;
            }

            return !_stagedBacklogWorkUnavailable;
        }
        finally
        {
            _stagedBacklogWorkGate.Release();
        }
    }

    private async Task<StagedBacklogWorkItem?> GetOrCreateBackgroundStagedWorkAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!await EnsureStagedBacklogWorkLoadedAsync(cancellationToken))
        {
            return null;
        }

        var stage = ResolveNextBackgroundStage(manifest);
        if (stage is null)
        {
            return null;
        }

        var stagedStage = ToStagedBacklogWorkStage(stage.Value);
        var inputRevision = BuildStagedWorkInputRevision(manifest, stagedStage);
        await _stagedBacklogWorkGate.WaitAsync(cancellationToken);
        try
        {
            var existing = _stagedBacklogWorkItems.FirstOrDefault(work =>
                string.Equals(work.SessionId, manifest.SessionId, StringComparison.Ordinal) &&
                work.Stage == stagedStage &&
                string.Equals(work.InputRevision, inputRevision, StringComparison.Ordinal) &&
                work.State is StagedBacklogWorkState.Pending or StagedBacklogWorkState.Retryable or StagedBacklogWorkState.Leased or StagedBacklogWorkState.Running);
            if (existing is not null)
            {
                return existing;
            }

            var work = new StagedBacklogWorkItem(
                StagedBacklogWorkItem.CurrentSchemaVersion,
                Guid.NewGuid(),
                manifest.SessionId,
                BuildManifestToken(manifestPath),
                inputRevision,
                OutputRevision: string.Empty,
                stagedStage,
                StagedBacklogWorkIntent.Background,
                StagedBacklogWorkState.Pending,
                Attempt: 0,
                LeaseToken: null,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                RetryAfterUtc: null,
                Reason: null);
            _stagedBacklogWorkItems.Add(work);
            try
            {
                await _stagedBacklogWorkStore.SaveAsync(_stagedBacklogWorkItems, cancellationToken);
                return work;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _stagedBacklogWorkItems.Remove(work);
                _stagedBacklogWorkUnavailable = true;
                _logger.Log($"Staged backlog ledger could not be updated and was left unchanged for repair: {exception.Message}");
                return null;
            }
        }
        finally
        {
            _stagedBacklogWorkGate.Release();
        }
    }

    private SessionProcessingStage? ResolveNextBackgroundStage(MeetingSessionManifest manifest)
    {
        if (manifest.TranscriptionStatus.State != StageExecutionState.Succeeded ||
            manifest.PublishStatus.State != StageExecutionState.Succeeded)
        {
            return SessionProcessingStage.Transcript;
        }

        if (!BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(_config.Current) &&
            manifest.DiarizationStatus.State is StageExecutionState.NotStarted or StageExecutionState.Queued or StageExecutionState.Failed &&
            _isSpeakerLabelingAvailableProvider())
        {
            return SessionProcessingStage.Diarization;
        }

        if (_config.Current.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
            manifest.SummarizationStatus.State is StageExecutionState.NotStarted or StageExecutionState.Queued or StageExecutionState.Failed)
        {
            return SessionProcessingStage.Summary;
        }

        return null;
    }

    private static StagedBacklogWorkStage ToStagedBacklogWorkStage(SessionProcessingStage stage) => stage switch
    {
        SessionProcessingStage.Transcript => StagedBacklogWorkStage.Transcript,
        SessionProcessingStage.Diarization => StagedBacklogWorkStage.Diarization,
        SessionProcessingStage.Summary => StagedBacklogWorkStage.Summary,
        _ => StagedBacklogWorkStage.FullPass,
    };

    private static SessionProcessingStage ToSessionProcessingStage(StagedBacklogWorkStage stage) => stage switch
    {
        StagedBacklogWorkStage.Transcript => SessionProcessingStage.Transcript,
        StagedBacklogWorkStage.Diarization => SessionProcessingStage.Diarization,
        StagedBacklogWorkStage.Summary => SessionProcessingStage.Summary,
        _ => SessionProcessingStage.FullPass,
    };

    private static string BuildStagedWorkInputRevision(
        MeetingSessionManifest manifest,
        StagedBacklogWorkStage stage) =>
        string.Join(
            ":",
            manifest.SessionId,
            stage.ToString(),
            manifest.TranscriptionStatus.UpdatedAtUtc.UtcTicks,
            manifest.DiarizationStatus.UpdatedAtUtc.UtcTicks,
            manifest.SummarizationStatus.UpdatedAtUtc.UtcTicks,
            manifest.PublishStatus.UpdatedAtUtc.UtcTicks);

    private static string BuildManifestToken(string manifestPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(manifestPath))));

    private StagedBacklogWorkItem? GetReservedStagedWork(string manifestPath)
    {
        lock (_processSyncRoot)
        {
            return _reservedStagedWorkByManifestPath.TryGetValue(manifestPath, out var stagedWork)
                ? stagedWork
                : null;
        }
    }

    private async Task<StagedBacklogWorkItem?> TryLeaseStagedWorkAsync(
        StagedBacklogWorkItem stagedWork,
        CancellationToken cancellationToken)
    {
        if (!await EnsureStagedBacklogWorkLoadedAsync(cancellationToken))
        {
            return null;
        }

        await _stagedBacklogWorkGate.WaitAsync(cancellationToken);
        try
        {
            var transition = StagedBacklogWorkLeaseCoordinator.TryLease(
                _stagedBacklogWorkItems,
                stagedWork.WorkId,
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow);
            if (!transition.Applied || transition.Work is null)
            {
                _logger.Log($"Staged queue lease rejected for work '{stagedWork.WorkId:D}': {transition.RejectionReason}");
                return null;
            }

            _stagedBacklogWorkItems = transition.WorkItems.ToList();
            await _stagedBacklogWorkStore.SaveAsync(_stagedBacklogWorkItems, cancellationToken);
            return transition.Work;
        }
        finally
        {
            _stagedBacklogWorkGate.Release();
        }
    }

    private async Task MarkStagedWorkRunningAsync(
        StagedBacklogWorkItem? stagedWork,
        CancellationToken cancellationToken)
    {
        if (stagedWork is null)
        {
            return;
        }

        await _stagedBacklogWorkGate.WaitAsync(cancellationToken);
        try
        {
            var transition = StagedBacklogWorkLeaseCoordinator.TryMarkRunning(
                _stagedBacklogWorkItems,
                stagedWork.WorkId,
                stagedWork.LeaseToken ?? string.Empty);
            if (!transition.Applied)
            {
                throw new InvalidOperationException($"Staged queue work '{stagedWork.WorkId:D}' lost its lease before the worker started.");
            }

            _stagedBacklogWorkItems = transition.WorkItems.ToList();
            await _stagedBacklogWorkStore.SaveAsync(_stagedBacklogWorkItems, cancellationToken);
        }
        finally
        {
            _stagedBacklogWorkGate.Release();
        }
    }

    private async Task CompleteStagedWorkAsync(
        StagedBacklogWorkItem? stagedWork,
        StagedBacklogWorkState outcome,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (stagedWork is null)
        {
            return;
        }

        await _stagedBacklogWorkGate.WaitAsync(cancellationToken);
        try
        {
            var transition = StagedBacklogWorkLeaseCoordinator.TryComplete(
                _stagedBacklogWorkItems,
                stagedWork.WorkId,
                stagedWork.LeaseToken ?? string.Empty,
                outcome,
                outcome == StagedBacklogWorkState.Succeeded
                    ? $"receipt-{DateTimeOffset.UtcNow.UtcTicks}"
                    : string.Empty,
                reason,
                retryAfterUtc: outcome == StagedBacklogWorkState.Retryable
                    ? DateTimeOffset.UtcNow
                    : null);
            if (!transition.Applied)
            {
                _logger.Log($"Ignored stale staged completion for work '{stagedWork.WorkId:D}': {transition.RejectionReason}");
                return;
            }

            _stagedBacklogWorkItems = transition.WorkItems.ToList();
            await _stagedBacklogWorkStore.SaveAsync(_stagedBacklogWorkItems, cancellationToken);
        }
        finally
        {
            _stagedBacklogWorkGate.Release();
        }
    }

    private async Task EnqueueNextStagedPassAfterSuccessAsync(
        string manifestPath,
        StagedBacklogWorkItem? completedWork,
        CancellationToken cancellationToken)
    {
        if (completedWork is null)
        {
            return;
        }

        var queueEntry = await LoadBackgroundQueueEntryAsync(
            manifestPath,
            GetWorkPriority(manifestPath),
            cancellationToken);
        if (queueEntry.StagedWork is null || queueEntry.StagedWork.Stage == completedWork.Stage)
        {
            return;
        }

        ProcessingQueueStatusSnapshot? snapshotToPublish;
        lock (_processSyncRoot)
        {
            var shouldSignal = UpsertQueuedEntryLocked(queueEntry, preferFront: false, markPreempted: false);
            snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
            if (shouldSignal)
            {
                _pendingManifestSignal.Release();
            }
        }

        PublishStatusSnapshot(snapshotToPublish);
    }

    private static bool HasMatchingStagedWorkReceipt(
        SessionProcessingWorkReceipt? receipt,
        StagedBacklogWorkItem stagedWork) =>
        receipt is not null &&
        receipt.SchemaVersion == SessionProcessingWorkReceipt.CurrentSchemaVersion &&
        receipt.WorkId == stagedWork.WorkId &&
        string.Equals(receipt.WorkRevision, stagedWork.InputRevision, StringComparison.Ordinal) &&
        string.Equals(receipt.LeaseToken, stagedWork.LeaseToken, StringComparison.Ordinal) &&
        receipt.Stage == ToSessionProcessingStage(stagedWork.Stage);

    private static SessionProcessingWorkReceipt? TryParseStagedWorkReceipt(string standardOutput)
    {
        foreach (var line in standardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<SessionProcessingWorkReceipt>(line);
                if (receipt is not null)
                {
                    return receipt;
                }
            }
            catch (JsonException)
            {
                // Legacy workers print a ready-marker path instead of a JSON
                // receipt. That output is never accepted for staged work.
            }
        }

        return null;
    }

    private static string BuildWorkerArguments(
        string argumentPrefix,
        string manifestPath,
        string configPath,
        SessionProcessingStage stage,
        SessionProcessingWorkLease? workLease)
    {
        var arguments = $"{argumentPrefix} --manifest {QuoteWorkerArgument(manifestPath)} --config {QuoteWorkerArgument(configPath)}";
        if (workLease is null)
        {
            return arguments;
        }

        return arguments +
            $" --stage {stage.ToString().ToLowerInvariant()}" +
            $" --work-id {workLease.WorkId:D}" +
            $" --work-revision {QuoteWorkerArgument(workLease.WorkRevision)}" +
            $" --lease-token {QuoteWorkerArgument(workLease.LeaseToken)}";
    }

    private static string BuildWorkerLogArguments(
        string argumentPrefix,
        string manifestPath,
        string configPath,
        SessionProcessingStage stage,
        SessionProcessingWorkLease? workLease) =>
        workLease is null
            ? BuildWorkerArguments(argumentPrefix, manifestPath, configPath, stage, null)
            : $"{argumentPrefix} --manifest {QuoteWorkerArgument(manifestPath)} --config {QuoteWorkerArgument(configPath)} " +
              $"--stage {stage.ToString().ToLowerInvariant()} --work-id {workLease.WorkId:D} " +
              "--work-revision [redacted] --lease-token [redacted]";

    private static string QuoteWorkerArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";

    private static QueuedManifestStatusEntry CreateQueueEntry(
        MeetingSessionManifest manifest,
        string manifestPath,
        ProcessingWorkPriority priority = ProcessingWorkPriority.Normal)
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
            manifest.PublishStatus,
            Priority: priority);
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
        var queueEntry = await LoadQueueEntryAsync(manifestPath, GetWorkPriority(manifestPath), cancellationToken);
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        lock (_processSyncRoot)
        {
            RemoveQueuedEntryLocked(manifestPath);
            if (_reservedStagedWorkByManifestPath.TryGetValue(manifestPath, out var stagedWork))
            {
                _activeStagedWorkByManifestPath[manifestPath] = stagedWork;
            }

            var activeItem = new ActiveQueueItemState(queueEntry, DateTimeOffset.UtcNow);
            _activeItemStatesByManifestPath[manifestPath] = activeItem;
            if (_currentItemState is null || string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal))
            {
                _currentItemState = activeItem;
                _currentManifestPath = manifestPath;
            }

            InitializeRunningStageTrackingLocked(activeItem);
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

        _reservedManifestPaths.Remove(manifestPath);
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
        if (_activeItemStatesByManifestPath.ContainsKey(queueEntry.ManifestPath))
        {
            return false;
        }

        var existingIndex = _queuedManifestEntries.FindIndex(entry =>
            string.Equals(entry.ManifestPath, queueEntry.ManifestPath, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = _queuedManifestEntries[existingIndex];
            _queuedManifestEntries.RemoveAt(existingIndex);
            _reservedManifestPaths.Remove(queueEntry.ManifestPath);
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

        _queuedManifestEntries[existingIndex] = queueEntry with
        {
            WasPreempted = _queuedManifestEntries[existingIndex].WasPreempted,
            Priority = _queuedManifestEntries[existingIndex].Priority,
            StagedWork = _queuedManifestEntries[existingIndex].StagedWork,
        };
    }

    private int GetMaximumConcurrentWorkerCount()
    {
        lock (_processSyncRoot)
        {
            var stage = GetActiveStagedBarrierStageLocked();
            var overnightDecision = OvernightAccelerationPolicyResolver.Resolve(_config.Current, _localNowProvider());
            if (stage is null)
            {
                return BackgroundProcessingPolicy.GetMaxWorkerCount(_config.Current);
            }

            if (overnightDecision.IsWindowActive)
            {
                return overnightDecision.IsAccelerating
                    ? overnightDecision.GetMaximumWorkerCount(stage)
                    : BackgroundProcessingPolicy.GetMaxWorkerCount(_config.Current);
            }

            if (BackgroundProcessingPolicy.IsTranscriptOnlyDrainActive(_config.Current))
            {
                return BackgroundProcessingPolicy.GetMaxWorkerCount(_config.Current);
            }

            if (!BacklogAccelerationProfileResolver.IsIdleCapacityEnabled(_config.Current))
            {
                return BackgroundProcessingPolicy.GetMaxWorkerCount(_config.Current);
            }

            var cpuCapacitySnapshot = _resourceCapacityMonitor.Snapshot;
            var cpuCapacityCap = IdleCpuCapacityPolicy.GetNextLaunchCap(cpuCapacitySnapshot, stage);
            if (stage == StagedBacklogWorkStage.Diarization &&
                cpuCapacitySnapshot.IsAvailable &&
                GpuCapacityPolicy.CanLaunchOneGpuWorker(
                    _gpuCapacityMonitor.Snapshot,
                    isGpuCapableProvider: true,
                    isProviderReady: IsGpuDiarizationReady()))
            {
                // The second worker is forced to CPU below. This preserves one GPU job
                // even when both queued workers share the Auto DirectML preference.
                return Math.Max(cpuCapacityCap, 2);
            }

            return cpuCapacityCap;
        }
    }

    private bool ShouldForceCpuDiarizationForGpuCapacity(string manifestPath, StagedBacklogWorkItem? stagedWork)
    {
        if (stagedWork?.Stage != StagedBacklogWorkStage.Diarization ||
            !_resourceCapacityMonitor.Snapshot.IsAvailable ||
            !GpuCapacityPolicy.CanLaunchOneGpuWorker(
                _gpuCapacityMonitor.Snapshot,
                isGpuCapableProvider: true,
                isProviderReady: IsGpuDiarizationReady()))
        {
            return false;
        }

        lock (_processSyncRoot)
        {
            if (_gpuDiarizationManifestPaths.Count == 0)
            {
                _gpuDiarizationManifestPaths.Add(manifestPath);
                return false;
            }

            return true;
        }
    }

    private bool IsGpuDiarizationReady()
    {
        try
        {
            return _isGpuDiarizationReadyProvider();
        }
        catch
        {
            return false;
        }
    }

    private bool InspectDirectMlDiarizationReadiness()
    {
        try
        {
            var config = _config.Current;
            if (config.DiarizationProviderPreference != DiarizationProviderPreference.LocalSherpa ||
                config.DiarizationAccelerationPreference != InferenceAccelerationPreference.Auto)
            {
                return false;
            }

            var status = new DiarizationAssetCatalogService().InspectInstalledAssets(config.DiarizationAssetPath);
            return status.IsReady && status.LastDirectMlProbeSucceeded == true;
        }
        catch
        {
            return false;
        }
    }

    private bool HasEligibleStagedBacklogForCapacity()
    {
        lock (_processSyncRoot)
        {
            if (!BacklogAccelerationProfileResolver.IsIdleCapacityEnabled(_config.Current))
            {
                return false;
            }

            if (GetActiveStagedBarrierStageLocked() is null)
            {
                return false;
            }

            var overnightDecision = OvernightAccelerationPolicyResolver.Resolve(_config.Current, _localNowProvider());
            return !overnightDecision.IsWindowActive &&
                   !BackgroundProcessingPolicy.IsTranscriptOnlyDrainActive(_config.Current);
        }
    }

    private bool IsOvernightAccelerationActiveForQueuedStage()
    {
        lock (_processSyncRoot)
        {
            return GetActiveStagedBarrierStageLocked() is not null &&
                   OvernightAccelerationPolicyResolver.Resolve(_config.Current, _localNowProvider()).IsAccelerating;
        }
    }

    private string BuildBackgroundPauseLogMessage() =>
        IsOvernightAccelerationActiveForQueuedStage()
            ? "Pausing new overnight-accelerated work because a live recording is active."
            : "Pausing new background processing because a live recording is active in responsive mode.";

    private StagedBacklogWorkStage? GetActiveStagedBarrierStageLocked()
    {
        var stage = _queuedManifestEntries
            .Select(entry => entry.StagedWork?.Stage)
            .Concat(_activeStagedWorkByManifestPath.Values.Select(work => (StagedBacklogWorkStage?)work.Stage))
            .Where(candidate => candidate is not null)
            .OrderBy(candidate => GetStagedBarrierOrder(candidate!.Value))
            .FirstOrDefault();
        return stage;
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
                    string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal) &&
                    !_reservedManifestPaths.Contains(entry.ManifestPath));
            if (rushRequest is null)
            {
                dequeueIndex = SelectFairQueueIndexLocked();
            }

            if (dequeueIndex < 0)
            {
                dequeueIndex = _queuedManifestEntries.FindIndex(entry => !_reservedManifestPaths.Contains(entry.ManifestPath));
            }

            if (dequeueIndex < 0)
            {
                manifestPath = string.Empty;
                return false;
            }

            var selected = _queuedManifestEntries[dequeueIndex];
            if (selected.Priority == ProcessingWorkPriority.Cleanup)
            {
                _normalJobsSinceCleanup = 0;
                _overnightCleanupBurstCount++;
            }
            else
            {
                _normalJobsSinceCleanup++;
                _overnightCleanupBurstCount = 0;
            }

            manifestPath = selected.ManifestPath;
            _reservedManifestPaths.Add(manifestPath);
            if (selected.StagedWork is { } stagedWork)
            {
                _reservedStagedWorkByManifestPath[manifestPath] = stagedWork;
            }
            else
            {
                _reservedStagedWorkByManifestPath.Remove(manifestPath);
            }
            return true;
        }
    }

    private int SelectFairQueueIndexLocked()
    {
        var activeBarrierOrder = _queuedManifestEntries
            .Where(entry => !_reservedManifestPaths.Contains(entry.ManifestPath) && entry.StagedWork is not null)
            .Select(entry => GetStagedBarrierOrder(entry.StagedWork!.Stage))
            .Concat(_activeStagedWorkByManifestPath.Values.Select(work => GetStagedBarrierOrder(work.Stage)))
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        var restrictToBarrier = activeBarrierOrder != int.MaxValue;
        var normalIndex = _queuedManifestEntries.FindIndex(entry =>
            entry.Priority == ProcessingWorkPriority.Normal &&
            !_reservedManifestPaths.Contains(entry.ManifestPath) &&
            IsAtActiveStagedBarrier(entry, activeBarrierOrder, restrictToBarrier));
        var cleanupIndex = _queuedManifestEntries.FindIndex(entry =>
            entry.Priority == ProcessingWorkPriority.Cleanup &&
            !_reservedManifestPaths.Contains(entry.ManifestPath) &&
            IsAtActiveStagedBarrier(entry, activeBarrierOrder, restrictToBarrier));

        if (cleanupIndex < 0)
        {
            return normalIndex;
        }

        if (normalIndex < 0)
        {
            return cleanupIndex;
        }

        return OvernightAccelerationPolicyResolver.Resolve(_config.Current, _localNowProvider()).IsWindowActive
            ? _overnightCleanupBurstCount < MeetingCleanupAutoApplyPlanner.MaxAutomaticFixesPerBatch
                ? cleanupIndex
                : normalIndex
            : _normalJobsSinceCleanup > 0
                ? cleanupIndex
                : normalIndex;
    }

    private static bool IsAtActiveStagedBarrier(
        QueuedManifestStatusEntry entry,
        int activeBarrierOrder,
        bool restrictToBarrier) =>
        !restrictToBarrier ||
        entry.StagedWork is { } stagedWork && GetStagedBarrierOrder(stagedWork.Stage) == activeBarrierOrder;

    private static int GetStagedBarrierOrder(StagedBacklogWorkStage stage) => stage switch
    {
        StagedBacklogWorkStage.Transcript => 0,
        StagedBacklogWorkStage.Diarization => 1,
        StagedBacklogWorkStage.Summary => 2,
        _ => 3,
    };

    private ProcessingWorkPriority GetWorkPriority(string manifestPath)
    {
        lock (_processSyncRoot)
        {
            return _activeItemStatesByManifestPath.TryGetValue(manifestPath, out var active)
                ? active.Summary.Priority
                : _queuedManifestEntries.FirstOrDefault(entry => string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal))?.Priority
                    ?? ProcessingWorkPriority.Normal;
        }
    }

    private void PublishWorkCompletion(
        string manifestPath,
        ProcessingWorkPriority priority,
        bool succeeded,
        string? detail)
    {
        WorkCompleted?.Invoke(new ProcessingWorkCompletion(manifestPath, priority, succeeded, detail));
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
        if (_manifestMonitorsByPath.Remove(manifestPath, out var existing))
        {
            existing.Cancellation.Cancel();
            existing.Cancellation.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        var task = Task.Run(() => MonitorCurrentManifestAsync(manifestPath, cts.Token));
        _manifestMonitorsByPath[manifestPath] = new ManifestMonitorState(cts, task);
    }

    private async Task MonitorCurrentManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var queueEntry = await LoadQueueEntryAsync(manifestPath, cancellationToken: cancellationToken);
                ProcessingQueueStatusSnapshot? snapshotToPublish = null;
                lock (_processSyncRoot)
                {
                    if (!_activeItemStatesByManifestPath.TryGetValue(manifestPath, out var activeItem))
                    {
                        return;
                    }

                    ApplyObservedStageTransitionsLocked(activeItem, queueEntry);
                    if (activeItem.Summary != queueEntry)
                    {
                        var updatedItem = activeItem with { Summary = queueEntry };
                        _activeItemStatesByManifestPath[manifestPath] = updatedItem;
                        if (string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal))
                        {
                            _currentItemState = updatedItem;
                        }

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

    private async Task StopCurrentManifestMonitorAsync(string manifestPath)
    {
        ManifestMonitorState? monitor;
        lock (_processSyncRoot)
        {
            _manifestMonitorsByPath.Remove(manifestPath, out monitor);
        }

        if (monitor is null)
        {
            return;
        }

        monitor.Cancellation.Cancel();
        _ = monitor.Task.ContinueWith(
            _ => monitor.Cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await Task.CompletedTask;
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
        var activeItems = _activeItemStatesByManifestPath.Values.ToArray();
        var displayItem = _currentItemState ?? activeItems.FirstOrDefault();
        var totalRemainingCount = queuedCount + activeItems.Length;
        var rushRequest = BuildRushedProcessingStateLocked();
        var stagedBarrier = GetActiveStagedBarrierStageLocked();
        var overnightDecision = OvernightAccelerationPolicyResolver.Resolve(_config.Current, _localNowProvider());
        var runState = activeItems.Length > 0
            ? ProcessingQueueRunState.Processing
            : _isBackgroundWorkPausedForRecording && queuedCount > 0
                ? ProcessingQueueRunState.Paused
                : queuedCount > 0
                    ? ProcessingQueueRunState.Queued
                    : ProcessingQueueRunState.Idle;
        var pauseReason = runState == ProcessingQueueRunState.Paused
            ? stagedBarrier is not null && overnightDecision.IsAccelerating
                ? ProcessingQueuePauseReason.LiveRecordingOvernightAcceleration
                : ProcessingQueuePauseReason.LiveRecordingResponsiveMode
            : ProcessingQueuePauseReason.None;
        var currentItemEstimatedRemaining = displayItem is null
            ? null
            : EstimateRemainingLocked(displayItem.Summary, nowUtc);
        var queuedRemaining = EstimateQueuedRemainingLocked(nowUtc);
        var activeRemaining = EstimateActiveRemainingLocked(activeItems, nowUtc);
        var overallEstimatedRemaining = activeRemaining is { } activeRemainingEstimate && queuedRemaining is { } queuedRemainingEstimate
            ? activeRemainingEstimate + queuedRemainingEstimate
            : activeItems.Length == 0
                ? queuedRemaining
                : null;
        var currentStageStatus = displayItem is null
            ? null
            : GetCurrentStageStatus(displayItem.Summary);

        return new ProcessingQueueStatusSnapshot(
            runState,
            pauseReason,
            queuedCount,
            totalRemainingCount,
            displayItem?.Summary.ManifestPath,
            displayItem?.Summary.Title,
            displayItem?.Summary.Platform,
            currentStageStatus?.StageName,
            currentStageStatus?.State,
            currentStageStatus?.UpdatedAtUtc,
            displayItem?.ProcessingStartedAtUtc,
            currentItemEstimatedRemaining,
            overallEstimatedRemaining,
            nowUtc,
            rushRequest,
            IsRushPauseBypassActiveLocked(rushRequest),
            _queuedManifestEntries.Any(entry => entry.WasPreempted),
            currentStageStatus?.Message,
            stagedBarrier is null
                ? null
                : BuildBackgroundPolicyStatusText(stagedBarrier.Value, overnightDecision, activeItems.Length));
    }

    private string BuildBackgroundPolicyStatusText(
        StagedBacklogWorkStage stage,
        OvernightAccelerationDecision overnightDecision,
        int activeWorkerCount)
    {
        var profile = BacklogAccelerationProfileResolver.GetStatusText(_config.Current);
        var workerCap = GetMaximumConcurrentWorkerCount();
        var detail = overnightDecision.IsWindowActive
            ? overnightDecision.GetStatusText(stage)
            : BackgroundProcessingPolicy.IsTranscriptOnlyDrainActive(_config.Current)
                ? "Transcript-only emergency mode is active."
                : BacklogAccelerationProfileResolver.IsIdleCapacityEnabled(_config.Current)
                    ? _resourceCapacityMonitor.Snapshot.Reason
                    : "Idle-capacity acceleration is off for this profile.";
        return $"{profile}: {detail} Current cap {workerCap}; {activeWorkerCount} running.";
    }

    private TimeSpan? EstimateActiveRemainingLocked(IReadOnlyList<ActiveQueueItemState> activeItems, DateTimeOffset nowUtc)
    {
        var total = TimeSpan.Zero;
        foreach (var activeItem in activeItems)
        {
            var estimatedRemaining = EstimateRemainingLocked(activeItem.Summary, nowUtc);
            if (estimatedRemaining is null)
            {
                return null;
            }

            total += estimatedRemaining.Value;
        }

        return total;
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

            if (_activeItemStatesByManifestPath.ContainsKey(queueEntry.ManifestPath) &&
                stageStatus.State == StageExecutionState.Running)
            {
                // Native speaker labeling has no incremental completion signal. Do not turn a
                // static startup estimate into a false countdown while its heartbeat is alive.
                if (string.Equals(stageStatus.StageName, "diarization", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

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
            return _activeWorkersByManifestPath.ContainsKey(manifestPath) ||
                   _activeItemStatesByManifestPath.ContainsKey(manifestPath);
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
            if (!ShouldPauseBackgroundProcessing(manifestPath))
            {
                break;
            }

            if (!_isBackgroundWorkPausedForRecording)
            {
                _isBackgroundWorkPausedForRecording = true;
                _logger.Log(BuildBackgroundPauseLogMessage());
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

        return TrySwitchReservedManifestToRush(manifestPath) ?? manifestPath;
    }

    private bool ShouldPauseBackgroundProcessing(string manifestPath)
    {
        if (_isRecordingProvider() && IsOvernightAccelerationActiveForQueuedStage())
        {
            return true;
        }

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

        var activeEntry = _currentItemState is not null &&
                          string.Equals(_currentItemState.Summary.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal)
            ? _currentItemState.Summary
            : null;
        var queuedEntry = activeEntry is null
            ? _queuedManifestEntries.FirstOrDefault(entry =>
                string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal))
            : null;
        var entry = activeEntry ?? queuedEntry;
        var title = entry?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(rushRequest.ManifestPath) ?? rushRequest.ManifestPath);
        }

        var lifecycle = entry is null
            ? "ASAP: status needs refresh"
            : _asapLifecycleResolver.Resolve(new AsapLifecycleInput(
                activeEntry is null ? SessionState.Queued : SessionState.Processing,
                entry.TranscriptionStatus.State,
                entry.DiarizationStatus.State,
                entry.PublishStatus.State,
                SkipSpeakerLabeling: !entry.ExpectsSpeakerLabeling,
                ForceSpeakerLabeling: false,
                HasRecoverableSource: true,
                IsSpeakerLabelingAvailable: entry.ExpectsSpeakerLabeling && IsSpeakerLabelingAvailableForAsap(),
                IsFailureRecoverable: false)).StatusText;

        return new RushedProcessingQueueState(
            rushRequest.ManifestPath,
            title ?? "Queued meeting",
            rushRequest.Behavior,
            rushRequest.RequestedAtUtc,
            lifecycle);
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

    private string? TrySwitchReservedManifestToRush(string manifestPath)
    {
        lock (_processSyncRoot)
        {
            var rushRequest = _config.Current.RushProcessingRequest;
            if (rushRequest is null ||
                string.Equals(rushRequest.ManifestPath, manifestPath, StringComparison.Ordinal) ||
                _reservedManifestPaths.Contains(rushRequest.ManifestPath))
            {
                return null;
            }

            if (_queuedManifestEntries.Any(entry => string.Equals(entry.ManifestPath, rushRequest.ManifestPath, StringComparison.Ordinal)))
            {
                _reservedManifestPaths.Remove(manifestPath);
                _reservedManifestPaths.Add(rushRequest.ManifestPath);
                return rushRequest.ManifestPath;
            }

            return null;
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
            _activeWorkersByManifestPath[manifestPath] = process;
            if (_currentWorker is null)
            {
                _currentWorker = process;
                _currentManifestPath = manifestPath;
            }

            StartCurrentManifestMonitorLocked(manifestPath);
        }
    }

    private async Task ClearCurrentWorkerAsync(IWorkerProcess process)
    {
        ProcessingQueueStatusSnapshot? snapshotToPublish = null;
        string? manifestPath = null;
        lock (_processSyncRoot)
        {
            manifestPath = _activeWorkersByManifestPath
                .FirstOrDefault(pair => ReferenceEquals(pair.Value, process))
                .Key;
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                _activeWorkersByManifestPath.Remove(manifestPath);
                _activeItemStatesByManifestPath.Remove(manifestPath);
                _reservedStagedWorkByManifestPath.Remove(manifestPath);
                _activeStagedWorkByManifestPath.Remove(manifestPath);
                _gpuDiarizationManifestPaths.Remove(manifestPath);
            }

            if (ReferenceEquals(_currentWorker, process) || string.Equals(_currentManifestPath, manifestPath, StringComparison.Ordinal))
            {
                var nextWorker = _activeWorkersByManifestPath.FirstOrDefault();
                _currentManifestPath = string.IsNullOrWhiteSpace(nextWorker.Key) ? null : nextWorker.Key;
                _currentWorker = string.IsNullOrWhiteSpace(nextWorker.Key) ? null : nextWorker.Value;
                _currentItemState = _currentManifestPath is not null &&
                    _activeItemStatesByManifestPath.TryGetValue(_currentManifestPath, out var activeItem)
                        ? activeItem
                        : null;
                snapshotToPublish = UpdateStatusSnapshotLocked(DateTimeOffset.UtcNow);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            await StopCurrentManifestMonitorAsync(manifestPath);
        }

        PublishStatusSnapshot(snapshotToPublish);
    }

    public void Dispose()
    {
        _resourceCapacityMonitor.Dispose();
        _gpuCapacityMonitor.Dispose();
        if (!_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
        }
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
    WorkerLaunchConfigSnapshot LaunchConfig,
    SessionProcessingWorkReceipt? Receipt);

internal sealed record WorkerRecoveryConfig(string ConfigPath, AppConfig Config);

internal enum ProcessingWorkPriority
{
    Normal = 0,
    Cleanup = 1,
}

internal sealed record ProcessingWorkCompletion(
    string ManifestPath,
    ProcessingWorkPriority Priority,
    bool Succeeded,
    string? Detail);

internal sealed record QueuedManifestStatusEntry(
    string ManifestPath,
    string Title,
    MeetingPlatform? Platform,
    TimeSpan? RecordingDuration,
    bool ExpectsSpeakerLabeling,
    ProcessingStageStatus TranscriptionStatus,
    ProcessingStageStatus DiarizationStatus,
    ProcessingStageStatus PublishStatus,
    bool WasPreempted = false,
    ProcessingWorkPriority Priority = ProcessingWorkPriority.Normal,
    StagedBacklogWorkItem? StagedWork = null)
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

internal sealed record ManifestMonitorState(CancellationTokenSource Cancellation, Task Task);

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
