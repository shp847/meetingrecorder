namespace MeetingRecorder.Core.Services;

/// <summary>
/// A serializable, path-free description of one requested backlog pass. This is
/// deliberately not wired into the legacy queue yet: it establishes the
/// dispatch and migration contract before a worker can receive a stage flag.
/// </summary>
internal sealed record StagedBacklogWorkItem(
    int SchemaVersion,
    Guid WorkId,
    string SessionId,
    string ManifestToken,
    string InputRevision,
    string OutputRevision,
    StagedBacklogWorkStage Stage,
    StagedBacklogWorkIntent Intent,
    StagedBacklogWorkState State,
    int Attempt,
    string? LeaseToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RetryAfterUtc,
    string? Reason = null)
{
    public const int CurrentSchemaVersion = 1;
}

internal enum StagedBacklogWorkStage
{
    Transcript = 0,
    Diarization = 1,
    Summary = 2,
    FullPass = 3,
}

internal enum StagedBacklogWorkIntent
{
    Background = 0,
    Manual = 1,
    Rush = 2,
}

internal enum StagedBacklogWorkState
{
    Pending = 0,
    Leased = 1,
    Running = 2,
    Succeeded = 3,
    Skipped = 4,
    Blocked = 5,
    Retryable = 6,
    Failed = 7,
    Cancelled = 8,
    Superseded = 9,
}

internal enum StagedBacklogWorkReason
{
    None = 0,
    InvalidSchema = 1,
    UnknownStage = 2,
    UnknownState = 3,
    MissingIdentity = 4,
    NotUserIntended = 5,
    StaleRevision = 6,
    RetryDelayed = 7,
    MissingSource = 8,
    TranscriptNotCurrent = 9,
    SpeakerLabelingUnavailable = 10,
    SummaryDisabled = 11,
    SummaryProviderUnavailable = 12,
    ActiveWorkConflict = 13,
    DuplicateWork = 14,
    AlreadySucceeded = 15,
    IntentionallySkipped = 16,
    BlockedForReview = 17,
    RetryRequired = 18,
    TerminalFailure = 19,
    Cancelled = 20,
    Superseded = 21,
}

/// <summary>
/// Facts gathered by existing configuration and manifest checks. The resolver
/// never reads a path, provider, power state, or process directly.
/// </summary>
internal sealed record StagedBacklogCandidate(
    StagedBacklogWorkItem Work,
    bool IsUserIntended,
    bool IsCurrentRevision,
    bool HasSourceAudio,
    bool HasCurrentTranscript,
    bool IsSpeakerLabelingAvailable,
    bool IsSummaryEnabled,
    bool IsSummaryProviderConfigured);

internal sealed record StagedBacklogResolutionInput(
    IReadOnlyList<StagedBacklogCandidate> Candidates,
    DateTimeOffset NowUtc,
    Guid? LastDispatchedWorkId = null);

internal sealed record StagedBacklogWorkAssessment(
    StagedBacklogWorkItem Work,
    StagedBacklogWorkReason Reason,
    bool IsDispatchable);

/// <summary>
/// A pure compare-and-swap transition for the durable queue. Callers persist
/// <see cref="WorkItems"/> before starting another process. A rejected
/// transition is deliberately non-destructive: an old worker must never be
/// able to acknowledge, retry, or complete a newer lease.
/// </summary>
internal sealed record StagedBacklogWorkLeaseTransition(
    bool Applied,
    IReadOnlyList<StagedBacklogWorkItem> WorkItems,
    StagedBacklogWorkItem? Work,
    string? RejectionReason = null);

internal sealed record StagedBacklogStageCounts(
    int TranscriptEligible,
    int DiarizationEligible,
    int SummaryEligible,
    int FullPassEligible,
    int DeferredByEarlierBarrier,
    int Blocked,
    int Failed,
    int Cancelled,
    int Stale,
    int Active);

