using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Globalization;

namespace MeetingRecorder.Core.Services;

internal sealed record ConfigEditorSnapshot(
    string AudioOutputDir,
    string TranscriptOutputDir,
    string WorkDir,
    string ModelCacheDir,
    string TranscriptionModelPath,
    string DiarizationAssetPath,
    bool UseGpuAcceleration,
    string AutoDetectThresholdText,
    string MeetingStopTimeoutText,
    bool MicCaptureEnabled,
    bool LaunchOnLoginEnabled,
    bool AutoDetectEnabled,
    bool CalendarTitleFallbackEnabled,
    bool MeetingAttendeeEnrichmentEnabled,
    bool UpdateCheckEnabled,
    bool AutoInstallUpdatesEnabled,
    string UpdateFeedUrl);

internal sealed record SpeakerLabelDraft(string OriginalLabel, string EditedLabel);

internal enum ShellStatusTarget
{
    None = 0,
    SettingsSetup = 1,
    SettingsUpdates = 2,
    SettingsGeneral = 3,
}

internal sealed record ShellStatusState(
    string Headline,
    string Body,
    ShellStatusTarget Target,
    string? ActionLabel);

internal enum DashboardPrimaryActionTarget
{
    None = 0,
    Setup = 1,
    SettingsUpdates = 2,
    SettingsGeneral = 3,
}

internal enum AppShutdownMode
{
    Deferred = 0,
    Immediate = 1,
}

internal sealed record DashboardPrimaryActionState(
    string Headline,
    string Body,
    DashboardPrimaryActionTarget Target,
    string? ActionLabel);

internal sealed record ConfigDependencyState(
    bool AutoInstallUpdatesEnabled,
    string AutoInstallUpdatesHint,
    bool AutoDetectTuningEnabled,
    string AutoDetectSettingsHint,
    string MicCaptureWarning,
    string MicCapturePendingBadgeText);

internal enum ModelsTabSetupActionKind
{
    DownloadRecommendedModel = 0,
    OpenTranscriptionManagement = 1,
    DownloadRecommendedDiarizationBundle = 2,
    OpenSpeakerLabelingManagement = 3,
}

internal sealed record ModelsTabSetupAction(
    ModelsTabSetupActionKind Kind,
    string Label,
    string NextStepStatusText,
    string FocusTargetName);

internal sealed record ModelsTabSetupState(
    string Status,
    string Body,
    ModelsTabSetupAction PrimaryAction)
{
    public string PrimaryActionLabel => PrimaryAction.Label;
}

internal sealed record MeetingInspectorState(
    string Title,
    string StartedAtUtc,
    string Duration,
    string Platform,
    string Status,
    string TranscriptionModelFileName,
    string SpeakerLabelState,
    IReadOnlyList<string> AttendeeNames,
    IReadOnlyList<string> RecommendationBadges)
{
    public string ProjectName { get; init; } = string.Empty;

    public string DetectedAudioSourceSummary { get; init; } = string.Empty;
}

internal sealed record MeetingContextActionState(
    bool ShowSingleMeetingActionGroup,
    bool ShowBulkMeetingActionGroup,
    bool CanOpenAudio,
    bool CanOpenTranscript,
    bool CanOpenContainingFolder,
    bool CanCopyAudioPath,
    bool CanCopyTranscriptPath,
    bool CanApplyRecommendedAction,
    bool CanRename,
    bool CanSuggestTitle,
    bool CanRegenerateTranscript,
    bool CanReTranscribeWithDifferentModel,
    bool CanAddSpeakerLabels,
    bool CanSplit,
    bool CanArchive,
    bool CanDeletePermanently,
    bool CanApplyRecommendationsForSelection,
    bool CanMergeSelected,
    bool CanReTranscribeSelectedWithModel,
    bool CanAddSpeakerLabelsToSelected,
    bool CanArchiveSelected,
    bool CanDeleteSelectedPermanently);

internal sealed record MeetingWorkspaceToolState(
    bool ShowCleanupTray,
    bool ShowWorkspaceTools,
    bool ShowProjectTool,
    bool ShowSingleMeetingActions,
    bool ShowMultiMeetingActions,
    bool ShowTitleAndTranscriptTool,
    bool ShowSplitTool,
    bool ShowMergeTool,
    bool ShowSpeakerLabelsTool);

internal static class MainWindowInteractionLogic
{
    public static ShellStatusState BuildShellStatus(
        bool hasValidModel,
        bool isRecording,
        bool micCaptureEnabled,
        bool updateChecksEnabled,
        bool autoInstallUpdatesEnabled,
        AppUpdateCheckResult? updateResult)
    {
        if (!hasValidModel)
        {
            return new ShellStatusState(
                "SETUP",
                "Model required",
                ShellStatusTarget.SettingsSetup,
                "Setup");
        }

        if (!micCaptureEnabled)
        {
            return new ShellStatusState(
                "MIC OFF",
                "Own voice omitted",
                ShellStatusTarget.SettingsGeneral,
                "Settings");
        }

        if (updateResult?.Status == AppUpdateStatusKind.UpdateAvailable && !autoInstallUpdatesEnabled)
        {
            return new ShellStatusState(
                "UPDATE",
                "Version available",
                ShellStatusTarget.SettingsUpdates,
                "Updates");
        }

        if (!updateChecksEnabled)
        {
            return new ShellStatusState(
                "CHECKS OFF",
                "Manual only",
                ShellStatusTarget.SettingsGeneral,
                "Settings");
        }

        if (isRecording)
        {
            return new ShellStatusState(
                "RECORDING",
                "Audio live",
                ShellStatusTarget.None,
                null);
        }

        return new ShellStatusState(
            "READY",
            "Manual or auto-detect",
            ShellStatusTarget.None,
            null);
    }

