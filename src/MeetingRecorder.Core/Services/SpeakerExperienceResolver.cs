using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Keeps speaker-label processing separate from local voice-profile name review.
/// Inputs are already-known meeting facts; this resolver never probes audio, models, or profile storage.
/// </summary>
public enum SpeakerExperienceState
{
    LabelsMissing = 0,
    LabelsQueued = 1,
    LabelsRunning = 2,
    LabelsReady = 3,
    LabelsSuspicious = 4,
    NamesReadyForReview = 5,
    LearningDisabled = 6,
    ProfilesUnavailable = 7,
    SamplesUnavailable = 8,
    RepairIneligible = 9,
    RefreshRequired = 10,
}

public enum SpeakerExperienceSurface
{
    Meeting = 0,
    NameReview = 1,
    Settings = 2,
}

public enum SpeakerExperienceAction
{
    AddSpeakerLabels = 0,
    RepairSpeakerLabels = 1,
    UseNameSuggestion = 2,
    RejectNameSuggestion = 3,
    ApplyNameChanges = 4,
    RefreshLocalSuggestions = 5,
    UndoProfileNames = 6,
    OpenSpeakerLabelingSetup = 7,
    RetryLocalProfileStore = 8,
}

public sealed record SpeakerExperienceInput(
    SpeakerExperienceSurface Surface,
    bool HasMeetingManifest,
    bool HasTranscript,
    bool HasDiarizationLabels,
    bool IsLabelingQueued,
    bool IsLabelingRunning,
    bool HasSuspiciousLabels,
    bool IsRepairEligible,
    bool HasVoiceSamples,
    bool IsLocalProfileStoreAvailable,
    int ActiveVoiceProfileCount,
    SpeakerNameLearningMode LearningMode,
    int NameSuggestionCount,
    bool HasProfileAttribution,
    bool RequiresRefresh);

public sealed record SpeakerExperienceStateResult(
    SpeakerExperienceState State,
    string Explanation,
    IReadOnlyList<SpeakerExperienceAction> PermittedActions)
{
    public bool Permits(SpeakerExperienceAction action) => PermittedActions.Contains(action);
}

public static class SpeakerExperienceResolver
{
    public static SpeakerExperienceStateResult Resolve(SpeakerExperienceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.RequiresRefresh)
        {
            return Build(
                SpeakerExperienceState.RefreshRequired,
                "Meeting speaker data changed. Reload before changing Meeting Display Names.");
        }

        if (input.Surface == SpeakerExperienceSurface.Settings)
        {
            return ResolveSettings(input);
        }

        if (!input.HasMeetingManifest || !input.HasTranscript)
        {
            return Build(
                SpeakerExperienceState.LabelsMissing,
                "Diarization Labels need a published transcript and meeting manifest before they can be added.");
        }

        if (input.IsLabelingRunning)
        {
            return Build(
                SpeakerExperienceState.LabelsRunning,
                "Diarization Label processing is running. Wait for completed labels before reviewing Meeting Display Names.");
        }

        if (input.IsLabelingQueued)
        {
            return Build(
                SpeakerExperienceState.LabelsQueued,
                "Diarization Label processing is queued. Queue acceptance does not mean labels are ready yet.");
        }

        if (!input.HasDiarizationLabels)
        {
            return input.IsRepairEligible
                ? Build(
                    SpeakerExperienceState.LabelsMissing,
                    "This transcript has no Diarization Labels. Add them before reviewing Meeting Display Names.",
                    SpeakerExperienceAction.AddSpeakerLabels)
                : Build(
                    SpeakerExperienceState.RepairIneligible,
                    "Diarization Labels cannot be added until local speaker labeling setup and meeting source are available.",
                    SpeakerExperienceAction.OpenSpeakerLabelingSetup);
        }

        if (input.HasSuspiciousLabels)
        {
            return input.IsRepairEligible
                ? Build(
                    SpeakerExperienceState.LabelsSuspicious,
                    "Diarization Labels look fragmented. Repair labels before trusting local Name Suggestions.",
                    SpeakerExperienceAction.RepairSpeakerLabels)
                : Build(
                    SpeakerExperienceState.RepairIneligible,
                    "Diarization Labels need repair, but this meeting is not eligible for reprocessing yet.");
        }