internal sealed record StagedBacklogResolution(
    StagedBacklogWorkStage? ActiveBarrier,
    IReadOnlyList<StagedBacklogWorkItem> Dispatchable,
    IReadOnlyList<StagedBacklogWorkAssessment> Assessments,
    StagedBacklogStageCounts Counts)
{
    public int DeferredLaterStageCount => Counts.DeferredByEarlierBarrier;
}

/// <summary>
/// Pure stage-barrier and conflict resolver. It intentionally returns work to
/// dispatch rather than leasing, starting, cancelling, or mutating a worker.
/// </summary>
internal sealed class StagedBacklogBarrierResolver
{
    private static readonly StagedBacklogWorkStage[] OrderedStages =
    [
        StagedBacklogWorkStage.Transcript,
        StagedBacklogWorkStage.Diarization,
        StagedBacklogWorkStage.Summary,
    ];

    public StagedBacklogResolution Resolve(StagedBacklogResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candidates = input.Candidates ?? throw new ArgumentNullException(nameof(input.Candidates));
        var reasons = new Dictionary<Guid, StagedBacklogWorkReason>();
        foreach (var candidate in candidates)
        {
            reasons[candidate.Work.WorkId] = Evaluate(candidate, input.NowUtc);
        }

        ApplyActiveWorkConflicts(candidates, reasons);
        ApplyDuplicateWorkRules(candidates, reasons);

        var dispatchable = candidates
            .Where(candidate => reasons[candidate.Work.WorkId] == StagedBacklogWorkReason.None)
            .ToArray();

        // Explicit full-pass work remains available for existing manual and
        // ASAP flows. It has priority over the new staged barrier, while the
        // conflict rules above prevent it from running beside staged work.
        var explicitFullPass = dispatchable
            .Where(candidate => candidate.Work.Stage == StagedBacklogWorkStage.FullPass &&
                                candidate.Work.Intent is StagedBacklogWorkIntent.Manual or StagedBacklogWorkIntent.Rush)
            .ToArray();
        if (explicitFullPass.Length > 0)
        {
            return BuildResolution(
                StagedBacklogWorkStage.FullPass,
                OrderFairly(explicitFullPass, input.LastDispatchedWorkId),
                candidates,
                reasons,
                deferredByBarrier: 0);
        }

        foreach (var stage in OrderedStages)
        {
            var atBarrier = dispatchable
                .Where(candidate => candidate.Work.Stage == stage)
                .ToArray();
            if (atBarrier.Length == 0)
            {
                continue;
            }

            var deferred = dispatchable.Count(candidate =>
                candidate.Work.Stage is StagedBacklogWorkStage.Diarization or StagedBacklogWorkStage.Summary &&
                Array.IndexOf(OrderedStages, candidate.Work.Stage) > Array.IndexOf(OrderedStages, stage));
            return BuildResolution(
                stage,
                OrderFairly(atBarrier, input.LastDispatchedWorkId),
                candidates,
                reasons,
                deferred);
        }

        // Migrated queue records are FullPass work. Do not make them disappear
        // just because the staged barrier has no eligible item yet.
        var legacyFullPass = dispatchable
            .Where(candidate => candidate.Work.Stage == StagedBacklogWorkStage.FullPass)
            .ToArray();
        return BuildResolution(
            legacyFullPass.Length == 0 ? null : StagedBacklogWorkStage.FullPass,
            OrderFairly(legacyFullPass, input.LastDispatchedWorkId),
            candidates,
            reasons,
            deferredByBarrier: 0);
    }