    public static DashboardPrimaryActionState BuildDashboardPrimaryAction(
        bool hasValidModel,
        bool isRecording,
        bool savedMicCaptureEnabled,
        bool hasPendingMicCaptureChange,
        bool pendingMicCaptureEnabled,
        bool updateChecksEnabled,
        bool autoInstallUpdatesEnabled,
        AppUpdateCheckResult? updateResult)
    {
        if (!hasValidModel)
        {
            return new DashboardPrimaryActionState(
                "Set up transcription first",
                "Download or import a valid Whisper model so new recordings can be transcribed without extra cleanup later.",
                DashboardPrimaryActionTarget.Setup,
                "Open Setup");
        }

        if (!savedMicCaptureEnabled)
        {
            var body = hasPendingMicCaptureChange && pendingMicCaptureEnabled
                ? "Microphone capture is still off in the saved settings. Save Changes to turn it on for future recordings."
                : "Your own voice may be missing from recordings until microphone capture is turned back on in Settings.";
            return new DashboardPrimaryActionState(
                "Microphone capture is off",
                body,
                DashboardPrimaryActionTarget.SettingsGeneral,
                "Open Settings");
        }

        if (updateResult?.Status == AppUpdateStatusKind.UpdateAvailable && !autoInstallUpdatesEnabled)
        {
            return new DashboardPrimaryActionState(
                "A newer app version is ready",
                "Open Settings and review the Updates section to install it when the app is idle.",
                DashboardPrimaryActionTarget.SettingsUpdates,
                "Review Updates");
        }

        if (!updateChecksEnabled)
        {
            return new DashboardPrimaryActionState(
                "Turn on daily update checks",
                "Enable automatic GitHub checks so the app can keep itself current and reduce manual maintenance.",
                DashboardPrimaryActionTarget.SettingsGeneral,
                "Open Settings");
        }

        if (isRecording)
        {
            return new DashboardPrimaryActionState(
                "Recording is in progress",
                "The app is already capturing audio. You can keep the current meeting title updated here while the recording runs.",
                DashboardPrimaryActionTarget.None,
                null);
        }

        return new DashboardPrimaryActionState(
            "You are ready to record",
            "Core setup looks healthy. Start from Home when you need a manual recording, or let auto-detection watch for supported meetings.",
            DashboardPrimaryActionTarget.None,
            null);
    }

    public static AppShutdownMode GetAppShutdownMode(
        bool installerRequestedShutdown,
        bool isRecording,
        bool isProcessingInProgress)
    {
        return installerRequestedShutdown && !isRecording && !isProcessingInProgress
            ? AppShutdownMode.Immediate
            : AppShutdownMode.Deferred;
    }

    public static ModelsTabSetupState BuildModelsTabTranscriptionSetupState(
        bool hasValidModel,
        string? activeModelFileName,
        WhisperRemoteModelAsset? recommendedModel)
    {
        if (!hasValidModel)
        {
            return new ModelsTabSetupState(
                "Needs setup",
                "Choose one Whisper model to activate transcription. Download the recommended model below, or import an approved local file if GitHub is blocked.",
                new ModelsTabSetupAction(
                    ModelsTabSetupActionKind.DownloadRecommendedModel,
                    "Download Recommended Model",
                    recommendedModel is null
                        ? "No recommended GitHub model is loaded yet. Review the transcription setup section below or import an approved local model file."
                        : $"Review the transcription setup section below to download '{recommendedModel.FileName}' or choose an approved local model file.",
                    "DownloadRecommendedRemoteModelButton"));
        }

        var activeModelText = string.IsNullOrWhiteSpace(activeModelFileName)
            ? "A valid Whisper model is active. Change the active model below if you want to trade speed for accuracy."
            : $"'{activeModelFileName.Trim()}' is active. Change the active model below if you want to trade speed for accuracy.";

        return new ModelsTabSetupState(
            "Ready",
            activeModelText,
            new ModelsTabSetupAction(
                ModelsTabSetupActionKind.OpenTranscriptionManagement,
                "Change Active Model",
                "Review the transcription setup section below to choose a different active Whisper model or import another approved local file.",
                "AvailableModelsComboBox"));
    }

    public static ModelsTabSetupState BuildModelsTabSpeakerLabelingSetupState(
        bool isReady,
        string? configuredAssetPath,
        DiarizationRemoteAsset? recommendedAsset)
    {
        if (!isReady)
        {
            return new ModelsTabSetupState(
                "Optional add-on",
                recommendedAsset is null
                    ? "Speaker labeling is optional. No recommended GitHub diarization model bundle is loaded right now, so open the local setup guide or review the setup options below."
                    : "Speaker labeling is optional. Download the recommended diarization model bundle from GitHub, open the local setup guide, or review the alternate public download locations below.",
                new ModelsTabSetupAction(
                    recommendedAsset is null
                        ? ModelsTabSetupActionKind.OpenSpeakerLabelingManagement
                        : ModelsTabSetupActionKind.DownloadRecommendedDiarizationBundle,
                    recommendedAsset is null
                        ? "Show Setup Options"
                        : "Download Recommended Bundle",
                    recommendedAsset is null
                        ? "Review the speaker-labeling setup section below for local help, approved imports, and any curated alternate public download locations."
                        : $"Review the speaker-labeling setup section below to download '{recommendedAsset.FileName}', open local setup help, or inspect alternate public download locations.",
                    recommendedAsset is null
                        ? "OpenSpeakerLabelingSetupGuideButton"
                        : "DownloadRecommendedDiarizationBundleButton"));
        }

        var configuredPathText = string.IsNullOrWhiteSpace(configuredAssetPath)
            ? "Speaker labeling is ready. Review the setup details below if you need to replace or repair it."
            : $"Speaker labeling is ready from '{configuredAssetPath.Trim()}'. Review the setup details below if you need to replace or repair it.";

        return new ModelsTabSetupState(
            "Ready",
            configuredPathText,
            new ModelsTabSetupAction(
                ModelsTabSetupActionKind.OpenSpeakerLabelingManagement,
                "Review Speaker Labeling Setup",
                "Review the speaker-labeling setup section below to inspect the current asset, open local setup help, or choose a replacement path.",
                "OpenSpeakerLabelingSetupGuideButton"));
    }

