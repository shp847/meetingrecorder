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
}
