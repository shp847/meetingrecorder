namespace MeetingRecorder.Core.Services;

public enum MeetingsExperienceState
{
    Library = 0,
    SingleSelection = 1,
    MultiSelection = 2,
    Detail = 3,
    Busy = 4,
    RefreshRequired = 5,
    Empty = 6,
}

public enum MeetingsExperienceRegion
{
    Search = 0,
    MeetingList = 1,
    PrimaryRecommendation = 2,
    ActionFamilies = 3,
    BulkActions = 4,
    RefreshStatus = 5,
    EmptyState = 6,
}

public enum MeetingsActionOwner
{
    Library = 0,
    SingleSelection = 1,
    MultiSelection = 2,
    Detail = 3,
    CleanupReview = 4,
    Advanced = 5,
    DestructiveExplicit = 6,
}

public sealed record MeetingsExperienceInput(
    int SelectedCount,
    bool HasMeetings,
    bool IsCatalogFresh,
    bool IsBusy,
    bool IsDetailOpen,
    bool HasPrimaryRecommendation,
    MeetingActionId? PrimaryRecommendationAction,
    bool HasFailedOrBlockedWork,
    bool IsRecordingActive);

public sealed record MeetingsExperienceResult(
    MeetingsExperienceState State,
    IReadOnlyList<MeetingsExperienceRegion> VisibleRegions,
    MeetingActionId? PrimaryAction,
    IReadOnlyList<MeetingActionFamily> SecondaryActionFamilies,
    string Summary,
    string? BlockReason,
    string FocusTarget);

/// <summary>
/// Pure workbench contract. It decides presentation and ownership only; callers
/// still resolve catalog eligibility and invoke existing explicit commands.
/// </summary>
public sealed class MeetingsExperienceResolver
{
    public MeetingsExperienceResult Resolve(MeetingsExperienceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var selectedCount = Math.Max(0, input.SelectedCount);

        if (!input.IsCatalogFresh)
        {
            return Result(
                MeetingsExperienceState.RefreshRequired,
                [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.RefreshStatus],
                null,
                "Meeting status needs refresh.",
                "Refresh before using a status-dependent meeting action.",
                "RefreshMeetingsButton");
        }

        if (input.IsBusy)
        {
            return Result(
                MeetingsExperienceState.Busy,
                [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.RefreshStatus],
                null,
                "A meeting action is in progress.",
                "Wait for the current meeting action to finish.",
                "MeetingsListView");
        }

        if (!input.HasMeetings)
        {
            return Result(
                MeetingsExperienceState.Empty,
                [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.EmptyState],
                null,
                "No meetings match the current view.",
                null,
                "MeetingSearchTextBox");
        }

        if (input.IsDetailOpen && selectedCount == 1)
        {
            return Result(
                MeetingsExperienceState.Detail,
                [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.ActionFamilies],
                null,
                "Meeting details are open.",
                null,
                "MeetingDetailWindow");
        }

        if (selectedCount > 1)
        {
            return Result(
                MeetingsExperienceState.MultiSelection,
                [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.BulkActions, MeetingsExperienceRegion.ActionFamilies],
                null,
                $"{selectedCount} meetings selected.",
                input.IsRecordingActive ? "Recording protection may block some processing actions." : null,
                "MeetingsListView");
        }

        if (selectedCount == 1)
        {
            var primaryAction = input.HasPrimaryRecommendation && !input.IsRecordingActive
                ? input.PrimaryRecommendationAction
                : null;
            return Result(
                MeetingsExperienceState.SingleSelection,
                primaryAction is null
                    ? [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.ActionFamilies]
                    : [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.PrimaryRecommendation, MeetingsExperienceRegion.ActionFamilies],
                primaryAction,
                primaryAction is null && input.HasPrimaryRecommendation && input.IsRecordingActive
                    ? "Recording protection keeps the recommendation available without dispatching it."
                    : input.HasFailedOrBlockedWork
                        ? "Review the selected meeting's recovery options."
                        : "Review meeting details or available actions.",
                input.IsRecordingActive && input.HasPrimaryRecommendation
                    ? "Stop or finish the active recording before applying this recommendation."
                    : null,
                primaryAction is null ? "MeetingsListView" : "MeetingPrimaryActionButton");
        }

        return Result(
            MeetingsExperienceState.Library,
            [MeetingsExperienceRegion.Search, MeetingsExperienceRegion.MeetingList, MeetingsExperienceRegion.ActionFamilies],
            null,
            input.HasFailedOrBlockedWork ? "Some meetings need attention." : "Browse recent meetings or search.",
            null,
            "MeetingSearchTextBox");
    }

    public static MeetingsActionOwner GetOwner(MeetingActionCatalogEntry entry) =>
        entry.ConfirmationPolicy == MeetingActionConfirmationPolicy.TypedPermanentDelete
            ? MeetingsActionOwner.DestructiveExplicit
            : entry.Id == MeetingActionId.ReviewRecommendation || entry.Id == MeetingActionId.ApplyRecommendations
                ? MeetingsActionOwner.CleanupReview
                : entry.SelectionCardinality == MeetingActionSelectionCardinality.None
                    ? MeetingsActionOwner.Library
                    : entry.SelectionCardinality is MeetingActionSelectionCardinality.OneOrMore or MeetingActionSelectionCardinality.AtLeastTwo
                        ? MeetingsActionOwner.MultiSelection
                        : entry.OutcomeTarget == MeetingActionOutcomeTarget.MeetingDetail
                            ? MeetingsActionOwner.Detail
                            : MeetingsActionOwner.SingleSelection;

    private static MeetingsExperienceResult Result(
        MeetingsExperienceState state,
        IReadOnlyList<MeetingsExperienceRegion> regions,
        MeetingActionId? primaryAction,
        string summary,
        string? blockReason,
        string focusTarget) =>
        new(state, regions, primaryAction, [MeetingActionFamily.Open, MeetingActionFamily.Fix, MeetingActionFamily.Organize, MeetingActionFamily.Processing], summary, blockReason, focusTarget);
}