    public static AppConfig PromotePendingUpdateToInstalledReleaseMetadata(AppConfig config, string currentVersion)
    {
        return config with
        {
            InstalledReleaseVersion = string.IsNullOrWhiteSpace(currentVersion)
                ? config.InstalledReleaseVersion
                : currentVersion,
            InstalledReleasePublishedAtUtc = config.PendingUpdatePublishedAtUtc ?? config.InstalledReleasePublishedAtUtc,
            InstalledReleaseAssetSizeBytes = config.PendingUpdateAssetSizeBytes ?? config.InstalledReleaseAssetSizeBytes,
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
        };
    }

    public static bool IsPendingUpdateAlreadyInstalled(AppConfig config, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(config.PendingUpdateVersion) ||
            string.IsNullOrWhiteSpace(currentVersion) ||
            !string.Equals(config.PendingUpdateVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (config.PendingUpdatePublishedAtUtc.HasValue)
        {
            if (!config.InstalledReleasePublishedAtUtc.HasValue ||
                config.PendingUpdatePublishedAtUtc.Value != config.InstalledReleasePublishedAtUtc.Value)
            {
                return false;
            }
        }

        if (config.PendingUpdateAssetSizeBytes.HasValue)
        {
            if (!config.InstalledReleaseAssetSizeBytes.HasValue ||
                config.PendingUpdateAssetSizeBytes.Value != config.InstalledReleaseAssetSizeBytes.Value)
            {
                return false;
            }
        }

        return true;
    }

    public static string BuildAutoInstallUpdatesHint(bool updateChecksEnabled)
    {
        return updateChecksEnabled
            ? "If enabled, the app will install newer releases automatically once recording and background processing are idle."
            : "Turn on daily GitHub checks first. Automatic install only works after update checks are enabled.";
    }

    public static ConfigDependencyState BuildConfigDependencyState(
        bool updateChecksEnabled,
        bool autoDetectEnabled,
        bool savedMicCaptureEnabled,
        bool pendingMicCaptureEnabled,
        bool isRecording)
    {
        var hasPendingMicCaptureChange = savedMicCaptureEnabled != pendingMicCaptureEnabled;

        return new ConfigDependencyState(
            AutoInstallUpdatesEnabled: updateChecksEnabled,
            AutoInstallUpdatesHint: BuildAutoInstallUpdatesHint(updateChecksEnabled),
            AutoDetectTuningEnabled: autoDetectEnabled,
            AutoDetectSettingsHint: BuildAutoDetectSettingsHint(autoDetectEnabled),
            MicCaptureWarning: BuildMicCaptureWarning(
                savedMicCaptureEnabled,
                isRecording,
                hasPendingMicCaptureChange,
                pendingMicCaptureEnabled),
            MicCapturePendingBadgeText: BuildMicCapturePendingBadgeText(
                savedMicCaptureEnabled,
                hasPendingMicCaptureChange,
                pendingMicCaptureEnabled));
    }

    public static string? GetPreferredRemoteModelSelectionFileName(
        IReadOnlyList<WhisperRemoteModelAsset> remoteModels,
        string? previouslySelectedFileName)
    {
        if (remoteModels.Count == 0)
        {
            return null;
        }

        var normalizedPreviousSelection = NormalizeText(previouslySelectedFileName);
        if (!string.IsNullOrWhiteSpace(normalizedPreviousSelection))
        {
            var matchingModel = remoteModels.FirstOrDefault(model =>
                string.Equals(
                    NormalizeText(model.FileName),
                    normalizedPreviousSelection,
                    StringComparison.OrdinalIgnoreCase));
            if (matchingModel is not null)
            {
                return matchingModel.FileName;
            }
        }

        return remoteModels.FirstOrDefault(model => model.IsRecommended)?.FileName ??
            remoteModels[0].FileName;
    }

    public static string BuildAutoDetectSettingsHint(bool autoDetectEnabled)
    {
        return autoDetectEnabled
            ? "The audio threshold and stop timeout below apply immediately while auto-detection is on."
            : "Auto-detection is off, so the threshold and timeout fields below are ignored until you turn it back on.";
    }

    public static string BuildDetectionSummary(DetectionDecision? decision)
    {
        if (decision is null)
        {
            return "No meeting detected.";
        }

        var platformName = GetPlatformName(decision.Platform);
        if (!decision.ShouldKeepRecording)
        {
            return $"{platformName} is open, but the current window is not an active meeting and should not trigger recording.";
        }

        if (!decision.ShouldStart)
        {
            return $"Possible {platformName} meeting '{decision.SessionTitle}' detected, but no active system audio was observed yet.";
        }

        return $"Detected {platformName} meeting '{decision.SessionTitle}' with confidence {decision.Confidence:P0}.";
    }

    public static string BuildRecordingControlsHint(
        bool isRecording,
        bool isRecordingTransitionInProgress,
        bool isUpdateInstallInProgress)
    {
        if (isUpdateInstallInProgress)
        {
            return "Recording controls are temporarily disabled while an update is being installed.";
        }

        if (isRecordingTransitionInProgress)
        {
            return isRecording
                ? "The app is finishing the current recording action."
                : "The app is switching recording state.";
        }

        return isRecording
            ? "Recording is active. Stop when you're ready to finish this session."
            : "Start manually here, or wait for supported auto-detection.";
    }

    public static string BuildDeferredUpdateInstallMessage(string blockReason, string downloadedPath)
    {
        var safeBlockReason = string.IsNullOrWhiteSpace(blockReason)
            ? "The app became busy before the installer handoff could finish."
            : blockReason.Trim();
        var safeDownloadedPath = string.IsNullOrWhiteSpace(downloadedPath)
            ? "the downloaded update ZIP"
            : $"'{downloadedPath}'";

        return
            $"{safeBlockReason} The update ZIP is already downloaded to {safeDownloadedPath}. " +
            "Finish the current recording or background work and try Install Update again. " +
            "If you want the update to apply right away, restarting the app is a safe next step.";
    }

    public static string BuildRecordingStoppedMessage(bool processingQueued)
    {
        return processingQueued
            ? "Recording stopped. Transcript processing is continuing in the background."
            : "Recording stopped. Processing is deferred until the next app launch.";
    }

    public static string BuildMicCaptureWarning(
        bool savedMicCaptureEnabled,
        bool isRecording,
        bool hasPendingMicCaptureChange = false,
        bool pendingMicCaptureEnabled = false)
    {
        if (!hasPendingMicCaptureChange)
        {
            if (savedMicCaptureEnabled)
            {
                return string.Empty;
            }

            return isRecording
                ? "Microphone capture is off, so this recording may miss your voice. Turn it back on in Config for future sessions."
                : "Microphone capture is off, so your voice may be missing from the next recording. Turn it back on in Config if you want both sides captured.";
        }

        if (!savedMicCaptureEnabled && pendingMicCaptureEnabled)
        {
            return isRecording
                ? "Microphone capture is still off in the saved config, so this recording may miss your voice. Save Config to turn it on for future sessions."
                : "Microphone capture is still off in the saved config. Save Config to turn it on for future recordings.";
        }

        if (savedMicCaptureEnabled && !pendingMicCaptureEnabled)
        {
            return string.Empty;
        }

        return isRecording
            ? "Microphone capture is still on in the saved config for this recording. Save Config to turn it off for future sessions."
            : "Microphone capture is still on in the saved config. Save Config if you want future recordings to stop capturing your microphone.";
    }

    public static string BuildMicCapturePendingBadgeText(
        bool savedMicCaptureEnabled,
        bool hasPendingMicCaptureChange,
        bool pendingMicCaptureEnabled)
    {
        if (!hasPendingMicCaptureChange)
        {
            return string.Empty;
        }

        if (!savedMicCaptureEnabled && pendingMicCaptureEnabled)
        {
            return "Unsaved: will turn on after Save Config";
        }

        if (savedMicCaptureEnabled && !pendingMicCaptureEnabled)
        {
            return "Unsaved: will turn off after Save Config";
        }

        return string.Empty;
    }

    public static bool ShouldPromptToEnableMicCapture(
        bool micCaptureEnabled,
        bool isRecording,
        bool hasDetectedMicrophoneActivity,
        bool alreadyPromptedForSession)
    {
        return !micCaptureEnabled &&
               isRecording &&
               hasDetectedMicrophoneActivity &&
               !alreadyPromptedForSession;
    }

    public static string BuildEnableMicCapturePromptMessage(string? sessionTitle)
    {
        var safeSessionTitle = string.IsNullOrWhiteSpace(sessionTitle)
            ? "this recording"
            : $"'{sessionTitle.Trim()}'";

        return
            $"Meeting Recorder detected live microphone activity during {safeSessionTitle}, but microphone capture is currently turned off. " +
            "Your voice may be missing from this recording. Do you want to turn microphone capture on for future recordings?";
    }

    public static bool ShouldAutoPromoteActiveMeetingTitle(
        MeetingPlatform activePlatform,
        string currentDetectedTitle,
        string currentEditorTitle,
        string proposedTitle,
        bool proposalCameFromCalendarFallback)
    {
        var normalizedCurrentTitle = NormalizeText(currentDetectedTitle);
        var normalizedCurrentEditorTitle = NormalizeText(currentEditorTitle);
        var normalizedProposedTitle = NormalizeText(proposedTitle);

        if (string.IsNullOrWhiteSpace(normalizedCurrentTitle) ||
            string.IsNullOrWhiteSpace(normalizedCurrentEditorTitle) ||
            string.IsNullOrWhiteSpace(normalizedProposedTitle))
        {
            return false;
        }

        if (!string.Equals(normalizedCurrentEditorTitle, normalizedCurrentTitle, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(normalizedCurrentTitle, normalizedProposedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsGenericMeetingTitle(activePlatform, normalizedCurrentTitle))
        {
            return false;
        }

        return proposalCameFromCalendarFallback ||
            !IsGenericMeetingTitle(activePlatform, normalizedProposedTitle);
    }

    public static bool TryParseMeetingSplitPoint(
        string? text,
        TimeSpan? duration,
        out TimeSpan splitPoint,
        out string errorMessage)
    {
        splitPoint = TimeSpan.Zero;

        if (duration is not { } totalDuration || totalDuration <= TimeSpan.FromSeconds(2))
        {
            errorMessage = "This meeting does not have enough readable audio duration to split yet.";
            return false;
        }

        if (!TryParseClockText(text, out splitPoint))
        {
            errorMessage = "Enter a split point as seconds, mm:ss, or hh:mm:ss.";
            return false;
        }

        var minimumSplitPoint = TimeSpan.FromSeconds(1);
        var maximumSplitPoint = totalDuration - TimeSpan.FromSeconds(1);
        if (splitPoint < minimumSplitPoint || splitPoint > maximumSplitPoint)
        {
            errorMessage =
                $"Enter a split point between {FormatMeetingSplitPoint(minimumSplitPoint)} and {FormatMeetingSplitPoint(maximumSplitPoint)}.";
            splitPoint = TimeSpan.Zero;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static string FormatMeetingSplitPoint(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        var wholeSeconds = TimeSpan.FromSeconds(Math.Floor(clamped.TotalSeconds));
        return wholeSeconds.TotalHours >= 1
            ? wholeSeconds.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : wholeSeconds.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    public static TimeSpan GetSuggestedMeetingSplitPoint(TimeSpan duration)
    {
        if (duration <= TimeSpan.FromSeconds(2))
        {
            return TimeSpan.Zero;
        }

        var midpointSeconds = Math.Floor(duration.TotalSeconds / 2d);
        var minimumSeconds = 1d;
        var maximumSeconds = Math.Floor(duration.TotalSeconds) - 1d;
        var clampedSeconds = Math.Clamp(midpointSeconds, minimumSeconds, maximumSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    public static string BuildMeetingSplitPreview(TimeSpan duration, TimeSpan splitPoint)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "Part-length preview will appear here once a valid meeting is selected.";
        }

        var firstPart = splitPoint < TimeSpan.Zero ? TimeSpan.Zero : splitPoint;
        if (firstPart > duration)
        {
            firstPart = duration;
        }

        var secondPart = duration - firstPart;
        return $"Part 1: {FormatMeetingSplitPoint(firstPart)} | Part 2: {FormatMeetingSplitPoint(secondPart)}";
    }

    public static bool HasPendingMeetingRename(string? currentTitle, string? editedTitle)
    {
        var normalizedEditedTitle = NormalizeText(editedTitle);
        if (string.IsNullOrWhiteSpace(normalizedEditedTitle))
        {
            return false;
        }

        return !string.Equals(
            NormalizeText(currentTitle),
            normalizedEditedTitle,
            StringComparison.Ordinal);
    }

    public static bool IsValidPermanentDeleteConfirmationText(string? confirmationText)
    {
        return string.Equals(confirmationText, "DELETE", StringComparison.Ordinal);
    }

    public static MeetingInspectorState BuildMeetingInspectorState(
        MeetingOutputRecord meeting,
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        CultureInfo? culture = null,
        TimeZoneInfo? localTimeZone = null)
    {
        var attendeeNames = meeting.Attendees
            .Select(attendee => attendee.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayedAttendeeNames = meeting.KeyAttendees is { Count: > 0 }
            ? meeting.KeyAttendees
                .Select(MeetingMetadataNameMatcher.NormalizeDisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : attendeeNames;
        var recommendationBadges = recommendations
            .Select(recommendation => BuildMeetingCleanupActionLabel(recommendation.Action))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new MeetingInspectorState(
            meeting.Title,
            FormatMeetingWorkspaceStartedAt(meeting.StartedAtUtc, culture, localTimeZone),
            FormatMeetingInspectorDuration(meeting.Duration),
            meeting.Platform.ToString(),
            MeetingOutputStatusResolver.ResolveDisplayStatus(meeting),
            string.IsNullOrWhiteSpace(meeting.TranscriptionModelFileName)
                ? "Not recorded"
                : meeting.TranscriptionModelFileName,
            meeting.HasSpeakerLabels
                ? "Speaker labels are present."
                : "Speaker labels are missing.",
            displayedAttendeeNames,
            recommendationBadges)
        {
            ProjectName = NormalizeText(meeting.ProjectName),
            DetectedAudioSourceSummary = BuildDetectedAudioSourceSummary(meeting.DetectedAudioSource),
        };
    }

    public static string BuildDetectedAudioSourceSummary(DetectedAudioSource? audioSource)
    {
        if (audioSource is null || string.IsNullOrWhiteSpace(audioSource.AppName))
        {
            return "Not captured.";
        }

        var targetTitle = !string.IsNullOrWhiteSpace(audioSource.BrowserTabTitle)
            ? audioSource.BrowserTabTitle.Trim()
            : !string.IsNullOrWhiteSpace(audioSource.WindowTitle)
                ? audioSource.WindowTitle.Trim()
                : null;
        var confidenceLabel = audioSource.Confidence.ToString();

        return string.IsNullOrWhiteSpace(targetTitle)
            ? $"{audioSource.AppName.Trim()} ({confidenceLabel} confidence)"
            : $"{audioSource.AppName.Trim()} from '{targetTitle}' ({confidenceLabel} confidence)";
    }

    public static MeetingContextActionState BuildMeetingContextActionState(
        int selectedMeetingCount,
        bool hasFocusedMeeting,
        bool canOpenAudio,
        bool canOpenTranscript,
        bool hasRecommendedAction,
        bool canRegenerateTranscript,
        bool canAddSpeakerLabels,
        bool isBusy)
    {
        var isSingleSelection = selectedMeetingCount == 1;
        var isMultiSelection = selectedMeetingCount > 1;
        var canUseSingleMeetingActions = hasFocusedMeeting && isSingleSelection && !isBusy;
        var canUseBulkActions = isMultiSelection && !isBusy;

        return new MeetingContextActionState(
            ShowSingleMeetingActionGroup: isSingleSelection,
            ShowBulkMeetingActionGroup: isMultiSelection,
            CanOpenAudio: canUseSingleMeetingActions && canOpenAudio,
            CanOpenTranscript: canUseSingleMeetingActions && canOpenTranscript,
            CanOpenContainingFolder: canUseSingleMeetingActions && (canOpenAudio || canOpenTranscript),
            CanCopyAudioPath: canUseSingleMeetingActions && canOpenAudio,
            CanCopyTranscriptPath: canUseSingleMeetingActions && canOpenTranscript,
            CanApplyRecommendedAction: canUseSingleMeetingActions && hasRecommendedAction,
            CanRename: canUseSingleMeetingActions,
            CanSuggestTitle: canUseSingleMeetingActions,
            CanRegenerateTranscript: canUseSingleMeetingActions && canRegenerateTranscript,
            CanReTranscribeWithDifferentModel: canUseSingleMeetingActions && canRegenerateTranscript,
            CanAddSpeakerLabels: canUseSingleMeetingActions && canAddSpeakerLabels,
            CanSplit: canUseSingleMeetingActions,
            CanArchive: canUseSingleMeetingActions,
            CanDeletePermanently: canUseSingleMeetingActions,
            CanApplyRecommendationsForSelection: canUseBulkActions && hasRecommendedAction,
            CanMergeSelected: canUseBulkActions && selectedMeetingCount >= 2,
            CanReTranscribeSelectedWithModel: canUseBulkActions,
            CanAddSpeakerLabelsToSelected: canUseBulkActions && canAddSpeakerLabels,
            CanArchiveSelected: canUseBulkActions,
            CanDeleteSelectedPermanently: canUseBulkActions);
    }

    public static MeetingWorkspaceToolState BuildMeetingWorkspaceToolState(
        int selectedMeetingCount,
        bool hasSpeakerLabels,
        bool hasCleanupRecommendations)
    {
        var isSingleSelection = selectedMeetingCount == 1;
        var isMultiSelection = selectedMeetingCount > 1;

        return new MeetingWorkspaceToolState(
            ShowCleanupTray: hasCleanupRecommendations,
            ShowWorkspaceTools: selectedMeetingCount > 0,
            ShowProjectTool: selectedMeetingCount > 0,
            ShowSingleMeetingActions: isSingleSelection,
            ShowMultiMeetingActions: isMultiSelection,
            ShowTitleAndTranscriptTool: isSingleSelection,
            ShowSplitTool: isSingleSelection,
            ShowMergeTool: isMultiSelection,
            ShowSpeakerLabelsTool: isSingleSelection && hasSpeakerLabels);
    }

    public static string FormatMeetingWorkspaceStartedAt(
        DateTimeOffset startedAtUtc,
        CultureInfo? culture = null,
        TimeZoneInfo? localTimeZone = null)
    {
        if (startedAtUtc == DateTimeOffset.MinValue)
        {
            return "Unknown";
        }

        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        var effectiveTimeZone = localTimeZone ?? TimeZoneInfo.Local;
        return TimeZoneInfo.ConvertTime(startedAtUtc, effectiveTimeZone).ToString("g", effectiveCulture);
    }

    public static string FormatMeetingWorkspaceGroupHeader(string groupLabel, int itemCount)
    {
        var normalizedLabel = NormalizeText(groupLabel);
        if (string.IsNullOrWhiteSpace(normalizedLabel))
        {
            normalizedLabel = "Other";
        }

        return itemCount > 0
            ? $"{normalizedLabel} ({itemCount})"
            : normalizedLabel;
    }

    public static IReadOnlyDictionary<string, bool> InitializeMeetingWorkspaceGroupExpansionState(
        IReadOnlyList<string> orderedGroupLabels)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        var isFirstVisibleGroup = true;
        foreach (var groupLabel in orderedGroupLabels
                     .Select(NormalizeText)
                     .Where(label => !string.IsNullOrWhiteSpace(label))
                     .Distinct(StringComparer.Ordinal))
        {
            states[groupLabel] = isFirstVisibleGroup;
            isFirstVisibleGroup = false;
        }

        return states;
    }

    public static string BuildMeetingCleanupBadgeText(IReadOnlyList<MeetingCleanupRecommendation> recommendations)
    {
        var primaryRecommendation = GetPrimaryMeetingCleanupRecommendation(recommendations);
        if (primaryRecommendation is null)
        {
            return string.Empty;
        }

        var headline = BuildMeetingCleanupActionLabel(primaryRecommendation.Action);
        return recommendations.Count == 1
            ? headline
            : $"{headline} +{recommendations.Count - 1}";
    }

    public static MeetingCleanupRecommendation? GetPrimaryMeetingCleanupRecommendation(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations)
    {
        return recommendations
            .OrderBy(recommendation => GetMeetingCleanupActionPriority(recommendation.Action))
            .ThenBy(recommendation => recommendation.Confidence)
            .FirstOrDefault();
    }

    private static string FormatMeetingInspectorDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "Unknown";
        }

        var value = duration.Value;
        return value.TotalHours >= 1d
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    public static string BuildMeetingCleanupActionLabel(MeetingCleanupAction action)
    {
        return action switch
        {
            MeetingCleanupAction.Archive => "Archive",
            MeetingCleanupAction.Merge => "Merge",
            MeetingCleanupAction.Split => "Split",
            MeetingCleanupAction.Rename => "Rename",
            MeetingCleanupAction.RegenerateTranscript => "Retry Transcript",
            MeetingCleanupAction.GenerateSpeakerLabels => "Add Speaker Labels",
            _ => "Review",
        };
    }

    public static bool MeetingMatchesWorkspaceSearch(
        string? searchText,
        string title,
        string platform,
        string status,
        IReadOnlyList<MeetingAttendee> attendees)
    {
        return MeetingMatchesWorkspaceSearch(
            searchText,
            title,
            projectName: null,
            platform,
            status,
            attendees,
            keyAttendees: null);
    }

    public static bool MeetingMatchesWorkspaceSearch(
        string? searchText,
        string title,
        string? projectName,
        string platform,
        string status,
        IReadOnlyList<MeetingAttendee> attendees,
        IReadOnlyList<string>? keyAttendees = null)
    {
        var normalizedSearchText = NormalizeText(searchText);
        if (string.IsNullOrWhiteSpace(normalizedSearchText))
        {
            return true;
        }

        return ContainsSearchText(title, normalizedSearchText) ||
               ContainsSearchText(projectName, normalizedSearchText) ||
               ContainsSearchText(platform, normalizedSearchText) ||
               ContainsSearchText(status, normalizedSearchText) ||
               (keyAttendees?.Any(attendee => ContainsSearchText(attendee, normalizedSearchText)) ?? false) ||
               attendees.Any(attendee => ContainsSearchText(attendee.Name, normalizedSearchText));
    }

    public static string BuildMeetingWorkspaceGroupLabel(
        MeetingsGroupKey groupKey,
        DateTimeOffset startedAtUtc,
        string platform,
        string status,
        CultureInfo? culture = null,
        TimeZoneInfo? localTimeZone = null)
    {
        var effectiveCulture = culture ?? CultureInfo.InvariantCulture;
        return groupKey switch
        {
            MeetingsGroupKey.Week => startedAtUtc == DateTimeOffset.MinValue
                ? "Unknown week"
                : $"Week of {GetMeetingWorkspaceWeekGroupStart(startedAtUtc, localTimeZone):yyyy-MM-dd}",
            MeetingsGroupKey.Month => startedAtUtc == DateTimeOffset.MinValue
                ? "Unknown month"
                : GetMeetingWorkspaceMonthGroupStart(startedAtUtc, localTimeZone).ToString("MMMM yyyy", effectiveCulture),
            MeetingsGroupKey.Platform => string.IsNullOrWhiteSpace(platform)
                ? "Unknown platform"
                : platform.Trim(),
            MeetingsGroupKey.Status => string.IsNullOrWhiteSpace(status)
                ? "Unknown status"
                : status.Trim(),
            _ => "Other",
        };
    }

    public static string GetMeetingWorkspaceGroupPropertyName(MeetingsGroupKey groupKey)
    {
        return groupKey switch
        {
            MeetingsGroupKey.Week => "WeekGroupLabel",
            MeetingsGroupKey.Month => "MonthGroupLabel",
            MeetingsGroupKey.Platform => "PlatformGroupLabel",
            MeetingsGroupKey.Status => "StatusGroupLabel",
            _ => "WeekGroupLabel",
        };
    }

    public static string GetMeetingWorkspaceGroupSortPropertyName(MeetingsGroupKey groupKey)
    {
        return groupKey switch
        {
            MeetingsGroupKey.Week => "WeekGroupSortValue",
            MeetingsGroupKey.Month => "MonthGroupSortValue",
            MeetingsGroupKey.Platform => "PlatformGroupLabel",
            MeetingsGroupKey.Status => "StatusGroupLabel",
            _ => "WeekGroupSortValue",
        };
    }

    public static string GetMeetingWorkspaceSortPropertyName(MeetingsSortKey sortKey)
    {
        return sortKey switch
        {
            MeetingsSortKey.Started => "StartedAtUtcSortValue",
            MeetingsSortKey.Title => "Title",
            MeetingsSortKey.Duration => "DurationSortValue",
            MeetingsSortKey.Platform => "Platform",
            _ => "StartedAtUtcSortValue",
        };
    }

    public static DateTime GetMeetingWorkspaceWeekGroupStart(
        DateTimeOffset startedAtUtc,
        TimeZoneInfo? localTimeZone = null)
    {
        if (startedAtUtc == DateTimeOffset.MinValue)
        {
            return DateTime.MinValue;
        }

        var date = TimeZoneInfo.ConvertTime(startedAtUtc, localTimeZone ?? TimeZoneInfo.Local).Date;
        var offsetFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offsetFromMonday);
    }

    public static DateTime GetMeetingWorkspaceMonthGroupStart(
        DateTimeOffset startedAtUtc,
        TimeZoneInfo? localTimeZone = null)
    {
        if (startedAtUtc == DateTimeOffset.MinValue)
        {
            return DateTime.MinValue;
        }

        var localStartedAt = TimeZoneInfo.ConvertTime(startedAtUtc, localTimeZone ?? TimeZoneInfo.Local).Date;
        return new DateTime(localStartedAt.Year, localStartedAt.Month, 1);
    }

    public static string BuildMeetingArtifactActionLabel(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "Missing"
            : "Open";
    }

    public static string BuildMeetingArtifactToolTip(string? path, string artifactName)
    {
        return string.IsNullOrWhiteSpace(path)
            ? $"{artifactName} is unavailable for this meeting."
            : path;
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> FilterMeetingCleanupRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        IReadOnlyList<string> selectedStems)
    {
        if (selectedStems.Count == 0)
        {
            return recommendations
                .OrderBy(recommendation => GetMeetingCleanupActionPriority(recommendation.Action))
                .ThenBy(recommendation => recommendation.Confidence)
                .ToArray();
        }

        var stemSet = new HashSet<string>(selectedStems, StringComparer.OrdinalIgnoreCase);
        return recommendations
            .OrderByDescending(recommendation => recommendation.RelatedStems.Any(stem => stemSet.Contains(stem)))
            .ThenBy(recommendation => GetMeetingCleanupActionPriority(recommendation.Action))
            .ThenBy(recommendation => recommendation.Confidence)
            .ToArray();
    }

    public static IReadOnlyList<MeetingCleanupRecommendation> GetAutoApplicableMeetingCleanupRecommendations(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations)
    {
        return recommendations
            .Where(IsSafeMeetingCleanupRecommendation)
            .OrderBy(recommendation => GetMeetingCleanupActionPriority(recommendation.Action))
            .ToArray();
    }

    public static bool IsSafeMeetingCleanupRecommendation(MeetingCleanupRecommendation recommendation)
    {
        return recommendation.CanApplyAutomatically &&
               recommendation.Confidence == MeetingCleanupConfidence.High &&
               recommendation.Action is MeetingCleanupAction.Archive or MeetingCleanupAction.Merge or MeetingCleanupAction.RegenerateTranscript or MeetingCleanupAction.GenerateSpeakerLabels;
    }

    public static string BuildMeetingCleanupSafetyLabel(MeetingCleanupRecommendation recommendation)
    {
        return IsSafeMeetingCleanupRecommendation(recommendation)
            ? "Safe Fix"
            : "Review First";
    }

    public static IReadOnlyDictionary<string, string> BuildSpeakerLabelMap(IEnumerable<SpeakerLabelDraft> rows)
    {
        var labelMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var originalLabel = NormalizeText(row.OriginalLabel);
            var editedLabel = NormalizeText(row.EditedLabel);
            if (string.IsNullOrWhiteSpace(originalLabel) ||
                string.IsNullOrWhiteSpace(editedLabel) ||
                string.Equals(originalLabel, editedLabel, StringComparison.Ordinal))
            {
                continue;
            }

            labelMap[originalLabel] = editedLabel;
        }

        return labelMap;
    }

    public static bool HasPendingConfigChanges(AppConfig currentConfig, ConfigEditorSnapshot editor)
    {
        return
            !string.Equals(currentConfig.AudioOutputDir, NormalizeText(editor.AudioOutputDir), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentConfig.TranscriptOutputDir, NormalizeText(editor.TranscriptOutputDir), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentConfig.WorkDir, NormalizeText(editor.WorkDir), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentConfig.ModelCacheDir, NormalizeText(editor.ModelCacheDir), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentConfig.TranscriptionModelPath, NormalizeText(editor.TranscriptionModelPath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentConfig.DiarizationAssetPath, NormalizeText(editor.DiarizationAssetPath), StringComparison.OrdinalIgnoreCase) ||
            (currentConfig.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto) != editor.UseGpuAcceleration ||
            !string.Equals(FormatThreshold(currentConfig.AutoDetectAudioPeakThreshold), NormalizeText(editor.AutoDetectThresholdText), StringComparison.Ordinal) ||
            !string.Equals(currentConfig.MeetingStopTimeoutSeconds.ToString(CultureInfo.InvariantCulture), NormalizeText(editor.MeetingStopTimeoutText), StringComparison.Ordinal) ||
            currentConfig.MicCaptureEnabled != editor.MicCaptureEnabled ||
            currentConfig.LaunchOnLoginEnabled != editor.LaunchOnLoginEnabled ||
            currentConfig.AutoDetectEnabled != editor.AutoDetectEnabled ||
            currentConfig.CalendarTitleFallbackEnabled != editor.CalendarTitleFallbackEnabled ||
            currentConfig.MeetingAttendeeEnrichmentEnabled != editor.MeetingAttendeeEnrichmentEnabled ||
            currentConfig.UpdateCheckEnabled != editor.UpdateCheckEnabled ||
            currentConfig.AutoInstallUpdatesEnabled != editor.AutoInstallUpdatesEnabled ||
            !string.Equals(currentConfig.UpdateFeedUrl, NormalizeText(editor.UpdateFeedUrl), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatThreshold(double threshold)
    {
        return threshold.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool TryParseClockText(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim()
            .Split(':', StringSplitOptions.TrimEntries);

        if (parts.Length is < 1 or > 3 || parts.Any(part => part.Length == 0))
        {
            return false;
        }

        if (!parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        var numbers = parts
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        if (numbers.Any(number => number < 0))
        {
            return false;
        }

        value = numbers.Length switch
        {
            1 => TimeSpan.FromSeconds(numbers[0]),
            2 when numbers[1] < 60 => new TimeSpan(0, numbers[0], numbers[1]),
            3 when numbers[1] < 60 && numbers[2] < 60 => new TimeSpan(numbers[0], numbers[1], numbers[2]),
            _ => TimeSpan.Zero,
        };

        return value > TimeSpan.Zero;
    }

    private static string GetPlatformName(MeetingPlatform platform)
    {
        return platform switch
        {
            MeetingPlatform.GoogleMeet => "Google Meet",
            MeetingPlatform.Teams => "Teams",
            MeetingPlatform.Manual => "Manual",
            _ => "A conferencing app",
        };
    }

    private static int GetMeetingCleanupActionPriority(MeetingCleanupAction action)
    {
        return action switch
        {
            MeetingCleanupAction.Archive => 0,
            MeetingCleanupAction.Merge => 1,
            MeetingCleanupAction.Rename => 2,
            MeetingCleanupAction.RegenerateTranscript => 3,
            MeetingCleanupAction.GenerateSpeakerLabels => 4,
            MeetingCleanupAction.Split => 5,
            _ => 10,
        };
    }

    private static bool IsGenericMeetingTitle(MeetingPlatform platform, string title)
    {
        var normalizedTitle = NormalizeText(title).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return true;
        }

        if (normalizedTitle is "detected meeting" or "meeting")
        {
            return true;
        }

        return platform switch
        {
            MeetingPlatform.Teams => normalizedTitle is "microsoft teams" or "teams" or "ms-teams" or "sharing control bar" or "search",
            MeetingPlatform.GoogleMeet => normalizedTitle is "google meet" or "meet",
            _ => false,
        };
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool ContainsSearchText(string? value, string normalizedSearchText)
    {
        return NormalizeText(value).Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase);
    }
}