    private static StagedBacklogResolution BuildResolution(
        StagedBacklogWorkStage? barrier,
        IReadOnlyList<StagedBacklogCandidate> selected,
        IReadOnlyList<StagedBacklogCandidate> candidates,
        IReadOnlyDictionary<Guid, StagedBacklogWorkReason> reasons,
        int deferredByBarrier)
    {
        var assessments = candidates
            .Select(candidate => new StagedBacklogWorkAssessment(
                candidate.Work,
                reasons[candidate.Work.WorkId],
                selected.Any(item => item.Work.WorkId == candidate.Work.WorkId)))
            .ToArray();

        return new StagedBacklogResolution(
            barrier,
            selected.Select(candidate => candidate.Work).ToArray(),
            assessments,
            new StagedBacklogStageCounts(
                TranscriptEligible: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.None && assessment.Work.Stage == StagedBacklogWorkStage.Transcript),
                DiarizationEligible: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.None && assessment.Work.Stage == StagedBacklogWorkStage.Diarization),
                SummaryEligible: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.None && assessment.Work.Stage == StagedBacklogWorkStage.Summary),
                FullPassEligible: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.None && assessment.Work.Stage == StagedBacklogWorkStage.FullPass),
                DeferredByEarlierBarrier: deferredByBarrier,
                Blocked: assessments.Count(assessment => assessment.Reason is StagedBacklogWorkReason.BlockedForReview or StagedBacklogWorkReason.SpeakerLabelingUnavailable or StagedBacklogWorkReason.SummaryDisabled or StagedBacklogWorkReason.SummaryProviderUnavailable or StagedBacklogWorkReason.MissingSource or StagedBacklogWorkReason.TranscriptNotCurrent),
                Failed: assessments.Count(assessment => assessment.Reason is StagedBacklogWorkReason.TerminalFailure or StagedBacklogWorkReason.RetryRequired),
                Cancelled: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.Cancelled),
                Stale: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.StaleRevision),
                Active: assessments.Count(assessment => assessment.Reason == StagedBacklogWorkReason.ActiveWorkConflict)));
    }

    private static IReadOnlyList<StagedBacklogCandidate> OrderFairly(
        IReadOnlyList<StagedBacklogCandidate> candidates,
        Guid? lastDispatchedWorkId)
    {
        var ordered = candidates
            .OrderBy(candidate => candidate.Work.CreatedAtUtc)
            .ThenBy(candidate => candidate.Work.WorkId)
            .ToArray();
        if (lastDispatchedWorkId is not { } last || ordered.Length < 2)
        {
            return ordered;
        }

        var index = Array.FindIndex(ordered, candidate => candidate.Work.WorkId == last);
        return index < 0
            ? ordered
            : ordered.Skip(index + 1).Concat(ordered.Take(index + 1)).ToArray();
    }

    private static StagedBacklogWorkReason Evaluate(StagedBacklogCandidate candidate, DateTimeOffset nowUtc)
    {
        var work = candidate.Work;
        if (work.SchemaVersion != StagedBacklogWorkItem.CurrentSchemaVersion)
        {
            return StagedBacklogWorkReason.InvalidSchema;
        }

        if (!Enum.IsDefined(work.Stage))
        {
            return StagedBacklogWorkReason.UnknownStage;
        }

        if (!Enum.IsDefined(work.State) || !Enum.IsDefined(work.Intent))
        {
            return StagedBacklogWorkReason.UnknownState;
        }

        if (work.WorkId == Guid.Empty || string.IsNullOrWhiteSpace(work.SessionId) ||
            string.IsNullOrWhiteSpace(work.ManifestToken) || string.IsNullOrWhiteSpace(work.InputRevision))
        {
            return StagedBacklogWorkReason.MissingIdentity;
        }

        if (!candidate.IsUserIntended)
        {
            return StagedBacklogWorkReason.NotUserIntended;
        }

        if (!candidate.IsCurrentRevision)
        {
            return StagedBacklogWorkReason.StaleRevision;
        }

        switch (work.State)
        {
            case StagedBacklogWorkState.Leased:
            case StagedBacklogWorkState.Running:
                return StagedBacklogWorkReason.ActiveWorkConflict;
            case StagedBacklogWorkState.Succeeded:
                return StagedBacklogWorkReason.AlreadySucceeded;
            case StagedBacklogWorkState.Skipped:
                return StagedBacklogWorkReason.IntentionallySkipped;
            case StagedBacklogWorkState.Blocked:
                return StagedBacklogWorkReason.BlockedForReview;
            case StagedBacklogWorkState.Failed:
                return StagedBacklogWorkReason.TerminalFailure;
            case StagedBacklogWorkState.Cancelled:
                return StagedBacklogWorkReason.Cancelled;
            case StagedBacklogWorkState.Superseded:
                return StagedBacklogWorkReason.Superseded;
            case StagedBacklogWorkState.Retryable when work.RetryAfterUtc is { } retryAfter && retryAfter > nowUtc:
                return StagedBacklogWorkReason.RetryDelayed;
            case StagedBacklogWorkState.Retryable:
                break;
            case StagedBacklogWorkState.Pending:
                break;
            default:
                return StagedBacklogWorkReason.UnknownState;
        }

        return work.Stage switch
        {
            StagedBacklogWorkStage.Transcript when !candidate.HasSourceAudio => StagedBacklogWorkReason.MissingSource,
            StagedBacklogWorkStage.Diarization when !candidate.HasCurrentTranscript => StagedBacklogWorkReason.TranscriptNotCurrent,
            StagedBacklogWorkStage.Diarization when !candidate.IsSpeakerLabelingAvailable => StagedBacklogWorkReason.SpeakerLabelingUnavailable,
            StagedBacklogWorkStage.Summary when !candidate.HasCurrentTranscript => StagedBacklogWorkReason.TranscriptNotCurrent,
            StagedBacklogWorkStage.Summary when !candidate.IsSummaryEnabled => StagedBacklogWorkReason.SummaryDisabled,
            StagedBacklogWorkStage.Summary when !candidate.IsSummaryProviderConfigured => StagedBacklogWorkReason.SummaryProviderUnavailable,
            StagedBacklogWorkStage.FullPass when !candidate.HasSourceAudio => StagedBacklogWorkReason.MissingSource,
            _ => StagedBacklogWorkReason.None,
        };
    }

    private static void ApplyActiveWorkConflicts(
        IReadOnlyList<StagedBacklogCandidate> candidates,
        IDictionary<Guid, StagedBacklogWorkReason> reasons)
    {
        var activeSessionIds = candidates
            .Where(candidate => candidate.IsCurrentRevision &&
                                candidate.Work.State is StagedBacklogWorkState.Leased or StagedBacklogWorkState.Running)
            .Select(candidate => candidate.Work.SessionId)
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (reasons[candidate.Work.WorkId] != StagedBacklogWorkReason.None ||
                !activeSessionIds.Contains(candidate.Work.SessionId))
            {
                continue;
            }

            reasons[candidate.Work.WorkId] = StagedBacklogWorkReason.ActiveWorkConflict;
        }
    }

    private static void ApplyDuplicateWorkRules(
        IReadOnlyList<StagedBacklogCandidate> candidates,
        IDictionary<Guid, StagedBacklogWorkReason> reasons)
    {
        var groups = candidates
            .Where(candidate => reasons[candidate.Work.WorkId] == StagedBacklogWorkReason.None)
            .GroupBy(candidate => (candidate.Work.SessionId, candidate.Work.Stage, candidate.Work.InputRevision));
        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(candidate => candidate.Work.Attempt)
                .ThenBy(candidate => candidate.Work.CreatedAtUtc)
                .ThenBy(candidate => candidate.Work.WorkId)
                .ToArray();
            foreach (var duplicate in ordered.Skip(1))
            {
                reasons[duplicate.Work.WorkId] = StagedBacklogWorkReason.DuplicateWork;
            }
        }
    }
}

