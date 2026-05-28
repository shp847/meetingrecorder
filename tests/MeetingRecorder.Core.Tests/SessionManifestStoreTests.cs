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
    public async Task SaveAsync_RoundTrips_Summary_Status_And_Content()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Summary Roundtrip",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var generatedAtUtc = DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        var expected = manifest with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Succeeded,
                generatedAtUtc,
                "Summary generated."),
            Summary = new MeetingSummary(
                "The team aligned on launch readiness.",
                ["Launch remains on track."],
                ["Proceed with the pilot."],
                [new MeetingSummaryActionItem("Send pilot checklist.", "Pranav", "Friday")],
                ["Confirm legal review timing."],
                new MeetingSummaryProviderInfo(SummaryChatProviderKind.OpenAi, "OpenAI", "gpt-5-mini", false),
                generatedAtUtc,
                "fingerprint-123"),
        };

        await store.SaveAsync(expected, manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        Assert.Equal(StageExecutionState.Succeeded, saved.SummarizationStatus.State);
        Assert.Equal("summarization", saved.SummarizationStatus.StageName);
        Assert.NotNull(saved.Summary);
        Assert.Equal("The team aligned on launch readiness.", saved.Summary.Overview);
        Assert.Equal("Send pilot checklist.", Assert.Single(saved.Summary.ActionItems).Text);
        Assert.Equal(SummaryChatProviderKind.OpenAi, saved.Summary.Provider.ProviderKind);
    }

    [Fact]
    public async Task LoadAsync_Normalizes_Legacy_Manifests_To_NotStarted_Summarization_Status()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var sessionRoot = Path.Combine(workDir, "legacy-session");
        Directory.CreateDirectory(sessionRoot);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "sessionId": "legacy-session",
              "platform": 2,
              "detectedTitle": "Legacy Session",
              "startedAtUtc": "2026-05-22T14:00:00Z",
              "state": 3,
              "transcriptionStatus": {
                "stageName": "transcription",
                "state": 0,
                "updatedAtUtc": "2026-05-22T14:00:00Z",
                "message": null
              },
              "diarizationStatus": {
                "stageName": "diarization",
                "state": 0,
                "updatedAtUtc": "2026-05-22T14:00:00Z",
                "message": null
              },
              "publishStatus": {
                "stageName": "publish",
                "state": 0,
                "updatedAtUtc": "2026-05-22T14:00:00Z",
                "message": null
              }
            }
            """);
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var loaded = await store.LoadAsync(manifestPath);

        Assert.Equal("summarization", loaded.SummarizationStatus.StageName);
        Assert.Equal(StageExecutionState.NotStarted, loaded.SummarizationStatus.State);
        Assert.Null(loaded.Summary);
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
    public async Task LoadAsync_Normalizes_Legacy_RawChunkPaths_Into_A_Full_Session_Loopback_Segment()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Legacy loopback chunks",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        var endedAtUtc = startedAtUtc.AddMinutes(4);
        var expected = manifest with
        {
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            RawChunkPaths =
            [
                Path.Combine(workDir, manifest.SessionId, "raw", "loopback-chunk-0001.wav"),
                Path.Combine(workDir, manifest.SessionId, "raw", "loopback-chunk-0002.wav"),
            ],
            LoopbackCaptureSegments = Array.Empty<LoopbackCaptureSegment>(),
        };

        await store.SaveAsync(expected, manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        var segment = Assert.Single(saved.LoopbackCaptureSegments);
        Assert.Equal(startedAtUtc, segment.StartedAtUtc);
        Assert.Equal(endedAtUtc, segment.EndedAtUtc);
        Assert.Equal(expected.RawChunkPaths, segment.ChunkPaths);
        Assert.Equal(expected.RawChunkPaths, saved.RawChunkPaths);
    }

    [Fact]
    public async Task SaveAsync_Flattens_Loopback_Segments_Back_Into_RawChunkPaths()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Segment flattening",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var segmentOne = new LoopbackCaptureSegment(
            manifest.StartedAtUtc,
            manifest.StartedAtUtc.AddMinutes(1),
            [Path.Combine(workDir, manifest.SessionId, "raw", "loopback-0001-chunk-0001.wav")],
            "device-1",
            "Laptop speakers",
            "Multimedia");
        var segmentTwo = new LoopbackCaptureSegment(
            manifest.StartedAtUtc.AddMinutes(1),
            manifest.StartedAtUtc.AddMinutes(2),
            [Path.Combine(workDir, manifest.SessionId, "raw", "loopback-0002-chunk-0001.wav")],
            "device-2",
            "USB headset",
            "Communications");

        await store.SaveAsync(
            manifest with
            {
                LoopbackCaptureSegments = [segmentOne, segmentTwo],
                RawChunkPaths = Array.Empty<string>(),
            },
            manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        Assert.Equal(2, saved.LoopbackCaptureSegments.Count);
        Assert.Equal(
            [segmentOne.ChunkPaths[0], segmentTwo.ChunkPaths[0]],
            saved.RawChunkPaths);
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
    public async Task FindPendingManifestPathsAsync_Skips_Unreadable_Manifests()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var validManifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Valid queued",
            Array.Empty<DetectionSignal>());
        var validManifestPath = Path.Combine(workDir, validManifest.SessionId, "manifest.json");
        var badManifestDir = Path.Combine(workDir, "empty-manifest");
        Directory.CreateDirectory(badManifestDir);
        await File.WriteAllTextAsync(Path.Combine(badManifestDir, "manifest.json"), string.Empty);

        var pending = await store.FindPendingManifestPathsAsync(workDir);

        Assert.Equal([validManifestPath], pending);
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
