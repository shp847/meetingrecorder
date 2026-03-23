using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace MeetingRecorder.App.Services;

internal sealed class ProcessingQueueService
{
    private readonly LiveAppConfig _config;
    private readonly SessionManifestStore _manifestStore;
    private readonly IMeetingMetadataEnricher _meetingMetadataEnricher;
    private readonly FileLogWriter _logger;
    private readonly Func<WorkerLaunch> _workerLaunchResolver;
    private readonly IWorkerProcessFactory _workerProcessFactory;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Channel<string> _pendingManifestPaths = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly object _processSyncRoot = new();
    private readonly Task _drainTask;
    private IWorkerProcess? _currentWorker;
    private string? _currentManifestPath;

    public ProcessingQueueService(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        FileLogWriter logger,
        IMeetingMetadataEnricher? meetingMetadataEnricher = null,
        Func<WorkerLaunch>? workerLaunchResolver = null,
        IWorkerProcessFactory? workerProcessFactory = null)
    {
        _config = config;
        _manifestStore = manifestStore;
        _meetingMetadataEnricher = meetingMetadataEnricher ?? new PassthroughMeetingMetadataEnricher();
        _logger = logger;
        _workerLaunchResolver = workerLaunchResolver ?? WorkerLocator.Resolve;
        _workerProcessFactory = workerProcessFactory ?? new SystemWorkerProcessFactory();
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
        var pending = await _manifestStore.FindPendingManifestPathsAsync(_config.Current.WorkDir, cancellationToken);
        foreach (var manifestPath in pending)
        {
            await EnqueueAsync(manifestPath, cancellationToken);
        }
    }

    public Task EnqueueAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_shutdownCts.IsCancellationRequested)
        {
            _logger.Log($"Skipping processing enqueue for '{manifestPath}' because shutdown is in progress.");
            return Task.CompletedTask;
        }

        if (!_pendingManifestPaths.Writer.TryWrite(manifestPath))
        {
            _logger.Log($"Skipping processing enqueue for '{manifestPath}' because the queue is no longer accepting work.");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _shutdownCts.Cancel();
        _pendingManifestPaths.Writer.TryComplete();

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
                    worker.Kill(entireProcessTree: true);
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
            while (await _pendingManifestPaths.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_pendingManifestPaths.Reader.TryRead(out var manifestPath))
                {
                    await ProcessManifestAsync(manifestPath, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await TryEnrichManifestAsync(manifestPath, cancellationToken);
        var launch = _workerLaunchResolver();
        var arguments = $"{launch.ArgumentPrefix} --manifest \"{manifestPath}\" --config \"{AppDataPaths.GetConfigPath()}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _logger.Log($"Launching worker for '{manifestPath}'. FileName='{startInfo.FileName}'. Arguments='{startInfo.Arguments}'.");
        var process = _workerProcessFactory.Start(startInfo);
        SetCurrentWorker(process, manifestPath);

        try
        {
            var standardOutputTask = process.ReadStandardOutputToEndAsync(cancellationToken);
            var standardErrorTask = process.ReadStandardErrorToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);

            var standardOutput = standardOutputTask.Result;
            var standardError = standardErrorTask.Result;

            if (process.ExitCode != 0)
            {
                var sessionLogPath = Path.Combine(
                    Path.GetDirectoryName(manifestPath) ?? _config.Current.WorkDir,
                    "logs",
                    "processing.log");
                _logger.Log($"Worker failed for '{manifestPath}' with exit code {process.ExitCode}: {standardError} See '{sessionLogPath}' for per-session diagnostics.");
                return;
            }

            _logger.Log($"Worker completed for '{manifestPath}': {standardOutput.Trim()}");
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            _logger.Log($"Worker canceled during shutdown for '{manifestPath}'.");
        }
        finally
        {
            ClearCurrentWorker(process);
            process.Dispose();
        }
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

    private void SetCurrentWorker(IWorkerProcess process, string manifestPath)
    {
        lock (_processSyncRoot)
        {
            _currentWorker = process;
            _currentManifestPath = manifestPath;
        }
    }

    private void ClearCurrentWorker(IWorkerProcess process)
    {
        lock (_processSyncRoot)
        {
            if (ReferenceEquals(_currentWorker, process))
            {
                _currentWorker = null;
                _currentManifestPath = null;
            }
        }
    }
}

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

    public void Dispose()
    {
        _process.Dispose();
    }
}
