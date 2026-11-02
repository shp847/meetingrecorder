using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
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

    [Fact]
    public async Task Terminal_Manifest_Reconciliation_Clears_Stale_Processing_Receipt()
    {
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var published = await manifestStore.CreateAsync(
            _root,
            MeetingPlatform.Manual,
            "Published cleanup",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(
            pathBuilder.BuildSessionRoot(_root, published.SessionId),
            "manifest.json");
        await manifestStore.SaveAsync(published with { State = SessionState.Published }, manifestPath);

        var ledger = CreateLedger();
        ledger.Record("published-cleanup", CleanupWorkState.Processing, manifestPath);

        var reconciled = await ledger.ReconcileTerminalManifestStatesAsync(manifestStore);

        Assert.Equal(1, reconciled);
        Assert.Equal(CleanupWorkState.Completed, Assert.Single(ledger.GetEntries()).State);
    }

    [Fact]
    public async Task Live_Reconciliation_Marks_Every_Active_Manifest_Processing_And_Completed_Manifest_Terminal()
    {
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var first = await manifestStore.CreateAsync(_root, MeetingPlatform.Manual, "First", Array.Empty<DetectionSignal>());
        var second = await manifestStore.CreateAsync(_root, MeetingPlatform.Manual, "Second", Array.Empty<DetectionSignal>());
        var completed = await manifestStore.CreateAsync(_root, MeetingPlatform.Manual, "Completed", Array.Empty<DetectionSignal>());
        var firstPath = Path.Combine(pathBuilder.BuildSessionRoot(_root, first.SessionId), "manifest.json");
        var secondPath = Path.Combine(pathBuilder.BuildSessionRoot(_root, second.SessionId), "manifest.json");
        var completedPath = Path.Combine(pathBuilder.BuildSessionRoot(_root, completed.SessionId), "manifest.json");
        await manifestStore.SaveAsync(first with { State = SessionState.Processing }, firstPath);
        await manifestStore.SaveAsync(second with { State = SessionState.Processing }, secondPath);
        await manifestStore.SaveAsync(completed with { State = SessionState.Published }, completedPath);

        var ledger = CreateLedger();
        ledger.Record("first", CleanupWorkState.Queued, firstPath, action: MeetingCleanupAction.GenerateSpeakerLabels);
        ledger.Record("second", CleanupWorkState.Queued, secondPath, action: MeetingCleanupAction.RepairSpeakerLabels);
        ledger.Record("completed", CleanupWorkState.Processing, completedPath, action: MeetingCleanupAction.GenerateSummary);

        var changed = await ledger.ReconcileLiveManifestStatesAsync(
            manifestStore,
            new HashSet<string>([firstPath, secondPath], StringComparer.Ordinal));

        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(3, changed);
        Assert.Equal(CleanupWorkState.Processing, entries["first"].State);
        Assert.Equal(CleanupWorkState.Processing, entries["second"].State);
        Assert.Equal(CleanupWorkState.Completed, entries["completed"].State);
    }

    [Fact]
    public async Task Live_Reconciliation_Holds_Unreadable_Manifest_For_Review()
    {
        var ledger = CreateLedger();
        ledger.Record(
            "missing",
            CleanupWorkState.Queued,
            Path.Combine(_root, "missing", "manifest.json"),
            action: MeetingCleanupAction.GenerateSpeakerLabels);

        await ledger.ReconcileLiveManifestStatesAsync(
            new SessionManifestStore(new ArtifactPathBuilder()),
            new HashSet<string>(StringComparer.Ordinal));

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.ManualReview, entry.State);
        Assert.Equal("Cleanup work manifest is unavailable. Review source work before retrying.", entry.Detail);
    }

    [Fact]
    public async Task Live_Reconciliation_Holds_Queued_Work_Without_A_Durable_Manifest_For_Review()
    {
        var ledger = CreateLedger();
        ledger.Record("legacy-queue", CleanupWorkState.Queued, action: MeetingCleanupAction.GenerateSummary);

        var changed = await ledger.ReconcileLiveManifestStatesAsync(
            new SessionManifestStore(new ArtifactPathBuilder()),
            new HashSet<string>(StringComparer.Ordinal));

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(1, changed);
        Assert.Equal(CleanupWorkState.ManualReview, entry.State);
        Assert.Equal("Cleanup work has no durable manifest. Review source work before retrying.", entry.Detail);
    }

    [Fact]
    public void Explicit_Retry_Releases_Only_Failed_NonDestructive_Work()
    {
        var ledger = CreateLedger();
        ledger.Record("labels", CleanupWorkState.Failed, action: MeetingCleanupAction.GenerateSpeakerLabels);
        ledger.Record("summary", CleanupWorkState.Failed, action: MeetingCleanupAction.GenerateSummary);
        ledger.Record("archive", CleanupWorkState.Failed, action: MeetingCleanupAction.Archive);

        var released = ledger.ReleaseFailedSafeWork(["labels", "summary", "archive"]);
        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);

        Assert.Equal(2, released);
        Assert.Equal(CleanupWorkState.Pending, entries["labels"].State);
        Assert.Equal(CleanupWorkState.Pending, entries["summary"].State);
        Assert.Equal(CleanupWorkState.Failed, entries["archive"].State);
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
