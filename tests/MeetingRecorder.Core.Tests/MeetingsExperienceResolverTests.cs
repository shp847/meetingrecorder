using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingsExperienceResolverTests
{
    private readonly MeetingsExperienceResolver _resolver = new();

    [Fact]
    public void Stale_And_Busy_State_Block_Status_Dependent_Actions_Before_Selection_Presentation()
    {
        var stale = _resolver.Resolve(Input(IsCatalogFresh: false, SelectedCount: 1, HasPrimaryRecommendation: true));
        var busy = _resolver.Resolve(Input(IsBusy: true, SelectedCount: 2));

        Assert.Equal(MeetingsExperienceState.RefreshRequired, stale.State);
        Assert.Null(stale.PrimaryAction);
        Assert.Contains(MeetingsExperienceRegion.RefreshStatus, stale.VisibleRegions);
        Assert.Equal(MeetingsExperienceState.Busy, busy.State);
        Assert.Null(busy.PrimaryAction);
        Assert.NotNull(busy.BlockReason);
    }

    [Fact]
    public void Empty_Library_Uses_Search_Focus_And_Never_Fabricates_A_Recommendation()
    {
        var result = _resolver.Resolve(Input(HasMeetings: false));

        Assert.Equal(MeetingsExperienceState.Empty, result.State);
        Assert.Null(result.PrimaryAction);
        Assert.Contains(MeetingsExperienceRegion.EmptyState, result.VisibleRegions);
        Assert.Equal("MeetingSearchTextBox", result.FocusTarget);
    }

    [Fact]
    public void Single_Selection_Promotes_One_Recommendation_Without_Hiding_Action_Families()
    {
        var result = _resolver.Resolve(Input(SelectedCount: 1, HasPrimaryRecommendation: true, PrimaryRecommendationAction: MeetingActionId.RetryTranscript));

        Assert.Equal(MeetingsExperienceState.SingleSelection, result.State);
        Assert.Equal(MeetingActionId.RetryTranscript, result.PrimaryAction);
        Assert.Contains(MeetingsExperienceRegion.PrimaryRecommendation, result.VisibleRegions);
        Assert.Contains(MeetingsExperienceRegion.ActionFamilies, result.VisibleRegions);
    }

    [Fact]
    public void Recording_Protection_Leaves_Recommendation_Visible_But_Never_Promotes_It_For_Dispatch()
    {
        var result = _resolver.Resolve(Input(SelectedCount: 1, HasPrimaryRecommendation: true, PrimaryRecommendationAction: MeetingActionId.Archive, IsRecordingActive: true));

        Assert.Equal(MeetingsExperienceState.SingleSelection, result.State);
        Assert.Null(result.PrimaryAction);
        Assert.NotNull(result.BlockReason);
    }

    [Fact]
    public void Multi_Selection_Uses_Bulk_Surface_And_Never_Reuses_A_Single_Primary_Action()
    {
        var result = _resolver.Resolve(Input(SelectedCount: 3, HasPrimaryRecommendation: true, PrimaryRecommendationAction: MeetingActionId.RetryTranscript));

        Assert.Equal(MeetingsExperienceState.MultiSelection, result.State);
        Assert.Null(result.PrimaryAction);
        Assert.Contains(MeetingsExperienceRegion.BulkActions, result.VisibleRegions);
    }

    [Fact]
    public void Catalog_Ownership_Keeps_Cleanup_And_Destructive_Actions_Separate()
    {
        var catalog = new MeetingActionCatalog();

        Assert.Equal(MeetingsActionOwner.CleanupReview, MeetingsExperienceResolver.GetOwner(catalog.All.Single(entry => entry.Id == MeetingActionId.ReviewRecommendation)));
        Assert.Equal(MeetingsActionOwner.DestructiveExplicit, MeetingsExperienceResolver.GetOwner(catalog.All.Single(entry => entry.Id == MeetingActionId.DeletePermanently)));
        Assert.Equal(MeetingsActionOwner.MultiSelection, MeetingsExperienceResolver.GetOwner(catalog.All.Single(entry => entry.Id == MeetingActionId.MergeSelected)));
    }

    private static MeetingsExperienceInput Input(
        int SelectedCount = 0,
        bool HasMeetings = true,
        bool IsCatalogFresh = true,
        bool IsBusy = false,
        bool IsDetailOpen = false,
        bool HasPrimaryRecommendation = false,
        MeetingActionId? PrimaryRecommendationAction = null,
        bool HasFailedOrBlockedWork = false,
        bool IsRecordingActive = false) =>
        new(SelectedCount, HasMeetings, IsCatalogFresh, IsBusy, IsDetailOpen, HasPrimaryRecommendation, PrimaryRecommendationAction, HasFailedOrBlockedWork, IsRecordingActive);
}
