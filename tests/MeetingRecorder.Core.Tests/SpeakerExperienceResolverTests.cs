using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerExperienceResolverTests
{
    [Fact]
    public void Resolve_MissingLabels_OffersOnly_AddLabels_WhenMeetingIsEligible()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            Surface = SpeakerExperienceSurface.Meeting,
            HasDiarizationLabels = false,
            IsRepairEligible = true,
        });

        Assert.Equal(SpeakerExperienceState.LabelsMissing, result.State);
        Assert.Contains("Diarization Labels", result.Explanation, StringComparison.Ordinal);
        Assert.Equal([SpeakerExperienceAction.AddSpeakerLabels], result.PermittedActions);
    }

    [Fact]
    public void Resolve_SuspiciousLabels_OffersRepair_NotNameReview()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            HasSuspiciousLabels = true,
            IsRepairEligible = true,
        });

        Assert.Equal(SpeakerExperienceState.LabelsSuspicious, result.State);
        Assert.Equal([SpeakerExperienceAction.RepairSpeakerLabels], result.PermittedActions);
    }

    [Fact]
    public void Resolve_QueuedLabels_DoesNotTreatQueueAcceptanceAsReady()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            HasDiarizationLabels = false,
            IsLabelingQueued = true,
        });

        Assert.Equal(SpeakerExperienceState.LabelsQueued, result.State);
        Assert.Empty(result.PermittedActions);
        Assert.Contains("does not mean labels are ready", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RunningLabels_BlocksNameReviewUntilCompletion()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            IsLabelingRunning = true,
        });

        Assert.Equal(SpeakerExperienceState.LabelsRunning, result.State);
        Assert.Empty(result.PermittedActions);
    }

    [Fact]
    public void Resolve_IneligibleRepair_ExplainsRecoveryWithoutQueueing()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            HasSuspiciousLabels = true,
            IsRepairEligible = false,
        });

        Assert.Equal(SpeakerExperienceState.RepairIneligible, result.State);
        Assert.Empty(result.PermittedActions);
    }

    [Fact]
    public void Resolve_DisabledLearning_PermitsOnlyMeetingDisplayNameEdits()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            LearningMode = SpeakerNameLearningMode.Disabled,
        });

        Assert.Equal(SpeakerExperienceState.LearningDisabled, result.State);
        Assert.Equal([SpeakerExperienceAction.ApplyNameChanges], result.PermittedActions);
        Assert.Contains("Meeting Display Names", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ProfileStoreFailure_OffersSafeRetryAndNameEdits()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            IsLocalProfileStoreAvailable = false,
        });

        Assert.Equal(SpeakerExperienceState.ProfilesUnavailable, result.State);
        Assert.True(result.Permits(SpeakerExperienceAction.RetryLocalProfileStore));
        Assert.True(result.Permits(SpeakerExperienceAction.ApplyNameChanges));
        Assert.False(result.Permits(SpeakerExperienceAction.RefreshLocalSuggestions));
    }

    [Fact]
    public void Resolve_MissingVoiceSamples_AllowsOnlyNameEdits()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            HasVoiceSamples = false,
        });

        Assert.Equal(SpeakerExperienceState.SamplesUnavailable, result.State);
        Assert.Equal([SpeakerExperienceAction.ApplyNameChanges], result.PermittedActions);
    }

    [Fact]
    public void Resolve_NameSuggestions_SeparatesSuggestionReviewFromConfirmedNames()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            ActiveVoiceProfileCount = 1,
            NameSuggestionCount = 1,
            HasProfileAttribution = true,
        });

        Assert.Equal(SpeakerExperienceState.NamesReadyForReview, result.State);
        Assert.Contains(SpeakerExperienceAction.UseNameSuggestion, result.PermittedActions);
        Assert.Contains(SpeakerExperienceAction.RejectNameSuggestion, result.PermittedActions);
        Assert.Contains(SpeakerExperienceAction.UndoProfileNames, result.PermittedActions);
        Assert.Contains("not confirmed identities", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RefreshRequired_BlocksEveryMutationUntilReloaded()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with { RequiresRefresh = true });

        Assert.Equal(SpeakerExperienceState.RefreshRequired, result.State);
        Assert.Empty(result.PermittedActions);
    }

    [Fact]
    public void Resolve_Settings_ExplainsProfilesOnlyAffectFutureSuggestions()
    {
        var result = SpeakerExperienceResolver.Resolve(Input() with
        {
            Surface = SpeakerExperienceSurface.Settings,
        });

        Assert.Equal(SpeakerExperienceState.LabelsReady, result.State);
        Assert.Contains("future suggestions", result.Explanation, StringComparison.Ordinal);
    }

    private static SpeakerExperienceInput Input() => new(
        SpeakerExperienceSurface.NameReview,
        HasMeetingManifest: true,
        HasTranscript: true,
        HasDiarizationLabels: true,
        IsLabelingQueued: false,
        IsLabelingRunning: false,
        HasSuspiciousLabels: false,
        IsRepairEligible: true,
        HasVoiceSamples: true,
        IsLocalProfileStoreAvailable: true,
        ActiveVoiceProfileCount: 1,
        LearningMode: SpeakerNameLearningMode.LocalAutoLearn,
        NameSuggestionCount: 0,
        HasProfileAttribution: false,
        RequiresRefresh: false);
}
