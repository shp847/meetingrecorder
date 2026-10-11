using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Services;

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

    [Fact]
    public void Record_Persists_Metadata_Required_To_Reconcile_Automated_Work_After_Restart()
    {
        var ledgerPath = Path.Combine(_root, "ledger.json");
        var ledger = new MeetingCleanupWorkLedgerService(ledgerPath);
        ledger.Record(
            "labels-1",
            CleanupWorkState.Queued,
            manifestPath: "C:\\work\\one\\manifest.json",
            action: MeetingCleanupAction.GenerateSpeakerLabels,
            affectedStems: ["one"],
            inputRevision: "input-revision-1");

        var reloaded = new MeetingCleanupWorkLedgerService(ledgerPath);
        var entry = Assert.Single(reloaded.GetEntries());

        Assert.Equal(MeetingCleanupAction.GenerateSpeakerLabels, entry.Action);
        Assert.Equal(["one"], entry.AffectedStems);
        Assert.Equal("input-revision-1", entry.InputRevision);
    }

    [Fact]
    public void RecordContainmentHold_Persists_One_PathFree_ManualReview_Receipt()
    {
        var ledger = CreateLedger();
        const string reason = "Automatic archive is paused. Review and apply this action manually if appropriate.";

        ledger.RecordContainmentHold("opaque-fingerprint", MeetingCleanupAction.Archive, reason);
        ledger.RecordContainmentHold("opaque-fingerprint", MeetingCleanupAction.Archive, reason);

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.ManualReview, entry.State);
        Assert.Equal(MeetingCleanupAction.Archive, entry.Action);
        Assert.Equal(reason, entry.Detail);
        Assert.Null(entry.ManifestPath);
        Assert.Null(entry.AffectedStems);
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
