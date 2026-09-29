using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingViewPresetResolverTests
{
    private readonly MeetingViewPresetResolver _resolver = new();

    [Theory]
    [InlineData(MeetingsViewPreset.Recent, "recent-1", "recent-2", "attention-1", "processing-1")]
    [InlineData(MeetingsViewPreset.NeedsAttention, "attention-1", "processing-1")]
    [InlineData(MeetingsViewPreset.Processing, "processing-1")]
    [InlineData(MeetingsViewPreset.Archived, "archived-1")]
    [InlineData(MeetingsViewPreset.Custom, "recent-1", "recent-2", "attention-1", "processing-1")]
    public void Resolve_Applies_Each_Preset_Scope(MeetingsViewPreset preset, params string[] expectedIds)
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            preset,
            CustomView(),
            Items(),
            SearchText: null,
            ArchiveCatalogAvailable: true));

        Assert.Equal(
            expectedIds.OrderBy(id => id, StringComparer.Ordinal),
            state.ScopedMeetingIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(preset == MeetingsViewPreset.Custom, state.ShowCustomControls);
        Assert.Equal(preset == MeetingsViewPreset.Archived, state.RequiresArchiveCatalog);
    }

    [Fact]
    public void Resolve_Applies_Search_After_Preset_Scope_Without_Mutating_Preset()
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.NeedsAttention,
            CustomView(),
            Items(),
            SearchText: "invoices",
            ArchiveCatalogAvailable: true));

        Assert.Equal(MeetingsViewPreset.NeedsAttention, state.Preset);
        Assert.Equal(["attention-1"], state.ScopedMeetingIds);
    }

    [Fact]
    public void Resolve_Reports_No_Search_Matches_Separately_From_Empty_Preset()
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.Recent,
            CustomView(),
            Items(),
            SearchText: "not-present",
            ArchiveCatalogAvailable: true));

        Assert.Empty(state.ScopedMeetingIds);
        Assert.Equal(MeetingsViewPresetEmptyState.NoMatchesForSearch, state.EmptyState);
        Assert.Equal("No matches for search.", state.StatusSummary);
    }

    [Theory]
    [InlineData(MeetingsViewPreset.NeedsAttention, MeetingsViewPresetEmptyState.NoMeetingsNeedAttention)]
    [InlineData(MeetingsViewPreset.Processing, MeetingsViewPresetEmptyState.NoProcessingWork)]
    [InlineData(MeetingsViewPreset.Archived, MeetingsViewPresetEmptyState.NoArchivedMeetings)]
    public void Resolve_Reports_Preset_Specific_Empty_States(
        MeetingsViewPreset preset,
        MeetingsViewPresetEmptyState expectedEmptyState)
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            preset,
            CustomView(),
            [Item("complete", MeetingViewWorkState.Complete)],
            SearchText: null,
            ArchiveCatalogAvailable: true));

        Assert.Empty(state.ScopedMeetingIds);
        Assert.Equal(expectedEmptyState, state.EmptyState);
    }

    [Fact]
    public void Resolve_Reports_No_Meetings_Yet_When_The_Catalog_Has_No_Rows()
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.Recent,
            CustomView(),
            Array.Empty<MeetingViewPresetItem>(),
            SearchText: null,
            ArchiveCatalogAvailable: true));

        Assert.Equal(MeetingsViewPresetEmptyState.NoMeetingsYet, state.EmptyState);
    }

    [Fact]
    public void Resolve_Reports_Archive_Catalog_Unavailable_Without_Reading_Archive_Items()
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.Archived,
            CustomView(),
            Items(),
            SearchText: null,
            ArchiveCatalogAvailable: false));

        Assert.Empty(state.ScopedMeetingIds);
        Assert.True(state.RequiresArchiveCatalog);
        Assert.Equal(MeetingsViewPresetEmptyState.ArchiveCatalogUnavailable, state.EmptyState);
    }

    [Fact]
    public void Resolve_Preserves_Custom_View_Choices_And_Uses_Fixed_Preset_Choices()
    {
        var custom = new MeetingsCustomViewState(
            MeetingsViewMode.Grouped,
            MeetingsSortKey.Title,
            SortDescending: false,
            MeetingsGroupKey.Month);

        var customState = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.Custom, custom, Items(), null, ArchiveCatalogAvailable: true));
        var presetState = _resolver.Resolve(new MeetingViewPresetInput(
            MeetingsViewPreset.Recent, custom, Items(), null, ArchiveCatalogAvailable: true));

        Assert.Equal(custom, new MeetingsCustomViewState(
            customState.ViewMode,
            customState.SortKey,
            customState.SortDescending,
            customState.GroupKey));
        Assert.Equal(MeetingsViewMode.Table, presetState.ViewMode);
        Assert.Equal(MeetingsSortKey.Started, presetState.SortKey);
        Assert.True(presetState.SortDescending);
    }

    [Fact]
    public void SelectInitialPreset_Uses_NeedsAttention_Only_For_Actionable_Normalized_Work()
    {
        Assert.Equal(MeetingsViewPreset.NeedsAttention, _resolver.SelectInitialPreset(Items()));
        Assert.Equal(MeetingsViewPreset.Recent, _resolver.SelectInitialPreset(
            [Item("complete", MeetingViewWorkState.Complete)]));
    }

    [Fact]
    public void Resolve_Unknown_Preset_Falls_Back_To_Recent()
    {
        var state = _resolver.Resolve(new MeetingViewPresetInput(
            (MeetingsViewPreset)999,
            CustomView(),
            Items(),
            null,
            ArchiveCatalogAvailable: true));

        Assert.Equal(MeetingsViewPreset.Recent, state.Preset);
        Assert.False(state.ShowCustomControls);
    }

    private static MeetingsCustomViewState CustomView() =>
        new(MeetingsViewMode.Grouped, MeetingsSortKey.Title, false, MeetingsGroupKey.Month);

    private static MeetingViewPresetItem[] Items() =>
    [
        Item("recent-1", MeetingViewWorkState.Complete, searchText: "Weekly planning"),
        Item("recent-2", MeetingViewWorkState.Unknown, searchText: "Partner update"),
        Item("attention-1", MeetingViewWorkState.Failed, searchText: "Invoices review"),
        Item("processing-1", MeetingViewWorkState.Processing, searchText: "Transcript running"),
        Item("archived-1", MeetingViewWorkState.Complete, isArchived: true, searchText: "Old review"),
    ];

    private static MeetingViewPresetItem Item(
        string id,
        MeetingViewWorkState state,
        bool hasRecommendation = false,
        bool isArchived = false,
        string? searchText = null) =>
        new(
            id,
            new DateTimeOffset(2026, 9, 27, 12, id.Length, 0, TimeSpan.Zero),
            state,
            hasRecommendation,
            isArchived,
            searchText ?? id);
}
