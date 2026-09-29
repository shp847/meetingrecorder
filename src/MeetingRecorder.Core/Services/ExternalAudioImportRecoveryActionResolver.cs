using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Safe, path-free facts used to select recovery controls. File existence,
/// path policy, receipts, revision, and lease are revalidated by the action
/// executor; this resolver only makes the current consequence understandable.
/// </summary>
public sealed record ExternalAudioImportRecoveryActionSnapshot(
    ExternalAudioImportJob? Job,
    ExternalAudioImportJobSchemaCompatibility? Schema,
    bool HasVerifiedStagedInput,
    bool HasSafeOriginalSource,
    bool HasCurrentPublishedArtifacts);

public enum ExternalAudioImportRecoveryActionKind
{
    None = 0,
    RetryFromStagedWork = 1,
    RetryFromOriginal = 2,
    ReplaceSource = 3,
    OpenSourceLocation = 4,
    OpenSetup = 5,
    RemoveDraft = 6,
}

public sealed record ExternalAudioImportRecoveryAction(
    ExternalAudioImportRecoveryActionKind Kind,
    string Label,
    string Consequence,
    bool IsEnabled,
    bool RequiresConfirmation,
    bool RequiresRevalidation,
    string? DisabledReason = null);

public sealed record ExternalAudioImportRecoveryActionPlan(
    string Summary,
    ExternalAudioImportRecoveryAction PrimaryAction,
    IReadOnlyList<ExternalAudioImportRecoveryAction> Actions);

/// <summary>
/// Maps a durable import state to named, safe recovery controls. It never
/// exposes a locator and it never authorizes source mutation: all retry paths
/// either use verified app-owned work or re-observe and copy an external file.
/// </summary>
public static class ExternalAudioImportRecoveryActionResolver
{
    public static ExternalAudioImportRecoveryActionPlan Resolve(
        ExternalAudioImportRecoveryActionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Job is null || snapshot.Schema is null || !snapshot.Schema.CanRead)
        {
            return Locked("This import record cannot be read. Recover it with a compatible version before changing it.");
        }

        if (!snapshot.Schema.CanWrite || snapshot.Job.IsReadOnly)
        {
            return Locked(string.IsNullOrWhiteSpace(snapshot.Schema.RecoveryText)
                ? "This import record is read-only and cannot be changed by this version."
                : snapshot.Schema.RecoveryText);
        }

        if (snapshot.Job.State == ExternalAudioImportJobState.Published && snapshot.HasCurrentPublishedArtifacts)
        {
            return NoAction("This import has current published artifacts. Use normal meeting controls for the published meeting.");
        }

