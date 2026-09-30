namespace MeetingRecorder.Core.Services;

public enum MeetingAttentionReasonFamily
{
    HardFailure = 0,
    Blocked = 1,
    DataIntegrity = 2,
    ProcessingRecovery = 3,
    Recommendation = 4,
    CosmeticMetadata = 5,
}

public enum MeetingAttentionInboxFreshness
{
    Current = 0,
    RefreshRequired = 1,
}

public enum MeetingAttentionInboxEmptyState
{
    None = 0,
    NoMeetings = 1,
    AllClear = 2,
    RefreshRequired = 3,
}

public sealed record MeetingAttentionInboxItem(
    string MeetingId,
    DateTimeOffset StartedAtUtc,
    bool IsArchived,
    MeetingPresentationState PresentationState,
    MeetingPrimaryRecommendation? Recommendation,
    bool HasSuspiciousSpeakerLabels,
    bool HasSummaryFailure);

public sealed record MeetingAttentionInboxRow(
    string MeetingId,
    MeetingAttentionReasonFamily ReasonFamily,
    MeetingRecommendationSeverity Severity,
    string Label,
    string Explanation,
    MeetingRecommendationActionTarget PrimaryActionTarget,
    bool IsDismissible,
    string GroupKey,
    MeetingAttentionInboxFreshness Freshness);

public sealed record MeetingAttentionInboxState(
    IReadOnlyList<MeetingAttentionInboxRow> Rows,
    MeetingAttentionInboxFreshness Freshness,
    MeetingAttentionInboxEmptyState EmptyState,
    string StatusSummary,
    int HardAttentionCount,
    int RecommendationCount);

/// <summary>
/// Metadata-only triage projection. It never dismisses, dispatches, or changes a meeting.
/// </summary>
public sealed class MeetingAttentionInboxResolver
{
    public MeetingAttentionInboxState Resolve(
        IReadOnlyList<MeetingAttentionInboxItem> items,
        bool isCatalogFresh)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!isCatalogFresh)
        {
            return new(
                Array.Empty<MeetingAttentionInboxRow>(),
                MeetingAttentionInboxFreshness.RefreshRequired,
                MeetingAttentionInboxEmptyState.RefreshRequired,
                "Meeting status needs refresh before attention can be assessed.",
                HardAttentionCount: 0,
                RecommendationCount: 0);
        }

        var rows = items
            .Where(item => !item.IsArchived)
            .Select(TryBuildRow)
            .Where(row => row is not null)
            .Select(row => row!)
            .OrderBy(row => row.ReasonFamily)
            .ThenByDescending(row => row.Severity)
            .ThenByDescending(row => items.First(item => string.Equals(item.MeetingId, row.MeetingId, StringComparison.Ordinal)).StartedAtUtc)
            .ThenBy(row => row.MeetingId, StringComparer.Ordinal)
            .ToArray();

        var emptyState = rows.Length > 0
            ? MeetingAttentionInboxEmptyState.None
            : items.Count == 0
                ? MeetingAttentionInboxEmptyState.NoMeetings
                : MeetingAttentionInboxEmptyState.AllClear;
        var hardAttentionCount = rows.Count(row => row.ReasonFamily is not MeetingAttentionReasonFamily.Recommendation and not MeetingAttentionReasonFamily.CosmeticMetadata);
        var recommendationCount = rows.Count(row => row.ReasonFamily is MeetingAttentionReasonFamily.Recommendation or MeetingAttentionReasonFamily.CosmeticMetadata);
        var summary = emptyState switch
        {
            MeetingAttentionInboxEmptyState.NoMeetings => "No meetings yet.",
            MeetingAttentionInboxEmptyState.AllClear => "All current meetings are clear.",
            _ => $"{rows.Length} meeting(s) need attention: {hardAttentionCount} hard, {recommendationCount} suggested.",
        };

        return new(
            rows,
            MeetingAttentionInboxFreshness.Current,
            emptyState,
            summary,
            hardAttentionCount,
            recommendationCount);
    }

    private static MeetingAttentionInboxRow? TryBuildRow(MeetingAttentionInboxItem item)
    {
        var (family, severity, label, explanation, target) = item.PresentationState switch
        {
            MeetingPresentationState.FailedOrNeedsAttention => (
                MeetingAttentionReasonFamily.HardFailure,
                MeetingRecommendationSeverity.High,
                "Recovery needed",
                "A required meeting artifact needs recovery.",
                MeetingRecommendationActionTarget.MeetingDetails),
            MeetingPresentationState.Blocked => (
                MeetingAttentionReasonFamily.Blocked,
                MeetingRecommendationSeverity.High,
                "Setup is blocking this meeting",
                "Setup is required before processing can finish.",
                MeetingRecommendationActionTarget.SettingsSetup),
            MeetingPresentationState.Processing when item.Recommendation?.Kind == MeetingPrimaryRecommendationKind.ReviewProcessing => (
                MeetingAttentionReasonFamily.ProcessingRecovery,
                MeetingRecommendationSeverity.Medium,
                item.Recommendation.Label,
                item.Recommendation.Reason,
                item.Recommendation.ActionTarget),
            _ => TryBuildRecommendationRow(item),
        };

        if (family is null)
            return null;

        var recommendation = item.Recommendation;
        var isLowRiskSuggestion = recommendation is not null &&
            recommendation.HasPrimaryAction &&
            recommendation.Severity is MeetingRecommendationSeverity.Low or MeetingRecommendationSeverity.Medium &&
            recommendation.Kind is MeetingPrimaryRecommendationKind.ReviewCleanup or
                MeetingPrimaryRecommendationKind.RetrySummary or
                MeetingPrimaryRecommendationKind.ImproveMetadata;
        return new(
            item.MeetingId,
            family.Value,
            severity,
            label,
            explanation,
            target,
            isLowRiskSuggestion && family is MeetingAttentionReasonFamily.Recommendation or MeetingAttentionReasonFamily.CosmeticMetadata,
            family.Value.ToString(),
            MeetingAttentionInboxFreshness.Current);
    }

    private static (MeetingAttentionReasonFamily? Family, MeetingRecommendationSeverity Severity, string Label, string Explanation, MeetingRecommendationActionTarget Target) TryBuildRecommendationRow(MeetingAttentionInboxItem item)
    {
        if (item.HasSummaryFailure)
        {
            return (
                MeetingAttentionReasonFamily.DataIntegrity,
                MeetingRecommendationSeverity.Medium,
                "Summary needs review",
                "A summary attempt needs review before it can be retried.",
                MeetingRecommendationActionTarget.MeetingDetails);
        }

        if (item.HasSuspiciousSpeakerLabels)
        {
            return (
                MeetingAttentionReasonFamily.DataIntegrity,
                MeetingRecommendationSeverity.Medium,
                "Speaker labels need review",
                "Speaker labels may need repair.",
                MeetingRecommendationActionTarget.MeetingDetails);
        }

        var recommendation = item.Recommendation;
        if (recommendation is null || !recommendation.HasPrimaryAction)
            return default;

        var family = recommendation.Kind == MeetingPrimaryRecommendationKind.ImproveMetadata
            ? MeetingAttentionReasonFamily.CosmeticMetadata
            : MeetingAttentionReasonFamily.Recommendation;
        return (family, recommendation.Severity, recommendation.Label, recommendation.Reason, recommendation.ActionTarget);
    }
}
