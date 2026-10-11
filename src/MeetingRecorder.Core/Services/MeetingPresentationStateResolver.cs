namespace MeetingRecorder.Core.Services;

public enum MeetingPresentationState
{
    RefreshRequired = 0,
    Archived = 1,
    FailedOrNeedsAttention = 2,
    Blocked = 3,
    Processing = 4,
    NeedsAction = 5,
    Complete = 6,
    Unavailable = 7,
}

public enum MeetingPresentationQueueState { None = 0, Queued = 1, Running = 2, Paused = 3 }

public sealed record MeetingPresentationStateInput(
    bool HasCatalogEntry,
    bool IsCatalogFresh,
    bool IsArchived,
    bool HasArtifactFailure,
    bool IsQueueFresh,
    MeetingPresentationQueueState QueueState,
    bool IsSetupBlocked,
    bool HasActionableRecommendation,
    bool HasReadableAudio,
    bool HasReadableTranscript);

public sealed record MeetingPresentationStateResult(
    MeetingPresentationState State,
    string Label,
    string Explanation,
    MeetingActionId? PrimaryAction,
    string AccessibleDescription,
    string DiagnosticReasonCode);

/// <summary>Metadata-only row truth. Unknown or stale inputs never report Complete.</summary>
public sealed class MeetingPresentationStateResolver
{
    public MeetingPresentationStateResult Resolve(MeetingPresentationStateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.HasCatalogEntry)
            return Result(MeetingPresentationState.Unavailable, "Unavailable", "Meeting data is unavailable.", null, "catalog-entry-missing");
        if (!input.IsCatalogFresh || !input.IsQueueFresh)
            return Result(MeetingPresentationState.RefreshRequired, "Status needs refresh", "Refresh meeting status before acting on it.", null, "stale-catalog-or-queue");
        if (input.IsArchived)
            return Result(MeetingPresentationState.Archived, "Archived", "This meeting is archived.", MeetingActionId.OpenDetails, "archived");
        if (input.HasArtifactFailure)
            return Result(MeetingPresentationState.FailedOrNeedsAttention, "Needs attention", "A required meeting artifact needs recovery.", MeetingActionId.RetryTranscript, "artifact-failure");
        if (input.IsSetupBlocked)
            return Result(MeetingPresentationState.Blocked, "Blocked", "Setup is required before this meeting can finish processing.", MeetingActionId.OpenDetails, "setup-blocked");
        if (input.QueueState is MeetingPresentationQueueState.Queued or MeetingPresentationQueueState.Running or MeetingPresentationQueueState.Paused)
            return Result(MeetingPresentationState.Processing, input.QueueState == MeetingPresentationQueueState.Running ? "Processing" : "Waiting to process", "Processing status is current.", null, "queue-active");
        if (input.HasActionableRecommendation)
            return Result(MeetingPresentationState.NeedsAction, "Needs action", "One safe next action is available.", MeetingActionId.ReviewRecommendation, "actionable-recommendation");
        if (input.HasReadableAudio && input.HasReadableTranscript)
            return Result(MeetingPresentationState.Complete, "Complete", "Transcript and recording are ready.", MeetingActionId.OpenDetails, "artifacts-complete");
        return Result(MeetingPresentationState.Unavailable, "Unavailable", "Expected meeting artifacts are unavailable.", MeetingActionId.OpenDetails, "expected-artifact-missing");
    }

    private static MeetingPresentationStateResult Result(MeetingPresentationState state, string label, string explanation, MeetingActionId? action, string code) =>
        new(state, label, explanation, action, $"{label}. {explanation}", code);
}
