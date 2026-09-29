namespace MeetingRecorder.App;

/// <summary>
/// Lifecycle timing shown beside a setting. Values are conservative where the
/// underlying runtime does not offer an immediate reconfiguration guarantee.
/// </summary>
internal enum SettingsApplyTiming
{
    Immediate = 0,
    NextRecording = 1,
    NextProcessingRun = 2,
    NextAppStart = 3,
    ExternalActionRequired = 4,
}

internal sealed record SettingsNavigationTarget(
    SettingsWindowSection Section,
    string? ControlId = null,
    bool IsKnownRoute = true);

internal sealed record SettingsControlDefinition(
    string ControlId,
    SettingsWindowSection Section,
    SettingsApplyTiming ApplyTiming);

/// <summary>
/// One source of truth for settings sections, legacy routes, visible focus
/// targets, and conservative application timing. This is intentionally pure:
/// resolving a route must not load, save, or normalize configuration.
/// </summary>
internal static class SettingsInformationArchitecture
{
    private static readonly IReadOnlyList<SettingsControlDefinition> ControlDefinitions =
    [
        new("ConfigPreferredTeamsIntegrationModeComboBox", SettingsWindowSection.Setup, SettingsApplyTiming.ExternalActionRequired),
        new("RunTeamsIntegrationProbeButton", SettingsWindowSection.Setup, SettingsApplyTiming.ExternalActionRequired),
        new("OpenTeamsThirdPartyApiGuideButton", SettingsWindowSection.Setup, SettingsApplyTiming.ExternalActionRequired),

        new("ConfigMicCaptureCheckBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),
        new("ConfigAutoDetectCheckBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),
        new("ConfigMeetingAttendeeEnrichmentCheckBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),
        new("ConfigCalendarTitleFallbackCheckBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),
        new("ConfigLaunchOnLoginCheckBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextAppStart),
        new("ConfigAutoDetectThresholdTextBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),
        new("ConfigMeetingStopTimeoutTextBox", SettingsWindowSection.Recording, SettingsApplyTiming.NextRecording),

        new("ConfigDiarizationGpuAccelerationCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("TestDiarizationGpuAccelerationButton", SettingsWindowSection.Processing, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSpeakerNameLearningCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("VoiceProfilesDataGrid", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("DisableVoiceProfileButton", SettingsWindowSection.Processing, SettingsApplyTiming.Immediate),
        new("DeleteVoiceProfileButton", SettingsWindowSection.Processing, SettingsApplyTiming.Immediate),
        new("DeleteAllVoiceProfilesButton", SettingsWindowSection.Processing, SettingsApplyTiming.Immediate),
        new("ConfigInitialProcessingStrategyComboBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigBackgroundSpeakerLabelingModeComboBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigIncrementalQueuedRecordingsCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigIncrementalSpeakerLabelsCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigIncrementalAiSummariesCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigIncrementalSafeCleanupCheckBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigOvernightInitialProcessingStrategyComboBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigOvernightDrainStartTextBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigOvernightDrainEndTextBox", SettingsWindowSection.Processing, SettingsApplyTiming.NextProcessingRun),
        new("ConfigBackgroundProcessingModeComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigTranscriptionProviderPreferenceComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("ConfigTranscriptionCliPathTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("ConfigTranscriptionCliArgumentsTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("TestTranscriptionCliProviderButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigDiarizationProviderPreferenceComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("ConfigDiarizationCliPathTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("ConfigDiarizationCliArgumentsTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("TestDiarizationCliProviderButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),

        new("ConfigSummaryGenerationEnabledCheckBox", SettingsWindowSection.Summaries, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryProviderPreferenceComboBox", SettingsWindowSection.Summaries, SettingsApplyTiming.NextProcessingRun),
        new("ConfigModelProxyValidationStatusTextBlock", SettingsWindowSection.Summaries, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigOpenAiValidationStatusTextBlock", SettingsWindowSection.Summaries, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSummaryModelProxyBaseUrlTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryModelProxyModelComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryModelProxyModelTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("RefreshModelProxySummaryModelsButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSummaryReasoningEffortComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ValidateModelProxySummaryProviderButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSummaryOpenAiModelComboBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryOpenAiModelTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("RefreshOpenAiSummaryModelsButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSummaryOpenAiKeyPasswordBox", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ValidateOpenAiSummaryProviderButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ClearOpenAiSummaryKeyButton", SettingsWindowSection.Advanced, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigSummaryRequestTimeoutTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryTranscriptChunkTargetTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),
        new("ConfigSummaryTranscriptChunkOverlapTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextProcessingRun),

        new("ConfigAudioOutputDirTextBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextRecording),
        new("ConfigTranscriptOutputDirTextBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextProcessingRun),
        new("ConfigImportInboxEnabledCheckBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextProcessingRun),
        new("ConfigImportInboxDirTextBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextProcessingRun),
        new("ConfigImportInboxArchiveAfterQueueEnabledCheckBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextProcessingRun),
        new("ConfigImportInboxMoveBlockedToErrorEnabledCheckBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextProcessingRun),
        new("ConfigUpdateCheckEnabledCheckBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextAppStart),
        new("ConfigAutoInstallUpdatesCheckBox", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.NextAppStart),
        new("CheckForUpdatesButton", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.ExternalActionRequired),
        new("InstallLatestUpdateButton", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.ExternalActionRequired),
        new("DownloadLatestUpdateButton", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.ExternalActionRequired),
        new("OpenLatestReleasePageButton", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.ExternalActionRequired),
        new("InstallQueuedUpdateNowButton", SettingsWindowSection.FilesAndUpdates, SettingsApplyTiming.ExternalActionRequired),
        new("ConfigWorkDirTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
        new("ConfigUpdateFeedUrlTextBox", SettingsWindowSection.Advanced, SettingsApplyTiming.NextAppStart),
    ];

    private static readonly IReadOnlyDictionary<string, SettingsNavigationTarget> RouteTargets =
        new Dictionary<string, SettingsNavigationTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["setup"] = new(SettingsWindowSection.Setup),
            ["recording"] = new(SettingsWindowSection.Recording),
            ["processing"] = new(SettingsWindowSection.Processing),
            ["summaries"] = new(SettingsWindowSection.Summaries),
            ["files-and-updates"] = new(SettingsWindowSection.FilesAndUpdates),
            ["advanced"] = new(SettingsWindowSection.Advanced),
            ["general"] = new(SettingsWindowSection.Recording),
            ["files"] = new(SettingsWindowSection.FilesAndUpdates),
            ["updates"] = new(SettingsWindowSection.FilesAndUpdates, "CheckForUpdatesButton"),
        };

    public static IReadOnlyList<SettingsControlDefinition> Controls => ControlDefinitions;

    public static SettingsNavigationTarget ResolveRoute(string? sectionId)
    {
        return !string.IsNullOrWhiteSpace(sectionId) && RouteTargets.TryGetValue(sectionId, out var target)
            ? target
            : new SettingsNavigationTarget(SettingsWindowSection.Recording, IsKnownRoute: false);
    }

    public static SettingsNavigationTarget ResolveControl(string controlId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);

        var definition = ControlDefinitions.SingleOrDefault(control =>
            string.Equals(control.ControlId, controlId, StringComparison.Ordinal));
        return definition is null
            ? new SettingsNavigationTarget(SettingsWindowSection.Recording, IsKnownRoute: false)
            : new SettingsNavigationTarget(definition.Section, definition.ControlId);
    }
}
