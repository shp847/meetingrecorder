namespace MeetingRecorder.Core.Services;

public enum BulkOperationKind { EditProject = 0, AddSpeakerLabels = 1, ReTranscribe = 2, Archive = 3, ApplyRecommendations = 4, Merge = 5 }
public enum BulkOperationDisposition { Eligible = 0, Blocked = 1 }
public enum BulkOperationOutcomeKind { Succeeded = 0, Queued = 1, Skipped = 2, Failed = 3, Cancelled = 4 }

public sealed record BulkOperationTarget(string MeetingId, string DisplayTitle, int ArtifactRevision, string SourceView, bool IsEligible, string? BlockReason);
public sealed record BulkOperationPreview(string MeetingId, string DisplayTitle, BulkOperationDisposition Disposition, string Reason, bool IsIrreversibleRisk, bool QueuesWork, string Recoverability);
public sealed record BulkOperationPlan(BulkOperationKind Kind, IReadOnlyList<BulkOperationPreview> Previews, int EligibleCount, int BlockedCount, bool RequiresExplicitReview, string Summary);
public sealed record BulkOperationOutcome(string MeetingId, BulkOperationOutcomeKind Kind, string Detail);
public sealed record BulkOperationResult(IReadOnlyList<BulkOperationOutcome> Outcomes, int SucceededCount, int QueuedCount, int SkippedCount, int FailedCount, int CancelledCount);

/// <summary>Pure, immutable preview/result contract; callers still own revalidation and dispatch.</summary>
public static class BulkOperationPlanner
{
    public static BulkOperationPlan Create(BulkOperationKind kind, IReadOnlyList<BulkOperationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var queuesWork = kind is BulkOperationKind.AddSpeakerLabels or BulkOperationKind.ReTranscribe;
        var previews = targets.Select(target => new BulkOperationPreview(target.MeetingId, target.DisplayTitle,
            target.IsEligible ? BulkOperationDisposition.Eligible : BulkOperationDisposition.Blocked,
            target.IsEligible ? SideEffect(kind) : target.BlockReason ?? "This meeting is no longer eligible.",
            IsIrreversibleRisk(kind), queuesWork, Recoverability(kind))).ToArray();
        var eligible = previews.Count(preview => preview.Disposition == BulkOperationDisposition.Eligible);
        return new(kind, previews, eligible, previews.Length - eligible, RequiresExplicitReview: true,
            eligible == 0 ? "No selected meetings are eligible. Review blocked reasons." : $"{eligible} selected meeting(s) are eligible; review before dispatch.");
    }

    public static BulkOperationResult Summarize(IReadOnlyList<BulkOperationOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return new(outcomes,
            outcomes.Count(item => item.Kind == BulkOperationOutcomeKind.Succeeded), outcomes.Count(item => item.Kind == BulkOperationOutcomeKind.Queued),
            outcomes.Count(item => item.Kind == BulkOperationOutcomeKind.Skipped), outcomes.Count(item => item.Kind == BulkOperationOutcomeKind.Failed),
            outcomes.Count(item => item.Kind == BulkOperationOutcomeKind.Cancelled));
    }

    private static bool IsIrreversibleRisk(BulkOperationKind kind) => kind == BulkOperationKind.Archive;
    private static string Recoverability(BulkOperationKind kind) => kind == BulkOperationKind.Archive ? "Archive recovery depends on the archive receipt." : "Review the meeting details after completion.";
    private static string SideEffect(BulkOperationKind kind) => kind switch
    {
        BulkOperationKind.EditProject => "Updates meeting project metadata.",
        BulkOperationKind.AddSpeakerLabels => "Queues speaker-label work.",
        BulkOperationKind.ReTranscribe => "Queues transcript recovery work.",
        BulkOperationKind.Archive => "Moves published artifacts to the archive.",
        BulkOperationKind.ApplyRecommendations => "Opens the selected cleanup recommendations for review.",
        _ => "Merges the selected meetings after review.",
    };
}
