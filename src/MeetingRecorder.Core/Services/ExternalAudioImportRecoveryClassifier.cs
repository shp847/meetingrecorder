using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Safe, path-free input for import recovery. Callers validate files, ready
/// markers, and any future lease separately, then hand this classifier only
/// the facts needed to decide whether startup may enqueue work.
/// </summary>
public sealed record ExternalAudioImportRecoverySnapshot(
    ExternalAudioImportJob? Job,
    ExternalAudioImportJobSchemaCompatibility? Schema,
    SessionState? ManifestState,
    bool HasVerifiedStagedInput,
    bool HasCurrentPublishedArtifacts);

public enum ExternalAudioImportRecoveryDisposition
{
    AwaitUser = 0,
    ResumeQueued = 1,
    ResumeProcessing = 2,
    RetryFromStagedWork = 3,
    BlockedBySetup = 4,
    Published = 5,
    Removed = 6,
    ManualRecovery = 7,
}

public sealed record ExternalAudioImportRecoveryDecision(
    ExternalAudioImportRecoveryDisposition Disposition,
    string Message)
{
    public bool MayEnqueue => Disposition is
        ExternalAudioImportRecoveryDisposition.ResumeQueued or
        ExternalAudioImportRecoveryDisposition.ResumeProcessing;
}

/// <summary>
/// Classifies durable import state before startup takes any action. It is
/// deliberately pure: no filesystem mutation, source access, job write, or
/// worker launch can occur before its decision is revalidated by a caller.
/// </summary>
public static class ExternalAudioImportRecoveryClassifier
{
    public static ExternalAudioImportRecoveryDecision Classify(
        ExternalAudioImportRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Job is null || snapshot.Schema is null || !snapshot.Schema.CanRead)
        {
            return Manual("This imported recording needs recovery because its import record cannot be read.");
        }

        var job = snapshot.Job;
        if (job.IsReadOnly || !snapshot.Schema.CanWrite)
        {
            return Manual("This imported recording needs recovery because its import record is not writable by this version.");
        }

        // A complete current publication wins over a stale queued/processing
        // checkpoint left by a crash between publish and job persistence.
        if (snapshot.ManifestState == SessionState.Published && snapshot.HasCurrentPublishedArtifacts)
        {
            return new ExternalAudioImportRecoveryDecision(
                ExternalAudioImportRecoveryDisposition.Published,
                "Published artifacts are current; this import is not backlog.");
        }

        return job.State switch
        {
            ExternalAudioImportJobState.Published => snapshot.HasCurrentPublishedArtifacts
                ? new ExternalAudioImportRecoveryDecision(
                    ExternalAudioImportRecoveryDisposition.Published,
                    "Published artifacts are current; this import is not backlog.")
                : Manual("This import is marked published but its current artifacts need repair."),
            ExternalAudioImportJobState.Removed => new ExternalAudioImportRecoveryDecision(
                ExternalAudioImportRecoveryDisposition.Removed,
                "This import was removed and will not return to the backlog."),
            ExternalAudioImportJobState.BlockedBySetup => new ExternalAudioImportRecoveryDecision(
                ExternalAudioImportRecoveryDisposition.BlockedBySetup,
                "This import is waiting for transcription setup before it can be resumed."),
            ExternalAudioImportJobState.Queued => Resume(
                snapshot.HasVerifiedStagedInput,
                ExternalAudioImportRecoveryDisposition.ResumeQueued,
                "This verified import can resume from the queue."),
            ExternalAudioImportJobState.Processing => Resume(
                snapshot.HasVerifiedStagedInput,
                ExternalAudioImportRecoveryDisposition.ResumeProcessing,
                "This verified import can resume interrupted processing."),
            ExternalAudioImportJobState.Failed => snapshot.HasVerifiedStagedInput
                ? new ExternalAudioImportRecoveryDecision(
                    ExternalAudioImportRecoveryDisposition.RetryFromStagedWork,
                    "This failed import has a verified staged copy and can be retried explicitly.")
                : Manual("This failed import has no verified staged copy. Select a replacement source to recover it."),
            ExternalAudioImportJobState.PendingReview or
            ExternalAudioImportJobState.Probing or
            ExternalAudioImportJobState.ReadyToQueue or
            ExternalAudioImportJobState.Changing or
            ExternalAudioImportJobState.SourceMissing => new ExternalAudioImportRecoveryDecision(
                ExternalAudioImportRecoveryDisposition.AwaitUser,
                "This import needs review before it can enter processing."),
            _ => Manual("This imported recording has an unknown recovery state."),
        };
    }

    private static ExternalAudioImportRecoveryDecision Resume(
        bool hasVerifiedStagedInput,
        ExternalAudioImportRecoveryDisposition disposition,
        string successMessage) => hasVerifiedStagedInput
        ? new ExternalAudioImportRecoveryDecision(disposition, successMessage)
        : Manual("This import cannot resume because its verified staged copy is unavailable.");

    private static ExternalAudioImportRecoveryDecision Manual(string message) => new(
        ExternalAudioImportRecoveryDisposition.ManualRecovery,
        message);
}
