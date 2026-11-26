using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed record CleanupReviewState(string Label, bool NeedsAttention);

internal static class CleanupReviewProjection
{
    public static CleanupReviewState Resolve(MeetingCleanupRecommendation recommendation, CleanupWorkLedgerEntry? entry, bool isScheduled = true)
    {
        if (entry?.State == CleanupWorkState.ManualReview)
            return new("Needs review", true);
        if (entry?.State == CleanupWorkState.Failed)
            return new("Failed; review", true);
        if (entry?.State == CleanupWorkState.Processing)
            return new("In progress", false);
        if (entry?.State == CleanupWorkState.Queued)
            return new("Queued", false);
        if (recommendation.Action is (MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels) &&
            !recommendation.CanApplyAutomatically)
            return new("Input needs review", true);
        if (entry?.State == CleanupWorkState.Completed)
            return new("Completed; checking", false);
        return recommendation.CanApplyAutomatically && recommendation.Confidence == MeetingCleanupConfidence.High
            ? new(isScheduled ? "Automatic backlog" : "Automation paused", false)
            : new("Optional suggestion", false);
    }
}
