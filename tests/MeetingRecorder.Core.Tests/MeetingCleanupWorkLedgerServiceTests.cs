using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupWorkLedgerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Legacy_Queued_Entry_Migrates_To_Manual_Review_And_Does_Not_AutoRetry()
    {
        var ledger = CreateLedger();
        ledger.MigrateLegacyEntries([
            new MeetingCleanupAutoApplyEntry("labels-1", null, string.Empty, DateTimeOffset.UtcNow),
        ]);

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.ManualReview, entry.State);
        Assert.False(ledger.IsEligibleForAutomaticApply("labels-1"));
    }

    [Fact]
    public void Completion_Changes_Only_The_Matching_Queued_Manifest()
    {
        var ledger = CreateLedger();
        ledger.Record("labels-1", CleanupWorkState.Queued, "C:\\work\\one\\manifest.json");
        ledger.Record("labels-2", CleanupWorkState.Queued, "C:\\work\\two\\manifest.json");

        ledger.RecordCompletionForManifest("C:\\work\\one\\manifest.json", succeeded: true);

        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(CleanupWorkState.Completed, entries["labels-1"].State);
        Assert.Equal(CleanupWorkState.Queued, entries["labels-2"].State);
    }

    [Fact]
    public void Processing_Status_Changes_Only_The_Matching_Queued_Manifest()
    {
        var ledger = CreateLedger();
        ledger.Record("labels-1", CleanupWorkState.Queued, "C:\\work\\one\\manifest.json");
        ledger.Record("labels-2", CleanupWorkState.Queued, "C:\\work\\two\\manifest.json");

        ledger.RecordProcessingForManifest("C:\\work\\one\\manifest.json");

        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(CleanupWorkState.Processing, entries["labels-1"].State);
        Assert.Equal(CleanupWorkState.Queued, entries["labels-2"].State);
    }

    [Fact]
    public void Failed_Work_Remains_Ineligible_For_Automatic_Retry()
    {
        var ledger = CreateLedger();
        ledger.Record("labels-1", CleanupWorkState.Failed, detail: "Worker failed");

        Assert.False(ledger.IsEligibleForAutomaticApply("labels-1"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MeetingCleanupWorkLedgerService CreateLedger()
    {
        Directory.CreateDirectory(_root);
        return new MeetingCleanupWorkLedgerService(Path.Combine(_root, "ledger.json"));
    }
}
