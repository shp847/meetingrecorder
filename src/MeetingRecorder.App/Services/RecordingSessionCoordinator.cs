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
    private readonly Func<IWaveIn> _microphoneCaptureFactory;

    public RecordingSessionCoordinator(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        FileLogWriter logger,
        Func<IWaveIn>? microphoneCaptureFactory = null)
    {
        _config = config;
        _manifestStore = manifestStore;
        _pathBuilder = pathBuilder;
        _logger = logger;
        _microphoneCaptureFactory = microphoneCaptureFactory ?? CreateMicrophoneCapture;
    }

    public ActiveRecordingSession? ActiveSession { get; private set; }

    public bool IsRecording => ActiveSession is not null;

    public async Task StartAsync(
        MeetingPlatform platform,
        string title,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        bool autoStarted,
        DetectedAudioSource? detectedAudioSource = null,
        CancellationToken cancellationToken = default)
    {
        if (ActiveSession is not null)
        {
            throw new InvalidOperationException("A recording session is already active.");
        }

        var currentConfig = _config.Current;
        var initialManifest = await _manifestStore.CreateAsync(currentConfig.WorkDir, platform, title, detectionEvidence, cancellationToken);
        if (detectedAudioSource is not null)
        {
            initialManifest = initialManifest with
            {
                DetectedAudioSource = detectedAudioSource,
            };
        }
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
        DateTimeOffset? activeMicrophoneSegmentStartedAtUtc = null;
        if (currentConfig.MicCaptureEnabled)
        {
            microphoneRecorder = new ChunkedWaveRecorder(
                _microphoneCaptureFactory,
                rawDir,
                "microphone",
                TimeSpan.FromSeconds(30),
                _logger);
            activeMicrophoneSegmentStartedAtUtc = initialManifest.StartedAtUtc;
        }

        try
        {
            loopbackRecorder.Start();
            microphoneRecorder?.Start();

            var manifest = initialManifest with
            {
                State = SessionState.Recording,
                MicrophoneCaptureSegments = BuildMicrophoneCaptureSegments(
                    completedSegments: [],
                    microphoneRecorder,
                    activeMicrophoneSegmentStartedAtUtc),
            };
            await _manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

            ActiveSession = new ActiveRecordingSession
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                LoopbackRecorder = loopbackRecorder,
                MicrophoneRecorder = microphoneRecorder,
                ActiveMicrophoneSegmentStartedAtUtc = activeMicrophoneSegmentStartedAtUtc,
                AutoStarted = autoStarted,
                MeetingLifecycleManaged = autoStarted,
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
        var finalizedMicrophoneCaptureSegments = FinalizeMicrophoneCaptureSegments(active, DateTimeOffset.UtcNow);

        var finalized = active.Manifest with
        {
            State = SessionState.Queued,
            EndedAtUtc = DateTimeOffset.UtcNow,
            RawChunkPaths = active.LoopbackRecorder.ChunkPaths.ToArray(),
            MicrophoneCaptureSegments = finalizedMicrophoneCaptureSegments,
            MicrophoneChunkPaths = finalizedMicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
        };

        await _manifestStore.SaveAsync(finalized, active.ManifestPath, cancellationToken);
        var loopbackDiagnostics = DescribeChunkSet(active.LoopbackRecorder.ChunkPaths, active.LoopbackRecorder.TotalBytesRecorded, "loopback");
        var microphoneDiagnostics = finalizedMicrophoneCaptureSegments.Count == 0
            ? "microphone=disabled"
            : DescribeChunkSet(finalized.MicrophoneChunkPaths, finalized.MicrophoneChunkPaths.Sum(GetAudioFileLength), "microphone");
        _logger.Log($"Recording stopped for session '{finalized.SessionId}'. Reason: {reason}. {loopbackDiagnostics}; {microphoneDiagnostics}");
        return active.ManifestPath;
    }

    public async Task<bool> SetMicrophoneCaptureEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        if (enabled)
        {
            if (ActiveSession.MicrophoneRecorder is not null)
            {
                return false;
            }

            var rawDir = Path.Combine(
                Path.GetDirectoryName(ActiveSession.ManifestPath)
                    ?? throw new InvalidOperationException("Active manifest path must include a session directory."),
                "raw");
            var microphoneRecorder = new ChunkedWaveRecorder(
                _microphoneCaptureFactory,
                rawDir,
                "microphone",
                TimeSpan.FromSeconds(30),
                _logger);
            microphoneRecorder.Start();
            ActiveSession.MicrophoneRecorder = microphoneRecorder;
            ActiveSession.ActiveMicrophoneSegmentStartedAtUtc = DateTimeOffset.UtcNow;
            await PersistActiveMicrophoneCaptureStateAsync(ActiveSession, cancellationToken);
            _logger.Log($"Microphone capture enabled for active session '{ActiveSession.Manifest.SessionId}' from now on.");
            return true;
        }

        if (ActiveSession.MicrophoneRecorder is null || !ActiveSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue)
        {
            return false;
        }

        var completedSegment = CompleteCurrentMicrophoneSegment(ActiveSession, DateTimeOffset.UtcNow);
        ActiveSession.CompletedMicrophoneCaptureSegments.Add(completedSegment);
        await PersistActiveMicrophoneCaptureStateAsync(ActiveSession, cancellationToken);
        _logger.Log($"Microphone capture disabled for active session '{ActiveSession.Manifest.SessionId}' from now on.");
        return true;
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

    public async Task<bool> UpdateActiveSessionMetadataAsync(
        string title,
        string? projectName,
        IReadOnlyList<string> keyAttendees,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(title));
        }

        var normalizedProjectName = string.IsNullOrWhiteSpace(projectName)
            ? null
            : projectName.Trim();
        var normalizedKeyAttendees = MeetingMetadataNameMatcher.MergeNames(keyAttendees, Array.Empty<string>());
        var updatedManifest = ActiveSession.Manifest with
        {
            DetectedTitle = trimmedTitle,
            ProjectName = normalizedProjectName,
            KeyAttendees = normalizedKeyAttendees,
        };
        if (updatedManifest == ActiveSession.Manifest)
        {
            return false;
        }

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Updated active session '{updatedManifest.SessionId}' metadata. " +
            $"Title='{trimmedTitle}', Project='{normalizedProjectName ?? string.Empty}', KeyAttendees={normalizedKeyAttendees.Count}.");
        return true;
    }

    public async Task<bool> ReclassifyActiveSessionAsync(
        MeetingPlatform platform,
        string detectedTitle,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        DetectedAudioSource? detectedAudioSource = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null ||
            platform == MeetingPlatform.Unknown)
        {
            return false;
        }

        var trimmedDetectedTitle = detectedTitle.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDetectedTitle))
        {
            throw new ArgumentException("A detected meeting title is required.", nameof(detectedTitle));
        }

        var previousPlatform = ActiveSession.Manifest.Platform;
        var previousTitle = ActiveSession.Manifest.DetectedTitle;
        var updatedManifest = ActiveSession.Manifest with
        {
            Platform = platform,
            DetectedTitle = trimmedDetectedTitle,
            DetectionEvidence = detectionEvidence.ToArray(),
            DetectedAudioSource = detectedAudioSource ?? ActiveSession.Manifest.DetectedAudioSource,
        };
        if (updatedManifest == ActiveSession.Manifest)
        {
            return false;
        }

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Active session '{updatedManifest.SessionId}' reclassified from '{previousPlatform}'/'{previousTitle}' to '{platform}'/'{trimmedDetectedTitle}'.");
        return true;
    }

    public async Task<bool> MergeActiveSessionAttendeesAsync(
        string expectedSessionId,
        IReadOnlyList<MeetingAttendee> attendees,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null ||
            !string.Equals(ActiveSession.Manifest.SessionId, expectedSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (attendees.Count == 0)
        {
            return false;
        }

        var updatedManifest = await TeamsLiveAttendeeCaptureService.MergeAttendeesIntoManifestAsync(
            _manifestStore,
            ActiveSession.Manifest,
            ActiveSession.ManifestPath,
            attendees,
            cancellationToken);

        if (ReferenceEquals(updatedManifest, ActiveSession.Manifest))
        {
            return false;
        }

        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Updated active session '{updatedManifest.SessionId}' with {updatedManifest.Attendees.Count} persisted attendee(s) from live Teams capture.");
        return true;
    }

    private async Task PersistActiveMicrophoneCaptureStateAsync(
        ActiveRecordingSession activeSession,
        CancellationToken cancellationToken)
    {
        var microphoneCaptureSegments = BuildMicrophoneCaptureSegments(
            activeSession.CompletedMicrophoneCaptureSegments,
            activeSession.MicrophoneRecorder,
            activeSession.ActiveMicrophoneSegmentStartedAtUtc);
        var updatedManifest = activeSession.Manifest with
        {
            MicrophoneCaptureSegments = microphoneCaptureSegments,
            MicrophoneChunkPaths = microphoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
        };
        await _manifestStore.SaveAsync(updatedManifest, activeSession.ManifestPath, cancellationToken);
        activeSession.Manifest = updatedManifest;
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> FinalizeMicrophoneCaptureSegments(
        ActiveRecordingSession activeSession,
        DateTimeOffset endedAtUtc)
    {
        if (activeSession.MicrophoneRecorder is not null && activeSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue)
        {
            var completedSegment = CompleteCurrentMicrophoneSegment(activeSession, endedAtUtc);
            activeSession.CompletedMicrophoneCaptureSegments.Add(completedSegment);
        }

        return activeSession.CompletedMicrophoneCaptureSegments.ToArray();
    }

    private static MicrophoneCaptureSegment CompleteCurrentMicrophoneSegment(
        ActiveRecordingSession activeSession,
        DateTimeOffset endedAtUtc)
    {
        var microphoneRecorder = activeSession.MicrophoneRecorder
            ?? throw new InvalidOperationException("An active microphone recorder is required.");
        var startedAtUtc = activeSession.ActiveMicrophoneSegmentStartedAtUtc
            ?? throw new InvalidOperationException("An active microphone segment start time is required.");
        microphoneRecorder.Stop();
        activeSession.MicrophoneRecorder = null;
        activeSession.ActiveMicrophoneSegmentStartedAtUtc = null;
        return new MicrophoneCaptureSegment(
            startedAtUtc,
            endedAtUtc,
            microphoneRecorder.ChunkPaths.ToArray());
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> BuildMicrophoneCaptureSegments(
        IReadOnlyList<MicrophoneCaptureSegment> completedSegments,
        ChunkedWaveRecorder? activeMicrophoneRecorder,
        DateTimeOffset? activeMicrophoneSegmentStartedAtUtc)
    {
        var segments = completedSegments.ToList();
        if (activeMicrophoneRecorder is not null && activeMicrophoneSegmentStartedAtUtc.HasValue)
        {
            segments.Add(new MicrophoneCaptureSegment(
                activeMicrophoneSegmentStartedAtUtc.Value,
                null,
                activeMicrophoneRecorder.ChunkPaths.ToArray()));
        }

        return segments;
    }

    private static long GetAudioFileLength(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0L;
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
