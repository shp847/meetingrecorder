using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.Text;

namespace MeetingRecorder.App.Services;

internal sealed class ProcessingQueueService
{
    private readonly LiveAppConfig _config;
    private readonly SessionManifestStore _manifestStore;
    private readonly FileLogWriter _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _processSyncRoot = new();
    private Process? _currentWorker;
    private string? _currentManifestPath;

    public ProcessingQueueService(LiveAppConfig config, SessionManifestStore manifestStore, FileLogWriter logger)
    {
        _config = config;
        _manifestStore = manifestStore;
        _logger = logger;
    }

    public async Task ResumePendingSessionsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _manifestStore.FindPendingManifestPathsAsync(_config.Current.WorkDir, cancellationToken);
        foreach (var manifestPath in pending)
        {
            await EnqueueAsync(manifestPath, cancellationToken);
        }
    }

    public async Task EnqueueAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await _gate.WaitAsync(linkedCts.Token);
        try
        {
            if (_shutdownCts.IsCancellationRequested)
            {
                _logger.Log($"Skipping processing enqueue for '{manifestPath}' because shutdown is in progress.");
                return;
            }

            var launch = WorkerLocator.Resolve();
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
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the processing worker.");
            SetCurrentWorker(process, manifestPath);

            try
            {
                var standardOutput = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                var standardError = await process.StandardError.ReadToEndAsync(linkedCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);

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
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _shutdownCts.Cancel();

        Process? worker;
        string? manifestPath;
        lock (_processSyncRoot)
        {
            worker = _currentWorker;
            manifestPath = _currentManifestPath;
        }

        if (worker is null)
        {
            return;
        }

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

    private void SetCurrentWorker(Process process, string manifestPath)
    {
        lock (_processSyncRoot)
        {
            _currentWorker = process;
            _currentManifestPath = manifestPath;
        }
    }

    private void ClearCurrentWorker(Process process)
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
