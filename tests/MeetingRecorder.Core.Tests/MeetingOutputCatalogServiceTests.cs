using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
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

        var audioPath = Path.Combine(audioDir, $"{sourceStem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{sourceStem}.md");
        var jsonPath = Path.Combine(transcriptDir, $"{sourceStem}.json");
        var readyPath = Path.Combine(transcriptDir, $"{sourceStem}.ready");

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
        Assert.True(File.Exists(Path.Combine(transcriptDir, $"{expectedStem}.json")));
        Assert.True(File.Exists(Path.Combine(transcriptDir, $"{expectedStem}.ready")));
        Assert.False(File.Exists(audioPath));
        Assert.False(File.Exists(markdownPath));
        Assert.False(File.Exists(jsonPath));
        Assert.False(File.Exists(readyPath));

        var markdown = await File.ReadAllTextAsync(Path.Combine(transcriptDir, $"{expectedStem}.md"));
        Assert.StartsWith("# Client Weekly Sync", markdown, StringComparison.Ordinal);

        var json = await File.ReadAllTextAsync(Path.Combine(transcriptDir, $"{expectedStem}.json"));
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
}
