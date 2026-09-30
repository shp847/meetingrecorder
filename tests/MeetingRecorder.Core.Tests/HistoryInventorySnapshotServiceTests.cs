using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class HistoryInventorySnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Create_And_WriteAtomicAsync_Stores_Only_Hashed_Metadata()
    {
        var audio = Path.Combine(_root, "audio"); var transcripts = Path.Combine(_root, "transcripts");
        Directory.CreateDirectory(audio); Directory.CreateDirectory(transcripts);
        await File.WriteAllTextAsync(Path.Combine(audio, "2026-09-29_120000_teams_private-meeting.wav"), "audio bytes");
        await File.WriteAllTextAsync(Path.Combine(transcripts, "2026-09-29_120000_teams_private-meeting.md"), "private transcript text");
        var service = new HistoryInventorySnapshotService(new MeetingOutputCatalogService(new ArtifactPathBuilder()));
        var snapshot = service.Create(audio, transcripts, workDir: null);
        var output = Path.Combine(_root, "evidence", "inventory.json");
        await service.WriteAtomicAsync(snapshot, output);
        var json = await File.ReadAllTextAsync(output);
        Assert.Single(snapshot.Entries); Assert.True(snapshot.Entries[0].HasAudio); Assert.True(snapshot.Entries[0].HasTranscriptMarkdown);
        Assert.DoesNotContain("private-meeting", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private transcript text", json, StringComparison.OrdinalIgnoreCase);
    }
}
