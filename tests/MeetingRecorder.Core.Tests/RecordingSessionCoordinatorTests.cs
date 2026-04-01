using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task SetMicrophoneCaptureEnabledAsync_Starts_Capture_And_Persists_An_Open_Segment_When_Enabled_During_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-live-mic-enable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, initialConfig, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn());

            var activeSession = await CreateActiveSessionAsync(
                root,
                initialConfig,
                manifestStore,
                pathBuilder,
                logger,
                microphoneRecorder: null,
                microphoneSegmentStartedAtUtc: null);

            SetActiveSession(coordinator, activeSession);

            var changed = await coordinator.SetMicrophoneCaptureEnabledAsync(true);
            var reloadedManifest = await manifestStore.LoadAsync(activeSession.ManifestPath);

            Assert.True(changed);
            Assert.NotNull(coordinator.ActiveSession);
            Assert.NotNull(coordinator.ActiveSession!.MicrophoneRecorder);
            Assert.True(coordinator.ActiveSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue);
            Assert.Single(reloadedManifest.MicrophoneCaptureSegments);
            Assert.Null(reloadedManifest.MicrophoneCaptureSegments[0].EndedAtUtc);
            Assert.NotEmpty(reloadedManifest.MicrophoneCaptureSegments[0].ChunkPaths);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SetMicrophoneCaptureEnabledAsync_Stops_Capture_And_Persists_A_Closed_Segment_When_Disabled_During_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-live-mic-disable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, initialConfig, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn());

            var microphoneRecorder = CreateRecorder(root, "microphone", logger);
            microphoneRecorder.Start();

            var activeSession = await CreateActiveSessionAsync(
                root,
                initialConfig,
                manifestStore,
                pathBuilder,
                logger,
                microphoneRecorder,
                DateTimeOffset.UtcNow.AddSeconds(-10));

            SetActiveSession(coordinator, activeSession);

            var changed = await coordinator.SetMicrophoneCaptureEnabledAsync(false);
            var reloadedManifest = await manifestStore.LoadAsync(activeSession.ManifestPath);

            Assert.True(changed);
            Assert.NotNull(coordinator.ActiveSession);
            Assert.Null(coordinator.ActiveSession!.MicrophoneRecorder);
            Assert.Null(coordinator.ActiveSession.ActiveMicrophoneSegmentStartedAtUtc);
            Assert.Single(reloadedManifest.MicrophoneCaptureSegments);
            Assert.NotNull(reloadedManifest.MicrophoneCaptureSegments[0].EndedAtUtc);
            Assert.NotEmpty(reloadedManifest.MicrophoneCaptureSegments[0].ChunkPaths);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StopAsync_Closes_Any_Open_Microphone_Segment_And_Persists_All_Segment_Chunk_Paths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-live-mic-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, initialConfig, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn());

            var microphoneRecorder = CreateRecorder(root, "microphone", logger);
            microphoneRecorder.Start();
            var completedSegment = new MicrophoneCaptureSegment(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(-4),
                [Path.Combine(root, "raw", "microphone-archive", "microphone-completed-chunk-0001.wav")]);

            Directory.CreateDirectory(Path.GetDirectoryName(completedSegment.ChunkPaths[0])!);
            File.WriteAllText(completedSegment.ChunkPaths[0], "legacy");

            var activeSession = await CreateActiveSessionAsync(
                root,
                initialConfig,
                manifestStore,
                pathBuilder,
                logger,
                microphoneRecorder,
                DateTimeOffset.UtcNow.AddSeconds(-20),
                [completedSegment]);

            SetActiveSession(coordinator, activeSession);

            var manifestPath = await coordinator.StopAsync("test stop");
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath!);

            Assert.Equal(SessionState.Queued, reloadedManifest.State);
            Assert.Equal(2, reloadedManifest.MicrophoneCaptureSegments.Count);
            Assert.All(reloadedManifest.MicrophoneCaptureSegments, segment => Assert.NotEmpty(segment.ChunkPaths));
            Assert.Equal(
                reloadedManifest.MicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths),
                reloadedManifest.MicrophoneChunkPaths);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReclassifyActiveSessionAsync_Persists_Title_Platform_And_Evidence_Even_When_The_Platform_Is_Unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-reclassify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var configPath = Path.Combine(root, "config", "appsettings.json");
            var documentsRoot = Path.Combine(root, "documents");
            var configStore = new AppConfigStore(configPath, documentsRoot);
            var initialConfig = await configStore.LoadOrCreateAsync();
            var liveConfig = new LiveAppConfig(configStore, initialConfig);
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger);

            var initialEvidence = new[]
            {
                new DetectionSignal("window-title", "Henry call", 0.85d, DateTimeOffset.UtcNow),
                new DetectionSignal("audio-activity", "Speakers; peak=0.192; status=active", 0.15d, DateTimeOffset.UtcNow),
            };
            var createdManifest = await manifestStore.CreateAsync(
                initialConfig.WorkDir,
                MeetingPlatform.Teams,
                "Henry call",
                initialEvidence);
            var activeManifest = createdManifest with
            {
                State = SessionState.Recording,
            };
            var manifestPath = Path.Combine(pathBuilder.BuildSessionRoot(initialConfig.WorkDir, createdManifest.SessionId), "manifest.json");
            await manifestStore.SaveAsync(activeManifest, manifestPath);

            var activeSession = new ActiveRecordingSession
            {
                Manifest = activeManifest,
                ManifestPath = manifestPath,
                LoopbackRecorder = CreateRecorder(root, "loopback", logger),
                MicrophoneRecorder = CreateRecorder(root, "microphone", logger),
                AutoStarted = true,
            };

            typeof(RecordingSessionCoordinator)
                .GetProperty(nameof(RecordingSessionCoordinator.ActiveSession), BindingFlags.Instance | BindingFlags.Public)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(coordinator, [activeSession]);

            var updatedEvidence = new[]
            {
                new DetectionSignal("window-title", "[Int] Global Foundries Connect | Microsoft Teams", 0.85d, DateTimeOffset.UtcNow),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, DateTimeOffset.UtcNow),
                new DetectionSignal("audio-activity", "Speakers; peak=0.237; status=active", 0.2d, DateTimeOffset.UtcNow),
            };
            var detectedAudioSource = new DetectedAudioSource(
                "Microsoft Teams",
                "[Int] Global Foundries Connect | Microsoft Teams",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.Medium,
                DateTimeOffset.UtcNow);

            var reclassified = await coordinator.ReclassifyActiveSessionAsync(
                MeetingPlatform.Teams,
                "[Int] Global Foundries Connect",
                updatedEvidence,
                detectedAudioSource);
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.True(reclassified);
            Assert.NotNull(coordinator.ActiveSession);
            Assert.Equal(MeetingPlatform.Teams, coordinator.ActiveSession!.Manifest.Platform);
            Assert.Equal("[Int] Global Foundries Connect", coordinator.ActiveSession.Manifest.DetectedTitle);
            Assert.Equal(MeetingPlatform.Teams, reloadedManifest.Platform);
            Assert.Equal("[Int] Global Foundries Connect", reloadedManifest.DetectedTitle);
            Assert.Equal(updatedEvidence, reloadedManifest.DetectionEvidence);
            Assert.Equal(detectedAudioSource, reloadedManifest.DetectedAudioSource);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }

    private static ChunkedWaveRecorder CreateRecorder(string root, string prefix, FileLogWriter logger)
    {
        return new ChunkedWaveRecorder(
            static () => new StubWaveIn(),
            Path.Combine(root, "raw", prefix),
            prefix,
            TimeSpan.FromSeconds(30),
            logger);
    }

    private static async Task<(LiveAppConfig LiveConfig, AppConfig InitialConfig, SessionManifestStore ManifestStore, ArtifactPathBuilder PathBuilder, FileLogWriter Logger)> CreateCoordinatorDependenciesAsync(string root)
    {
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var documentsRoot = Path.Combine(root, "documents");
        var configStore = new AppConfigStore(configPath, documentsRoot);
        var initialConfig = await configStore.LoadOrCreateAsync();
        var liveConfig = new LiveAppConfig(configStore, initialConfig);
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        return (liveConfig, initialConfig, manifestStore, pathBuilder, logger);
    }

    private static async Task<ActiveRecordingSession> CreateActiveSessionAsync(
        string root,
        AppConfig initialConfig,
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        FileLogWriter logger,
        ChunkedWaveRecorder? microphoneRecorder,
        DateTimeOffset? microphoneSegmentStartedAtUtc,
        IReadOnlyList<MicrophoneCaptureSegment>? completedMicrophoneCaptureSegments = null)
    {
        var createdManifest = await manifestStore.CreateAsync(
            initialConfig.WorkDir,
            MeetingPlatform.Teams,
            "Active Session",
            Array.Empty<DetectionSignal>());
        var activeManifest = createdManifest with
        {
            State = SessionState.Recording,
        };
        var manifestPath = Path.Combine(pathBuilder.BuildSessionRoot(initialConfig.WorkDir, createdManifest.SessionId), "manifest.json");
        await manifestStore.SaveAsync(activeManifest, manifestPath);

        return new ActiveRecordingSession
        {
            Manifest = activeManifest,
            ManifestPath = manifestPath,
            LoopbackRecorder = CreateRecorder(root, "loopback", logger),
            MicrophoneRecorder = microphoneRecorder,
            ActiveMicrophoneSegmentStartedAtUtc = microphoneSegmentStartedAtUtc,
            CompletedMicrophoneCaptureSegments = completedMicrophoneCaptureSegments?.ToList() ?? [],
            AutoStarted = true,
        };
    }

    private static void SetActiveSession(RecordingSessionCoordinator coordinator, ActiveRecordingSession activeSession)
    {
        typeof(RecordingSessionCoordinator)
            .GetProperty(nameof(RecordingSessionCoordinator.ActiveSession), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(coordinator, [activeSession]);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test files.
        }
    }

    private sealed class StubCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        public CalendarMeetingDetailsCandidate? TryGetMeetingTitle(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            return null;
        }
    }

    private sealed class StubWaveIn : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

#pragma warning disable CS0067
        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;
#pragma warning restore CS0067

        public void StartRecording()
        {
        }

        public void StopRecording()
        {
        }

        public void Dispose()
        {
        }
    }
}
