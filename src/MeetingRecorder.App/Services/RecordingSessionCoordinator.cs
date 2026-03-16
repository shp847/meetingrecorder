using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.App.Services;

internal sealed class RecordingSessionCoordinator
{
    private readonly LiveAppConfig _config;
    private readonly SessionManifestStore _manifestStore;
    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly FileLogWriter _logger;

    public RecordingSessionCoordinator(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        FileLogWriter logger)
    {
        _config = config;
        _manifestStore = manifestStore;
        _pathBuilder = pathBuilder;
        _logger = logger;
    }

    public ActiveRecordingSession? ActiveSession { get; private set; }

    public bool IsRecording => ActiveSession is not null;

    public async Task StartAsync(
        MeetingPlatform platform,
        string title,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        bool autoStarted,
        CancellationToken cancellationToken = default)
    {
        if (ActiveSession is not null)
        {
            throw new InvalidOperationException("A recording session is already active.");
        }

        var currentConfig = _config.Current;
        var initialManifest = await _manifestStore.CreateAsync(currentConfig.WorkDir, platform, title, detectionEvidence, cancellationToken);
        var sessionRoot = _pathBuilder.BuildSessionRoot(currentConfig.WorkDir, initialManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var rawDir = Path.Combine(sessionRoot, "raw");

        var loopbackRecorder = new ChunkedWaveRecorder(
            () => new WasapiLoopbackCapture(),
            rawDir,
            "loopback",
            TimeSpan.FromSeconds(30),
            _logger);

        ChunkedWaveRecorder? microphoneRecorder = null;
        if (currentConfig.MicCaptureEnabled)
        {
            microphoneRecorder = new ChunkedWaveRecorder(
                CreateMicrophoneCapture,
                rawDir,
                "microphone",
                TimeSpan.FromSeconds(30),
                _logger);
        }

        try
        {
            loopbackRecorder.Start();
            microphoneRecorder?.Start();

            var manifest = initialManifest with
            {
                State = SessionState.Recording,
            };
            await _manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

            ActiveSession = new ActiveRecordingSession
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                LoopbackRecorder = loopbackRecorder,
                MicrophoneRecorder = microphoneRecorder,
                AutoStarted = autoStarted,
            };

            var evidence = detectionEvidence.Count == 0
                ? "none"
                : string.Join("; ", detectionEvidence.Select(signal => $"{signal.Source}='{signal.Value}' w={signal.Weight:0.00}"));
            _logger.Log($"Recording started for session '{manifest.SessionId}' ({platform}). Title='{title}'. AutoStarted={autoStarted}. DetectionEvidence={evidence}.");
        }
        catch
        {
            loopbackRecorder.Dispose();
            microphoneRecorder?.Dispose();
            throw;
        }
    }

    public async Task<string?> StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (ActiveSession is null)
        {
            return null;
        }

        var active = ActiveSession;
        ActiveSession = null;

        active.LoopbackRecorder.Stop();
        active.MicrophoneRecorder?.Stop();

        var finalized = active.Manifest with
        {
            State = SessionState.Queued,
            EndedAtUtc = DateTimeOffset.UtcNow,
            RawChunkPaths = active.LoopbackRecorder.ChunkPaths.ToArray(),
            MicrophoneChunkPaths = active.MicrophoneRecorder?.ChunkPaths.ToArray() ?? Array.Empty<string>(),
        };

        await _manifestStore.SaveAsync(finalized, active.ManifestPath, cancellationToken);
        var loopbackDiagnostics = DescribeChunkSet(active.LoopbackRecorder.ChunkPaths, active.LoopbackRecorder.TotalBytesRecorded, "loopback");
        var microphoneDiagnostics = active.MicrophoneRecorder is null
            ? "microphone=disabled"
            : DescribeChunkSet(active.MicrophoneRecorder.ChunkPaths, active.MicrophoneRecorder.TotalBytesRecorded, "microphone");
        _logger.Log($"Recording stopped for session '{finalized.SessionId}'. Reason: {reason}. {loopbackDiagnostics}; {microphoneDiagnostics}");
        return active.ManifestPath;
    }

    public async Task<bool> RenameActiveSessionAsync(string newTitle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        var trimmedTitle = newTitle.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(newTitle));
        }

        var updatedManifest = ActiveSession.Manifest with
        {
            DetectedTitle = trimmedTitle,
        };

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log($"Active session '{updatedManifest.SessionId}' renamed to '{trimmedTitle}'.");
        return true;
    }

    private static IWaveIn CreateMicrophoneCapture()
    {
        return new WaveInEvent
        {
            BufferMilliseconds = 250,
            WaveFormat = new WaveFormat(16000, 16, 1),
        };
    }

    private static string DescribeChunkSet(IReadOnlyList<string> chunkPaths, long audioBytesRecorded, string label)
    {
        var fileSizes = chunkPaths
            .Select(path => $"{Path.GetFileName(path)}:{(File.Exists(path) ? new FileInfo(path).Length : 0)}")
            .ToArray();
        var joinedSizes = fileSizes.Length == 0 ? "none" : string.Join(", ", fileSizes);
        return $"{label}ChunkCount={chunkPaths.Count}; {label}AudioBytes={audioBytesRecorded}; {label}Files=[{joinedSizes}]";
    }
}
