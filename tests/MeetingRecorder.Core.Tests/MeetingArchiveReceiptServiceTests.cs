using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingArchiveReceiptServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ArchiveAndRestoreAsync_RoundTrips_Complete_Published_Artifacts()
    {
        var source = Path.Combine(_root, "published");
        var archive = Path.Combine(_root, "archive");
        Directory.CreateDirectory(source);
        var audio = await WriteAsync(source, "meeting.wav", "audio");
        var transcript = await WriteAsync(source, "meeting.md", "transcript");
        var service = new MeetingArchiveReceiptService();

        var receipt = await service.ArchiveAsync("meeting", "user-archive", [("audio", audio), ("transcript", transcript)], archive);
        var receiptPath = Path.Combine(archive, "user-archive", "meeting", MeetingArchiveReceiptService.ReceiptFileName);

        Assert.True(File.Exists(receiptPath));
        Assert.False(File.Exists(audio));
        Assert.False(File.Exists(transcript));
        Assert.Equal(2, receipt.Artifacts.Count);

        var restored = await service.RestoreAsync(receiptPath);

        Assert.Equal("meeting", restored.MeetingStem);
        Assert.Equal("audio", await File.ReadAllTextAsync(audio));
        Assert.Equal("transcript", await File.ReadAllTextAsync(transcript));
    }

    [Fact]
    public async Task RestoreAsync_Rejects_Drift_Without_Moving_Artifacts()
    {
        var source = Path.Combine(_root, "published");
        var archive = Path.Combine(_root, "archive");
        Directory.CreateDirectory(source);
        var audio = await WriteAsync(source, "meeting.wav", "audio");
        var service = new MeetingArchiveReceiptService();
        await service.ArchiveAsync("meeting", "user-archive", [("audio", audio)], archive);
        var archivedAudio = Path.Combine(archive, "user-archive", "meeting", "meeting.wav");
        await File.WriteAllTextAsync(archivedAudio, "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(Path.Combine(archive, "user-archive", "meeting", MeetingArchiveReceiptService.ReceiptFileName)));

        Assert.True(File.Exists(archivedAudio));
        Assert.False(File.Exists(audio));
    }

    [Fact]
    public async Task RestoreAsync_Rejects_Collision_Without_Moving_Artifacts()
    {
        var source = Path.Combine(_root, "published");
        var archive = Path.Combine(_root, "archive");
        Directory.CreateDirectory(source);
        var audio = await WriteAsync(source, "meeting.wav", "audio");
        var service = new MeetingArchiveReceiptService();
        await service.ArchiveAsync("meeting", "user-archive", [("audio", audio)], archive);
        await File.WriteAllTextAsync(audio, "new-publish");
        var archivedAudio = Path.Combine(archive, "user-archive", "meeting", "meeting.wav");

        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(Path.Combine(archive, "user-archive", "meeting", MeetingArchiveReceiptService.ReceiptFileName)));

        Assert.True(File.Exists(archivedAudio));
        Assert.Equal("new-publish", await File.ReadAllTextAsync(audio));
    }

    [Fact]
    public async Task FindReceiptsAsync_Returns_Only_Valid_Manual_Archive_Receipts()
    {
        var source = Path.Combine(_root, "published");
        var archive = Path.Combine(_root, "archive");
        Directory.CreateDirectory(source);
        var audio = await WriteAsync(source, "meeting.wav", "audio");
        var service = new MeetingArchiveReceiptService();
        await service.ArchiveAsync("meeting", "user-archive", [("audio", audio)], archive);
        var invalidDirectory = Path.Combine(archive, "legacy", "bad");
        Directory.CreateDirectory(invalidDirectory);
        await File.WriteAllTextAsync(Path.Combine(invalidDirectory, MeetingArchiveReceiptService.ReceiptFileName), "not json");

        var found = await service.FindReceiptsAsync(archive);

        var item = Assert.Single(found);
        Assert.Equal("meeting", item.Receipt.MeetingStem);
        Assert.EndsWith(MeetingArchiveReceiptService.ReceiptFileName, item.ReceiptPath, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task<string> WriteAsync(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