/// <summary>
/// Owns only state transitions for a selected staged item. Scheduling stays
/// with <see cref="StagedBacklogBarrierResolver"/> so lease ownership can be
/// proven independently of a process launch or local-store implementation.
/// </summary>
internal static class StagedBacklogWorkLeaseCoordinator
{
    public static StagedBacklogWorkLeaseTransition TryLease(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        Guid workId,
        string leaseToken,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        if (workId == Guid.Empty || string.IsNullOrWhiteSpace(leaseToken))
        {
            return Reject(workItems, "A work id and lease token are required.");
        }

        var target = workItems.SingleOrDefault(work => work.WorkId == workId);
        if (target is null)
        {
            return Reject(workItems, "The selected work item is no longer present.");
        }

        if (target.State is not (StagedBacklogWorkState.Pending or StagedBacklogWorkState.Retryable) ||
            target.RetryAfterUtc is { } retryAfterUtc && retryAfterUtc > nowUtc)
        {
            return Reject(workItems, "The selected work item is not currently leaseable.");
        }

        if (workItems.Any(work =>
                work.WorkId != target.WorkId &&
                string.Equals(work.SessionId, target.SessionId, StringComparison.Ordinal) &&
                string.Equals(work.InputRevision, target.InputRevision, StringComparison.Ordinal) &&
                work.State is StagedBacklogWorkState.Leased or StagedBacklogWorkState.Running))
        {
            return Reject(workItems, "Another current work item already owns this session revision.");
        }

        var leased = target with
        {
            State = StagedBacklogWorkState.Leased,
            Attempt = checked(target.Attempt + 1),
            LeaseToken = leaseToken,
            RetryAfterUtc = null,
            Reason = null,
        };
        return Replace(workItems, leased);
    }

