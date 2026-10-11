using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingActionCatalogTests
{
    private readonly MeetingActionCatalog _catalog = new();

    [Fact]
    public void Catalog_Maps_Every_Action_Exactly_Once_With_A_Family_And_Accessible_Label()
    {
        var entries = _catalog.All;

        Assert.Equal(Enum.GetValues<MeetingActionId>().OrderBy(id => id), entries.Select(entry => entry.Id).OrderBy(id => id));
        Assert.All(entries, entry =>
        {
            Assert.True(Enum.IsDefined(entry.Family));
            Assert.True(Enum.IsDefined(entry.OutcomeTarget));
            Assert.False(string.IsNullOrWhiteSpace(entry.AccessibleLabel));
        });
        Assert.Equal(MeetingActionFamily.Danger, entries.Single(entry => entry.Id == MeetingActionId.DeletePermanently).Family);
        Assert.Equal(MeetingActionConfirmationPolicy.TypedPermanentDelete, entries.Single(entry => entry.Id == MeetingActionId.DeletePermanently).ConfirmationPolicy);
        Assert.Equal(MeetingActionConfirmationPolicy.TypedPermanentDelete, entries.Single(entry => entry.Id == MeetingActionId.DeleteSelectedPermanently).ConfirmationPolicy);
    }

    [Fact]
    public void Resolve_Requires_Exactly_One_Row_For_Single_Meeting_Actions()
    {
        var noSelection = _catalog.Resolve(Input(selectedCount: 0));
        var multiSelection = _catalog.Resolve(Input(selectedCount: 2));
        var singleSelection = _catalog.Resolve(Input(selectedCount: 1));

        Assert.False(noSelection[MeetingActionId.OpenDetails].IsEligible);
        Assert.False(multiSelection[MeetingActionId.Archive].IsEligible);
        Assert.True(singleSelection[MeetingActionId.OpenDetails].IsEligible);
        Assert.True(singleSelection[MeetingActionId.Archive].IsEligible);
        Assert.Equal("Select exactly one meeting first.", multiSelection[MeetingActionId.Archive].BlockReason);
    }

    [Fact]
    public void Resolve_Blocks_Every_Action_While_Busy()
    {
        var state = _catalog.Resolve(Input(selectedCount: 1, isBusy: true));

        Assert.All(state.Actions, action =>
        {
            Assert.False(action.IsEligible);
            Assert.Equal("Wait for the current meeting action to finish.", action.BlockReason);
        });
    }

    [Fact]
    public void Resolve_Explains_Missing_Artifacts_Without_Offering_An_Open_Action()
    {
        var state = _catalog.Resolve(Input(selectedCount: 1, canOpenAudio: false, canOpenTranscript: false));

        Assert.False(state[MeetingActionId.OpenTranscript].IsEligible);
        Assert.False(state[MeetingActionId.OpenAudio].IsEligible);
        Assert.False(state[MeetingActionId.OpenContainingFolder].IsEligible);
        Assert.Contains("artifact is unavailable", state[MeetingActionId.OpenTranscript].BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Reports_Mixed_Bulk_Eligibility_With_First_Block_Reason()
    {
        var availability = new Dictionary<MeetingActionId, MeetingActionSelectionAvailability>
        {
            [MeetingActionId.AddSpeakerLabelsToSelected] = new(2, 1, "One selected meeting has no eligible transcript."),
        };
        var state = _catalog.Resolve(Input(
            selectedCount: 3,
            selectionAvailability: availability));
        var action = state[MeetingActionId.AddSpeakerLabelsToSelected];

        Assert.True(action.IsEligible);
        Assert.Equal(3, action.SelectedCount);
        Assert.Equal(2, action.EligibleCount);
        Assert.Equal(1, action.BlockedCount);
        Assert.Equal("One selected meeting has no eligible transcript.", action.BlockReason);
    }

    [Fact]
    public void Resolve_Does_Not_Offer_Bulk_Action_When_No_Selected_Row_Is_Eligible()
    {
        var availability = new Dictionary<MeetingActionId, MeetingActionSelectionAvailability>
        {
            [MeetingActionId.ArchiveSelected] = new(0, 2, "Selected meetings are already archived."),
        };
        var state = _catalog.Resolve(Input(selectedCount: 2, selectionAvailability: availability));
        var action = state[MeetingActionId.ArchiveSelected];

        Assert.False(action.IsEligible);
        Assert.Equal("Selected meetings are already archived.", action.BlockReason);
        Assert.Equal(2, action.BlockedCount);
    }

    [Fact]
    public void Resolve_Uses_Separate_Processing_Actions_For_Mark_And_Clear_Asap()
    {
        var mark = _catalog.Resolve(Input(selectedCount: 1, canChangeRushProcessing: true));
        var clear = _catalog.Resolve(Input(selectedCount: 1, isMarkedAsap: true));

        Assert.True(mark[MeetingActionId.ProcessAsap].IsEligible);
        Assert.False(mark[MeetingActionId.ClearAsap].IsEligible);
        Assert.False(clear[MeetingActionId.ProcessAsap].IsEligible);
        Assert.True(clear[MeetingActionId.ClearAsap].IsEligible);
    }

    private static MeetingActionCatalogInput Input(
        int selectedCount = 1,
        bool isBusy = false,
        bool canOpenAudio = true,
        bool canOpenTranscript = true,
        bool canChangeRushProcessing = false,
        bool isMarkedAsap = false,
        IReadOnlyDictionary<MeetingActionId, MeetingActionSelectionAvailability>? selectionAvailability = null) =>
        new(
            selectedCount,
            isBusy,
            HasFocusedMeeting: true,
            canOpenAudio,
            canOpenTranscript,
            HasPrimaryRecommendation: true,
            CanRegenerateTranscript: true,
            CanSplit: true,
            CanAddSpeakerLabels: true,
            canChangeRushProcessing,
            isMarkedAsap,
            HasCleanupRecommendations: true,
            CanMergeSelected: selectedCount >= 2,
            CanReTranscribeSelected: selectedCount > 0,
            CanAddSpeakerLabelsToSelected: selectedCount > 0,
            CanArchiveSelected: selectedCount > 0,
            CanDeleteSelectedPermanently: selectedCount > 0,
            HasProcessingBacklog: true,
            selectionAvailability);
}
