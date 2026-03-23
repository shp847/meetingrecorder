using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingOutputCatalogServiceTests
{
    [Fact]
    public async Task RenameMeetingAsync_Renames_All_Published_Artifacts_And_Updates_Transcript_Title()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var sourceStem = "2026-03-15_235430_teams_echo";
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        Directory.CreateDirectory(transcriptJsonDir);

        var audioPath = Path.Combine(audioDir, $"{sourceStem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{sourceStem}.md");
        var jsonPath = Path.Combine(transcriptJsonDir, $"{sourceStem}.json");
        var readyPath = Path.Combine(transcriptJsonDir, $"{sourceStem}.ready");

        await File.WriteAllTextAsync(audioPath, "audio");
        await File.WriteAllTextAsync(markdownPath, "# Echo" + Environment.NewLine + Environment.NewLine + "Body");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
        {
            SessionId = "session-1",
            Platform = "Teams",
            Title = "Echo",
            Segments = Array.Empty<object>(),
        }));
        await File.WriteAllTextAsync(readyPath, "ready");

        var renamed = await service.RenameMeetingAsync(audioDir, transcriptDir, sourceStem, "Client Weekly Sync");

        var expectedStem = "2026-03-15_235430_teams_client-weekly-sync";
        Assert.Equal(expectedStem, renamed.Stem);
        Assert.True(File.Exists(Path.Combine(audioDir, $"{expectedStem}.wav")));
        Assert.True(File.Exists(Path.Combine(transcriptDir, $"{expectedStem}.md")));
        Assert.True(File.Exists(Path.Combine(transcriptJsonDir, $"{expectedStem}.json")));
        Assert.True(File.Exists(Path.Combine(transcriptJsonDir, $"{expectedStem}.ready")));
        Assert.False(File.Exists(audioPath));
        Assert.False(File.Exists(markdownPath));
        Assert.False(File.Exists(jsonPath));
        Assert.False(File.Exists(readyPath));

        var markdown = await File.ReadAllTextAsync(Path.Combine(transcriptDir, $"{expectedStem}.md"));
        Assert.StartsWith("# Client Weekly Sync", markdown, StringComparison.Ordinal);

        var json = await File.ReadAllTextAsync(Path.Combine(transcriptJsonDir, $"{expectedStem}.json"));
        Assert.Contains("\"Title\": \"Client Weekly Sync\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListMeetings_Prefers_WorkManifest_Title_When_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Quarterly business review",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");

        var meetings = service.ListMeetings(audioDir, transcriptDir, workDir);
        var meeting = Assert.Single(meetings);
        Assert.Equal("Quarterly business review", meeting.Title);
    }

    [Fact]
    public async Task RenameMeetingAsync_Updates_WorkManifest_Title_When_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Initial Title",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest, manifestPath);

        var sourceStem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{sourceStem}.wav"), "audio");

        await service.RenameMeetingAsync(audioDir, transcriptDir, sourceStem, "Updated Title", workDir);

        var updatedManifest = await manifestStore.LoadAsync(manifestPath);
        Assert.Equal("Updated Title", updatedManifest.DetectedTitle);
    }

    [Fact]
    public async Task ListMeetings_Includes_Matching_WorkManifest_Path_And_State()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Retry Candidate",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest with { State = SessionState.Failed }, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");

        var meetings = service.ListMeetings(audioDir, transcriptDir, workDir);
        var meeting = Assert.Single(meetings);
        Assert.Equal(manifestPath, meeting.ManifestPath);
        Assert.Equal(SessionState.Failed, meeting.ManifestState);
    }

    [Fact]
    public async Task ListMeetings_Prefers_Manifest_Duration_When_End_Time_Is_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Duration Test",
            Array.Empty<DetectionSignal>());

        var endedAtUtc = manifest.StartedAtUtc.AddMinutes(42);
        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest with
        {
            EndedAtUtc = endedAtUtc,
            State = SessionState.Published,
        }, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{stem}.wav"), TimeSpan.FromSeconds(3));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));

        Assert.Equal(TimeSpan.FromMinutes(42), meeting.Duration);
    }

    [Fact]
    public async Task ListMeetings_Falls_Back_To_Audio_Duration_When_Manifest_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-16_004645_teams_audio-duration";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{stem}.wav"), TimeSpan.FromSeconds(5));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.NotNull(meeting.Duration);
        Assert.InRange(meeting.Duration.Value.TotalSeconds, 4.9d, 5.1d);
    }

    [Fact]
    public async Task ListMeetings_Returns_Legacy_Published_Files_Without_A_WorkManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(Path.Combine(transcriptDir, "json"));

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-15_235430_teams_legacy-sync";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, "json", $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                Title = "Legacy Sync",
                Segments = Array.Empty<object>(),
            }));

        var meetings = service.ListMeetings(audioDir, transcriptDir, workDir: null);

        var meeting = Assert.Single(meetings);
        Assert.Equal(stem, meeting.Stem);
        Assert.Equal("Legacy Sync", meeting.Title);
        Assert.Null(meeting.ManifestPath);
        Assert.Null(meeting.ManifestState);
        Assert.Equal(MeetingPlatform.Teams, meeting.Platform);
        Assert.Empty(meeting.Attendees);
        Assert.Null(meeting.TranscriptionModelFileName);
        Assert.False(meeting.HasSpeakerLabels);
    }

    [Fact]
    public async Task ListMeetings_Prefers_Manifest_Attendees_And_Processing_Metadata_Over_Json_Sidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(transcriptJsonDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Client Sync",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var enrichedManifest = manifest with
        {
            Attendees = [new MeetingAttendee("Manifest Person", [MeetingAttendeeSource.TeamsLiveRoster])],
            ProcessingMetadata = new MeetingProcessingMetadata("manifest-model.bin", true),
        };
        await manifestStore.SaveAsync(enrichedManifest, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptJsonDir, $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                title = "Client Sync",
                attendees = new[]
                {
                    new
                    {
                        name = "Json Person",
                        sources = new[] { "OutlookCalendar" },
                    },
                },
                transcriptionModelFileName = "json-model.bin",
                hasSpeakerLabels = false,
                segments = Array.Empty<object>(),
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));

        Assert.Collection(meeting.Attendees, attendee => Assert.Equal("Manifest Person", attendee.Name));
        Assert.Equal("manifest-model.bin", meeting.TranscriptionModelFileName);
        Assert.True(meeting.HasSpeakerLabels);
    }

    [Fact]
    public async Task ListMeetings_Prefers_Manifest_Detected_Audio_Source_Over_Json_Sidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(transcriptJsonDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var service = new MeetingOutputCatalogService(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Client Sync",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var enrichedManifest = manifest with
        {
            DetectedAudioSource = new DetectedAudioSource(
                "Microsoft Teams",
                "Client Sync | Microsoft Teams",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.High,
                DateTimeOffset.UtcNow),
        };
        await manifestStore.SaveAsync(enrichedManifest, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptJsonDir, $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                title = "Client Sync",
                detectedAudioSource = new
                {
                    appName = "Google Meet",
                    matchKind = "BrowserTab",
                    confidence = "Medium",
                    observedAtUtc = DateTimeOffset.UtcNow,
                },
                segments = Array.Empty<object>(),
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));

        Assert.NotNull(meeting.DetectedAudioSource);
        Assert.Equal("Microsoft Teams", meeting.DetectedAudioSource!.AppName);
        Assert.Equal(AudioSourceMatchKind.Window, meeting.DetectedAudioSource.MatchKind);
    }

    [Fact]
    public async Task ListMeetings_Falls_Back_To_Json_Attendees_And_Processing_Metadata_When_Manifest_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(transcriptJsonDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-15_235430_teams_json-fallback";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptJsonDir, $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                title = "Json Fallback",
                attendees = new[]
                {
                    new
                    {
                        name = "Jane Smith",
                        sources = new[] { "OutlookCalendar", "TeamsLiveRoster" },
                    },
                },
                transcriptionModelFileName = "ggml-small.bin",
                hasSpeakerLabels = true,
                segments = new[]
                {
                    new
                    {
                        speakerLabel = "Jane Smith",
                        text = "Hello",
                    },
                },
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.Collection(meeting.Attendees, attendee =>
        {
            Assert.Equal("Jane Smith", attendee.Name);
            Assert.Equal(
                [MeetingAttendeeSource.OutlookCalendar, MeetingAttendeeSource.TeamsLiveRoster],
                attendee.Sources);
        });
        Assert.Equal("ggml-small.bin", meeting.TranscriptionModelFileName);
        Assert.True(meeting.HasSpeakerLabels);
    }

    [Fact]
    public async Task ListMeetings_Infers_Speaker_Labels_From_Legacy_Json_Segments_When_Metadata_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(transcriptJsonDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-15_235430_teams_legacy-speakers";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptJsonDir, $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                title = "Legacy Speakers",
                segments = new[]
                {
                    new
                    {
                        speakerLabel = "Speaker 1",
                        text = "Hello",
                    },
                },
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.True(meeting.HasSpeakerLabels);
        Assert.Empty(meeting.Attendees);
        Assert.Null(meeting.TranscriptionModelFileName);
    }

    [Fact]
    public async Task ListMeetings_Returns_Audio_Only_Records_When_Transcript_Files_Are_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-16_004645_teams_test-call-3";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.Equal(stem, meeting.Stem);
        Assert.Equal("Test Call 3", meeting.Title);
        Assert.Equal(MeetingPlatform.Teams, meeting.Platform);
        Assert.NotNull(meeting.AudioPath);
        Assert.Null(meeting.MarkdownPath);
        Assert.Null(meeting.JsonPath);
        Assert.Null(meeting.ManifestPath);
    }

    [Fact]
    public async Task CreateSyntheticManifestForPublishedMeetingAsync_Creates_A_Queued_WorkManifest_For_Audio_Only_Record()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var service = new MeetingOutputCatalogService(pathBuilder);
        var stem = "2026-03-16_004645_teams_test-call-3";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        await File.WriteAllTextAsync(audioPath, "audio");
        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));

        var manifestPath = await service.CreateSyntheticManifestForPublishedMeetingAsync(meeting, workDir);

        var manifest = await new SessionManifestStore(pathBuilder).LoadAsync(manifestPath);
        Assert.Equal(SessionState.Queued, manifest.State);
        Assert.Equal(MeetingPlatform.Teams, manifest.Platform);
        Assert.Equal("Test Call 3", manifest.DetectedTitle);
        Assert.Equal(audioPath, manifest.MergedAudioPath);
        Assert.Empty(manifest.RawChunkPaths);
    }

    [Fact]
    public async Task MergeMeetingsAsync_Creates_A_Queued_Merged_WorkManifest_With_Combined_Audio()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var service = new MeetingOutputCatalogService(pathBuilder);
        var firstStem = "2026-03-17_130551_teams_microsoft-teams";
        var secondStem = "2026-03-17_130926_teams_ducey-gallina-nick-tyler-yang";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{firstStem}.wav"), TimeSpan.FromSeconds(2));
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{secondStem}.wav"), TimeSpan.FromSeconds(3));

        var meetings = service.ListMeetings(audioDir, transcriptDir, workDir: null)
            .OrderBy(record => record.StartedAtUtc)
            .ToArray();

        var mergeResult = await service.MergeMeetingsAsync(
            meetings,
            "Dream Team Reboot",
            workDir);

        var manifest = await new SessionManifestStore(pathBuilder).LoadAsync(mergeResult.ManifestPath);

        Assert.Equal("Dream Team Reboot", manifest.DetectedTitle);
        Assert.Equal(MeetingPlatform.Teams, manifest.Platform);
        Assert.Equal(SessionState.Queued, manifest.State);
        Assert.NotNull(manifest.MergedAudioPath);
        Assert.True(File.Exists(manifest.MergedAudioPath));
        Assert.Equal(new DateTimeOffset(2026, 03, 17, 13, 05, 51, TimeSpan.Zero), manifest.StartedAtUtc);
        Assert.Equal("2026-03-17_130551_teams_dream-team-reboot", mergeResult.ExpectedStem);

        using var reader = new AudioFileReader(manifest.MergedAudioPath);
        Assert.InRange(reader.TotalTime.TotalSeconds, 4.9d, 5.1d);
    }

    [Fact]
    public async Task SplitMeetingAsync_Creates_Two_Queued_WorkManifests_With_Split_Audio()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var service = new MeetingOutputCatalogService(pathBuilder);
        var stem = "2026-03-17_130551_teams_client-sync";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{stem}.wav"), TimeSpan.FromSeconds(5));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        var splitResult = await service.SplitMeetingAsync(
            meeting,
            TimeSpan.FromSeconds(2),
            workDir);

        var manifestStore = new SessionManifestStore(pathBuilder);
        var firstManifest = await manifestStore.LoadAsync(splitResult.FirstManifestPath);
        var secondManifest = await manifestStore.LoadAsync(splitResult.SecondManifestPath);

        Assert.Equal("Client Sync part 1", firstManifest.DetectedTitle);
        Assert.Equal("Client Sync part 2", secondManifest.DetectedTitle);
        Assert.Equal(MeetingPlatform.Teams, firstManifest.Platform);
        Assert.Equal(MeetingPlatform.Teams, secondManifest.Platform);
        Assert.Equal(SessionState.Queued, firstManifest.State);
        Assert.Equal(SessionState.Queued, secondManifest.State);
        Assert.Equal(new DateTimeOffset(2026, 03, 17, 13, 05, 51, TimeSpan.Zero), firstManifest.StartedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 03, 17, 13, 05, 53, TimeSpan.Zero), secondManifest.StartedAtUtc);
        Assert.True(File.Exists(firstManifest.MergedAudioPath));
        Assert.True(File.Exists(secondManifest.MergedAudioPath));
        Assert.Equal("2026-03-17_130551_teams_client-sync-part-1", splitResult.FirstExpectedStem);
        Assert.Equal("2026-03-17_130553_teams_client-sync-part-2", splitResult.SecondExpectedStem);

        using var firstReader = new AudioFileReader(firstManifest.MergedAudioPath);
        using var secondReader = new AudioFileReader(secondManifest.MergedAudioPath);
        Assert.InRange(firstReader.TotalTime.TotalSeconds, 1.9d, 2.1d);
        Assert.InRange(secondReader.TotalTime.TotalSeconds, 2.9d, 3.1d);
    }

    [Fact]
    public async Task RenameSpeakerLabelsAsync_Updates_Json_And_Markdown_Transcript_Artifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(Path.Combine(transcriptDir, "json"));

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-15_235430_teams_client-call";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var jsonPath = Path.Combine(transcriptDir, "json", $"{stem}.json");

        await File.WriteAllTextAsync(audioPath, "audio");
        await File.WriteAllTextAsync(
            markdownPath,
            string.Join(
                Environment.NewLine,
                "# Client Call",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:05] **Speaker 1:** Hello there",
                "[00:00:05 - 00:00:10] **Speaker 2:** Thanks everyone"));
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(new
            {
                Title = "Client Call",
                Segments = new object[]
                {
                    new { Start = "00:00:00", End = "00:00:05", SpeakerLabel = "Speaker 1", Text = "Hello there" },
                    new { Start = "00:00:05", End = "00:00:10", SpeakerLabel = "Speaker 2", Text = "Thanks everyone" },
                },
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));
        Assert.Equal(new[] { "Speaker 1", "Speaker 2" }, service.ListSpeakerLabels(meeting));

        await service.RenameSpeakerLabelsAsync(
            meeting,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Speaker 1"] = "Pranav",
                ["Speaker 2"] = "Emily",
            });

        var updatedMarkdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("**Pranav:** Hello there", updatedMarkdown, StringComparison.Ordinal);
        Assert.Contains("**Emily:** Thanks everyone", updatedMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("**Speaker 1:**", updatedMarkdown, StringComparison.Ordinal);

        var updatedJson = await File.ReadAllTextAsync(jsonPath);
        Assert.Contains("\"SpeakerLabel\": \"Pranav\"", updatedJson, StringComparison.Ordinal);
        Assert.Contains("\"SpeakerLabel\": \"Emily\"", updatedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListSpeakerLabels_Falls_Back_To_Markdown_When_Json_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-15_235430_teams_client-call";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{stem}.md"),
            string.Join(
                Environment.NewLine,
                "# Client Call",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:05] **Speaker 1:** Hello there",
                "[00:00:05 - 00:00:10] **Speaker 1:** Follow up",
                "[00:00:10 - 00:00:12] **Speaker 2:** Thanks everyone"));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.Equal(new[] { "Speaker 1", "Speaker 2" }, service.ListSpeakerLabels(meeting));
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        var format = new WaveFormat(16000, 16, 1);
        var totalBytes = (int)(format.AverageBytesPerSecond * duration.TotalSeconds);
        var buffer = new byte[totalBytes];

        using var writer = new WaveFileWriter(path, format);
        writer.Write(buffer, 0, buffer.Length);
        writer.Flush();
        return Task.CompletedTask;
    }
}