    public static StagedBacklogWorkLeaseTransition TryMarkRunning(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        Guid workId,
        string leaseToken)
    {
        return TryTransitionOwned(
            workItems,
            workId,
            leaseToken,
            StagedBacklogWorkState.Leased,
            work => work with { State = StagedBacklogWorkState.Running, Reason = null });
    }

    public static StagedBacklogWorkLeaseTransition TryComplete(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        Guid workId,
        string leaseToken,
        StagedBacklogWorkState terminalState,
        string outputRevision,
        string? reason,
        DateTimeOffset? retryAfterUtc = null)
    {
        if (terminalState is not (StagedBacklogWorkState.Succeeded or
            StagedBacklogWorkState.Skipped or
            StagedBacklogWorkState.Blocked or
            StagedBacklogWorkState.Retryable or
            StagedBacklogWorkState.Failed or
            StagedBacklogWorkState.Cancelled or
            StagedBacklogWorkState.Superseded))
        {
            return Reject(workItems, "Only a durable terminal or retryable state may complete a lease.");
        }

        return TryTransitionOwned(
            workItems,
            workId,
            leaseToken,
            StagedBacklogWorkState.Leased,
            work => work with
            {
                State = terminalState,
                LeaseToken = null,
                OutputRevision = outputRevision ?? string.Empty,
                RetryAfterUtc = terminalState == StagedBacklogWorkState.Retryable ? retryAfterUtc : null,
                Reason = reason,
            },
            alsoAllowRunning: true);
    }

    /// <summary>
    /// App restart has no surviving worker process, so previous active leases
    /// become explicit retryable work. Other records, including future schema
    /// records, are preserved as-is for repair.
    /// </summary>
    public static IReadOnlyList<StagedBacklogWorkItem> RecoverInterruptedLeases(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        return workItems.Select(work => work.State is StagedBacklogWorkState.Leased or StagedBacklogWorkState.Running
            ? work with
            {
                State = StagedBacklogWorkState.Retryable,
                LeaseToken = null,
                RetryAfterUtc = nowUtc,
                Reason = "Recovered after the app stopped before the worker receipt was recorded.",
            }
            : work).ToArray();
    }

    private static StagedBacklogWorkLeaseTransition TryTransitionOwned(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        Guid workId,
        string leaseToken,
        StagedBacklogWorkState requiredState,
        Func<StagedBacklogWorkItem, StagedBacklogWorkItem> transition,
        bool alsoAllowRunning = false)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        if (workId == Guid.Empty || string.IsNullOrWhiteSpace(leaseToken))
        {
            return Reject(workItems, "A work id and lease token are required.");
        }