        if (input.Surface == SpeakerExperienceSurface.Meeting)
        {
            return Build(
                SpeakerExperienceState.LabelsReady,
                "Diarization Labels are ready. Review Meeting Display Names separately from speaker-label repair.");
        }

        if (input.LearningMode == SpeakerNameLearningMode.Disabled)
        {
            return Build(
                SpeakerExperienceState.LearningDisabled,
                "Local Voice Profile learning is disabled. You can still edit Meeting Display Names without creating profiles.",
                SpeakerExperienceAction.ApplyNameChanges);
        }

        if (!input.HasVoiceSamples)
        {
            return Build(
                SpeakerExperienceState.SamplesUnavailable,
                "Local Name Suggestions need stored speaker voice samples from this meeting.",
                SpeakerExperienceAction.ApplyNameChanges);
        }

        if (!input.IsLocalProfileStoreAvailable)
        {
            return Build(
                SpeakerExperienceState.ProfilesUnavailable,
                "Local Voice Profiles are unavailable. Retry local profile storage or edit Meeting Display Names only.",
                SpeakerExperienceAction.ApplyNameChanges,
                SpeakerExperienceAction.RetryLocalProfileStore);
        }

        if (input.ActiveVoiceProfileCount == 0)
        {
            return Build(
                SpeakerExperienceState.ProfilesUnavailable,
                "No active local Voice Profiles are available yet. Confirm a Meeting Display Name to teach one locally.",
                SpeakerExperienceAction.ApplyNameChanges);
        }

        if (input.NameSuggestionCount > 0)
        {
            var actions = new List<SpeakerExperienceAction>
            {
                SpeakerExperienceAction.UseNameSuggestion,
                SpeakerExperienceAction.RejectNameSuggestion,
                SpeakerExperienceAction.ApplyNameChanges,
                SpeakerExperienceAction.RefreshLocalSuggestions,
            };
            if (input.HasProfileAttribution)
            {
                actions.Add(SpeakerExperienceAction.UndoProfileNames);
            }

            return new SpeakerExperienceStateResult(
                SpeakerExperienceState.NamesReadyForReview,
                "Review each local Name Suggestion before applying a Meeting Display Name. Suggestions are not confirmed identities.",
                actions);
        }

        var readyActions = new List<SpeakerExperienceAction>
        {
            SpeakerExperienceAction.ApplyNameChanges,
            SpeakerExperienceAction.RefreshLocalSuggestions,
        };
        if (input.HasProfileAttribution)
        {
            readyActions.Add(SpeakerExperienceAction.UndoProfileNames);
        }

        return new SpeakerExperienceStateResult(
            SpeakerExperienceState.LabelsReady,
            "Diarization Labels are ready for Meeting Display Name review. Refresh searches local Voice Profiles only.",
            readyActions);
    }

    private static SpeakerExperienceStateResult ResolveSettings(SpeakerExperienceInput input)
    {
        if (!input.IsLocalProfileStoreAvailable)
        {
            return Build(
                SpeakerExperienceState.ProfilesUnavailable,
                "Local Voice Profiles are unavailable. Retry local profile storage before changing profile settings.",
                SpeakerExperienceAction.RetryLocalProfileStore);
        }

        if (input.LearningMode == SpeakerNameLearningMode.Disabled)
        {
            return Build(
                SpeakerExperienceState.LearningDisabled,
                "Local Voice Profile learning is disabled. Existing profiles remain local and can be managed here.");
        }

        return Build(
            SpeakerExperienceState.LabelsReady,
            "Voice Profiles stay on this PC. Disable or delete changes future suggestions, not existing Meeting Display Names.");
    }

    private static SpeakerExperienceStateResult Build(
        SpeakerExperienceState state,
        string explanation,
        params SpeakerExperienceAction[] permittedActions) =>
        new(state, explanation, permittedActions);
}
