using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAsync_Is_A_NoOp_When_Inbox_Is_Disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root) with { ImportInboxEnabled = false };
        Directory.CreateDirectory(config.ImportInboxDir);
        await File.WriteAllTextAsync(Path.Combine(config.ImportInboxDir, "memo.wav"), "audio");

        var result = await new ImportInboxReconciliationService().ReconcileAsync(config, DateTimeOffset.UtcNow);

        Assert.Equal(ImportInboxReconciliationStatus.Disabled, result.Status);
        Assert.Equal(0, result.DiscoveredCount);
        Assert.False(File.Exists(Path.Combine(config.WorkDir, "import-inbox", "journal.json")));
    }

    [Fact]
    public async Task ReconcileAsync_Writes_One_Local_Receipt_Per_Supported_TopLevel_Source_Without_Queuing_Or_Moving_It()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.ImportInboxDir);
        var memoPath = Path.Combine(config.ImportInboxDir, "memo.wav");
        await File.WriteAllTextAsync(memoPath, "synthetic audio");
        await File.WriteAllTextAsync(Path.Combine(config.ImportInboxDir, "notes.txt"), "not audio");
        var nestedDirectory = Path.Combine(config.ImportInboxDir, "nested");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(Path.Combine(nestedDirectory, "nested.wav"), "synthetic audio");

        var result = await new ImportInboxReconciliationService().ReconcileAsync(
            config,
            DateTimeOffset.Parse("2026-09-27T18:00:00Z"));
        var journal = await new ImportInboxJournalStore(Path.Combine(config.WorkDir, "import-inbox", "journal.json")).LoadAsync();

        Assert.Equal(ImportInboxReconciliationStatus.Ready, result.Status);
        Assert.Equal(1, result.DiscoveredCount);
        Assert.Equal(1, result.IgnoredCount);
        var entry = Assert.Single(journal.Entries);
        Assert.Equal("memo.wav", entry.RelativeSourcePath);
        Assert.Equal(ImportInboxEntryState.Discovered, entry.State);
        Assert.True(File.Exists(memoPath));
        Assert.True(File.Exists(Path.Combine(nestedDirectory, "nested.wav")));
        Assert.Empty(Directory.EnumerateFiles(config.WorkDir, "manifest.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReconcileAsync_Blocks_An_Unavailable_Inbox_Before_Creating_A_Journal()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        var fileInsteadOfDirectory = Path.Combine(root, "not-a-directory");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(fileInsteadOfDirectory, "not a folder");
        config = config with { ImportInboxDir = fileInsteadOfDirectory };

        var result = await new ImportInboxReconciliationService().ReconcileAsync(config, DateTimeOffset.UtcNow);

        Assert.Equal(ImportInboxReconciliationStatus.BlockedStorage, result.Status);
        Assert.False(File.Exists(Path.Combine(config.WorkDir, "import-inbox", "journal.json")));
    }

    private static AppConfig CreateConfig(string root) => new()
    {
        AudioOutputDir = Path.Combine(root, "Meetings", "Recordings"),
        TranscriptOutputDir = Path.Combine(root, "Meetings", "Transcripts"),
        WorkDir = Path.Combine(root, "work"),
        ImportInboxDir = Path.Combine(root, "Meetings", "Import Inbox"),
        ImportInboxEnabled = true,
        ImportInboxScanIntervalSeconds = 60,
        ImportInboxMaxBatchSize = 20,
    };
}
