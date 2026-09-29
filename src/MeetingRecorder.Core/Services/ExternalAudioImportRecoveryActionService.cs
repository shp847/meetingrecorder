using System.Collections.Concurrent;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ExternalAudioImportRecoveryActionFailure
{
    None = 0,
    RevisionConflict = 1,
    NotEligible = 2,
    StagedWorkUnavailable = 3,
    ReadOnly = 4,
}

public sealed record ExternalAudioImportRecoveryActionResult(
    bool Applied,
    ExternalAudioImportRecoveryActionFailure Failure,
    ExternalAudioImportJob? Job,
    ExternalAudioImportRecoveryActionReceipt? Receipt,
    string Message);

/// <summary>
/// Executes the first recovery action that has no external-source mutation:
/// retrying a failed import from an already verified app-owned staged copy.
/// It serializes same-process clicks, rechecks the persisted revision and
/// receipt, and writes a bounded opaque result receipt with the next job
/// checkpoint. Retry-from-original and source replacement deliberately remain
/// separate transactions because they must re-observe a local source first.
/// </summary>
public sealed class ExternalAudioImportRecoveryActionService
{
    private const int MaximumRecoveryReceipts = 10;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ExternalAudioImportRecoveryActionResult> RetryFromStagedWorkAsync(
        string jobPath,
        int expectedRevision,
        ExternalAudioImportReadinessSnapshot readiness,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobPath);
        ArgumentNullException.ThrowIfNull(readiness);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedJobPath = Path.GetFullPath(jobPath);
        var gate = _jobGates.GetOrAdd(normalizedJobPath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RetryFromStagedWorkCoreAsync(
                normalizedJobPath,
                expectedRevision,
                readiness,
                nowUtc,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<ExternalAudioImportRecoveryActionResult> RetryFromStagedWorkCoreAsync(
        string jobPath,
        int expectedRevision,
        ExternalAudioImportReadinessSnapshot readiness,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var store = new ExternalAudioImportJobStore(jobPath);
        ExternalAudioImportJobLoadResult loaded;
        try
        {
            loaded = await store.LoadAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Rejected(ExternalAudioImportRecoveryActionFailure.NotEligible, "This import record is no longer available.");
        }

        var job = loaded.Job;
        if (job is null || !loaded.Schema.CanWrite || job.IsReadOnly)
        {
            return Rejected(
                ExternalAudioImportRecoveryActionFailure.ReadOnly,
                string.IsNullOrWhiteSpace(loaded.Schema.RecoveryText)
                    ? "This import record cannot be changed by this version."
                    : loaded.Schema.RecoveryText);
        }

        if (job.Revision != expectedRevision)
        {
            return Rejected(
                ExternalAudioImportRecoveryActionFailure.RevisionConflict,
                "This import changed before recovery started. Refresh it and choose an action again.");
        }

        if (job.State != ExternalAudioImportJobState.Failed)
        {
            return Rejected(
                ExternalAudioImportRecoveryActionFailure.NotEligible,
                "Only a failed import can retry from its staged work copy.");
        }

        if (!TryGetVerifiedStagedPath(jobPath, job, out _))
        {
            return Rejected(
                ExternalAudioImportRecoveryActionFailure.StagedWorkUnavailable,
                "The verified app-owned staged copy is unavailable. Select a replacement source to recover this import.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        var probing = ExternalAudioImportJobTransitions.TryTransition(
            job with { ReadinessSnapshot = readiness },
            expectedRevision,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            normalizedNowUtc);
        if (!probing.Applied || probing.Job is null)
        {
            return Rejected(ExternalAudioImportRecoveryActionFailure.NotEligible, "This import could not enter safe recovery.");
        }

        var next = readiness.CanQueue
            ? TransitionToQueue(probing.Job, normalizedNowUtc)
            : ExternalAudioImportJobTransitions.TryTransition(
                probing.Job,
                probing.Job.Revision,
                ExternalAudioImportJobState.BlockedBySetup,
                readiness.State == ExternalAudioImportReadinessState.Unknown
                    ? ExternalAudioImportJobReason.ReadinessUnknown
                    : ExternalAudioImportJobReason.SetupRequired,
                normalizedNowUtc);
        if (!next.Applied || next.Job is null)
        {
            return Rejected(ExternalAudioImportRecoveryActionFailure.NotEligible, "This import could not enter its next safe state.");
        }

        var result = readiness.CanQueue ? "Queued" : "BlockedBySetup";
        var receipt = new ExternalAudioImportRecoveryActionReceipt(
            Guid.NewGuid(),
            ExternalAudioImportRecoveryActionKind.RetryFromStagedWork,
            expectedRevision,
            next.Job.Revision,
            result,
            normalizedNowUtc,
            readiness.CanQueue
                ? "Verified staged work was rechecked and queued for a new attempt."
                : "Verified staged work was retained while transcription setup remains blocked.");
        var committed = next.Job with
        {
            RecoveryReceipts = AppendReceipt(next.Job.RecoveryReceipts, receipt),
        };
        cancellationToken.ThrowIfCancellationRequested();
        await store.SaveAsync(committed, cancellationToken);
        return new ExternalAudioImportRecoveryActionResult(
            Applied: true,
            ExternalAudioImportRecoveryActionFailure.None,
            committed,
            receipt,
            receipt.Message);
    }

    private static ExternalAudioImportJobTransitionResult TransitionToQueue(
        ExternalAudioImportJob probing,
        DateTimeOffset nowUtc)
    {
        var ready = ExternalAudioImportJobTransitions.TryTransition(
            probing,
            probing.Revision,
            ExternalAudioImportJobState.ReadyToQueue,
            ExternalAudioImportJobReason.None,
            nowUtc);
        return !ready.Applied || ready.Job is null
            ? ready
            : ExternalAudioImportJobTransitions.TryTransition(
                ready.Job,
                ready.Job.Revision,
                ExternalAudioImportJobState.Queued,
                ExternalAudioImportJobReason.None,
                nowUtc);
    }

    private static IReadOnlyList<ExternalAudioImportRecoveryActionReceipt> AppendReceipt(
        IReadOnlyList<ExternalAudioImportRecoveryActionReceipt> existing,
        ExternalAudioImportRecoveryActionReceipt receipt)
    {
        var receipts = (existing ?? Array.Empty<ExternalAudioImportRecoveryActionReceipt>())
            .Append(receipt)
            .TakeLast(MaximumRecoveryReceipts)
            .ToArray();
        return receipts;
    }

    private static bool TryGetVerifiedStagedPath(
        string jobPath,
        ExternalAudioImportJob job,
        out string stagedPath)
    {
        stagedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(job.StagedWorkIdentity) || job.ProbeReceipt is null)
        {
            return false;
        }

        var sessionRoot = Path.GetDirectoryName(jobPath);
        if (string.IsNullOrWhiteSpace(sessionRoot))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(sessionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, job.StagedWorkIdentity));
        var processingPrefix = normalizedRoot + Path.DirectorySeparatorChar + "processing" + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(processingPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return false;
        }

        var file = new FileInfo(candidate);
        var observation = ExternalAudioImportIdentity.BuildObservationKey(
            candidate,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc));
        if (!string.Equals(observation, job.ProbeReceipt.StagedObservationKey, StringComparison.Ordinal) ||
            !string.Equals(job.Source.ObservationKey, job.ProbeReceipt.SourceObservationKey, StringComparison.Ordinal))
        {
            return false;
        }

        stagedPath = candidate;
        return true;
    }

    private static ExternalAudioImportRecoveryActionResult Rejected(
        ExternalAudioImportRecoveryActionFailure failure,
        string message) => new(false, failure, null, null, message);
}
