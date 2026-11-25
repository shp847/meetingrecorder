using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupWorkLedgerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultLedger_UsesTheIsolatedAppRootDuringFixtures()
    {
        using var scope = AppDataPaths.PushTestAppRoot(_root);
        new MeetingCleanupWorkLedgerService().Record("fixture", CleanupWorkState.Pending);
        Assert.True(File.Exists(Path.Combine(_root, "cache", "meeting-cleanup-work-ledger-v1.json")));
    }

    [Fact]
    public void RetiredSummaryWork_DoesNotHideOrChangeOtherCleanupWork()
    {
        var ledger = CreateLedger();
        ledger.Record("summary", CleanupWorkState.Queued, "summary-manifest", action: MeetingCleanupAction.GenerateSummary);
        ledger.Record("labels", CleanupWorkState.Failed, "labels-manifest", action: MeetingCleanupAction.GenerateSpeakerLabels);

        Assert.Equal(1, ledger.RetireObsoleteSummaryWork());
        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(CleanupWorkState.Retired, entries["summary"].State);
        Assert.Equal(CleanupWorkState.Failed, entries["labels"].State);
        Assert.DoesNotContain("summary-manifest", ledger.GetQueuedManifestPaths());
        Assert.Equal(0, ledger.RetireObsoleteSummaryWork());
    }

    [Fact]
    public async Task PublishedTranscript_DoesNotMarkFailedSpeakerLabelingComplete()
    {
        var builder = new ArtifactPathBuilder();
        var store = new SessionManifestStore(builder);
        var manifest = await store.CreateAsync(_root, MeetingPlatform.Manual, "Label failure", Array.Empty<DetectionSignal>());
        var path = Path.Combine(builder.BuildSessionRoot(_root, manifest.SessionId), "manifest.json");
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(manifest with
        {
            State = SessionState.Published,
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, now, null),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Failed, now, "Worker timed out"),
        }, path);
        var ledger = CreateLedger();
        ledger.Record("labels", CleanupWorkState.Processing, path, action: MeetingCleanupAction.GenerateSpeakerLabels);

        await ledger.ReconcileLiveManifestStatesAsync(store, new HashSet<string>());

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.Failed, entry.State);
        Assert.Equal(1, ledger.ReleaseTransientFailures(now.AddMinutes(2)));
    }

    [Fact]
    public async Task WorkerExitSuccess_DoesNotMaskAFailedLabelStage()
    {
        var builder = new ArtifactPathBuilder();
        var store = new SessionManifestStore(builder);
        var manifest = await store.CreateAsync(_root, MeetingPlatform.Manual, "Label failure", Array.Empty<DetectionSignal>());
        var path = Path.Combine(builder.BuildSessionRoot(_root, manifest.SessionId), "manifest.json");
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(manifest with
        {
            State = SessionState.Published,
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, now, null),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Failed, now, "timed out on private source"),
        }, path);
        var ledger = CreateLedger();
        ledger.Record("labels", CleanupWorkState.Processing, path, action: MeetingCleanupAction.GenerateSpeakerLabels);

        await ledger.ReconcileCompletionForManifestAsync(store, path, workerSucceeded: true, detail: null);

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.Failed, entry.State);
        Assert.DoesNotContain("private source", entry.Detail);
    }

    [Fact]
    public async Task LegacyFailedLabelWork_UsesUnderlyingTransientTranscriptionFailure()
    {
        var builder = new ArtifactPathBuilder();
        var store = new SessionManifestStore(builder);
        var manifest = await store.CreateAsync(_root, MeetingPlatform.Manual, "Legacy failure", Array.Empty<DetectionSignal>());
        var path = Path.Combine(builder.BuildSessionRoot(_root, manifest.SessionId), "manifest.json");
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(manifest with
        {
            State = SessionState.Failed,
            ErrorSummary = "The process cannot access the file because it is being used by another process.",
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Failed, now, "file in use"),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Skipped, now, "Skipped because source processing failed."),
        }, path);
        var ledger = CreateLedger();
        ledger.Record("legacy-label", CleanupWorkState.Failed, path, "Work could not be completed", MeetingCleanupAction.GenerateSpeakerLabels);

        await ledger.ReconcileTerminalManifestStatesAsync(store);

        var entry = Assert.Single(ledger.GetEntries());
        Assert.Equal(CleanupWorkState.Failed, entry.State);
        Assert.Contains("Temporary", entry.Detail);
        Assert.Equal(1, ledger.ReleaseTransientFailures(now.AddMinutes(2)));
    }

    [Fact]
    public void TransientFailures_AreRetriedWithBoundedDurableBackoff()
    {
        var path = Path.Combine(_root, "ledger.json");
        var ledger = new MeetingCleanupWorkLedgerService(path);
        ledger.Record("labels", CleanupWorkState.Failed, detail: "Worker timed out", action: MeetingCleanupAction.GenerateSpeakerLabels);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(0, ledger.ReleaseTransientFailures(now));
        Assert.Equal(1, ledger.ReleaseTransientFailures(now.AddMinutes(2)));
        for (var retry = 1; retry <= 3; retry++)
        {
            ledger = new MeetingCleanupWorkLedgerService(path);
            Assert.Equal(retry, Assert.Single(ledger.GetEntries()).AutomaticRetryCount);
            ledger.Record("labels", CleanupWorkState.Failed, detail: "Worker timed out");
            Assert.Equal(retry < 3 ? 1 : 0, ledger.ReleaseTransientFailures(DateTimeOffset.UtcNow.AddMinutes(31)));
        }
        ledger.Record("other", CleanupWorkState.Failed, detail: "Missing model", action: MeetingCleanupAction.GenerateSpeakerLabels);
        Assert.Equal(0, ledger.ReleaseTransientFailures(DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public void ProtectedFailure_RetriesOnlyAfterRecoveryWasVerified()
    {
        var ledger = CreateLedger();
        ledger.Record("legacy", CleanupWorkState.Failed, detail: "timed out", action: MeetingCleanupAction.Archive);
        ledger.Record("safe", CleanupWorkState.Failed, detail: "timed out", action: MeetingCleanupAction.Merge, recoveryVerified: true);
        Assert.Equal(1, ledger.ReleaseTransientFailures(DateTimeOffset.UtcNow.AddMinutes(2)));
        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(CleanupWorkState.Failed, entries["legacy"].State);
        Assert.Equal(CleanupWorkState.Pending, entries["safe"].State);
    }

    [Fact]
    public void ChangedInput_ReleasesOnlyARecoverableFailureAndResetsAttempts()
    {
        var ledger = CreateLedger();
        ledger.Record("labels", CleanupWorkState.Failed, detail: "timeout", action: MeetingCleanupAction.GenerateSpeakerLabels);
        ledger.Record("unsafe", CleanupWorkState.Failed, action: MeetingCleanupAction.Archive);
        Assert.False(ledger.ObserveFailureInputRevision("labels", "first"));
        Assert.False(ledger.ObserveFailureInputRevision("unsafe", "first"));
        Assert.True(ledger.ObserveFailureInputRevision("labels", "second"));
        Assert.False(ledger.ObserveFailureInputRevision("unsafe", "second"));
        var entries = ledger.GetEntries().ToDictionary(entry => entry.Fingerprint);
        Assert.Equal(CleanupWorkState.Pending, entries["labels"].State);
        Assert.Equal(0, entries["labels"].AutomaticRetryCount);
        Assert.Equal(CleanupWorkState.Failed, entries["unsafe"].State);
    }

    [Fact]
    public void LegacyPublishedLabelRecovery_IsReleasedAtMostOnce()
    {
        var ledger = CreateLedger();
        ledger.Record("legacy", CleanupWorkState.Failed, action: MeetingCleanupAction.GenerateSpeakerLabels);
        ledger.Record("other", CleanupWorkState.Failed, action: MeetingCleanupAction.Archive);
        Assert.True(ledger.ReleaseLegacyPublishedLabelFailure("legacy"));
        ledger.Record("legacy", CleanupWorkState.Failed, detail: "still failed");
        Assert.False(ledger.ReleaseLegacyPublishedLabelFailure("legacy"));
        Assert.False(ledger.ReleaseLegacyPublishedLabelFailure("other"));
        Assert.Equal(CleanupWorkState.Failed, ledger.GetEntries().Single(entry => entry.Fingerprint == "legacy").State);
    }

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

        Assert.Equal(1, released);
        Assert.Equal(CleanupWorkState.Pending, entries["labels"].State);
        Assert.Equal(CleanupWorkState.Failed, entries["summary"].State);
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
