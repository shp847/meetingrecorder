using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxJournalStoreTests
{
    [Fact]
    public async Task UpsertDiscoveredAsync_Persists_Only_A_Relative_Path_And_Opaque_Observation_Key()
    {
        var store = CreateStore(out var journalPath);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");

        var entry = await store.UpsertDiscoveredAsync(
            Path.Combine("batch", "memo.wav"),
            new string('a', 64),
            2_048,
            now.AddMinutes(-1),
            now);
        var loaded = await store.LoadAsync();
        var serialized = await File.ReadAllTextAsync(journalPath);

        Assert.Equal(ImportInboxEntryState.Discovered, entry.State);
        Assert.Equal(0, entry.Revision);
        Assert.Equal(entry.EntryId, Assert.Single(loaded.Entries).EntryId);
        Assert.DoesNotContain("C:\\", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(journalPath + ".bak"));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_Allows_Only_One_Active_Owner_Then_Recovers_After_Expiry()
    {
        var store = CreateStore(out _);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var key = new string('b', 64);
        await store.UpsertDiscoveredAsync("memo.wav", key, 2_048, now.AddMinutes(-1), now);
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();

        var first = await store.TryAcquireLeaseAsync(key, firstOwner, TimeSpan.FromMinutes(2), now);
        var rejected = await store.TryAcquireLeaseAsync(key, secondOwner, TimeSpan.FromMinutes(2), now.AddMinutes(1));
        var recovered = await store.TryAcquireLeaseAsync(key, secondOwner, TimeSpan.FromMinutes(2), now.AddMinutes(3));

        Assert.True(first.Acquired);
        Assert.False(rejected.Acquired);
        Assert.True(recovered.Acquired);
        Assert.Equal(secondOwner, recovered.Entry!.Lease!.OwnerId);
        Assert.Equal(2, recovered.Entry.Revision);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_Requires_The_Lease_Owner_And_Preserves_The_Item()
    {
        var store = CreateStore(out _);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var key = new string('c', 64);
        await store.UpsertDiscoveredAsync("memo.wav", key, 2_048, now.AddMinutes(-1), now);
        var owner = Guid.NewGuid();
        await store.TryAcquireLeaseAsync(key, owner, TimeSpan.FromMinutes(2), now);

        var rejected = await store.ReleaseLeaseAsync(key, Guid.NewGuid(), now.AddSeconds(1));
        var released = await store.ReleaseLeaseAsync(key, owner, now.AddSeconds(2));
        var entry = Assert.Single((await store.LoadAsync()).Entries);

        Assert.False(rejected);
        Assert.True(released);
        Assert.Equal(ImportInboxEntryState.Ready, entry.State);
        Assert.Null(entry.Lease);
        Assert.Equal(2, entry.Revision);
    }

    [Fact]
    public async Task CompleteLeaseAsQueuedAsync_Requires_The_Active_Owner_And_Records_The_Session()
    {
        var store = CreateStore(out _);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var key = new string('e', 64);
        await store.UpsertDiscoveredAsync("memo.wav", key, 2_048, now.AddMinutes(-1), now);
        var owner = Guid.NewGuid();
        await store.TryAcquireLeaseAsync(key, owner, TimeSpan.FromMinutes(2), now);

        var queued = await store.CompleteLeaseAsQueuedAsync(key, owner, "session-1", now.AddSeconds(1));

        Assert.Equal(ImportInboxEntryState.Queued, queued.State);
        Assert.Equal("session-1", queued.SessionId);
        Assert.Null(queued.Lease);
        Assert.Equal(2, queued.Revision);
    }

    [Fact]
    public async Task Retry_And_Terminal_Preflight_Receipts_Are_Durable_And_Observation_Bound()
    {
        var store = CreateStore(out _);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var retryKey = new string('r', 64);
        var terminalKey = new string('t', 64);
        await store.UpsertDiscoveredAsync("retry.wav", retryKey, 2_048, now.AddMinutes(-1), now);
        await store.UpsertDiscoveredAsync("terminal.wav", terminalKey, 2_048, now.AddMinutes(-1), now);
        var retryOwner = Guid.NewGuid();
        var terminalOwner = Guid.NewGuid();
        await store.TryAcquireLeaseAsync(retryKey, retryOwner, TimeSpan.FromMinutes(2), now);
        await store.TryAcquireLeaseAsync(terminalKey, terminalOwner, TimeSpan.FromMinutes(2), now);

        var retry = await store.CompleteLeaseAsRetryPendingAsync(
            retryKey,
            retryOwner,
            "StillCopying",
            now.AddMinutes(1),
            now);
        var terminal = await store.CompleteLeaseAsTerminalFailureAsync(
            terminalKey,
            terminalOwner,
            "DecodeFailed",
            now);
        var beforeDue = await store.TryAcquireLeaseAsync(retryKey, Guid.NewGuid(), TimeSpan.FromMinutes(2), now.AddSeconds(30));
        var afterDue = await store.TryAcquireLeaseAsync(retryKey, Guid.NewGuid(), TimeSpan.FromMinutes(2), now.AddMinutes(1));
        var suppressed = await store.TryAcquireLeaseAsync(terminalKey, Guid.NewGuid(), TimeSpan.FromMinutes(2), now.AddMinutes(2));

        Assert.Equal(ImportInboxEntryState.RetryPending, retry.State);
        Assert.Equal("StillCopying", retry.PreflightStatusCode);
        Assert.Equal(1, retry.ProbeAttemptCount);
        Assert.Equal(now.AddMinutes(1), retry.NextProbeAtUtc);
        Assert.Equal(ImportInboxEntryState.TerminalFailure, terminal.State);
        Assert.Equal("DecodeFailed", terminal.PreflightStatusCode);
        Assert.Equal(1, terminal.ProbeAttemptCount);
        Assert.False(beforeDue.Acquired);
        Assert.True(afterDue.Acquired);
        Assert.False(suppressed.Acquired);
    }

    [Fact]
    public async Task ArchiveReceipt_Stays_Leased_Until_The_Archive_Move_Commits()
    {
        var store = CreateStore(out _);
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var key = new string('f', 64);
        await store.UpsertDiscoveredAsync("memo.wav", key, 2_048, now.AddMinutes(-1), now);
        var owner = Guid.NewGuid();
        await store.TryAcquireLeaseAsync(key, owner, TimeSpan.FromMinutes(2), now);

        var pendingArchive = await store.CompleteLeaseAsQueuedAsync(
            key,
            owner,
            "session-1",
            now.AddSeconds(1),
            archiveRequested: true);
        var archived = await store.CompleteLeaseAsArchivedAsync(
            key,
            owner,
            Path.Combine("Archive", "memo.wav"),
            now.AddSeconds(2));

        Assert.Equal(ImportInboxEntryState.ArchivePending, pendingArchive.State);
        Assert.NotNull(pendingArchive.Lease);
        Assert.Equal(ImportInboxEntryState.Queued, archived.State);
        Assert.Equal(Path.Combine("Archive", "memo.wav"), archived.ArchivedRelativePath);
        Assert.Null(archived.Lease);
    }

    [Theory]
    [InlineData("C:\\private\\memo.wav")]
    [InlineData("..\\memo.wav")]
    public async Task UpsertDiscoveredAsync_Rejects_A_Path_Outside_The_Configured_Inbox(string relativePath)
    {
        var store = CreateStore(out _);

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertDiscoveredAsync(
            relativePath,
            new string('d', 64),
            2_048,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
    }

    private static ImportInboxJournalStore CreateStore(out string journalPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        journalPath = Path.Combine(root, "work", "import-inbox", "journal.json");
        return new ImportInboxJournalStore(journalPath);
    }
}
