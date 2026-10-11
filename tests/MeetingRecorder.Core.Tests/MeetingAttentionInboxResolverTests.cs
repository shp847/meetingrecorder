using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingAttentionInboxResolverTests
{
    private readonly MeetingAttentionInboxResolver _resolver = new();

    [Fact]
    public void Resolve_Orders_One_Row_Per_Meeting_By_Hard_State_Before_Suggestions()
    {
        var state = _resolver.Resolve(
        [
            Item("cleanup", MeetingPresentationState.NeedsAction, Recommendation(MeetingPrimaryRecommendationKind.ReviewCleanup, MeetingRecommendationSeverity.Low)),
            Item("blocked", MeetingPresentationState.Blocked, Recommendation(MeetingPrimaryRecommendationKind.ImproveMetadata, MeetingRecommendationSeverity.Low)),
            Item("failure", MeetingPresentationState.FailedOrNeedsAttention, Recommendation(MeetingPrimaryRecommendationKind.ReviewCleanup, MeetingRecommendationSeverity.Low)),
        ], isCatalogFresh: true);

        Assert.Equal(["failure", "blocked", "cleanup"], state.Rows.Select(row => row.MeetingId));
        Assert.Equal(2, state.HardAttentionCount);
        Assert.Equal(1, state.RecommendationCount);
        Assert.All(state.Rows, row => Assert.Equal(MeetingAttentionInboxFreshness.Current, row.Freshness));
    }

    [Fact]
    public void Resolve_Does_Not_Hide_A_Hard_Failure_Behind_A_Dismissible_Recommendation()
    {
        var state = _resolver.Resolve(
        [
            Item("failed", MeetingPresentationState.FailedOrNeedsAttention, Recommendation(MeetingPrimaryRecommendationKind.ReviewCleanup, MeetingRecommendationSeverity.Low)),
        ], isCatalogFresh: true);

        var row = Assert.Single(state.Rows);
        Assert.Equal(MeetingAttentionReasonFamily.HardFailure, row.ReasonFamily);
        Assert.False(row.IsDismissible);
        Assert.Equal(MeetingRecommendationActionTarget.MeetingDetails, row.PrimaryActionTarget);
    }

    [Fact]
    public void Resolve_Allows_Dismissal_Only_For_Current_Low_Risk_Suggestions()
    {
        var state = _resolver.Resolve(
        [
            Item("cleanup", MeetingPresentationState.NeedsAction, Recommendation(MeetingPrimaryRecommendationKind.ReviewCleanup, MeetingRecommendationSeverity.Low)),
            Item("labels", MeetingPresentationState.NeedsAction, Recommendation(MeetingPrimaryRecommendationKind.RepairSpeakerLabels, MeetingRecommendationSeverity.High)),
        ], isCatalogFresh: true);

        Assert.True(state.Rows.Single(row => row.MeetingId == "cleanup").IsDismissible);
        Assert.False(state.Rows.Single(row => row.MeetingId == "labels").IsDismissible);
    }

    [Fact]
    public void Resolve_Reports_Refresh_Required_Instead_Of_All_Clear_For_Stale_Catalog()
    {
        var state = _resolver.Resolve([Item("complete", MeetingPresentationState.Complete)], isCatalogFresh: false);

        Assert.Empty(state.Rows);
        Assert.Equal(MeetingAttentionInboxEmptyState.RefreshRequired, state.EmptyState);
        Assert.Equal(MeetingAttentionInboxFreshness.RefreshRequired, state.Freshness);
        Assert.DoesNotContain("clear", state.StatusSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, MeetingAttentionInboxEmptyState.NoMeetings)]
    [InlineData(true, MeetingAttentionInboxEmptyState.AllClear)]
    public void Resolve_Uses_Truthful_Empty_State(bool hasCompleteMeeting, MeetingAttentionInboxEmptyState expected)
    {
        var items = hasCompleteMeeting
            ? new[] { Item("complete", MeetingPresentationState.Complete) }
            : Array.Empty<MeetingAttentionInboxItem>();

        var state = _resolver.Resolve(items, isCatalogFresh: true);

        Assert.Equal(expected, state.EmptyState);
    }

    [Fact]
    public void Resolve_Excludes_Archived_Meetings()
    {
        var state = _resolver.Resolve([Item("archived", MeetingPresentationState.FailedOrNeedsAttention, isArchived: true)], isCatalogFresh: true);

        Assert.Empty(state.Rows);
        Assert.Equal(MeetingAttentionInboxEmptyState.AllClear, state.EmptyState);
    }

    private static MeetingAttentionInboxItem Item(
        string id,
        MeetingPresentationState presentationState,
        MeetingPrimaryRecommendation? recommendation = null,
        bool isArchived = false) =>
        new(id, new DateTimeOffset(2026, 9, 29, 12, id.Length, 0, TimeSpan.Zero), isArchived, presentationState, recommendation, false, false);

    private static MeetingPrimaryRecommendation Recommendation(MeetingPrimaryRecommendationKind kind, MeetingRecommendationSeverity severity) =>
        new(kind, "Review this meeting", "A safe review is available.", severity,
            MeetingRecommendationActionTarget.MeetingDetails, true, null, kind.ToString(),
            MeetingRecommendationResolver.CurrentRecommendationVersion, 1, DateTimeOffset.UtcNow, false,
            "test", MeetingRecommendationScope.SingleMeeting, MeetingRecommendationFreshness.Current);
}
