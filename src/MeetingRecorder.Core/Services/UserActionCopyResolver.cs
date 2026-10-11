namespace MeetingRecorder.Core.Services;

/// <summary>
/// The operation a person is being asked to start. Keep this separate from the
/// implementation command so normal UI copy can explain an outcome without
/// exposing a provider, file, exception, or other diagnostic detail.
/// </summary>
internal enum UserActionIntent
{
    None,
    ConfigureSummary,
    GenerateSummary,
    RetrySummary,
    ValidateSummaryProvider,
    ValidateLocalSummaryProvider,
    ValidateHostedSummaryProvider,
    RefreshSummaryProviderModels,
    SaveSummarySettings,
    AddSpeakerLabels,
    RepairSpeakerLabels,
    ApplyMeetingDisplayNames,
    RefreshLocalNameSuggestions,
    UndoProfileNames,
    EnableVoiceProfile,
    DisableVoiceProfile,
    DeleteVoiceProfile,
    DeleteAllVoiceProfiles,
    RefreshMeetingDetails,
    ArchiveMeetings,
    DeleteMeetings,
    ApplyCleanupRecommendations,
    DismissCleanupRecommendations,
    StartRecording,
    StopRecording,
    RenameMeeting,
    SuggestMeetingTitle,
    UpdateMeetingProject,
    RegenerateTranscript,
    MergeMeetings,
    SplitMeeting,
    SaveSettings,
    ImportAudio,
    ManageModelAssets,
    CheckForUpdates,
    InstallUpdates,
    UpdateSpeakerLabelingMode,
    UpdateLaunchOnLogin,
    UpdateRushProcessing,
    OpenArtifact,
    CopyArtifactPath,
}

internal enum UserActionScope
{
    Home,
    Setup,
    Settings,
    Meetings,
    MeetingDetails,
    LocalProfileStore,
}

internal enum UserActionRemedyDestination
{
    None,
    Settings,
    SummarySettings,
    TranscriptionSetup,
    SpeakerLabelingSetup,
    MeetingDetails,
    RefreshMeeting,
    Help,
}

internal enum UserActionBlockedReasonKind
{
    None,
    SelectionRequired,
    ActionNotEligible,
    NoEligibleItems,
    SummaryDisabled,
    SummaryProviderNotConfigured,
    HostedSummaryConsentRequired,
    ModelUnavailable,
    TranscriptUnavailable,
    ArtifactUnavailable,
    SetupPending,
    RecordingActive,
    AppBusy,
    QueuePaused,
    OperationInProgress,
    SpeakerLabelsMissing,
    SpeakerVoiceSamplesMissing,
    SpeakerLearningDisabled,
    VoiceProfilesUnavailable,
    LocalStorageUnavailable,
    PermissionDenied,
    NetworkUnavailable,
    DataStale,
    NoSafeRemedy,
    OperationFailed,
}

/// <summary>
/// Typed, user-safe explanation of why an operation cannot run. Its fields are
/// selected from the taxonomy below; do not put exception, path, key, provider,
/// transcript, or embedding values in this record.
/// </summary>
internal sealed record BlockedReason(
    UserActionBlockedReasonKind Kind,
    string Condition,
    UserActionScope Scope,
    string SafetyConstraint,
    UserActionRemedyDestination RemedyDestination,
    string? HelpText,
    bool RequiresFreshState);

internal sealed record UserActionCopy(
    string Label,
    string HelperText,
    string? ConfirmationText,
    string ProgressText,
    string SuccessText,
    string BlockedTitle,
    string BlockedText,
    string PrimaryActionText,
    string LiveAnnouncement,
    bool IsAvailable);

/// <summary>
/// Central source for outcome, scope, safety, and recovery copy. Diagnostics
/// remain in logs; normal UI receives only this fixed vocabulary.
/// </summary>
internal static class UserActionCopyResolver
{
    public static UserActionCopy Resolve(
        UserActionIntent intent,
        UserActionBlockedReasonKind blockedReason = UserActionBlockedReasonKind.None)
    {
        var action = ResolveAction(intent);
        var reason = ResolveBlockedReason(blockedReason);
        return new UserActionCopy(
            action.Label,
            action.HelperText,
            action.ConfirmationText,
            action.ProgressText,
            action.SuccessText,
            reason.Kind == UserActionBlockedReasonKind.None ? string.Empty : "Action unavailable",
            reason.Kind == UserActionBlockedReasonKind.None ? string.Empty : reason.Condition,
            ResolvePrimaryAction(action.Label, reason),
            reason.Kind == UserActionBlockedReasonKind.None ? action.LiveAnnouncement : reason.Condition,
            reason.Kind == UserActionBlockedReasonKind.None);
    }