        var target = workItems.SingleOrDefault(work => work.WorkId == workId);
        if (target is null)
        {
            return Reject(workItems, "The selected work item is no longer present.");
        }

        if (!string.Equals(target.LeaseToken, leaseToken, StringComparison.Ordinal) ||
            (target.State != requiredState && (!alsoAllowRunning || target.State != StagedBacklogWorkState.Running)))
        {
            return Reject(workItems, "The worker no longer owns the current lease.");
        }

        return Replace(workItems, transition(target));
    }

    private static StagedBacklogWorkLeaseTransition Replace(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        StagedBacklogWorkItem replacement)
    {
        var updated = workItems
            .Select(work => work.WorkId == replacement.WorkId ? replacement : work)
            .ToArray();
        return new StagedBacklogWorkLeaseTransition(true, updated, replacement);
    }

    private static StagedBacklogWorkLeaseTransition Reject(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        string reason) =>
        new(false, workItems.ToArray(), null, reason);
}

internal static class StagedBacklogWorkMigration
{
    public static StagedBacklogWorkItem CreateLegacyFullPass(
        Guid workId,
        string sessionId,
        string manifestToken,
        string inputRevision,
        DateTimeOffset createdAtUtc,
        StagedBacklogWorkIntent intent = StagedBacklogWorkIntent.Background) =>
        new(
            StagedBacklogWorkItem.CurrentSchemaVersion,
            workId,
            sessionId,
            manifestToken,
            inputRevision,
            OutputRevision: string.Empty,
            StagedBacklogWorkStage.FullPass,
            intent,
            StagedBacklogWorkState.Pending,
            Attempt: 0,
            LeaseToken: null,
            CreatedAtUtc: createdAtUtc,
            RetryAfterUtc: null,
            Reason: "Migrated legacy full-pass work.");

    /// <summary>
    /// A new transcript revision invalidates only nonterminal enrichment work.
    /// Completed historical receipts remain intact for audit/recovery.
    /// </summary>
    public static IReadOnlyList<StagedBacklogWorkItem> SupersedePendingEnrichmentForNewTranscriptRevision(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        string sessionId,
        string newTranscriptRevision)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTranscriptRevision);

        return workItems.Select(work =>
        {
            var shouldSupersede = string.Equals(work.SessionId, sessionId, StringComparison.Ordinal) &&
                                  work.InputRevision != newTranscriptRevision &&
                                  work.Stage is StagedBacklogWorkStage.Diarization or StagedBacklogWorkStage.Summary &&
                                  work.State is StagedBacklogWorkState.Pending or StagedBacklogWorkState.Retryable or StagedBacklogWorkState.Blocked;
            return shouldSupersede
                ? work with
                {
                    State = StagedBacklogWorkState.Superseded,
                    LeaseToken = null,
                    Reason = "Superseded by a newer transcript revision.",
                }
                : work;
        }).ToArray();
    }
}

/// <summary>
/// Atomic local persistence for the staged-work contract. The legacy queue does
/// not call this store yet; keeping that wiring for the later scheduler slice
/// prevents this model from changing existing dispatch behavior prematurely.
/// </summary>
internal sealed class StagedBacklogWorkStore
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _storePath;

    public StagedBacklogWorkStore(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
    }

    public async Task<IReadOnlyList<StagedBacklogWorkItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storePath))
        {
            return Array.Empty<StagedBacklogWorkItem>();
        }

        try
        {
            await using var stream = File.OpenRead(_storePath);
            return await System.Text.Json.JsonSerializer.DeserializeAsync<List<StagedBacklogWorkItem>>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                ?? throw new InvalidDataException("The staged backlog store was empty.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            // Preserve the original file for repair. Callers must not treat an
            // unreadable queue as empty or silently drop pending user work.
            throw new InvalidDataException("The staged backlog store could not be read safely.", exception);
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<StagedBacklogWorkItem> workItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await System.Text.Json.JsonSerializer.SerializeAsync(stream, workItems, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }
}
