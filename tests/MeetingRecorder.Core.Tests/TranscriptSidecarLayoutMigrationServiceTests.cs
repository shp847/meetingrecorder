using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSidecarLayoutMigrationServiceTests
{
    [Fact]
    public void Migrate_Moves_Root_Json_And_Ready_Files_Into_Json_Subfolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(transcriptDir);

        var markdownPath = Path.Combine(transcriptDir, "meeting.md");
        var jsonPath = Path.Combine(transcriptDir, "meeting.json");
        var readyPath = Path.Combine(transcriptDir, "meeting.ready");
        File.WriteAllText(markdownPath, "# Meeting");
        File.WriteAllText(jsonPath, "{ }");
        File.WriteAllText(readyPath, "ready");

        var result = TranscriptSidecarLayoutMigrationService.Migrate(transcriptDir);

        Assert.Equal(2, result.MovedArtifactCount);
        Assert.Equal(0, result.SkippedArtifactCount);
        Assert.True(File.Exists(markdownPath));
        Assert.False(File.Exists(jsonPath));
        Assert.False(File.Exists(readyPath));
        Assert.True(File.Exists(Path.Combine(transcriptDir, "json", "meeting.json")));
        Assert.True(File.Exists(Path.Combine(transcriptDir, "json", "meeting.ready")));
    }

    [Fact]
    public void Migrate_Does_Not_Overwrite_Existing_Files_In_The_Json_Subfolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var transcriptDir = Path.Combine(root, "transcripts");
        var sidecarDir = Path.Combine(transcriptDir, "json");
        Directory.CreateDirectory(sidecarDir);

        var rootJsonPath = Path.Combine(transcriptDir, "meeting.json");
        var sidecarJsonPath = Path.Combine(sidecarDir, "meeting.json");
        File.WriteAllText(rootJsonPath, "{ \"source\": \"root\" }");
        File.WriteAllText(sidecarJsonPath, "{ \"source\": \"sidecar\" }");

        var result = TranscriptSidecarLayoutMigrationService.Migrate(transcriptDir);

        Assert.Equal(0, result.MovedArtifactCount);
        Assert.Equal(1, result.SkippedArtifactCount);
        Assert.True(File.Exists(rootJsonPath));
        Assert.Equal("{ \"source\": \"sidecar\" }", File.ReadAllText(sidecarJsonPath));
    }
}