    public static BlockedReason ResolveBlockedReason(UserActionBlockedReasonKind kind) => kind switch
    {
        UserActionBlockedReasonKind.None => new(kind, string.Empty, UserActionScope.Home, string.Empty, UserActionRemedyDestination.None, null, false),
        UserActionBlockedReasonKind.SelectionRequired => new(kind, "Select a local Voice Profile first.", UserActionScope.Settings, "No profile is changed until one is selected.", UserActionRemedyDestination.Settings, "Select a profile in Settings, then choose an action.", false),
        UserActionBlockedReasonKind.ActionNotEligible => new(kind, "The selected local Voice Profile is already in that state.", UserActionScope.Settings, "No profile is changed when the requested state already applies.", UserActionRemedyDestination.Settings, "Select a different profile or choose an available action.", false),
        UserActionBlockedReasonKind.NoEligibleItems => new(kind, "There are no local Voice Profiles to change.", UserActionScope.Settings, "No profile or meeting display name is changed.", UserActionRemedyDestination.Settings, "Teach a profile from an explicit meeting display-name correction first.", false),
        UserActionBlockedReasonKind.SummaryDisabled => new(kind, "Summaries are off for future requests.", UserActionScope.Settings, "No transcript is sent while summaries are off.", UserActionRemedyDestination.SummarySettings, "Turn summaries on in Settings, then return to this meeting.", false),
        UserActionBlockedReasonKind.SummaryProviderNotConfigured => new(kind, "Summary provider setup is incomplete.", UserActionScope.Settings, "No summary request starts until required setup is saved.", UserActionRemedyDestination.SummarySettings, "Review summary settings and save the required provider setup.", false),
        UserActionBlockedReasonKind.HostedSummaryConsentRequired => new(kind, "Hosted summary use needs your saved consent.", UserActionScope.Settings, "No transcript is sent to a hosted route without current consent.", UserActionRemedyDestination.SummarySettings, "Review the hosted-summary notice in Settings and save your choice.", false),
        UserActionBlockedReasonKind.ModelUnavailable => new(kind, "The required model is not ready.", UserActionScope.Setup, "The action does not start with an unavailable model.", UserActionRemedyDestination.TranscriptionSetup, "Open Setup to install or select a ready model, then try again.", false),
        UserActionBlockedReasonKind.TranscriptUnavailable => new(kind, "A readable published transcript is needed first.", UserActionScope.MeetingDetails, "The action only uses a published transcript for this meeting.", UserActionRemedyDestination.TranscriptionSetup, "Finish or retry transcription, then refresh this meeting.", true),
        UserActionBlockedReasonKind.ArtifactUnavailable => new(kind, "The required meeting artifact is not available.", UserActionScope.Meetings, "No replacement or deletion is attempted automatically.", UserActionRemedyDestination.RefreshMeeting, "Refresh the meeting, then retry when the artifact is available.", true),
        UserActionBlockedReasonKind.SetupPending => new(kind, "Required setup is not ready yet.", UserActionScope.Setup, "The action stays paused until setup is complete.", UserActionRemedyDestination.Help, "Complete the related setup, then try again.", false),
        UserActionBlockedReasonKind.RecordingActive => new(kind, "Recording is active.", UserActionScope.Home, "This action waits so it does not interrupt the active recording.", UserActionRemedyDestination.Help, "Stop recording before trying this action.", true),
        UserActionBlockedReasonKind.AppBusy => new(kind, "The app is busy with another change.", UserActionScope.Home, "Only one conflicting change can run at a time.", UserActionRemedyDestination.RefreshMeeting, "Wait for the current change to finish, then refresh and try again.", true),
        UserActionBlockedReasonKind.QueuePaused => new(kind, "Processing is paused.", UserActionScope.Home, "No new background work is started while processing is paused.", UserActionRemedyDestination.Help, "Resume processing, then try again.", true),
        UserActionBlockedReasonKind.OperationInProgress => new(kind, "This meeting is already being updated.", UserActionScope.MeetingDetails, "Only one update can change this meeting at a time.", UserActionRemedyDestination.RefreshMeeting, "Wait for the current update to finish, then refresh this meeting.", true),
        UserActionBlockedReasonKind.SpeakerLabelsMissing => new(kind, "Speaker labels are needed before names can be reviewed.", UserActionScope.MeetingDetails, "No names are inferred or changed until speaker labels exist.", UserActionRemedyDestination.SpeakerLabelingSetup, "Add speaker labels, then return to review meeting display names.", true),
        UserActionBlockedReasonKind.SpeakerVoiceSamplesMissing => new(kind, "Voice samples are needed before a local suggestion can be made.", UserActionScope.MeetingDetails, "No profile suggestion is made without usable local samples.", UserActionRemedyDestination.SpeakerLabelingSetup, "Add speaker labels and voice samples, then refresh this meeting.", true),
        UserActionBlockedReasonKind.SpeakerLearningDisabled => new(kind, "Local speaker-name learning is off.", UserActionScope.Settings, "Existing meeting display names stay unchanged.", UserActionRemedyDestination.SpeakerLabelingSetup, "Turn on local speaker-name learning in Settings if you want future suggestions.", false),
        UserActionBlockedReasonKind.VoiceProfilesUnavailable => new(kind, "Local Voice Profiles are unavailable right now.", UserActionScope.Settings, "No meeting display names are changed while local profiles are unavailable.", UserActionRemedyDestination.RefreshMeeting, "Retry local storage, then refresh Settings.", true),
        UserActionBlockedReasonKind.LocalStorageUnavailable => new(kind, "Local storage is unavailable right now.", UserActionScope.LocalProfileStore, "No partial local change is reported as complete.", UserActionRemedyDestination.RefreshMeeting, "Retry when local storage is available.", true),
        UserActionBlockedReasonKind.PermissionDenied => new(kind, "The app does not have permission for this action.", UserActionScope.Home, "No protected data is changed.", UserActionRemedyDestination.Help, "Review the app permissions, then try again.", false),
        UserActionBlockedReasonKind.NetworkUnavailable => new(kind, "The required network connection is unavailable.", UserActionScope.Settings, "No hosted request is sent until the connection is available.", UserActionRemedyDestination.RefreshMeeting, "Check the connection, then retry.", true),
        UserActionBlockedReasonKind.DataStale => new(kind, "This meeting changed before the action could finish.", UserActionScope.MeetingDetails, "Your existing drafts are preserved and no stale change is applied.", UserActionRemedyDestination.RefreshMeeting, "Refresh the meeting and review the current values before trying again.", true),
        UserActionBlockedReasonKind.NoSafeRemedy => new(kind, "This action cannot continue safely.", UserActionScope.Home, "No automatic recovery or retry is attempted.", UserActionRemedyDestination.Help, "Review the app activity or support guidance before trying again.", false),
        UserActionBlockedReasonKind.OperationFailed => new(kind, "The action did not finish.", UserActionScope.Home, "No incomplete result is presented as complete.", UserActionRemedyDestination.RefreshMeeting, "Retry from the current meeting state.", true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown blocked reason."),
    };

    private static ActionCopy ResolveAction(UserActionIntent intent) => intent switch
    {
        UserActionIntent.None => new("Continue", "Review the current state before continuing.", null, "Working...", "Done.", "State updated."),
        UserActionIntent.ConfigureSummary => new("Configure Summaries", "Review how future published transcripts may be summarized.", null, "Opening summary settings...", "Summary settings are ready to review.", "Summary settings opened."),
        UserActionIntent.GenerateSummary => new("Generate Summary", "Create a summary from this meeting's published transcript.", null, "Generating summary from the published transcript...", "Summary generated from this meeting's published transcript.", "Summary generated."),
        UserActionIntent.RetrySummary => new("Retry Summary", "Retry summary generation from the current published transcript.", null, "Retrying summary generation from the published transcript...", "Summary generated from this meeting's published transcript.", "Summary retry started."),
        UserActionIntent.ValidateSummaryProvider => new("Validate Summary Provider", "Test the configured summary provider with a synthetic request.", null, "Validating summary provider...", "Summary provider validation finished.", "Summary provider validation finished."),
        UserActionIntent.ValidateLocalSummaryProvider => new("Validate ModelProxy", "Test the local summary gateway with a synthetic request.", null, "Validating the local summary gateway...", "Local summary gateway validation finished.", "Local summary gateway validation finished."),
        UserActionIntent.ValidateHostedSummaryProvider => new("Validate OpenAI", "Test the hosted summary provider with a synthetic request.", null, "Validating the hosted summary provider...", "Hosted summary provider validation finished.", "Hosted summary provider validation finished."),
        UserActionIntent.RefreshSummaryProviderModels => new("Refresh Models", "Load the current model choices for the configured summary provider.", null, "Refreshing summary models...", "Summary model choices refreshed.", "Summary models refreshed."),
        UserActionIntent.SaveSummarySettings => new("Save Summary Settings", "Save summary choices for future requests.", null, "Saving summary settings...", "Summary settings saved.", "Summary settings saved."),
        UserActionIntent.AddSpeakerLabels => new("Add Speaker Labels", "Queue speaker labeling for this meeting.", null, "Queueing speaker labeling...", "Speaker labeling was queued.", "Speaker labeling queued."),
        UserActionIntent.RepairSpeakerLabels => new("Repair Speaker Labels", "Queue a repair for this meeting's speaker labels.", null, "Queueing speaker-label repair...", "Speaker-label repair was queued.", "Speaker-label repair queued."),
        UserActionIntent.ApplyMeetingDisplayNames => new("Apply Meeting Display Names", "Save the reviewed display names for this meeting.", null, "Saving meeting display names...", "Meeting display names saved.", "Meeting display names saved."),
        UserActionIntent.RefreshLocalNameSuggestions => new("Refresh Local Suggestions", "Recheck local suggestions for this meeting.", null, "Refreshing local suggestions...", "Local suggestions refreshed.", "Local suggestions refreshed."),
        UserActionIntent.UndoProfileNames => new("Undo Profile Names", "Remove profile-applied names from this meeting only.", "This changes display names for this meeting only.", "Undoing profile-applied names...", "Profile-applied names were removed from this meeting.", "Profile-applied names removed."),
        UserActionIntent.EnableVoiceProfile => new("Enable Voice Profile", "Allow this local profile to inform future suggestions.", null, "Enabling local Voice Profile...", "Local Voice Profile enabled for future suggestions.", "Local Voice Profile enabled."),
        UserActionIntent.DisableVoiceProfile => new("Disable Voice Profile", "Stop this local profile from informing future suggestions.", "Existing meeting display names stay unchanged.", "Disabling local Voice Profile...", "Local Voice Profile disabled for future suggestions.", "Local Voice Profile disabled."),
        UserActionIntent.DeleteVoiceProfile => new("Delete Voice Profile", "Remove this local profile from future suggestions.", "Existing meeting display names stay unchanged.", "Deleting local Voice Profile...", "Local Voice Profile deleted from future suggestions.", "Local Voice Profile deleted."),
        UserActionIntent.DeleteAllVoiceProfiles => new("Delete All Voice Profiles", "Remove all local profiles from future suggestions.", "Existing meeting display names stay unchanged.", "Deleting local Voice Profiles...", "Local Voice Profiles deleted from future suggestions.", "Local Voice Profiles deleted."),
        UserActionIntent.RefreshMeetingDetails => new("Refresh Meeting", "Load the current state for this meeting.", null, "Refreshing meeting...", "Meeting refreshed.", "Meeting refreshed."),
        UserActionIntent.ArchiveMeetings => new("Archive Meetings", "Move the selected published meetings to the archive.", "Archived meetings remain recoverable in the archive until they are permanently deleted.", "Archiving selected meetings...", "Selected meetings archived.", "Selected meetings archived."),
        UserActionIntent.DeleteMeetings => new("Delete Meetings Permanently", "Permanently delete the selected published meetings.", "This cannot be undone after confirmation.", "Deleting selected meetings permanently...", "Selected meetings deleted permanently.", "Selected meetings deleted permanently."),
        UserActionIntent.ApplyCleanupRecommendations => new("Apply Cleanup Recommendations", "Apply the selected approved cleanup actions.", null, "Applying selected cleanup recommendations...", "Selected cleanup recommendations applied.", "Selected cleanup recommendations applied."),
        UserActionIntent.DismissCleanupRecommendations => new("Dismiss Cleanup Recommendations", "Hide the selected cleanup recommendations from future review.", "Dismissed recommendations can be regenerated only when their source state changes.", "Dismissing selected cleanup recommendations...", "Selected cleanup recommendations dismissed.", "Selected cleanup recommendations dismissed."),
        UserActionIntent.StartRecording => new("Start Recording", "Start recording current meeting audio.", null, "Starting recording...", "Recording started.", "Recording started."),
        UserActionIntent.StopRecording => new("Stop Recording", "Finish current recording and queue it for processing.", null, "Stopping recording...", "Recording stopped and queued for processing.", "Recording stopped."),
        UserActionIntent.RenameMeeting => new("Rename Meeting", "Save a new display title for this published meeting.", null, "Renaming meeting...", "Meeting renamed.", "Meeting renamed."),
        UserActionIntent.SuggestMeetingTitle => new("Suggest Meeting Title", "Find a better local calendar or meeting-history title.", null, "Finding a meeting title suggestion...", "Meeting title suggestion ready to review.", "Meeting title suggestion ready."),
        UserActionIntent.UpdateMeetingProject => new("Update Meeting Project", "Save this meeting's project assignment.", null, "Updating meeting project...", "Meeting project updated.", "Meeting project updated."),
        UserActionIntent.RegenerateTranscript => new("Regenerate Transcript", "Queue a new transcript from this meeting's published audio.", null, "Queueing transcript regeneration...", "Transcript regeneration queued.", "Transcript regeneration queued."),
        UserActionIntent.MergeMeetings => new("Merge Meetings", "Combine selected published meetings into one new meeting.", "Original meetings remain available until the merge completes.", "Merging selected meetings...", "Selected meetings merged.", "Selected meetings merged."),
        UserActionIntent.SplitMeeting => new("Split Meeting", "Create two meetings at selected split point.", "Original meeting remains available until split processing completes.", "Splitting meeting...", "Meeting split queued.", "Meeting split queued."),
        UserActionIntent.SaveSettings => new("Save Settings", "Save changes for future app behavior.", null, "Saving settings...", "Settings saved.", "Settings saved."),
        UserActionIntent.ImportAudio => new("Import Audio", "Create a meeting from selected local audio.", null, "Preparing audio import...", "Audio import queued.", "Audio import queued."),
        UserActionIntent.ManageModelAssets => new("Manage Model Assets", "Change available transcription or speaker-labeling assets.", null, "Updating model assets...", "Model assets updated.", "Model assets updated."),
        UserActionIntent.CheckForUpdates => new("Check For Updates", "Check configured update source for a newer version.", null, "Checking for updates...", "Update check finished.", "Update check finished."),
        UserActionIntent.InstallUpdates => new("Install Update", "Install already-downloaded update when app is idle.", "Recording and background processing must finish before installation.", "Installing update...", "Update installation started.", "Update installation started."),
        UserActionIntent.UpdateSpeakerLabelingMode => new("Update Speaker Labeling Mode", "Save speaker-labeling behavior for future processing.", null, "Updating speaker-labeling mode...", "Speaker-labeling mode updated.", "Speaker-labeling mode updated."),
        UserActionIntent.UpdateLaunchOnLogin => new("Update Launch On Login", "Save whether Meeting Recorder starts with Windows.", null, "Updating launch-on-login...", "Launch-on-login setting updated.", "Launch-on-login setting updated."),
        UserActionIntent.UpdateRushProcessing => new("Update Rush Processing", "Change priority for selected meeting processing.", null, "Updating rush processing...", "Rush processing updated.", "Rush processing updated."),
        UserActionIntent.OpenArtifact => new("Open Meeting Artifact", "Open selected local meeting artifact.", null, "Opening meeting artifact...", "Meeting artifact opened.", "Meeting artifact opened."),
        UserActionIntent.CopyArtifactPath => new("Copy Artifact Path", "Copy selected local artifact path.", null, "Copying artifact path...", "Artifact path copied.", "Artifact path copied."),
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown user action intent."),
    };

    private static string ResolvePrimaryAction(string actionLabel, BlockedReason reason) => reason.RemedyDestination switch
    {
        UserActionRemedyDestination.None => actionLabel,
        UserActionRemedyDestination.Settings => "Open Settings",
        UserActionRemedyDestination.SummarySettings => "Open Summary Settings",
        UserActionRemedyDestination.TranscriptionSetup => "Open Transcription Setup",
        UserActionRemedyDestination.SpeakerLabelingSetup => "Open Speaker Labeling Setup",
        UserActionRemedyDestination.MeetingDetails => "Open Meeting Details",
        UserActionRemedyDestination.RefreshMeeting => "Refresh Meeting",
        UserActionRemedyDestination.Help => "Open Help",
        _ => actionLabel,
    };

    private sealed record ActionCopy(
        string Label,
        string HelperText,
        string? ConfirmationText,
        string ProgressText,
        string SuccessText,
        string LiveAnnouncement);
}
