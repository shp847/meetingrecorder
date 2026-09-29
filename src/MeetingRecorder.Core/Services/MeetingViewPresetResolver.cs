using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public enum MeetingViewWorkState
{
    Complete = 0,
    Queued = 1,
    Processing = 2,
    Failed = 3,
    Blocked = 4,
    RetryNeeded = 5,
    Unknown = 6,
}

public enum MeetingsViewPresetEmptyState
{
    None = 0,
    NoMeetingsYet = 1,
    NoMatchesForSearch = 2,
    NoMeetingsNeedAttention = 3,
    NoProcessingWork = 4,
    ArchiveCatalogUnavailable = 5,
    NoArchivedMeetings = 6,
}

public sealed record MeetingViewPresetItem(
    string Id,
    DateTimeOffset StartedAtUtc,
    MeetingViewWorkState WorkState,
    bool HasActionableRecommendation,
    bool IsArchived,
    string SearchText);

public sealed record MeetingsCustomViewState(
    MeetingsViewMode ViewMode,
    MeetingsSortKey SortKey,
    bool SortDescending,
    MeetingsGroupKey GroupKey);

public sealed record MeetingViewPresetInput(
    MeetingsViewPreset Preset,
    MeetingsCustomViewState CustomView,
    IReadOnlyList<MeetingViewPresetItem> Items,
    string? SearchText,
    bool ArchiveCatalogAvailable);

/// <summary>
/// Read-only projection for Meetings scopes. Archive availability is supplied by
/// a catalog owner; this resolver never reads, creates, moves, or deletes files.
/// </summary>
public sealed record MeetingViewPresetState(
    MeetingsViewPreset Preset,
    IReadOnlyList<string> ScopedMeetingIds,
    MeetingsViewMode ViewMode,
    MeetingsSortKey SortKey,
    bool SortDescending,
    MeetingsGroupKey GroupKey,
    string StatusSummary,
    MeetingsViewPresetEmptyState EmptyState,
    bool RequiresArchiveCatalog,
    bool ShowCustomControls);

public sealed class MeetingViewPresetResolver
{
    public MeetingViewPresetState Resolve(MeetingViewPresetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var preset = NormalizePreset(input.Preset);
        var requiresArchiveCatalog = preset == MeetingsViewPreset.Archived;
        if (requiresArchiveCatalog && !input.ArchiveCatalogAvailable)
        {
            return BuildState(
                preset,
                Array.Empty<MeetingViewPresetItem>(),
                input.CustomView,
                "Archive catalog is not available.",
                MeetingsViewPresetEmptyState.ArchiveCatalogUnavailable,
                requiresArchiveCatalog);
        }

        var scope = input.Items
            .Where(item => IsInScope(item, preset))
            .OrderByDescending(item => item.StartedAtUtc)
            .ToArray();
        var searched = scope
            .Where(item => MatchesSearch(item, input.SearchText))
            .ToArray();
        var emptyState = GetEmptyState(preset, input.Items.Count, scope.Length, searched.Length, input.SearchText);
        var summary = BuildStatusSummary(preset, searched.Length, input.Items.Count, emptyState);

        return BuildState(preset, searched, input.CustomView, summary, emptyState, requiresArchiveCatalog);
    }

    public MeetingsViewPreset SelectInitialPreset(IReadOnlyList<MeetingViewPresetItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Any(item => !item.IsArchived && IsAttention(item))
            ? MeetingsViewPreset.NeedsAttention
            : MeetingsViewPreset.Recent;
    }

    private static MeetingViewPresetState BuildState(
        MeetingsViewPreset preset,
        IReadOnlyList<MeetingViewPresetItem> items,
        MeetingsCustomViewState customView,
        string statusSummary,
        MeetingsViewPresetEmptyState emptyState,
        bool requiresArchiveCatalog)
    {
        var (viewMode, sortKey, sortDescending, groupKey) = preset switch
        {
            MeetingsViewPreset.Recent => (MeetingsViewMode.Table, MeetingsSortKey.Started, true, MeetingsGroupKey.Week),
            MeetingsViewPreset.NeedsAttention => (MeetingsViewMode.Grouped, MeetingsSortKey.Started, true, MeetingsGroupKey.Status),
            MeetingsViewPreset.Processing => (MeetingsViewMode.Grouped, MeetingsSortKey.Started, true, MeetingsGroupKey.Status),
            MeetingsViewPreset.Archived => (MeetingsViewMode.Table, MeetingsSortKey.Started, true, MeetingsGroupKey.Week),
            _ => (customView.ViewMode, customView.SortKey, customView.SortDescending, customView.GroupKey),
        };

        return new MeetingViewPresetState(
            preset,
            items.Select(item => item.Id).ToArray(),
            viewMode,
            sortKey,
            sortDescending,
            groupKey,
            statusSummary,
            emptyState,
            requiresArchiveCatalog,
            preset == MeetingsViewPreset.Custom);
    }

