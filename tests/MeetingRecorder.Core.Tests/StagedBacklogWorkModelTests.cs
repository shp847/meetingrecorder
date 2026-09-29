using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class StagedBacklogWorkModelTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
    private readonly StagedBacklogBarrierResolver _resolver = new();

    [Fact]
    public void Resolve_Uses_Transcript_Barrier_Before_Eligible_Labels_And_Summaries()
    {
        var transcript = Candidate(StagedBacklogWorkStage.Transcript, 1, sessionId: "a");
        var labels = Candidate(StagedBacklogWorkStage.Diarization, 2, sessionId: "b");
        var summary = Candidate(StagedBacklogWorkStage.Summary, 3, sessionId: "c");

        var result = Resolve(transcript, labels, summary);

        Assert.Equal(StagedBacklogWorkStage.Transcript, result.ActiveBarrier);
        Assert.Equal([transcript.Work.WorkId], result.Dispatchable.Select(work => work.WorkId));
        Assert.Equal(2, result.DeferredLaterStageCount);
        Assert.Equal(1, result.Counts.TranscriptEligible);
        Assert.Equal(1, result.Counts.DiarizationEligible);
        Assert.Equal(1, result.Counts.SummaryEligible);
    }

    [Fact]
    public void Resolve_Uses_Diarization_Barrier_Before_Summary_When_Transcript_Backlog_Is_Empty()
    {
        var labels = Candidate(StagedBacklogWorkStage.Diarization, 1, sessionId: "a");
        var summary = Candidate(StagedBacklogWorkStage.Summary, 2, sessionId: "b");

        var result = Resolve(labels, summary);

        Assert.Equal(StagedBacklogWorkStage.Diarization, result.ActiveBarrier);
        Assert.Equal([labels.Work.WorkId], result.Dispatchable.Select(work => work.WorkId));
        Assert.Equal(1, result.DeferredLaterStageCount);
    }

    [Fact]
    public void Resolve_Does_Not_Let_Blocked_Transcript_Work_Deadlock_Eligible_Later_Work()
    {
        var blockedTranscript = Candidate(
            StagedBacklogWorkStage.Transcript,
            1,
            sessionId: "no-source",
            hasSourceAudio: false);
        var labels = Candidate(StagedBacklogWorkStage.Diarization, 2, sessionId: "ready-for-labels");

        var result = Resolve(blockedTranscript, labels);

        Assert.Equal(StagedBacklogWorkStage.Diarization, result.ActiveBarrier);
        Assert.Equal([labels.Work.WorkId], result.Dispatchable.Select(work => work.WorkId));
        Assert.Equal(1, result.Counts.Blocked);
        AssertReason(result, blockedTranscript.Work.WorkId, StagedBacklogWorkReason.MissingSource);
    }

    [Fact]
    public void Resolve_Requires_Current_Transcript_And_Configured_Summary_Consent_Boundary()
    {
        var staleTranscript = Candidate(
            StagedBacklogWorkStage.Summary,
            1,
            sessionId: "stale",
            hasCurrentTranscript: false);
        var disabledSummary = Candidate(
            StagedBacklogWorkStage.Summary,
            2,
            sessionId: "disabled",
            isSummaryEnabled: false);
        var noProvider = Candidate(
            StagedBacklogWorkStage.Summary,
            3,
            sessionId: "no-provider",
            isSummaryProviderConfigured: false);

        var result = Resolve(staleTranscript, disabledSummary, noProvider);

        Assert.Null(result.ActiveBarrier);
        Assert.Empty(result.Dispatchable);
        Assert.Equal(3, result.Counts.Blocked);
        AssertReason(result, staleTranscript.Work.WorkId, StagedBacklogWorkReason.TranscriptNotCurrent);
        AssertReason(result, disabledSummary.Work.WorkId, StagedBacklogWorkReason.SummaryDisabled);
        AssertReason(result, noProvider.Work.WorkId, StagedBacklogWorkReason.SummaryProviderUnavailable);
    }

    [Fact]
    public void Resolve_Preserves_Manual_FullPass_Priority_And_Prevents_Concurrent_Stage_Work()
    {
        var manualFullPass = Candidate(
            StagedBacklogWorkStage.FullPass,
            1,
            sessionId: "manual",
            intent: StagedBacklogWorkIntent.Manual);
        var transcript = Candidate(StagedBacklogWorkStage.Transcript, 2, sessionId: "background");
        var activeFullPass = Candidate(
            StagedBacklogWorkStage.FullPass,
            3,
            sessionId: "same-session",
            state: StagedBacklogWorkState.Running);
        var blockedStage = Candidate(StagedBacklogWorkStage.Transcript, 4, sessionId: "same-session");

        var result = Resolve(manualFullPass, transcript, activeFullPass, blockedStage);

        Assert.Equal(StagedBacklogWorkStage.FullPass, result.ActiveBarrier);
        Assert.Equal([manualFullPass.Work.WorkId], result.Dispatchable.Select(work => work.WorkId));
        AssertReason(result, activeFullPass.Work.WorkId, StagedBacklogWorkReason.ActiveWorkConflict);
        AssertReason(result, blockedStage.Work.WorkId, StagedBacklogWorkReason.ActiveWorkConflict);
    }

    [Fact]
    public void Resolve_Coalesces_Duplicate_Work_And_Rotates_Fairly_After_The_Last_Dispatch()
    {
        var first = Candidate(StagedBacklogWorkStage.Transcript, 1, sessionId: "a", createdAtUtc: NowUtc.AddMinutes(-3));
        var duplicate = Candidate(StagedBacklogWorkStage.Transcript, 2, sessionId: "a", createdAtUtc: NowUtc.AddMinutes(-2));
        var second = Candidate(StagedBacklogWorkStage.Transcript, 3, sessionId: "b", createdAtUtc: NowUtc.AddMinutes(-1));

        var result = _resolver.Resolve(new StagedBacklogResolutionInput(
            [first, duplicate, second],
            NowUtc,
            LastDispatchedWorkId: first.Work.WorkId));

        Assert.Equal([second.Work.WorkId, first.Work.WorkId], result.Dispatchable.Select(work => work.WorkId));
        AssertReason(result, duplicate.Work.WorkId, StagedBacklogWorkReason.DuplicateWork);
    }

    [Fact]
    public void Resolve_Leaves_Future_Schema_And_Unknown_Stage_Visible_But_Undispatched()
    {
        var futureSchema = Candidate(StagedBacklogWorkStage.Transcript, 1) with
        {
            Work = Candidate(StagedBacklogWorkStage.Transcript, 1).Work with { SchemaVersion = 99 },
        };
        var unknownStage = Candidate((StagedBacklogWorkStage)99, 2);

        var result = Resolve(futureSchema, unknownStage);

        Assert.Null(result.ActiveBarrier);
        Assert.Empty(result.Dispatchable);
        AssertReason(result, futureSchema.Work.WorkId, StagedBacklogWorkReason.InvalidSchema);
        AssertReason(result, unknownStage.Work.WorkId, StagedBacklogWorkReason.UnknownStage);
    }

    [Fact]
    public void Legacy_Migration_Retains_FullPass_Work_And_New_Transcript_Supersedes_Only_Pending_Enrichment()
    {
        var legacy = StagedBacklogWorkMigration.CreateLegacyFullPass(
            Id(1),
            "session-a",
            "manifest-a",
            "revision-1",
            NowUtc);
        var pendingLabels = Candidate(StagedBacklogWorkStage.Diarization, 2, sessionId: "session-a", inputRevision: "revision-1").Work;
        var completedSummary = Candidate(
            StagedBacklogWorkStage.Summary,
            3,
            sessionId: "session-a",
            inputRevision: "revision-1",
            state: StagedBacklogWorkState.Succeeded).Work;

        var reconciled = StagedBacklogWorkMigration.SupersedePendingEnrichmentForNewTranscriptRevision(
            [legacy, pendingLabels, completedSummary],
            "session-a",
            "revision-2");

        Assert.Equal(StagedBacklogWorkStage.FullPass, legacy.Stage);
        Assert.Equal(StagedBacklogWorkState.Pending, legacy.State);
        Assert.Equal(StagedBacklogWorkState.Superseded, reconciled.Single(work => work.WorkId == pendingLabels.WorkId).State);
        Assert.Equal(StagedBacklogWorkState.Succeeded, reconciled.Single(work => work.WorkId == completedSummary.WorkId).State);
    }

    [Fact]
    public async Task Store_Preserves_Future_Work_For_Repair_And_Restarts_With_Valid_Legacy_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(root, "staged-backlog.json");
        var legacy = StagedBacklogWorkMigration.CreateLegacyFullPass(
            Id(1),
            "legacy-session",
            "legacy-manifest",
            "revision-1",
            NowUtc);
        var future = legacy with
        {
            WorkId = Id(2),
            SchemaVersion = 99,
            Stage = (StagedBacklogWorkStage)99,
        };

        await new StagedBacklogWorkStore(storePath).SaveAsync([legacy, future]);
        var reloaded = await new StagedBacklogWorkStore(storePath).LoadAsync();
        var result = Resolve(reloaded.Select(AsCandidate).ToArray());

        Assert.Equal(2, reloaded.Count);
        Assert.Equal(StagedBacklogWorkStage.FullPass, result.ActiveBarrier);
        Assert.Equal([legacy.WorkId], result.Dispatchable.Select(work => work.WorkId));
        AssertReason(result, future.WorkId, StagedBacklogWorkReason.InvalidSchema);
    }

    [Fact]
    public async Task Store_Rejects_Corrupt_Contents_Without_Treating_Them_As_An_Empty_Queue()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(root, "staged-backlog.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(storePath, "not-json");

        await Assert.ThrowsAsync<InvalidDataException>(() => new StagedBacklogWorkStore(storePath).LoadAsync());
        Assert.True(File.Exists(storePath));
    }

    [Fact]
    public void LeaseCoordinator_Requires_Current_Lease_For_Completion_And_Preserves_New_Owner()
    {
        var pending = Candidate(StagedBacklogWorkStage.Transcript, 1, sessionId: "session-a").Work;
        var firstLease = StagedBacklogWorkLeaseCoordinator.TryLease(
            [pending],
            pending.WorkId,
            "lease-one",
            NowUtc);
        var running = StagedBacklogWorkLeaseCoordinator.TryMarkRunning(
            firstLease.WorkItems,
            pending.WorkId,
            "lease-one");
        var recovered = StagedBacklogWorkLeaseCoordinator.RecoverInterruptedLeases(running.WorkItems, NowUtc.AddMinutes(1));
        var secondLease = StagedBacklogWorkLeaseCoordinator.TryLease(
            recovered,
            pending.WorkId,
            "lease-two",
            NowUtc.AddMinutes(1));

        var staleCompletion = StagedBacklogWorkLeaseCoordinator.TryComplete(
            secondLease.WorkItems,
            pending.WorkId,
            "lease-one",
            StagedBacklogWorkState.Succeeded,
            "output-1",
            null);
        var currentCompletion = StagedBacklogWorkLeaseCoordinator.TryComplete(
            secondLease.WorkItems,
            pending.WorkId,
            "lease-two",
            StagedBacklogWorkState.Succeeded,
            "output-2",
            null);

        Assert.True(firstLease.Applied);
        Assert.True(running.Applied);
        Assert.False(staleCompletion.Applied);
        Assert.Contains("no longer owns", staleCompletion.RejectionReason, StringComparison.Ordinal);
        Assert.True(currentCompletion.Applied);
        Assert.Equal(StagedBacklogWorkState.Succeeded, currentCompletion.Work!.State);
        Assert.Equal("output-2", currentCompletion.Work.OutputRevision);
        Assert.Null(currentCompletion.Work.LeaseToken);
    }

    [Fact]
    public void LeaseCoordinator_Rejects_Competing_Current_Session_Revision()
    {
        var active = Candidate(
            StagedBacklogWorkStage.Transcript,
            1,
            sessionId: "session-a",
            inputRevision: "revision-1",
            state: StagedBacklogWorkState.Running).Work;
        var pending = Candidate(
            StagedBacklogWorkStage.Diarization,
            2,
            sessionId: "session-a",
            inputRevision: "revision-1").Work;

        var transition = StagedBacklogWorkLeaseCoordinator.TryLease(
            [active, pending],
            pending.WorkId,
            "lease-two",
            NowUtc);

        Assert.False(transition.Applied);
        Assert.Contains("already owns", transition.RejectionReason, StringComparison.Ordinal);
        Assert.Equal(StagedBacklogWorkState.Pending, transition.WorkItems.Single(work => work.WorkId == pending.WorkId).State);
    }

    private StagedBacklogResolution Resolve(params StagedBacklogCandidate[] candidates) =>
        _resolver.Resolve(new StagedBacklogResolutionInput(candidates, NowUtc));

    private static void AssertReason(
        StagedBacklogResolution result,
        Guid workId,
        StagedBacklogWorkReason expected) =>
        Assert.Equal(expected, result.Assessments.Single(assessment => assessment.Work.WorkId == workId).Reason);

    private static StagedBacklogCandidate Candidate(
        StagedBacklogWorkStage stage,
        int id,
        string sessionId = "session",
        string inputRevision = "revision-1",
        StagedBacklogWorkState state = StagedBacklogWorkState.Pending,
        StagedBacklogWorkIntent intent = StagedBacklogWorkIntent.Background,
        bool isUserIntended = true,
        bool isCurrentRevision = true,
        bool hasSourceAudio = true,
        bool hasCurrentTranscript = true,
        bool isSpeakerLabelingAvailable = true,
        bool isSummaryEnabled = true,
        bool isSummaryProviderConfigured = true,
        DateTimeOffset? createdAtUtc = null) =>
        new(
            new StagedBacklogWorkItem(
                StagedBacklogWorkItem.CurrentSchemaVersion,
                Id(id),
                sessionId,
                $"manifest-{sessionId}",
                inputRevision,
                "output-revision",
                stage,
                intent,
                state,
                Attempt: 0,
                LeaseToken: state is StagedBacklogWorkState.Leased or StagedBacklogWorkState.Running ? "lease" : null,
                createdAtUtc ?? NowUtc,
                RetryAfterUtc: null),
            isUserIntended,
            isCurrentRevision,
            hasSourceAudio,
            hasCurrentTranscript,
            isSpeakerLabelingAvailable,
            isSummaryEnabled,
            isSummaryProviderConfigured);

    private static StagedBacklogCandidate AsCandidate(StagedBacklogWorkItem work) =>
        new(work, true, true, true, true, true, true, true);

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
