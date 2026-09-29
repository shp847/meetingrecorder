namespace MeetingRecorder.Core.Services;

public enum MeetingDetailTaskCenterSection
{
    Read = 0,
    Details = 1,
    Fix = 2,
    Organize = 3,
    Danger = 4,
}

public sealed record MeetingDetailSnapshotRevision(
    string MeetingStem,
    int CatalogRevision,
    int ArtifactRevision,
    int RecommendationRevision,
    bool IsFresh);

public sealed record MeetingDetailTaskCenterInput(
    MeetingDetailSnapshotRevision Revision,
    MeetingPrimaryRecommendation PrimaryRecommendation,
    bool HasTranscript,
    string TranscriptStatus,
    bool CanOpenAudio,
    bool CanOpenTranscript,
    MeetingActionCatalogState ActionCatalog);

public sealed record MeetingDetailTaskAction(
    MeetingActionId Id,
    MeetingDetailTaskCenterSection Section,
    string Label,
    bool IsEnabled,
    string? BlockReason);

public sealed record MeetingDetailTaskCenterState(
    MeetingDetailSnapshotRevision Revision,
    string Headline,
    string PrimaryReason,
    string FreshnessText,
    string TranscriptStatus,
    IReadOnlyList<MeetingDetailTaskAction> ReadActions,
    IReadOnlyList<MeetingDetailTaskAction> FixActions,
    IReadOnlyList<MeetingDetailTaskAction> OrganizeActions,
    IReadOnlyList<MeetingDetailTaskAction> DangerActions);

public sealed record MeetingDetailDraftState(
    bool HasTitleDraft,
    bool HasProjectDraft,
    bool HasSpeakerDraft,
    bool HasSplitDraft)
{
    public bool HasPendingDrafts => HasTitleDraft || HasProjectDraft || HasSpeakerDraft || HasSplitDraft;
}

public enum MeetingDetailRefreshDisposition
{
    ApplyReadOnlyRefresh = 0,
    KeepDraftsAndOfferReload = 1,
    CloseInvalidatedDetail = 2,
}

public static class MeetingDetailTaskCenterResolver
{
    public static MeetingDetailTaskCenterState Resolve(MeetingDetailTaskCenterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Revision);
        ArgumentNullException.ThrowIfNull(input.PrimaryRecommendation);
        ArgumentNullException.ThrowIfNull(input.ActionCatalog);

        var actions = input.ActionCatalog.Actions
            .Select(action => new MeetingDetailTaskAction(
                action.Entry.Id,
                ToSection(action.Entry.Family),
                action.Entry.AccessibleLabel,
                action.IsEligible,
                action.BlockReason))
            .ToArray();
        return new MeetingDetailTaskCenterState(
            input.Revision,
            input.PrimaryRecommendation.IsDismissed
                ? "Recommendation dismissed"
                : input.PrimaryRecommendation.Label,
            input.PrimaryRecommendation.BlockReason ?? input.PrimaryRecommendation.Reason,
            input.Revision.IsFresh
                ? "Current meeting snapshot"
                : "Meeting snapshot needs refresh; unavailable actions stay disabled.",
            input.HasTranscript
                ? input.TranscriptStatus
                : "Transcript is not available in the current snapshot.",
            actions.Where(action => action.Section == MeetingDetailTaskCenterSection.Read).ToArray(),
            actions.Where(action => action.Section == MeetingDetailTaskCenterSection.Fix).ToArray(),
            actions.Where(action => action.Section == MeetingDetailTaskCenterSection.Organize).ToArray(),
            actions.Where(action => action.Section == MeetingDetailTaskCenterSection.Danger).ToArray());
    }

    public static MeetingDetailRefreshDisposition ResolveRefresh(
        MeetingDetailSnapshotRevision current,
        MeetingDetailSnapshotRevision incoming,
        MeetingDetailDraftState drafts,
        bool isArchivedOrDeleted)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(drafts);

        if (isArchivedOrDeleted ||
            !string.Equals(current.MeetingStem, incoming.MeetingStem, StringComparison.OrdinalIgnoreCase))
        {
            return MeetingDetailRefreshDisposition.CloseInvalidatedDetail;
        }

        var materialRevisionChanged = current.CatalogRevision != incoming.CatalogRevision ||
            current.ArtifactRevision != incoming.ArtifactRevision ||
            current.RecommendationRevision != incoming.RecommendationRevision;
        return materialRevisionChanged && drafts.HasPendingDrafts
            ? MeetingDetailRefreshDisposition.KeepDraftsAndOfferReload
            : MeetingDetailRefreshDisposition.ApplyReadOnlyRefresh;
    }

    private static MeetingDetailTaskCenterSection ToSection(MeetingActionFamily family) =>
        family switch
        {
            MeetingActionFamily.Open => MeetingDetailTaskCenterSection.Read,
            MeetingActionFamily.Fix => MeetingDetailTaskCenterSection.Fix,
            MeetingActionFamily.Organize => MeetingDetailTaskCenterSection.Organize,
            MeetingActionFamily.Processing => MeetingDetailTaskCenterSection.Fix,
            MeetingActionFamily.Danger => MeetingDetailTaskCenterSection.Danger,
            _ => MeetingDetailTaskCenterSection.Details,
        };
}