    private static bool IsInScope(MeetingViewPresetItem item, MeetingsViewPreset preset)
    {
        return preset switch
        {
            MeetingsViewPreset.Recent or MeetingsViewPreset.Custom => !item.IsArchived,
            MeetingsViewPreset.NeedsAttention => !item.IsArchived && IsAttention(item),
            MeetingsViewPreset.Processing => !item.IsArchived && item.WorkState is
                MeetingViewWorkState.Queued or MeetingViewWorkState.Processing or MeetingViewWorkState.RetryNeeded,
            MeetingsViewPreset.Archived => item.IsArchived,
            _ => !item.IsArchived,
        };
    }

    private static bool IsAttention(MeetingViewPresetItem item)
    {
        return item.HasActionableRecommendation || item.WorkState is
            MeetingViewWorkState.Failed or
            MeetingViewWorkState.Blocked or
            MeetingViewWorkState.Queued or
            MeetingViewWorkState.Processing or
            MeetingViewWorkState.RetryNeeded;
    }

    private static bool MatchesSearch(MeetingViewPresetItem item, string? searchText)
    {
        return string.IsNullOrWhiteSpace(searchText) ||
            item.SearchText.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static MeetingsViewPresetEmptyState GetEmptyState(
        MeetingsViewPreset preset,
        int totalItemCount,
        int scopeCount,
        int searchedCount,
        string? searchText)
    {
        if (searchedCount > 0)
        {
            return MeetingsViewPresetEmptyState.None;
        }

        if (!string.IsNullOrWhiteSpace(searchText) && scopeCount > 0)
        {
            return MeetingsViewPresetEmptyState.NoMatchesForSearch;
        }

        if (totalItemCount == 0)
        {
            return MeetingsViewPresetEmptyState.NoMeetingsYet;
        }

        return preset switch
        {
            MeetingsViewPreset.NeedsAttention => MeetingsViewPresetEmptyState.NoMeetingsNeedAttention,
            MeetingsViewPreset.Processing => MeetingsViewPresetEmptyState.NoProcessingWork,
            MeetingsViewPreset.Archived => MeetingsViewPresetEmptyState.NoArchivedMeetings,
            _ => MeetingsViewPresetEmptyState.NoMatchesForSearch,
        };
    }

    private static string BuildStatusSummary(
        MeetingsViewPreset preset,
        int displayedCount,
        int totalCount,
        MeetingsViewPresetEmptyState emptyState)
    {
        if (emptyState != MeetingsViewPresetEmptyState.None)
        {
            return emptyState switch
            {
                MeetingsViewPresetEmptyState.NoMeetingsYet => "No meetings yet.",
                MeetingsViewPresetEmptyState.NoMatchesForSearch => "No matches for search.",
                MeetingsViewPresetEmptyState.NoMeetingsNeedAttention => "No meetings need attention.",
                MeetingsViewPresetEmptyState.NoProcessingWork => "No processing work.",
                MeetingsViewPresetEmptyState.ArchiveCatalogUnavailable => "Archive catalog is not available.",
                MeetingsViewPresetEmptyState.NoArchivedMeetings => "No archived meetings.",
                _ => "No meetings in this view.",
            };
        }

        var scope = preset switch
        {
            MeetingsViewPreset.NeedsAttention => "need attention",
            MeetingsViewPreset.Processing => "are processing",
            MeetingsViewPreset.Archived => "are archived",
            MeetingsViewPreset.Custom => "match Custom View",
            _ => "are recent",
        };
        return $"Showing {displayedCount} of {totalCount} meetings that {scope}.";
    }

    private static MeetingsViewPreset NormalizePreset(MeetingsViewPreset preset)
    {
        return Enum.IsDefined(preset) ? preset : MeetingsViewPreset.Recent;
    }
}
