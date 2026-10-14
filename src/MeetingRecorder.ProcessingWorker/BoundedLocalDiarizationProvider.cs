using System.Diagnostics;
using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.ProcessingWorker;

internal sealed record LocalDiarizationHelperRequest(
    string AudioPath,
    IReadOnlyList<TranscriptSegment> TranscriptSegments,
    string ProgressPath);

/// <summary>
/// Hosts the non-cancellable Sherpa call in a child process. A timeout therefore releases
/// native resources while the parent worker can safely continue to publish the transcript.
/// </summary>
internal sealed class BoundedLocalDiarizationProvider : IDiarizationProvider, IDiarizationProgressSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _configPath;
    private readonly FileLogWriter _logger;

    public BoundedLocalDiarizationProvider(string configPath, FileLogWriter logger)
    {
        _configPath = configPath;
        _logger = logger;
    }

    public event Action<DiarizationProgress>? ProgressChanged;

    public async Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken)
    {
        var jobDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization", "jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        var requestPath = Path.Combine(jobDirectory, "request.json");
        var resultPath = Path.Combine(jobDirectory, "result.json");
        var progressPath = Path.Combine(jobDirectory, "progress.json");
        var request = new LocalDiarizationHelperRequest(audioPath, transcriptSegments, progressPath);
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), cancellationToken);

        using var process = StartHelper(requestPath, resultPath);
        var helperErrorTask = process.StandardError.ReadToEndAsync();
        using var pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pollingTask = PollProgressAsync(progressPath, process, pollingCts.Token);
        Publish("Launching helper", null, 0, null, null, null, "Starting bounded native speaker labeling.");

        try
        {
            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = Task.Delay(BackgroundProcessingPolicy.DiarizationTimeout, CancellationToken.None);
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completed = await Task.WhenAny(exitTask, timeoutTask, cancellationTask);
            if (completed == cancellationTask)
            {
                TryTerminate(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (completed == timeoutTask)
            {
                TryTerminate(process);
                await exitTask;
                var message = $"Speaker labeling timed out after the {BackgroundProcessingPolicy.DiarizationTimeout.TotalMinutes:0}-minute optional processing limit and needs manual review.";
                Publish("Timed out", null, 0, null, null, null, message);
                _logger.Log(message);
                return new DiarizationResult(transcriptSegments, false, message);
            }

            await exitTask;
            var helperError = await helperErrorTask;
            if (process.ExitCode != 0 || !File.Exists(resultPath))
            {
                throw new InvalidOperationException($"Local speaker-labeling helper failed ({ClassifyHelperFailure(helperError)}).");
            }

            var serializedResult = await File.ReadAllTextAsync(resultPath, cancellationToken);
            var result = JsonSerializer.Deserialize<DiarizationResult>(serializedResult, JsonOptions)
                ?? throw new InvalidOperationException("Local speaker-labeling helper returned an empty result.");
            return result;
        }
        finally
        {
            pollingCts.Cancel();
            try
            {
                await pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected while the helper is being cleaned up.
            }

            TryTerminate(process);
            TryDeleteDirectory(jobDirectory);
        }
    }

    private Process StartHelper(string requestPath, string resultPath)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Unable to locate the processing worker executable for bounded speaker labeling.");
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        ConfigureHelperHostArguments(
            startInfo,
            executablePath,
            System.Reflection.Assembly.GetEntryAssembly()?.Location);
        startInfo.ArgumentList.Add("--run-local-diarization");
        startInfo.ArgumentList.Add("--diarization-request");
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add("--diarization-result");
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(_configPath);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the local speaker-labeling helper.");
    }

    internal static string ClassifyHelperFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "no_diagnostic";
        }

        if (error.Contains("phase=audio_preparation", StringComparison.Ordinal))
        {
            return "audio_preparation_" + GetSafeHelperExceptionKind(error);
        }

        if (error.Contains("phase=request_read", StringComparison.Ordinal) ||
            error.Contains("phase=request_deserialize", StringComparison.Ordinal) ||
            error.Contains("phase=config", StringComparison.Ordinal) ||
            error.Contains("phase=provider", StringComparison.Ordinal))
        {
            return "helper_startup_" + GetSafeHelperExceptionKind(error);
        }

        if (error.Contains("phase=provider_selection", StringComparison.Ordinal) ||
            error.Contains("phase=sample_loading", StringComparison.Ordinal) ||
            error.Contains("phase=samples_loaded", StringComparison.Ordinal) ||
            error.Contains("phase=native_inference", StringComparison.Ordinal))
        {
            return "native_inference_" + GetSafeHelperExceptionKind(error);
        }

        if (error.Contains("phase=completed", StringComparison.Ordinal))
        {
            return "runtime_status_" + GetSafeHelperExceptionKind(error);
        }

        if (error.Contains("model", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("asset", StringComparison.OrdinalIgnoreCase))
        {
            return "model_or_asset";
        }

        if (error.Contains("sherpa", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("native", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("dll", StringComparison.OrdinalIgnoreCase))
        {
            return "native_runtime";
        }

        if (error.Contains("file", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("directory", StringComparison.OrdinalIgnoreCase))
        {
            return "input_or_file";
        }

        return "unknown";
    }

    private static string GetSafeHelperExceptionKind(string error)
    {
        foreach (var kind in new[] { "file_not_found", "directory_not_found", "access_denied", "not_supported", "audio_prepare_output", "audio_inspect", "audio_normalize_input", "audio_open_decoder", "audio_convert", "invalid_operation", "invalid_data", "invalid_argument", "format", "type_load", "naudio", "com", "external", "io", "cancelled", "other" })
        {
            if (error.Contains($"exception={kind}", StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return "unknown";
    }

    internal static void ConfigureHelperHostArguments(
        ProcessStartInfo startInfo,
        string executablePath,
        string? entryAssemblyPath)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        // Apphosts relaunch themselves directly.  When a fixture or a
        // development host starts the worker as `dotnet Worker.dll`, though,
        // the helper must receive that entry assembly before its worker args.
        if (!string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entryAssemblyPath) ||
            !string.Equals(Path.GetExtension(entryAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unable to locate the processing worker assembly for bounded speaker labeling.");
        }

        startInfo.ArgumentList.Add(entryAssemblyPath);
    }

    private async Task PollProgressAsync(string progressPath, Process process, CancellationToken cancellationToken)
    {
        DiarizationProgress? lastProgress = null;
        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            try
            {
                if (File.Exists(progressPath))
                {
                    var parsed = JsonSerializer.Deserialize<DiarizationProgress>(await File.ReadAllTextAsync(progressPath, cancellationToken), JsonOptions);
                    if (parsed is not null && parsed != lastProgress)
                    {
                        lastProgress = parsed;
                        Publish(parsed.Phase, parsed.ExecutionProvider, parsed.Attempt, parsed.AttemptCount, parsed.InputDuration, parsed.InputBytes, parsed.Detail, parsed.WorkingSetBytes);
                    }
                }

                Publish(
                    lastProgress?.Phase ?? "Native inference",
                    lastProgress?.ExecutionProvider,
                    lastProgress?.Attempt ?? 0,
                    lastProgress?.AttemptCount,
                    lastProgress?.InputDuration,
                    lastProgress?.InputBytes,
                    lastProgress?.Detail ?? "Native speaker labeling is still running.",
                    process.WorkingSet64);
            }
            catch (IOException)
            {
                // The helper can replace progress while it is being read; the next heartbeat retries.
            }
            catch (JsonException)
            {
                // Ignore a partially written progress snapshot and wait for the next heartbeat.
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private void Publish(
        string phase,
        string? provider,
        int attempt,
        int? attemptCount,
        TimeSpan? inputDuration,
        long? inputBytes,
        string? detail,
        long? workingSetBytes = null)
    {
        ProgressChanged?.Invoke(new DiarizationProgress(
            phase,
            provider,
            attempt,
            attemptCount,
            inputDuration,
            inputBytes,
            workingSetBytes,
            detail,
            DateTimeOffset.UtcNow));
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and kill.
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup will retry on the next processing pass.
        }
        catch (UnauthorizedAccessException)
        {
            // Temp cleanup will retry on the next processing pass.
        }
    }
}
