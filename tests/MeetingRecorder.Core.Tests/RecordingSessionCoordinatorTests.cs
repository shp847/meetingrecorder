using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task StartAsync_Persists_The_Initial_Loopback_Segment_And_Started_Timeline_Event()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-loopback-factory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var loopbackFactory = new SpyLoopbackCaptureFactory(
                new LoopbackCaptureEvaluation(
                    new LoopbackCaptureSelection(
                        Role.Multimedia,
                        "device-1",
                        "Laptop speakers",
                        0.20d,
                        true,
                        0,
                        0,
                        "Preferred multimedia render endpoint.",
                        false),
                    Multimedia: null,
                    Communications: null));
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn(),
                loopbackFactory);

            var detectedAudioSource = new DetectedAudioSource(
                "Google Meet",
                "Meet - abc-defg-hij - Work - Microsoft Edge",
                "Meet - abc-defg-hij",
                AudioSourceMatchKind.BrowserTab,
                AudioSourceConfidence.High,
                DateTimeOffset.UtcNow);

            await coordinator.StartAsync(
                MeetingPlatform.GoogleMeet,
                "Meet - abc-defg-hij",
                Array.Empty<DetectionSignal>(),
                autoStarted: true,
                detectedAudioSource);

            var activeSession = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession);
            var reloadedManifest = await manifestStore.LoadAsync(activeSession.ManifestPath);

            Assert.Equal(MeetingPlatform.GoogleMeet, loopbackFactory.LastPlatform);
            Assert.Equal(detectedAudioSource, loopbackFactory.LastDetectedAudioSource);
            Assert.Equal(liveConfig.Current.AutoDetectAudioPeakThreshold, loopbackFactory.LastThreshold, 3);
            Assert.Single(reloadedManifest.LoopbackCaptureSegments);
            Assert.Single(reloadedManifest.CaptureTimeline);
            Assert.Equal(CaptureTimelineEventKind.Started, reloadedManifest.CaptureTimeline[0].Kind);
            Assert.Equal("device-1", reloadedManifest.LoopbackCaptureSegments[0].EndpointDeviceId);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

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
                ActiveLoopbackSelection = new LoopbackCaptureSelection(
                    Role.Multimedia,
                    "device-1",
                    "Laptop speakers",
                    0.15d,
                    true,
                    0,
                    0,
                    "Preferred multimedia render endpoint.",
                    false),
                ActiveLoopbackSegmentStartedAtUtc = activeManifest.StartedAtUtc,
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
            ActiveLoopbackSelection = new LoopbackCaptureSelection(
                Role.Multimedia,
                "device-1",
                "Laptop speakers",
                0.15d,
                true,
                0,
                0,
                "Preferred multimedia render endpoint.",
                false),
            ActiveLoopbackSegmentStartedAtUtc = activeManifest.StartedAtUtc,
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

    [Fact]
    public async Task RefreshLoopbackCaptureAsync_Swaps_To_A_Stable_Better_Endpoint_And_Persists_Segments()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-loopback-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var firstEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Multimedia,
                    "device-1",
                    "Laptop speakers",
                    0.15d,
                    true,
                    0,
                    0,
                    "Preferred multimedia render endpoint.",
                    false),
                Multimedia: null,
                Communications: null);
            var secondEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Communications,
                    "device-2",
                    "USB headset",
                    0.35d,
                    true,
                    1,
                    1,
                    "Communications endpoint has supported meeting audio.",
                    false),
                Multimedia: null,
                Communications: null);
            var loopbackFactory = new SpyLoopbackCaptureFactory(firstEvaluation, secondEvaluation, secondEvaluation);
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn(),
                loopbackFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            var firstRefresh = await coordinator.RefreshLoopbackCaptureAsync(MeetingPlatform.Teams, detectedAudioSource: null);
            var secondRefresh = await coordinator.RefreshLoopbackCaptureAsync(MeetingPlatform.Teams, detectedAudioSource: null);
            var manifestPath = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession).ManifestPath;
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.False(firstRefresh.SwapPerformed);
            Assert.True(secondRefresh.SwapPerformed);
            Assert.Equal(2, reloadedManifest.LoopbackCaptureSegments.Count);
            Assert.Equal(
                reloadedManifest.LoopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths),
                reloadedManifest.RawChunkPaths);
            Assert.Contains(reloadedManifest.CaptureTimeline, entry => entry.Kind == CaptureTimelineEventKind.Swapped);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefreshLoopbackCaptureAsync_Allows_Immediate_Swap_When_The_Current_Endpoint_Is_Quiet()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-loopback-immediate-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var firstEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Multimedia,
                    "device-1",
                    "Laptop speakers",
                    0.10d,
                    true,
                    0,
                    0,
                    "Preferred multimedia render endpoint.",
                    false),
                new LoopbackCaptureProbeSnapshot(Role.Multimedia, "device-1", "Laptop speakers", 0d, false, Array.Empty<AudioSourceSessionSnapshot>()),
                new LoopbackCaptureProbeSnapshot(Role.Communications, "device-2", "USB headset", 0.40d, true,
                [
                    new AudioSourceSessionSnapshot(1, "ms-teams", 0.40d, true, false, false, "Microsoft Teams", "teams-session"),
                ]));
            var secondEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Communications,
                    "device-2",
                    "USB headset",
                    0.40d,
                    true,
                    1,
                    1,
                    "Communications endpoint has supported meeting audio.",
                    false),
                new LoopbackCaptureProbeSnapshot(Role.Multimedia, "device-1", "Laptop speakers", 0d, false, Array.Empty<AudioSourceSessionSnapshot>()),
                new LoopbackCaptureProbeSnapshot(Role.Communications, "device-2", "USB headset", 0.40d, true,
                [
                    new AudioSourceSessionSnapshot(1, "ms-teams", 0.40d, true, false, false, "Microsoft Teams", "teams-session"),
                ]));
            var loopbackFactory = new SpyLoopbackCaptureFactory(firstEvaluation, secondEvaluation);
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn(),
                loopbackFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            var refresh = await coordinator.RefreshLoopbackCaptureAsync(MeetingPlatform.Teams, detectedAudioSource: null);
            var manifestPath = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession).ManifestPath;
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.True(refresh.SwapPerformed);
            Assert.Equal(2, reloadedManifest.LoopbackCaptureSegments.Count);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefreshLoopbackCaptureAsync_Records_SwapFailure_And_Keeps_The_Current_Recorder_Active()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-loopback-swap-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var firstEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Multimedia,
                    "device-1",
                    "Laptop speakers",
                    0.10d,
                    true,
                    0,
                    0,
                    "Preferred multimedia render endpoint.",
                    false),
                Multimedia: null,
                Communications: null);
            var secondEvaluation = new LoopbackCaptureEvaluation(
                new LoopbackCaptureSelection(
                    Role.Communications,
                    "device-2",
                    "USB headset",
                    0.40d,
                    true,
                    1,
                    1,
                    "Communications endpoint has supported meeting audio.",
                    false),
                Multimedia: null,
                Communications: null);
            var loopbackFactory = new SpyLoopbackCaptureFactory(firstEvaluation, secondEvaluation, secondEvaluation)
            {
                FailingDeviceId = "device-2",
            };
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                static () => new StubWaveIn(),
                loopbackFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            await coordinator.RefreshLoopbackCaptureAsync(MeetingPlatform.Teams, detectedAudioSource: null);
            var refresh = await coordinator.RefreshLoopbackCaptureAsync(MeetingPlatform.Teams, detectedAudioSource: null);
            var activeSession = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession);
            var reloadedManifest = await manifestStore.LoadAsync(activeSession.ManifestPath);

            Assert.False(refresh.SwapPerformed);
            Assert.True(refresh.SwapFailed);
            Assert.Single(reloadedManifest.LoopbackCaptureSegments);
            Assert.Contains(reloadedManifest.CaptureTimeline, entry => entry.Kind == CaptureTimelineEventKind.SwapFailed);
            Assert.Equal("device-1", activeSession.ActiveLoopbackSelection.DeviceId);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefreshMicrophoneCaptureAsync_Swaps_To_A_Stable_Better_Input_And_Persists_Segments()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-microphone-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var microphoneFactory = new SpyMicrophoneCaptureFactory(
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Multimedia,
                        "mic-1",
                        "Laptop microphone",
                        0.05d,
                        true,
                        "Preferred default microphone.",
                        false),
                    Multimedia: null,
                    Communications: null),
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Communications,
                        "mic-2",
                        "USB headset microphone",
                        0.31d,
                        true,
                        "Communications microphone is active.",
                        false),
                    Multimedia: null,
                    Communications: null),
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Communications,
                        "mic-2",
                        "USB headset microphone",
                        0.31d,
                        true,
                        "Communications microphone is active.",
                        false),
                    Multimedia: null,
                    Communications: null));
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                loopbackCaptureFactory: new SpyLoopbackCaptureFactory(
                    new LoopbackCaptureEvaluation(
                        new LoopbackCaptureSelection(
                            Role.Multimedia,
                            "device-1",
                            "Laptop speakers",
                            0.15d,
                            true,
                            0,
                            0,
                            "Preferred multimedia render endpoint.",
                            false),
                        Multimedia: null,
                        Communications: null)),
                microphoneCaptureFactory: microphoneFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            var firstRefresh = await coordinator.RefreshMicrophoneCaptureAsync();
            var secondRefresh = await coordinator.RefreshMicrophoneCaptureAsync();
            var manifestPath = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession).ManifestPath;
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.False(firstRefresh.SwapPerformed);
            Assert.True(secondRefresh.SwapPerformed);
            Assert.Equal(2, reloadedManifest.MicrophoneCaptureSegments.Count);
            Assert.Equal(
                reloadedManifest.MicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths),
                reloadedManifest.MicrophoneChunkPaths);
            Assert.Contains(reloadedManifest.CaptureTimeline, entry => entry.Kind == CaptureTimelineEventKind.Swapped && entry.Summary.Contains("microphone", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefreshMicrophoneCaptureAsync_Allows_Immediate_Swap_When_The_Current_Input_Is_Inactive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-microphone-immediate-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var microphoneFactory = new SpyMicrophoneCaptureFactory(
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Multimedia,
                        "mic-1",
                        "Laptop microphone",
                        0.00d,
                        false,
                        "Preferred default microphone.",
                        false),
                    new MicrophoneCaptureProbeSnapshot(Role.Multimedia, "mic-1", "Laptop microphone", 0.00d, false),
                    new MicrophoneCaptureProbeSnapshot(Role.Communications, "mic-2", "USB headset microphone", 0.28d, true)),
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Communications,
                        "mic-2",
                        "USB headset microphone",
                        0.28d,
                        true,
                        "Communications microphone is active.",
                        false),
                    new MicrophoneCaptureProbeSnapshot(Role.Multimedia, "mic-1", "Laptop microphone", 0.00d, false),
                    new MicrophoneCaptureProbeSnapshot(Role.Communications, "mic-2", "USB headset microphone", 0.28d, true)));
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                loopbackCaptureFactory: new SpyLoopbackCaptureFactory(
                    new LoopbackCaptureEvaluation(
                        new LoopbackCaptureSelection(
                            Role.Multimedia,
                            "device-1",
                            "Laptop speakers",
                            0.15d,
                            true,
                            0,
                            0,
                            "Preferred multimedia render endpoint.",
                            false),
                        Multimedia: null,
                        Communications: null)),
                microphoneCaptureFactory: microphoneFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            var refresh = await coordinator.RefreshMicrophoneCaptureAsync();
            var manifestPath = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession).ManifestPath;
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.True(refresh.SwapPerformed);
            Assert.Equal(2, reloadedManifest.MicrophoneCaptureSegments.Count);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefreshMicrophoneCaptureAsync_Records_SwapFailure_And_Keeps_The_Current_Recorder_Active()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-microphone-swap-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var (liveConfig, _, manifestStore, pathBuilder, logger) = await CreateCoordinatorDependenciesAsync(root);
            var microphoneFactory = new SpyMicrophoneCaptureFactory(
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Multimedia,
                        "mic-1",
                        "Laptop microphone",
                        0.05d,
                        true,
                        "Preferred default microphone.",
                        false),
                    Multimedia: null,
                    Communications: null),
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Communications,
                        "mic-2",
                        "USB headset microphone",
                        0.31d,
                        true,
                        "Communications microphone is active.",
                        false),
                    Multimedia: null,
                    Communications: null),
                new MicrophoneCaptureEvaluation(
                    new MicrophoneCaptureSelection(
                        Role.Communications,
                        "mic-2",
                        "USB headset microphone",
                        0.31d,
                        true,
                        "Communications microphone is active.",
                        false),
                    Multimedia: null,
                    Communications: null))
            {
                FailingDeviceId = "mic-2",
            };
            var coordinator = new RecordingSessionCoordinator(
                liveConfig,
                manifestStore,
                pathBuilder,
                logger,
                loopbackCaptureFactory: new SpyLoopbackCaptureFactory(
                    new LoopbackCaptureEvaluation(
                        new LoopbackCaptureSelection(
                            Role.Multimedia,
                            "device-1",
                            "Laptop speakers",
                            0.15d,
                            true,
                            0,
                            0,
                            "Preferred multimedia render endpoint.",
                            false),
                        Multimedia: null,
                        Communications: null)),
                microphoneCaptureFactory: microphoneFactory);

            await coordinator.StartAsync(
                MeetingPlatform.Teams,
                "Client Sync",
                Array.Empty<DetectionSignal>(),
                autoStarted: true);

            await coordinator.RefreshMicrophoneCaptureAsync();
            var refresh = await coordinator.RefreshMicrophoneCaptureAsync();
            var activeSession = Assert.IsType<ActiveRecordingSession>(coordinator.ActiveSession);
            var reloadedManifest = await manifestStore.LoadAsync(activeSession.ManifestPath);

            Assert.False(refresh.SwapPerformed);
            Assert.True(refresh.SwapFailed);
            Assert.Single(reloadedManifest.MicrophoneCaptureSegments);
            Assert.Contains(reloadedManifest.CaptureTimeline, entry => entry.Kind == CaptureTimelineEventKind.SwapFailed && entry.Summary.Contains("microphone", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(activeSession.MicrophoneRecorder);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private sealed class SpyLoopbackCaptureFactory : ILoopbackCaptureFactory
    {
        private readonly Queue<LoopbackCaptureEvaluation> _evaluations;

        public SpyLoopbackCaptureFactory(params LoopbackCaptureEvaluation[] evaluations)
        {
            _evaluations = new Queue<LoopbackCaptureEvaluation>(evaluations);
        }

        public MeetingPlatform? LastPlatform { get; private set; }

        public DetectedAudioSource? LastDetectedAudioSource { get; private set; }

        public double LastThreshold { get; private set; }

        public string? FailingDeviceId { get; set; }

        public LoopbackCaptureEvaluation Evaluate(MeetingPlatform platform, DetectedAudioSource? detectedAudioSource, double activityThreshold)
        {
            LastPlatform = platform;
            LastDetectedAudioSource = detectedAudioSource;
            LastThreshold = activityThreshold;
            return _evaluations.Count > 1
                ? _evaluations.Dequeue()
                : _evaluations.Peek();
        }

        public IWaveIn Create(LoopbackCaptureSelection selection)
        {
            if (string.Equals(selection.DeviceId, FailingDeviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated failure for {selection.DeviceId}.");
            }

            return new StubWaveIn();
        }
    }

    private sealed class SpyMicrophoneCaptureFactory : IMicrophoneCaptureFactory
    {
        private readonly Queue<MicrophoneCaptureEvaluation> _evaluations;

        public SpyMicrophoneCaptureFactory(params MicrophoneCaptureEvaluation[] evaluations)
        {
            _evaluations = new Queue<MicrophoneCaptureEvaluation>(evaluations);
        }

        public string? FailingDeviceId { get; set; }

        public MicrophoneCaptureEvaluation Evaluate(double activityThreshold)
        {
            return _evaluations.Count > 1
                ? _evaluations.Dequeue()
                : _evaluations.Peek();
        }

        public IWaveIn Create(MicrophoneCaptureSelection selection)
        {
            if (string.Equals(selection.DeviceId, FailingDeviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated failure for {selection.DeviceId}.");
            }

            return new StubWaveIn();
        }
    }
}