        return snapshot.Job.State switch
        {
            ExternalAudioImportJobState.Failed => Failed(snapshot),
            ExternalAudioImportJobState.Changing or ExternalAudioImportJobState.SourceMissing => SourceNeedsReview(snapshot),
            ExternalAudioImportJobState.BlockedBySetup => SetupBlocked(),
            ExternalAudioImportJobState.PendingReview or ExternalAudioImportJobState.Probing or ExternalAudioImportJobState.ReadyToQueue =>
                ReviewPending(snapshot),
            ExternalAudioImportJobState.Published => NoAction(
                "This import is marked published, but its artifacts need repair through normal meeting recovery."),
            ExternalAudioImportJobState.Removed => NoAction(
                "This import was removed and cannot be recovered from the import queue."),
            ExternalAudioImportJobState.Queued or ExternalAudioImportJobState.Processing => NoAction(
                "This import is already active. Its current queue state will update when processing reaches a safe checkpoint."),
            _ => Locked("This import has an unknown state and needs manual recovery before it can be changed."),
        };
    }

    private static ExternalAudioImportRecoveryActionPlan Failed(
        ExternalAudioImportRecoveryActionSnapshot snapshot)
    {
        var actions = new List<ExternalAudioImportRecoveryAction>
        {
            RetryFromStaged(snapshot.HasVerifiedStagedInput),
            RetryFromOriginal(snapshot.HasSafeOriginalSource),
            ReplaceSource(),
            RemoveDraft(),
        };
        if (snapshot.HasSafeOriginalSource)
        {
            actions.Add(OpenSource());
        }

        var primary = snapshot.HasVerifiedStagedInput
            ? actions[0]
            : snapshot.HasSafeOriginalSource
                ? actions[1]
                : actions[2];
        var summary = snapshot.HasVerifiedStagedInput
            ? "Retry this failed import from its verified app-owned staged copy."
            : snapshot.HasSafeOriginalSource
                ? "The staged copy is unavailable; recheck the current original before retrying."
                : "Neither a verified staged copy nor the original source is available. Select a replacement source to recover this import.";
        return new ExternalAudioImportRecoveryActionPlan(summary, primary, actions);
    }

    private static ExternalAudioImportRecoveryActionPlan SourceNeedsReview(
        ExternalAudioImportRecoveryActionSnapshot snapshot)
    {
        var actions = new List<ExternalAudioImportRecoveryAction>
        {
            RetryFromOriginal(snapshot.HasSafeOriginalSource),
            ReplaceSource(),
            RemoveDraft(),
        };
        if (snapshot.HasSafeOriginalSource)
        {
            actions.Add(OpenSource());
        }

        var primary = snapshot.HasSafeOriginalSource ? actions[0] : actions[1];
        var summary = snapshot.HasSafeOriginalSource
            ? "The source observation changed. Recheck it before retrying this import."
            : "The current source is unavailable. Select a replacement source to recover this import.";
        return new ExternalAudioImportRecoveryActionPlan(summary, primary, actions);
    }

    private static ExternalAudioImportRecoveryActionPlan SetupBlocked()
    {
        var setup = new ExternalAudioImportRecoveryAction(
            ExternalAudioImportRecoveryActionKind.OpenSetup,
            "Open Setup",
            "Opens transcription setup. This import stays blocked until setup is checked again.",
            IsEnabled: true,
            RequiresConfirmation: false,
            RequiresRevalidation: false);
        return new ExternalAudioImportRecoveryActionPlan(
            "This import is waiting for transcription setup. It will not queue automatically.",
            setup,
            [setup]);
    }

    private static ExternalAudioImportRecoveryActionPlan ReviewPending(
        ExternalAudioImportRecoveryActionSnapshot snapshot)
    {
        var actions = new List<ExternalAudioImportRecoveryAction> { RemoveDraft() };
        if (snapshot.HasSafeOriginalSource)
        {
            actions.Add(OpenSource());
        }

        return new ExternalAudioImportRecoveryActionPlan(
            "Review this import before it enters processing.",
            None(),
            actions);
    }

    private static ExternalAudioImportRecoveryAction RetryFromStaged(bool isAvailable) => isAvailable
        ? new ExternalAudioImportRecoveryAction(
            ExternalAudioImportRecoveryActionKind.RetryFromStagedWork,
            "Retry from work copy",
            "Rechecks the verified app-owned staged copy, then queues a new attempt. The original file stays in place.",
            IsEnabled: true,
            RequiresConfirmation: false,
            RequiresRevalidation: true)
        : Disabled(
            ExternalAudioImportRecoveryActionKind.RetryFromStagedWork,
            "Retry from work copy",
            "A verified app-owned staged copy is required before this import can retry from work.");

    private static ExternalAudioImportRecoveryAction RetryFromOriginal(bool isAvailable) => isAvailable
        ? new ExternalAudioImportRecoveryAction(
            ExternalAudioImportRecoveryActionKind.RetryFromOriginal,
            "Retry from original",
            "Rechecks and copies the current original source into app-owned work before retrying. The original file stays in place.",
            IsEnabled: true,
            RequiresConfirmation: false,
            RequiresRevalidation: true)
        : Disabled(
            ExternalAudioImportRecoveryActionKind.RetryFromOriginal,
            "Retry from original",
            "The current original source is unavailable. Select a replacement source instead.");

    private static ExternalAudioImportRecoveryAction ReplaceSource() => new(
        ExternalAudioImportRecoveryActionKind.ReplaceSource,
        "Replace source",
        "Selects and rechecks a replacement file, preserving this import's metadata while leaving the old source untouched.",
        IsEnabled: true,
        RequiresConfirmation: false,
        RequiresRevalidation: true);

    private static ExternalAudioImportRecoveryAction OpenSource() => new(
        ExternalAudioImportRecoveryActionKind.OpenSourceLocation,
        "Open source location",
        "Opens the current local source location only after a local path and existence check. No file is changed.",
        IsEnabled: true,
        RequiresConfirmation: false,
        RequiresRevalidation: true);

    private static ExternalAudioImportRecoveryAction RemoveDraft() => new(
        ExternalAudioImportRecoveryActionKind.RemoveDraft,
        "Remove draft",
        "Removes only this unqueued import record and its app-owned staged copy. The original file stays in place.",
        IsEnabled: true,
        RequiresConfirmation: true,
        RequiresRevalidation: true);

    private static ExternalAudioImportRecoveryActionPlan Locked(string summary)
    {
        var disabled = new[]
        {
            Disabled(ExternalAudioImportRecoveryActionKind.RetryFromStagedWork, "Retry from work copy", summary),
            Disabled(ExternalAudioImportRecoveryActionKind.RetryFromOriginal, "Retry from original", summary),
            Disabled(ExternalAudioImportRecoveryActionKind.ReplaceSource, "Replace source", summary),
            Disabled(ExternalAudioImportRecoveryActionKind.RemoveDraft, "Remove draft", summary),
        };
        return new ExternalAudioImportRecoveryActionPlan(summary, None(), disabled);
    }

    private static ExternalAudioImportRecoveryActionPlan NoAction(string summary) => new(summary, None(), Array.Empty<ExternalAudioImportRecoveryAction>());

    private static ExternalAudioImportRecoveryAction None() => new(
        ExternalAudioImportRecoveryActionKind.None,
        string.Empty,
        string.Empty,
        IsEnabled: false,
        RequiresConfirmation: false,
        RequiresRevalidation: false);

    private static ExternalAudioImportRecoveryAction Disabled(
        ExternalAudioImportRecoveryActionKind kind,
        string label,
        string disabledReason) => new(
        kind,
        label,
        string.Empty,
        IsEnabled: false,
        RequiresConfirmation: false,
        RequiresRevalidation: true,
        disabledReason);
}
