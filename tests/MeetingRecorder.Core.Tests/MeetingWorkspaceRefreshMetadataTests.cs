using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingWorkspaceRefreshMetadataTests
{
    [Fact]
    public void TranscriptRenderer_RenderMarkdown_And_Json_Include_Project_Name()
    {
        var renderer = new TranscriptRenderer();
        var manifest = new MeetingSessionManifest
        {
            SessionId = "session-1",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Intel CIO Discussion",
            StartedAtUtc = DateTimeOffset.Parse("2026-03-17T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            State = SessionState.Published,
            ProjectName = "Project Atlas",
            KeyAttendees = ["Pranav Sharma", "Jane Smith"],
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "ok"),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "done"),
        };

        var markdown = renderer.RenderMarkdown(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Speaker 1", "Hello team")]);
        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Speaker 1", "Hello team")]);

        Assert.Contains("- Project: Project Atlas", markdown, StringComparison.Ordinal);
        Assert.Contains("- Key Attendees: Pranav Sharma, Jane Smith", markdown, StringComparison.Ordinal);

        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");
        Assert.Equal("Project Atlas", document["projectName"]?.GetValue<string>());
        Assert.Equal(
            ["Pranav Sharma", "Jane Smith"],
            document["keyAttendees"]?.AsArray().Select(node => node?.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task UpdateMeetingProjectAsync_Persists_Project_To_Manifest_Json_And_Listing()
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
            "Intel CIO Discussion",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(manifest, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var jsonPath = Path.Combine(transcriptJsonDir, $"{stem}.json");
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(markdownPath, "# Intel CIO Discussion" + Environment.NewLine + Environment.NewLine + "Body");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
        {
            title = "Intel CIO Discussion",
            segments = Array.Empty<object>(),
        }));

        var updated = await service.UpdateMeetingProjectAsync(
            audioDir,
            transcriptDir,
            stem,
            "Project Atlas",
            workDir);
        var reloadedManifest = await manifestStore.LoadAsync(manifestPath);
        var listedMeeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");
        var markdown = await File.ReadAllTextAsync(markdownPath);

        Assert.Equal("Project Atlas", updated.ProjectName);
        Assert.Equal("Project Atlas", reloadedManifest.ProjectName);
        Assert.Equal("Project Atlas", listedMeeting.ProjectName);
        Assert.Equal("Project Atlas", json["projectName"]?.GetValue<string>());
        Assert.Contains("- Project: Project Atlas", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergeMeetingAttendeesAsync_Merges_New_Attendees_Without_Duplicates()
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
            "Client AI Review",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(manifest with
        {
            Attendees = [new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar])],
        }, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        var jsonPath = Path.Combine(transcriptJsonDir, $"{stem}.json");
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
        {
            title = "Client AI Review",
            attendees = new[]
            {
                new
                {
                    name = "Jane Smith",
                    sources = new[] { "OutlookCalendar" },
                },
            },
            segments = Array.Empty<object>(),
        }));

        var updated = await service.MergeMeetingAttendeesAsync(
            audioDir,
            transcriptDir,
            stem,
            [
                new MeetingAttendee("jane smith", [MeetingAttendeeSource.TeamsLiveRoster]),
                new MeetingAttendee("John Doe", [MeetingAttendeeSource.OutlookCalendar]),
            ],
            workDir);

        Assert.Collection(updated.Attendees,
            attendee =>
            {
                Assert.Equal("Jane Smith", attendee.Name);
                Assert.Equal(
                    [MeetingAttendeeSource.OutlookCalendar, MeetingAttendeeSource.TeamsLiveRoster],
                    attendee.Sources);
            },
            attendee =>
            {
                Assert.Equal("John Doe", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
            });
    }

    [Fact]
    public async Task MergeMeetingAttendeesAsync_Updates_Key_Attendees_With_Reasonable_Partial_Matches()
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
            "Client AI Review",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(manifest with
        {
            KeyAttendees = ["Pranav"],
        }, manifestPath);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, manifest.DetectedTitle);
        var jsonPath = Path.Combine(transcriptJsonDir, $"{stem}.json");
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
        {
            title = "Client AI Review",
            keyAttendees = new[] { "Pranav" },
            segments = Array.Empty<object>(),
        }));

        var updated = await service.MergeMeetingAttendeesAsync(
            audioDir,
            transcriptDir,
            stem,
            [
                new MeetingAttendee("Pranav Sharma", [MeetingAttendeeSource.OutlookCalendar]),
            ],
            workDir);
        var reloadedManifest = await manifestStore.LoadAsync(manifestPath);
        var listedMeeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        Assert.Equal(["Pranav Sharma"], updated.KeyAttendees);
        Assert.Equal(["Pranav Sharma"], reloadedManifest.KeyAttendees);
        Assert.Equal(["Pranav Sharma"], listedMeeting.KeyAttendees);
        Assert.Equal(["Pranav Sharma"], json["keyAttendees"]?.AsArray().Select(node => node?.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task ListMeetings_Preserves_Stored_Title_Casing_When_Metadata_Is_Available()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var transcriptJsonDir = Path.Combine(transcriptDir, "json");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(transcriptJsonDir);

        var service = new MeetingOutputCatalogService(new ArtifactPathBuilder());
        var stem = "2026-03-20_224715_teams_intel-cio-ai-review";
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");
        await File.WriteAllTextAsync(
            Path.Combine(transcriptJsonDir, $"{stem}.json"),
            JsonSerializer.Serialize(new
            {
                title = "Intel CIO AI Review",
                segments = Array.Empty<object>(),
            }));

        var meeting = Assert.Single(service.ListMeetings(audioDir, transcriptDir, workDir: null));

        Assert.Equal("Intel CIO AI Review", meeting.Title);
    }
}
