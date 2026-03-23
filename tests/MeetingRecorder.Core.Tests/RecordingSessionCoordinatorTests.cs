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
    public async Task ReclassifyActiveSessionAsync_Persists_Platform_And_Evidence_Without_Changing_Title()
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
                new DetectionSignal("window-title", "Google Meet and 10 more pages - Work - Microsoft Edge", 0.85d, DateTimeOffset.UtcNow),
                new DetectionSignal("browser-window", "Google Meet and 10 more pages - Work - Microsoft Edge", 0.15d, DateTimeOffset.UtcNow),
            };
            var createdManifest = await manifestStore.CreateAsync(
                initialConfig.WorkDir,
                MeetingPlatform.GoogleMeet,
                "Google Meet and 10 more pages - Work - Microsoft Edge",
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
                new DetectionSignal("window-title", "GF/Bharat | AI workshop Sync Sourcing | Microsoft Teams", 0.85d, DateTimeOffset.UtcNow),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, DateTimeOffset.UtcNow),
                new DetectionSignal("audio-activity", "Speakers; peak=0.237; status=active", 0.2d, DateTimeOffset.UtcNow),
            };
            var detectedAudioSource = new DetectedAudioSource(
                "Microsoft Teams",
                "GF/Bharat | AI workshop Sync Sourcing",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.High,
                DateTimeOffset.UtcNow);

            var reclassified = await coordinator.ReclassifyActiveSessionAsync(
                MeetingPlatform.Teams,
                updatedEvidence,
                detectedAudioSource);
            var reloadedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.True(reclassified);
            Assert.NotNull(coordinator.ActiveSession);
            Assert.Equal(MeetingPlatform.Teams, coordinator.ActiveSession!.Manifest.Platform);
            Assert.Equal(activeManifest.DetectedTitle, coordinator.ActiveSession.Manifest.DetectedTitle);
            Assert.Equal(MeetingPlatform.Teams, reloadedManifest.Platform);
            Assert.Equal(activeManifest.DetectedTitle, reloadedManifest.DetectedTitle);
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
