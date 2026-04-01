using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SessionManifestStoreTests
{
    [Fact]
    public async Task CreateAsync_Persists_A_Queued_Manifest_With_Default_Statuses()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Architecture Review",
            new[]
            {
                new DetectionSignal("window-title", "Architecture Review | Microsoft Teams", 0.9, DateTimeOffset.UtcNow),
            });

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var saved = await store.LoadAsync(manifestPath);

        Assert.Equal(SessionState.Queued, manifest.State);
        Assert.Equal("Architecture Review", saved.DetectedTitle);
        Assert.Equal(StageExecutionState.NotStarted, saved.TranscriptionStatus.State);
        Assert.Single(saved.DetectionEvidence);
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_Attendees_And_Processing_Metadata()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Attendee Roundtrip",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var expected = manifest with
        {
            Attendees =
            [
                new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar]),
                new MeetingAttendee("John Doe", [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar]),
            ],
            ProcessingOverrides = new MeetingProcessingOverrides(
                @"C:\models\ggml-small.bin",
                "ggml-small.bin"),
            ProcessingMetadata = new MeetingProcessingMetadata(
                "ggml-medium.bin",
                true),
        };

        await store.SaveAsync(expected, manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        Assert.Collection(saved.Attendees,
            attendee =>
            {
                Assert.Equal("Jane Smith", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
            },
            attendee =>
            {
                Assert.Equal("John Doe", attendee.Name);
                Assert.Equal(
                    [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                    attendee.Sources);
            });
        Assert.Equal(@"C:\models\ggml-small.bin", saved.ProcessingOverrides?.TranscriptionModelPath);
        Assert.Equal("ggml-small.bin", saved.ProcessingOverrides?.TranscriptionModelFileName);
        Assert.Equal("ggml-medium.bin", saved.ProcessingMetadata?.TranscriptionModelFileName);
        Assert.True(saved.ProcessingMetadata?.HasSpeakerLabels);
    }

    [Fact]
    public async Task LoadAsync_Normalizes_Legacy_Microphone_Chunk_Paths_Into_A_Full_Session_Segment()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Legacy microphone chunks",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15);
        var endedAtUtc = startedAtUtc.AddMinutes(5);
        var expected = manifest with
        {
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            MicrophoneChunkPaths =
            [
                Path.Combine(workDir, manifest.SessionId, "raw", "microphone-chunk-0001.wav"),
                Path.Combine(workDir, manifest.SessionId, "raw", "microphone-chunk-0002.wav"),
            ],
            MicrophoneCaptureSegments = Array.Empty<MicrophoneCaptureSegment>(),
        };

        await store.SaveAsync(expected, manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        var segment = Assert.Single(saved.MicrophoneCaptureSegments);
        Assert.Equal(startedAtUtc, segment.StartedAtUtc);
        Assert.Equal(endedAtUtc, segment.EndedAtUtc);
        Assert.Equal(expected.MicrophoneChunkPaths, segment.ChunkPaths);
    }

    [Fact]
    public async Task FindPendingManifestPathsAsync_Prioritizes_Already_Transcribed_Sessions_Before_Fresh_Queued_Work()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var freshManifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Fresh queued",
            Array.Empty<DetectionSignal>());
        var repairedManifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.GoogleMeet,
            "Recovered queued",
            Array.Empty<DetectionSignal>());

        var freshManifestPath = Path.Combine(workDir, freshManifest.SessionId, "manifest.json");
        var repairedManifestPath = Path.Combine(workDir, repairedManifest.SessionId, "manifest.json");
        var repairedAudioPath = Path.Combine(workDir, repairedManifest.SessionId, "processing", "merged.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(repairedAudioPath)!);
        await File.WriteAllTextAsync(repairedAudioPath, "placeholder audio");

        var stale = DateTimeOffset.UtcNow.AddMinutes(-30);
        await store.SaveAsync(
            freshManifest with
            {
                State = SessionState.Queued,
                StartedAtUtc = stale.AddMinutes(5),
            },
            freshManifestPath);
        await store.SaveAsync(
            repairedManifest with
            {
                State = SessionState.Queued,
                StartedAtUtc = stale,
                EndedAtUtc = stale.AddMinutes(10),
                MergedAudioPath = repairedAudioPath,
                ProcessingOverrides = new MeetingProcessingOverrides(
                    TranscriptionModelPath: null,
                    TranscriptionModelFileName: null,
                    SkipSpeakerLabeling: true),
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, stale, "done"),
                DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Queued, stale, null),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, stale, null),
            },
            repairedManifestPath);

        var pending = await store.FindPendingManifestPathsAsync(workDir);

        Assert.Equal(2, pending.Count);
        Assert.Equal(repairedManifestPath, pending[0]);
        Assert.Equal(freshManifestPath, pending[1]);
    }

    [Fact]
    public void GetPendingResumePriority_Prefers_Already_Transcribed_Work_That_Only_Needs_Publish_Completion()
    {
        var now = DateTimeOffset.UtcNow;
        var freshQueued = new MeetingSessionManifest
        {
            StartedAtUtc = now,
            State = SessionState.Queued,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.NotStarted, now, null),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
        };
        var repairedQueued = freshQueued with
        {
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, now, "done"),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Queued, now, null),
        };

        var repairedPriority = SessionManifestStore.GetPendingResumePriority(repairedQueued);
        var freshPriority = SessionManifestStore.GetPendingResumePriority(freshQueued);

        Assert.True(repairedPriority < freshPriority);
    }
}
