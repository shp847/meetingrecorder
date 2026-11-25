using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed record MeetingLibraryStatus(string Label, MeetingViewWorkState WorkState);

internal static class MeetingLibraryStatusResolver
{
    public static MeetingLibraryStatus Resolve(
        MeetingOutputRecord meeting,
        IReadOnlyList<CleanupWorkLedgerEntry> cleanup,
        bool automaticCleanupEnabled)
    {
        cleanup = cleanup.Where(item => item.Action != MeetingCleanupAction.GenerateSummary).ToArray();
        if (cleanup.Any(item => item.State == CleanupWorkState.ManualReview ||
            item.State == CleanupWorkState.Failed &&
            (!MeetingCleanupWorkLedgerService.IsRetryableSafeWork(item) ||
             !MeetingCleanupWorkLedgerService.IsTransientFailure(item.Detail) || item.AutomaticRetryCount >= 3)))
            return new("Needs attention", MeetingViewWorkState.Failed);

        var hasRetry = cleanup.Any(item =>
            item.State == CleanupWorkState.Failed &&
            MeetingCleanupWorkLedgerService.IsRetryableSafeWork(item) &&
            MeetingCleanupWorkLedgerService.IsTransientFailure(item.Detail) && item.AutomaticRetryCount < 3 ||
            item.State == CleanupWorkState.Pending && item.AutomaticRetryCount > 0);
        if (hasRetry)
            return new(automaticCleanupEnabled ? "Retry scheduled" : "Cleanup paused", MeetingViewWorkState.RetryNeeded);

        if (meeting.ManifestState is SessionState.Processing or SessionState.Finalizing ||
            cleanup.Any(item => item.State == CleanupWorkState.Processing))
            return new("Processing", MeetingViewWorkState.Processing);
        if (meeting.ManifestState == SessionState.Queued || cleanup.Any(item => item.State == CleanupWorkState.Queued))
            return new("Queued", MeetingViewWorkState.Queued);
        if (meeting.ManifestState == SessionState.Failed)
            return new("Needs attention", MeetingViewWorkState.Failed);

        var hasAudio = !string.IsNullOrWhiteSpace(meeting.AudioPath);
        var hasTranscript = !string.IsNullOrWhiteSpace(meeting.MarkdownPath) || !string.IsNullOrWhiteSpace(meeting.JsonPath);
        if (hasAudio && hasTranscript) return new("Ready", MeetingViewWorkState.Complete);
        if (hasAudio) return new("Audio only", MeetingViewWorkState.Blocked);
        if (hasTranscript) return new("Transcript only", MeetingViewWorkState.Blocked);
        return new("Unavailable", MeetingViewWorkState.Blocked);
    }
}
