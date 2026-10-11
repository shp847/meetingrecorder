using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxIntakeServiceTests
{
    [Fact]
    public async Task TryQueueNextAsync_Queues_A_Leased_Inbox_Source_And_Leaves_The_Original_In_Place()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.ImportInboxDir);
        var sourcePath = Path.Combine(config.ImportInboxDir, "memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        var now = DateTimeOffset.UtcNow;
        var reconciler = new ImportInboxReconciliationService();
        await reconciler.ReconcileAsync(config, now);
        var pathBuilder = new ArtifactPathBuilder();

        var result = await new ImportInboxIntakeService(pathBuilder)
            .TryQueueNextAsync(config, Guid.NewGuid(), now.AddSeconds(1));
        var journal = await new ImportInboxJournalStore(Path.Combine(config.WorkDir, "import-inbox", "journal.json")).LoadAsync();
        var journalEntry = Assert.Single(journal.Entries);
        var sessionRoot = pathBuilder.BuildSessionRoot(config.WorkDir, journalEntry.SessionId!);
        var manifest = await new SessionManifestStore(pathBuilder).LoadAsync(Path.Combine(sessionRoot, "manifest.json"));

        Assert.Equal(ImportInboxIntakeStatus.Queued, result.Status);
        Assert.Equal(Path.Combine(sessionRoot, "manifest.json"), result.ManifestPath);
        Assert.Equal(ImportInboxEntryState.Queued, journalEntry.State);
        Assert.NotNull(journalEntry.SessionId);
        Assert.Null(journalEntry.Lease);
        Assert.Equal(ExternalAudioImportMethod.ImportInbox, manifest.ImportedSourceAudio!.ImportMethod);
        Assert.True(manifest.ImportedSourceAudio.SourceRetained);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(manifest.MergedAudioPath));
    }

    [Fact]
    public async Task TryQueueNextAsync_Does_Not_Create_A_Work_Session_When_The_Inbox_Is_Disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root) with { ImportInboxEnabled = false };

        var result = await new ImportInboxIntakeService(new ArtifactPathBuilder())
            .TryQueueNextAsync(config, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ImportInboxIntakeStatus.Disabled, result.Status);
        Assert.False(Directory.Exists(config.WorkDir));
    }

    [Fact]
    public async Task TryQueueNextAsync_Archives_Only_The_ReceiptBacked_Inbox_Source_After_Queueing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root) with { ImportInboxArchiveAfterQueueEnabled = true };
        Directory.CreateDirectory(config.ImportInboxDir);
        var sourcePath = Path.Combine(config.ImportInboxDir, "memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        var now = DateTimeOffset.UtcNow;
        await new ImportInboxReconciliationService().ReconcileAsync(config, now);

        var result = await new ImportInboxIntakeService(new ArtifactPathBuilder())
            .TryQueueNextAsync(config, Guid.NewGuid(), now.AddSeconds(1));
        var journal = await new ImportInboxJournalStore(Path.Combine(config.WorkDir, "import-inbox", "journal.json")).LoadAsync();
        var entry = Assert.Single(journal.Entries);

        Assert.Equal(ImportInboxIntakeStatus.Queued, result.Status);
        Assert.Equal(ImportInboxEntryState.Queued, entry.State);
        Assert.NotNull(entry.ArchivedRelativePath);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(config.ImportInboxDir, entry.ArchivedRelativePath!)));
    }

    [Fact]
    public async Task ArchiveHeldEntryAsync_Leaves_The_Source_And_Durable_Pending_State_When_Archive_Is_Unavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root) with { ImportInboxArchiveAfterQueueEnabled = true };
        Directory.CreateDirectory(config.ImportInboxDir);
        var sourcePath = Path.Combine(config.ImportInboxDir, "memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        var sourceInfo = new FileInfo(sourcePath);
        var now = DateTimeOffset.UtcNow;
        var journal = new ImportInboxJournalStore(Path.Combine(config.WorkDir, "import-inbox", "journal.json"));
        var entry = await journal.UpsertDiscoveredAsync(
            "memo.wav",
            ExternalAudioImportIdentity.BuildObservationKey(sourcePath, sourceInfo.Length, new DateTimeOffset(sourceInfo.LastWriteTimeUtc)),
            sourceInfo.Length,
            new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
            now);
        var owner = Guid.NewGuid();
        await journal.TryAcquireLeaseAsync(entry.ObservationKey, owner, TimeSpan.FromMinutes(5), now);
        var pending = await journal.CompleteLeaseAsQueuedAsync(
            entry.ObservationKey,
            owner,
            "session-1",
            now.AddSeconds(1),
            archiveRequested: true);
        await File.WriteAllTextAsync(Path.Combine(config.ImportInboxDir, "Archive"), "not a folder");

        var result = await new ImportInboxArchiveService().ArchiveHeldEntryAsync(
            config,
            journal,
            pending,
            owner,
            now.AddSeconds(2));
        var persisted = Assert.Single((await journal.LoadAsync()).Entries);

        Assert.Equal(ImportInboxArchiveStatus.Pending, result.Status);
        Assert.Equal(ImportInboxEntryState.ArchivePending, persisted.State);
        Assert.Null(persisted.Lease);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task TryQueueNextAsync_Moves_Only_A_Terminally_Unreadable_Inbox_Source_To_Error_When_Enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root) with { ImportInboxMoveBlockedToErrorEnabled = true };
        Directory.CreateDirectory(config.ImportInboxDir);
        var sourcePath = Path.Combine(config.ImportInboxDir, "unreadable.wav");
        await File.WriteAllTextAsync(sourcePath, "not wave audio");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        var now = DateTimeOffset.UtcNow;
        await new ImportInboxReconciliationService().ReconcileAsync(config, now);

        var result = await new ImportInboxIntakeService(new ArtifactPathBuilder())
            .TryQueueNextAsync(config, Guid.NewGuid(), now.AddSeconds(1));
        var entry = Assert.Single((await new ImportInboxJournalStore(
            Path.Combine(config.WorkDir, "import-inbox", "journal.json")).LoadAsync()).Entries);

        Assert.Equal(ImportInboxIntakeStatus.Blocked, result.Status);
        Assert.Equal(ImportInboxEntryState.Errored, entry.State);
        Assert.Equal(nameof(ExternalAudioImportPreflightStatus.UnsupportedCodec), entry.ErrorReasonCode);
        Assert.NotNull(entry.ErrorRelativePath);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(config.ImportInboxDir, entry.ErrorRelativePath!)));
    }

    [Fact]
    public async Task TryQueueNextAsync_Suppresses_An_Unchanged_Terminal_Source_Without_Moving_It_By_Default()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.ImportInboxDir);
        var sourcePath = Path.Combine(config.ImportInboxDir, "unreadable.wav");
        await File.WriteAllTextAsync(sourcePath, "not wave audio");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        var now = DateTimeOffset.UtcNow;
        await new ImportInboxReconciliationService().ReconcileAsync(config, now);
        var intake = new ImportInboxIntakeService(new ArtifactPathBuilder());

        var first = await intake.TryQueueNextAsync(config, Guid.NewGuid(), now.AddSeconds(1));
        var second = await intake.TryQueueNextAsync(config, Guid.NewGuid(), now.AddMinutes(2));
        var entry = Assert.Single((await new ImportInboxJournalStore(
            Path.Combine(config.WorkDir, "import-inbox", "journal.json")).LoadAsync()).Entries);

        Assert.Equal(ImportInboxIntakeStatus.Blocked, first.Status);
        Assert.Equal(ImportInboxIntakeStatus.NoEligibleItem, second.Status);
        Assert.Equal(ImportInboxEntryState.TerminalFailure, entry.State);
        Assert.Equal(nameof(ExternalAudioImportPreflightStatus.UnsupportedCodec), entry.PreflightStatusCode);
        Assert.True(File.Exists(sourcePath));
        Assert.False(Directory.Exists(Path.Combine(config.ImportInboxDir, "Error")));
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

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        var buffer = new byte[(int)(format.AverageBytesPerSecond * duration.TotalSeconds)];
        using var writer = new WaveFileWriter(path, format);
        writer.Write(buffer, 0, buffer.Length);
        writer.Flush();
        return Task.CompletedTask;
    }
}
