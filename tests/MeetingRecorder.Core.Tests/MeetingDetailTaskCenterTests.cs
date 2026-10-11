using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingDetailTaskCenterTests
{
    [Fact]
    public void Resolve_Presents_One_Recommendation_And_Catalog_Families_Without_Enabling_Blocked_Actions()
    {
        var catalog = new MeetingActionCatalog().Resolve(new MeetingActionCatalogInput(
            SelectedCount: 1,
            IsBusy: false,
            HasFocusedMeeting: true,
            CanOpenAudio: true,
            CanOpenTranscript: false,
            HasPrimaryRecommendation: true,
            CanRegenerateTranscript: true,
            CanSplit: true,
            CanAddSpeakerLabels: false,
            CanChangeRushProcessing: false,
            IsMarkedAsap: false,
            HasCleanupRecommendations: false,
            CanMergeSelected: false,
            CanReTranscribeSelected: true,
            CanAddSpeakerLabelsToSelected: false,
            CanArchiveSelected: true,
            CanDeleteSelectedPermanently: true,
            HasProcessingBacklog: false));
        var primary = new MeetingPrimaryRecommendation(
            MeetingPrimaryRecommendationKind.Blocked,
            "Set up transcript recovery",
            "Transcript recovery needs a local transcription model before it can start.",
            MeetingRecommendationSeverity.High,
            MeetingRecommendationActionTarget.SettingsSetup,
            true,
            "Set up a local transcription model, then return to this meeting.",
            "fingerprint",
            1,
            4,
            DateTimeOffset.UtcNow,
            false);

        var state = MeetingDetailTaskCenterResolver.Resolve(new MeetingDetailTaskCenterInput(
            new MeetingDetailSnapshotRevision("meeting-1", 4, 3, 2, IsFresh: false),
            primary,
            HasTranscript: false,
            TranscriptStatus: "Transcript unavailable.",
            CanOpenAudio: true,
            CanOpenTranscript: false,
            catalog));

        Assert.Equal(primary.Label, state.Headline);
        Assert.Contains("needs refresh", state.FreshnessText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(state.ReadActions, action => action.Id == MeetingActionId.OpenAudio && action.IsEnabled);
        Assert.Contains(state.ReadActions, action => action.Id == MeetingActionId.OpenTranscript && !action.IsEnabled);
        Assert.Contains(state.FixActions, action => action.Id == MeetingActionId.RetryTranscript && action.IsEnabled);
        Assert.Contains(state.OrganizeActions, action => action.Id == MeetingActionId.Archive && action.IsEnabled);
        Assert.Contains(state.DangerActions, action => action.Id == MeetingActionId.DeletePermanently && action.IsEnabled);
    }

    [Fact]
    public void ResolveRefresh_Keeps_Drafts_When_A_Material_Background_Revision_Changes()
    {
        var current = new MeetingDetailSnapshotRevision("meeting-1", 1, 1, 1, true);
        var incoming = current with { ArtifactRevision = 2 };

        var disposition = MeetingDetailTaskCenterResolver.ResolveRefresh(
            current,
            incoming,
            new MeetingDetailDraftState(HasTitleDraft: true, false, false, false),
            isArchivedOrDeleted: false);

        Assert.Equal(MeetingDetailRefreshDisposition.KeepDraftsAndOfferReload, disposition);
    }

    [Fact]
    public void ResolveRefresh_Applies_Read_Only_Refresh_Without_Drafts_And_Closes_Invalidated_Detail()
    {
        var current = new MeetingDetailSnapshotRevision("meeting-1", 1, 1, 1, true);
        var incoming = current with { RecommendationRevision = 2 };

        Assert.Equal(
            MeetingDetailRefreshDisposition.ApplyReadOnlyRefresh,
            MeetingDetailTaskCenterResolver.ResolveRefresh(
                current,
                incoming,
                new MeetingDetailDraftState(false, false, false, false),
                isArchivedOrDeleted: false));
        Assert.Equal(
            MeetingDetailRefreshDisposition.CloseInvalidatedDetail,
            MeetingDetailTaskCenterResolver.ResolveRefresh(
                current,
                incoming,
                new MeetingDetailDraftState(false, false, false, false),
                isArchivedOrDeleted: true));
    }
}
