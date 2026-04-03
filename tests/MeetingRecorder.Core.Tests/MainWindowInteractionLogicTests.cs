using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Globalization;

namespace MeetingRecorder.Core.Tests;

public sealed class MainWindowInteractionLogicTests
{
    [Fact]
    public void BuildShellStatus_Prioritizes_Model_Setup_When_No_Valid_Model_Is_Configured()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: false,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.SettingsSetup, result.Target);
        Assert.Equal("Setup", result.ActionLabel);
        Assert.Equal("SETUP", result.Headline);
        Assert.Equal("Model required", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Points_To_Updates_When_Manual_Update_Is_Available()
    {
        var updateResult = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.1",
            "0.2",
            "https://example.com/app.zip",
            "https://example.com/release",
            DateTimeOffset.UtcNow,
            123L,
            true,
            false,
            false,
            "Version 0.2 is available.");

        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: false,
            updateResult: updateResult);

        Assert.Equal(ShellStatusTarget.SettingsUpdates, result.Target);
        Assert.Equal("Updates", result.ActionLabel);
        Assert.Equal("UPDATE", result.Headline);
        Assert.Equal("Version available", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Points_To_General_Settings_When_Update_Checks_Are_Disabled()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: false,
            autoInstallUpdatesEnabled: false,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.SettingsGeneral, result.Target);
        Assert.Equal("Settings", result.ActionLabel);
        Assert.Equal("CHECKS OFF", result.Headline);
        Assert.Equal("Manual only", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Returns_Ready_State_When_Core_Setup_Is_Healthy()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: new AppUpdateCheckResult(
                AppUpdateStatusKind.UpToDate,
                "0.1",
                "0.1",
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                "You are already on version 0.1."));

        Assert.Equal(ShellStatusTarget.None, result.Target);
        Assert.Null(result.ActionLabel);
        Assert.Equal("READY", result.Headline);
        Assert.Equal("Manual or auto-detect", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Points_To_General_Settings_When_Microphone_Capture_Is_Off()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: false,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.SettingsGeneral, result.Target);
        Assert.Equal("Settings", result.ActionLabel);
        Assert.Equal("MIC OFF", result.Headline);
        Assert.Equal("Own voice omitted", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Returns_Informational_State_When_Recording_Is_Already_In_Progress()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: true,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.None, result.Target);
        Assert.Null(result.ActionLabel);
        Assert.Equal("RECORDING", result.Headline);
        Assert.Equal("Audio live", result.Body);
    }

    [Fact]
    public void BuildShellStatus_Uses_Setup_For_Model_Gaps_And_Settings_For_Maintenance_Actions()
    {
        var setupResult = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: false,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);
        var settingsResult = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: false,
            autoInstallUpdatesEnabled: false,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.SettingsSetup, setupResult.Target);
        Assert.Equal(ShellStatusTarget.SettingsGeneral, settingsResult.Target);
    }

    [Fact]
    public void BuildShellStatus_Uses_Compact_Copy_Suitable_For_The_Header_Capsule()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: true,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.True(result.Headline.Length <= 12, $"Expected a compact shell label, but got '{result.Headline}'.");
        Assert.True(result.Body.Length <= 24, $"Expected compact shell detail text, but got '{result.Body}'.");
        Assert.DoesNotContain("Start from Home", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("auto-detection watch", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildShellStatus_Points_To_General_Settings_When_AutoDetection_Is_Off()
    {
        var result = MainWindowInteractionLogic.BuildShellStatus(
            hasValidModel: true,
            isRecording: false,
            micCaptureEnabled: true,
            autoDetectEnabled: false,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.Equal(ShellStatusTarget.SettingsGeneral, result.Target);
        Assert.Equal("Settings", result.ActionLabel);
        Assert.Equal("MANUAL", result.Headline);
        Assert.Equal("Auto-detect off", result.Body);
    }

    [Fact]
    public void GetEligibleActiveSessionReclassification_Returns_The_Detected_Teams_Decision_For_A_Quiet_Manual_Takeover()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "HR Track: IonQ/SkyWater Integration | anonymous",
            Signals:
            [
                new DetectionSignal("window-title", "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-session-match", "Microsoft Teams; window=HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams; process=ms-teams; peak=0.000; confidence=Medium", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams",
                null,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium,
                now));

        var reclassification = MainWindowInteractionLogic.GetEligibleActiveSessionReclassification(
            decision,
            MeetingPlatform.Manual,
            "Manual session 2026-04-01 14:09",
            policy);

        Assert.Same(decision, reclassification);
    }

    [Fact]
    public void GetEligibleActiveSessionReclassification_Returns_Null_When_The_Current_Detection_Does_Not_Qualify()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "Google Meet and 28 more pages - Work - Microsoft Edge",
            Signals:
            [
                new DetectionSignal("window-title", "Google Meet and 28 more pages - Work - Microsoft Edge", 0.85d, now),
                new DetectionSignal("browser-window", "Google Meet and 28 more pages - Work - Microsoft Edge", 0.15d, now),
            ],
            Reason: "Detection confidence did not meet the recording threshold.");

        var reclassification = MainWindowInteractionLogic.GetEligibleActiveSessionReclassification(
            decision,
            MeetingPlatform.Manual,
            "Manual session 2026-04-01 14:09",
            policy);

        Assert.Null(reclassification);
    }

    [Fact]
    public void ShouldRefreshMeetingCatalogForConfigChange_Returns_False_For_Runtime_Only_Config_Changes()
    {
        var previous = new AppConfig
        {
            UpdateCheckEnabled = true,
            AutoDetectEnabled = true,
            MicCaptureEnabled = true,
            LastUpdateCheckUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        var current = previous with
        {
            UpdateCheckEnabled = false,
            AutoDetectEnabled = false,
            MicCaptureEnabled = false,
            LastUpdateCheckUtc = DateTimeOffset.UtcNow,
        };

        var result = MainWindowInteractionLogic.ShouldRefreshMeetingCatalogForConfigChange(previous, current);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRefreshMeetingCatalogForConfigChange_Returns_True_When_Meeting_Workspace_Folders_Change()
    {
        var previous = new AppConfig
        {
            AudioOutputDir = @"C:\Meetings\Audio-A",
            TranscriptOutputDir = @"C:\Meetings\Transcripts-A",
            WorkDir = @"C:\Meetings\Work-A",
        };
        var current = previous with
        {
            AudioOutputDir = @"C:\Meetings\Audio-B",
            TranscriptOutputDir = @"C:\Meetings\Transcripts-B",
            WorkDir = @"C:\Meetings\Work-B",
        };

        var result = MainWindowInteractionLogic.ShouldRefreshMeetingCatalogForConfigChange(previous, current);

        Assert.True(result);
    }

    [Fact]
    public void ShouldDeferMeetingRefresh_Returns_True_While_Recording_Or_When_Meetings_Is_Not_Visible()
    {
        Assert.True(MainWindowInteractionLogic.ShouldDeferMeetingRefresh(isRecording: true, isMeetingsTabSelected: true));
        Assert.True(MainWindowInteractionLogic.ShouldDeferMeetingRefresh(isRecording: false, isMeetingsTabSelected: false));
        Assert.False(MainWindowInteractionLogic.ShouldDeferMeetingRefresh(isRecording: false, isMeetingsTabSelected: true));
    }

    [Fact]
    public void BuildProcessingQueueHeaderState_IsHidden_When_No_Backlog_Exists()
    {
        var snapshot = new ProcessingQueueStatusSnapshot(
            ProcessingQueueRunState.Idle,
            ProcessingQueuePauseReason.None,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var headerState = MainWindowInteractionLogic.BuildProcessingQueueHeaderState(snapshot, DateTimeOffset.UtcNow);

        Assert.False(headerState.IsVisible);
        Assert.Equal(string.Empty, headerState.Label);
        Assert.Equal(string.Empty, headerState.Detail);
    }

    [Fact]
    public void BuildProcessingQueueHeaderState_Shows_Remaining_Count_And_Paused_Reason_When_Backlog_Is_Paused()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ProcessingQueueStatusSnapshot(
            ProcessingQueueRunState.Paused,
            ProcessingQueuePauseReason.LiveRecordingResponsiveMode,
            5,
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            now);

        var headerState = MainWindowInteractionLogic.BuildProcessingQueueHeaderState(snapshot, now);

        Assert.True(headerState.IsVisible);
        Assert.Equal("PAUSED 5", headerState.Label);
        Assert.Equal("Paused by live recording", headerState.Detail);
    }

    [Fact]
    public void BuildMeetingsProcessingStripState_Shows_Current_Stage_Elapsed_Time_Current_Item_Eta_And_Overall_Eta()
    {
        var snapshotTime = new DateTimeOffset(2026, 03, 27, 18, 0, 0, TimeSpan.Zero);
        var currentItemStartedAtUtc = snapshotTime.AddMinutes(-2);
        var currentStageUpdatedAtUtc = snapshotTime.AddMinutes(-1);
        var snapshot = new ProcessingQueueStatusSnapshot(
            ProcessingQueueRunState.Processing,
            ProcessingQueuePauseReason.None,
            4,
            5,
            @"C:\Meetings\work\abc\manifest.json",
            "AI Super Users",
            MeetingPlatform.Teams,
            "transcription",
            StageExecutionState.Running,
            currentStageUpdatedAtUtc,
            currentItemStartedAtUtc,
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(32),
            snapshotTime);

        var stripState = MainWindowInteractionLogic.BuildMeetingsProcessingStripState(
            snapshot,
            "Loading cleanup suggestions in the background.",
            snapshotTime.AddMinutes(1));

        Assert.True(stripState.IsVisible);
        Assert.Contains("PROCESSING", stripState.Line1);
        Assert.Contains("5 remaining", stripState.Line1);
        Assert.Contains("AI Super Users", stripState.Line2);
        Assert.Contains("transcription running", stripState.Line2);
        Assert.Contains("00:03:00 elapsed", stripState.Line2);
        Assert.Contains("ETA ~7m", stripState.Line2);
        Assert.Contains("Overall queue", stripState.Line3);
        Assert.Contains("ETA ~31m", stripState.Line3);
        Assert.Equal("Loading cleanup suggestions in the background.", stripState.SecondaryText);
    }

    [Fact]
    public void BuildMeetingsProcessingStripState_Uses_Eta_Unavailable_When_The_Queue_Cannot_Estimate_Remaining_Time()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ProcessingQueueStatusSnapshot(
            ProcessingQueueRunState.Queued,
            ProcessingQueuePauseReason.None,
            3,
            3,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            now);

        var stripState = MainWindowInteractionLogic.BuildMeetingsProcessingStripState(snapshot, null, now);

        Assert.True(stripState.IsVisible);
        Assert.Contains("QUEUED", stripState.Line1);
        Assert.Contains("3 remaining", stripState.Line1);
        Assert.Contains("ETA unavailable", stripState.Line3);
    }

    [Fact]
    public void BuildModelsTabTranscriptionSetupState_Prefers_Setup_When_No_Valid_Model_Is_Configured()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabTranscriptionSetupState(
            requestedProfile: TranscriptionModelProfilePreference.Standard,
            activeProfile: TranscriptionModelProfilePreference.Standard,
            hasValidModel: false,
            retryRecommended: false);

        Assert.Equal("Needs setup", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Transcription is not ready yet. Download Standard now, choose Higher Accuracy to try the optional larger download, or import an approved local file.",
            result.Body);
    }

    [Fact]
    public void BuildModelsTabTranscriptionSetupState_Points_To_Model_Management_When_HigherAccuracy_Is_Ready()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabTranscriptionSetupState(
            requestedProfile: TranscriptionModelProfilePreference.HighAccuracyDownloaded,
            activeProfile: TranscriptionModelProfilePreference.HighAccuracyDownloaded,
            hasValidModel: true,
            retryRecommended: false);

        Assert.Equal("Higher Accuracy ready", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Higher Accuracy transcription is active.",
            result.Body);
    }

    [Fact]
    public void BuildModelsTabSpeakerLabelingSetupState_Uses_Setup_When_Sidecar_Is_Not_Ready()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabSpeakerLabelingSetupState(
            requestedProfile: SpeakerLabelingModelProfilePreference.Standard,
            activeProfile: SpeakerLabelingModelProfilePreference.Standard,
            isReady: false,
            retryRecommended: false);

        Assert.Equal("Optional", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Speaker labeling is optional right now. Download Standard or Higher Accuracy when you want grouped-by-speaker output, or import an approved local bundle.",
            result.Body);
    }

    [Fact]
    public void BuildModelsTabSpeakerLabelingSetupState_Uses_OffForNow_Copy_When_SpeakerLabeling_Is_Disabled()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabSpeakerLabelingSetupState(
            requestedProfile: SpeakerLabelingModelProfilePreference.Disabled,
            activeProfile: SpeakerLabelingModelProfilePreference.Disabled,
            isReady: false,
            retryRecommended: false);

        Assert.Equal("Optional", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Speaker labeling is off for now. Download Standard or Higher Accuracy later when you want grouped-by-speaker output, or import an approved local bundle.",
            result.Body);
    }

    [Fact]
    public void BuildMeetingInspectorState_Includes_Detected_Audio_Source_Summary()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-03-23T16:10:00Z", null, DateTimeStyles.RoundtripKind);
        var meeting = new MeetingOutputRecord(
            "2026-03-23_161000_teams_client-sync",
            "Client Sync",
            startedAtUtc,
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(30),
            @"C:\audio.wav",
            @"C:\transcript.md",
            @"C:\transcript.json",
            ReadyMarkerPath: null,
            ManifestPath: null,
            ManifestState: SessionState.Published,
            Attendees: Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: null,
            ProjectName: "Alpha",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "Client Sync | Microsoft Teams",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.High,
                startedAtUtc));

        var inspectorState = MainWindowInteractionLogic.BuildMeetingInspectorState(
            meeting,
            Array.Empty<MeetingCleanupRecommendation>(),
            CultureInfo.InvariantCulture,
            TimeZoneInfo.Utc);

        Assert.Contains("Microsoft Teams", inspectorState.DetectedAudioSourceSummary, StringComparison.Ordinal);
        Assert.Contains("Client Sync", inspectorState.DetectedAudioSourceSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModelsTabSpeakerLabelingSetupState_Keeps_SpeakerLabeling_Optional_When_Standard_Bundle_Is_Not_Ready()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabSpeakerLabelingSetupState(
            requestedProfile: SpeakerLabelingModelProfilePreference.Standard,
            activeProfile: SpeakerLabelingModelProfilePreference.Standard,
            isReady: false,
            retryRecommended: true);

        Assert.Equal("Optional", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Speaker labeling is optional right now. Download Standard or Higher Accuracy when you want grouped-by-speaker output, or import an approved local bundle.",
            result.Body);
    }

    [Fact]
    public void BuildModelsTabSpeakerLabelingSetupState_Points_To_Management_When_HigherAccuracy_Is_Ready()
    {
        var result = MainWindowInteractionLogic.BuildModelsTabSpeakerLabelingSetupState(
            requestedProfile: SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
            activeProfile: SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
            isReady: true,
            retryRecommended: false);

        Assert.Equal("Higher Accuracy ready", result.Status);
        Assert.Equal("Open Setup", result.PrimaryActionLabel);
        Assert.Equal(
            "Higher Accuracy speaker labeling is active.",
            result.Body);
    }

    [Fact]
    public void BuildDetectionSummary_Uses_NonMeeting_Message_For_Suppressed_Teams_View()
    {
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15,
            SessionTitle: "ms-teams",
            Signals: Array.Empty<DetectionSignal>(),
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var result = MainWindowInteractionLogic.BuildDetectionSummary(decision, autoDetectEnabled: true);

        Assert.DoesNotContain("Detected Teams meeting", result, StringComparison.Ordinal);
        Assert.Contains("not an active meeting", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetectionSummary_Uses_PossibleMeeting_Message_When_Audio_Is_Not_Yet_Active()
    {
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1.0,
            SessionTitle: "Sharing control bar",
            Signals: Array.Empty<DetectionSignal>(),
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var result = MainWindowInteractionLogic.BuildDetectionSummary(decision, autoDetectEnabled: true);

        Assert.Contains("Possible Teams meeting", result, StringComparison.Ordinal);
        Assert.Contains("no active system audio", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetectionSummary_Uses_Manual_Mode_Message_When_AutoDetection_Is_Off()
    {
        var result = MainWindowInteractionLogic.BuildDetectionSummary(
            decision: null,
            autoDetectEnabled: false);

        Assert.Contains("Auto-detection is off", result, StringComparison.Ordinal);
        Assert.Contains("turn it back on", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRecordingControlsHint_Explains_Why_Controls_Are_Disabled_During_Update_Install()
    {
        var result = MainWindowInteractionLogic.BuildRecordingControlsHint(
            isRecording: false,
            isRecordingTransitionInProgress: false,
            isUpdateInstallInProgress: true);

        Assert.Contains("update", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeferredUpdateInstallMessage_Explains_Blocker_And_Suggests_Restart()
    {
        var result = MainWindowInteractionLogic.BuildDeferredUpdateInstallMessage(
            "Wait for background processing to finish before installing an update.",
            @"C:\Users\test\Downloads\MeetingRecorder-v0.2-win-x64.zip");

        Assert.Contains("background processing", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"MeetingRecorder-v0.2-win-x64.zip", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRecordingStoppedMessage_Explains_Background_Processing_When_Queued()
    {
        var result = MainWindowInteractionLogic.BuildRecordingStoppedMessage(processingQueued: true);

        Assert.Contains("stopped", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("processing", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseMeetingSplitPoint_Parses_Minutes_And_Seconds_Against_Selected_Duration()
    {
        var success = MainWindowInteractionLogic.TryParseMeetingSplitPoint(
            "01:30",
            TimeSpan.FromMinutes(4),
            out var splitPoint,
            out var errorMessage);

        Assert.True(success);
        Assert.Equal(TimeSpan.FromSeconds(90), splitPoint);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryParseMeetingSplitPoint_Rejects_Values_Outside_The_Selected_Meeting_Duration()
    {
        var success = MainWindowInteractionLogic.TryParseMeetingSplitPoint(
            "05:00",
            TimeSpan.FromMinutes(4),
            out var splitPoint,
            out var errorMessage);

        Assert.False(success);
        Assert.Equal(TimeSpan.Zero, splitPoint);
        Assert.Contains("between", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSuggestedMeetingSplitPoint_Returns_Midpoint_Clamped_Inside_Valid_Bounds()
    {
        var result = MainWindowInteractionLogic.GetSuggestedMeetingSplitPoint(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30), result);
    }

    [Fact]
    public void BuildMeetingSplitPreview_Returns_Both_Part_Durations()
    {
        var result = MainWindowInteractionLogic.BuildMeetingSplitPreview(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(90));

        Assert.Contains("Part 1: 01:30", result, StringComparison.Ordinal);
        Assert.Contains("Part 2: 03:30", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAutoInstallUpdatesHint_Disables_Dependent_Message_When_Update_Checks_Are_Off()
    {
        var result = MainWindowInteractionLogic.BuildAutoInstallUpdatesHint(updateChecksEnabled: false);

        Assert.Contains("Turn on daily GitHub checks first", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppShutdownMode_Returns_Immediate_When_Installer_Requested_Shutdown()
    {
        var result = MainWindowInteractionLogic.GetAppShutdownMode(
            installerRequestedShutdown: true,
            isRecording: false,
            isProcessingInProgress: false);

        Assert.Equal(AppShutdownMode.Immediate, result);
    }

    [Fact]
    public void GetAppShutdownMode_Returns_Deferred_When_Installer_Requested_Shutdown_During_Active_Work()
    {
        var recordingResult = MainWindowInteractionLogic.GetAppShutdownMode(
            installerRequestedShutdown: true,
            isRecording: true,
            isProcessingInProgress: false);
        var processingResult = MainWindowInteractionLogic.GetAppShutdownMode(
            installerRequestedShutdown: true,
            isRecording: false,
            isProcessingInProgress: true);

        Assert.Equal(AppShutdownMode.Deferred, recordingResult);
        Assert.Equal(AppShutdownMode.Deferred, processingResult);
    }

    [Fact]
    public void GetPreferredRemoteModelSelectionFileName_Preserves_Previously_Selected_Model_When_Still_Available()
    {
        var remoteModels = new[]
        {
            new WhisperRemoteModelAsset("ggml-base.en-q8_0.bin", "https://example.com/base", 10, true, "base"),
            new WhisperRemoteModelAsset("ggml-small.en-q8_0.bin", "https://example.com/small", 20, false, "small"),
        };

        var result = MainWindowInteractionLogic.GetPreferredRemoteModelSelectionFileName(
            remoteModels,
            previouslySelectedFileName: "ggml-small.en-q8_0.bin");

        Assert.Equal("ggml-small.en-q8_0.bin", result);
    }

    [Fact]
    public void GetPreferredRemoteModelSelectionFileName_Falls_Back_To_Recommended_Model_When_Previous_Selection_Is_Missing()
    {
        var remoteModels = new[]
        {
            new WhisperRemoteModelAsset("ggml-base.en-q8_0.bin", "https://example.com/base", 10, true, "base"),
            new WhisperRemoteModelAsset("ggml-small.en-q8_0.bin", "https://example.com/small", 20, false, "small"),
        };

        var result = MainWindowInteractionLogic.GetPreferredRemoteModelSelectionFileName(
            remoteModels,
            previouslySelectedFileName: "ggml-medium.en-q8_0.bin");

        Assert.Equal("ggml-base.en-q8_0.bin", result);
    }

    [Fact]
    public void BuildAutoDetectSettingsHint_Explains_When_Auto_Detect_Is_Off()
    {
        var result = MainWindowInteractionLogic.BuildAutoDetectSettingsHint(autoDetectEnabled: false);

        Assert.Contains("ignored", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMicCaptureWarning_Returns_Clear_Warning_When_Microphone_Is_Disabled()
    {
        var result = MainWindowInteractionLogic.BuildMicCaptureWarning(
            savedMicCaptureEnabled: false,
            isRecording: true,
            hasPendingMicCaptureChange: false,
            pendingMicCaptureEnabled: false);

        Assert.Contains("microphone", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("voice", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recording", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from now on", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMicCaptureWarning_Explains_When_Config_Checkbox_Is_On_But_Saved_Setting_Is_Still_Off()
    {
        var result = MainWindowInteractionLogic.BuildMicCaptureWarning(
            savedMicCaptureEnabled: false,
            isRecording: false,
            hasPendingMicCaptureChange: true,
            pendingMicCaptureEnabled: true);

        Assert.Contains("still off", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("future recordings", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMicCapturePendingBadgeText_Shows_Unsaved_Enablement_State()
    {
        var result = MainWindowInteractionLogic.BuildMicCapturePendingBadgeText(
            savedMicCaptureEnabled: false,
            hasPendingMicCaptureChange: true,
            pendingMicCaptureEnabled: true);

        Assert.Contains("Unsaved", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn on", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildConfigDependencyState_Combines_Existing_Dependency_And_Warning_Outputs_For_Sectioned_Config_Layout()
    {
        var result = MainWindowInteractionLogic.BuildConfigDependencyState(
            updateChecksEnabled: false,
            autoDetectEnabled: false,
            savedMicCaptureEnabled: false,
            pendingMicCaptureEnabled: true,
            isRecording: false);

        Assert.False(result.AutoInstallUpdatesEnabled);
        Assert.Equal(
            "Turn on daily GitHub checks first. Automatic install only works after update checks are enabled.",
            result.AutoInstallUpdatesHint);
        Assert.False(result.AutoDetectTuningEnabled);
        Assert.Equal(
            "Auto-detection is off, so the threshold and timeout fields below are ignored until you turn it back on.",
            result.AutoDetectSettingsHint);
        Assert.Equal(
            "Microphone capture is still off in the saved config. Save Config to turn it on for future recordings.",
            result.MicCaptureWarning);
        Assert.Equal(
            "Unsaved: will turn on after Save Config",
            result.MicCapturePendingBadgeText);
    }

    [Fact]
    public void ShouldPromptToEnableMicCapture_Returns_True_When_Microphone_Is_Disabled_And_Activity_Is_Detected()
    {
        var result = MainWindowInteractionLogic.ShouldPromptToEnableMicCapture(
            micCaptureEnabled: false,
            isRecording: true,
            hasDetectedMicrophoneActivity: true,
            alreadyPromptedForSession: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPromptToEnableMicCapture_Returns_False_After_The_Current_Session_Was_Already_Prompted()
    {
        var result = MainWindowInteractionLogic.ShouldPromptToEnableMicCapture(
            micCaptureEnabled: false,
            isRecording: true,
            hasDetectedMicrophoneActivity: true,
            alreadyPromptedForSession: true);

        Assert.False(result);
    }

    [Fact]
    public void BuildEnableMicCapturePromptMessage_Explains_That_The_Current_Recording_Is_Affected_From_Now_On()
    {
        var result = MainWindowInteractionLogic.BuildEnableMicCapturePromptMessage("Chao Adam");

        Assert.Contains("from now on", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("future recordings", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chao Adam", result, StringComparison.Ordinal);
        Assert.Contains("microphone", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldAutoPromoteActiveMeetingTitle_Returns_True_For_Attendee_Title_When_Current_Title_Is_Generic()
    {
        var result = MainWindowInteractionLogic.ShouldAutoPromoteActiveMeetingTitle(
            MeetingPlatform.Teams,
            currentDetectedTitle: "Microsoft Teams",
            currentEditorTitle: "Microsoft Teams",
            proposedTitle: "Chao, Adam",
            proposalCameFromCalendarFallback: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldAutoPromoteActiveMeetingTitle_Returns_True_For_Calendar_Title_When_Current_Title_Is_Generic()
    {
        var result = MainWindowInteractionLogic.ShouldAutoPromoteActiveMeetingTitle(
            MeetingPlatform.Teams,
            currentDetectedTitle: "Search",
            currentEditorTitle: "Search",
            proposedTitle: "Intel CIO Discussion",
            proposalCameFromCalendarFallback: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldAutoPromoteActiveMeetingTitle_Returns_False_When_User_Has_A_Pending_Title_Edit()
    {
        var result = MainWindowInteractionLogic.ShouldAutoPromoteActiveMeetingTitle(
            MeetingPlatform.Teams,
            currentDetectedTitle: "Microsoft Teams",
            currentEditorTitle: "My custom title",
            proposedTitle: "Chao, Adam",
            proposalCameFromCalendarFallback: false);

        Assert.False(result);
    }

    [Fact]
    public void HasPendingMeetingRename_Returns_False_When_Title_Is_Unchanged_After_Trimming()
    {
        var result = MainWindowInteractionLogic.HasPendingMeetingRename("Weekly Sync", "  Weekly Sync  ");

        Assert.False(result);
    }

    [Fact]
    public void BuildSpeakerLabelMap_Ignores_Blanks_And_Unchanged_Rows()
    {
        var rows = new[]
        {
            new SpeakerLabelDraft("Speaker 1", " Pranav "),
            new SpeakerLabelDraft("Speaker 2", "Speaker 2"),
            new SpeakerLabelDraft("Speaker 3", " "),
        };

        var result = MainWindowInteractionLogic.BuildSpeakerLabelMap(rows);

        Assert.Equal("Pranav", Assert.Single(result).Value);
        Assert.Equal("Speaker 1", result.Keys.Single());
    }

    [Fact]
    public void HasPendingConfigChanges_Returns_False_When_Editor_Matches_Current_Config()
    {
        var config = new AppConfig
        {
            AudioOutputDir = @"C:\Audio",
            TranscriptOutputDir = @"C:\Transcripts",
            WorkDir = @"C:\Work",
            ModelCacheDir = @"C:\Models",
            TranscriptionModelPath = @"C:\Models\ggml-base.en-q8_0.bin",
            DiarizationAssetPath = @"C:\Models\diarizer.onnx",
            MicCaptureEnabled = true,
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = true,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = false,
            UpdateFeedUrl = "https://example.com/releases/latest",
            AutoDetectAudioPeakThreshold = 0.125,
            MeetingStopTimeoutSeconds = 15,
            PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
        };
        var editor = new ConfigEditorSnapshot(
            @" C:\Audio ",
            @" C:\Transcripts ",
            @" C:\Work ",
            true,
            "0.125",
            "15",
            true,
            true,
            true,
            false,
            true,
            true,
            false,
            " https://example.com/releases/latest ",
            PreferredTeamsIntegrationMode.Auto,
            BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode.Deferred);

        var result = MainWindowInteractionLogic.HasPendingConfigChanges(config, editor);

        Assert.False(result);
    }

    [Fact]
    public void HasPendingConfigChanges_Returns_True_When_Any_Field_Differs()
    {
        var config = new AppConfig
        {
            AudioOutputDir = @"C:\Audio",
            TranscriptOutputDir = @"C:\Transcripts",
            WorkDir = @"C:\Work",
            ModelCacheDir = @"C:\Models",
            TranscriptionModelPath = @"C:\Models\ggml-base.en-q8_0.bin",
            DiarizationAssetPath = @"C:\Models\diarizer.onnx",
            MicCaptureEnabled = false,
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = true,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = false,
            UpdateFeedUrl = "https://example.com/releases/latest",
            AutoDetectAudioPeakThreshold = 0.125,
            MeetingStopTimeoutSeconds = 15,
            PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
        };
        var editor = new ConfigEditorSnapshot(
            @"C:\Audio",
            @"C:\Transcripts",
            @"C:\Work",
            true,
            "0.125",
            "15",
            true,
            true,
            true,
            false,
            true,
            true,
            false,
            "https://example.com/releases/latest",
            PreferredTeamsIntegrationMode.ThirdPartyApi,
            BackgroundProcessingMode.Balanced,
            BackgroundSpeakerLabelingMode.Throttled);

        var result = MainWindowInteractionLogic.HasPendingConfigChanges(config, editor);

        Assert.True(result);
    }

    [Fact]
    public void HasPendingConfigChanges_Returns_True_When_Teams_Integration_Settings_Differ()
    {
        var config = new AppConfig
        {
            PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
        };
        var editor = new ConfigEditorSnapshot(
            string.Empty,
            string.Empty,
            string.Empty,
            true,
            "0.02",
            "30",
            true,
            false,
            false,
            false,
            true,
            true,
            true,
            string.Empty,
            PreferredTeamsIntegrationMode.ThirdPartyApi,
            BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode.Deferred);

        var result = MainWindowInteractionLogic.HasPendingConfigChanges(config, editor);

        Assert.True(result);
    }

    [Fact]
    public void BuildMeetingCleanupBadgeText_Uses_Top_Action_And_Count()
    {
        var recommendations = new[]
        {
            new MeetingCleanupRecommendation(
                "archive-1",
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive obvious false start",
                "This looks like a false start.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
            new MeetingCleanupRecommendation(
                "rename-1",
                MeetingCleanupAction.Rename,
                MeetingCleanupConfidence.Medium,
                "Rename generic title",
                "A better title is available.",
                "meeting-1",
                new[] { "meeting-1" },
                false,
                "Better Title",
                null),
        };

        var result = MainWindowInteractionLogic.BuildMeetingCleanupBadgeText(recommendations);

        Assert.Equal("Archive +1", result);
    }

    [Fact]
    public void GetPrimaryMeetingCleanupRecommendation_Returns_Highest_Priority_Item()
    {
        var rename = new MeetingCleanupRecommendation(
            "rename-1",
            MeetingCleanupAction.Rename,
            MeetingCleanupConfidence.Medium,
            "Rename generic title",
            "A better title is available.",
            "meeting-1",
            new[] { "meeting-1" },
            false,
            "Better Title",
            null);
        var archive = new MeetingCleanupRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            "Archive obvious false start",
            "This looks like a false start.",
            "meeting-1",
            new[] { "meeting-1" },
            true,
            null,
            null);

        var result = MainWindowInteractionLogic.GetPrimaryMeetingCleanupRecommendation(new[] { rename, archive });

        Assert.NotNull(result);
        Assert.Equal("archive-1", result!.Fingerprint);
    }

    [Fact]
    public void FilterMeetingCleanupRecommendations_Returns_All_When_No_Meeting_Is_Selected()
    {
        var archive = new MeetingCleanupRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            "Archive obvious false start",
            "This looks like a false start.",
            "meeting-1",
            new[] { "meeting-1" },
            true,
            null,
            null);
        var merge = new MeetingCleanupRecommendation(
            "merge-1",
            MeetingCleanupAction.Merge,
            MeetingCleanupConfidence.High,
            "Merge split pair",
            "These two meetings should be merged.",
            "meeting-2",
            new[] { "meeting-2", "meeting-3" },
            true,
            null,
            null);

        var result = MainWindowInteractionLogic.FilterMeetingCleanupRecommendations(
            new[] { archive, merge },
            Array.Empty<string>());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterMeetingCleanupRecommendations_Prioritizes_Selected_Meeting_Items_For_Multi_Select()
    {
        var archive = new MeetingCleanupRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            "Archive obvious false start",
            "This looks like a false start.",
            "meeting-1",
            new[] { "meeting-1" },
            true,
            null,
            null);
        var merge = new MeetingCleanupRecommendation(
            "merge-1",
            MeetingCleanupAction.Merge,
            MeetingCleanupConfidence.High,
            "Merge split pair",
            "These two meetings should be merged.",
            "meeting-2",
            new[] { "meeting-2", "meeting-3" },
            true,
            null,
            null);

        var result = MainWindowInteractionLogic.FilterMeetingCleanupRecommendations(
            new[] { archive, merge },
            new[] { "meeting-2", "meeting-3" });

        Assert.Equal(2, result.Count);
        Assert.Equal("merge-1", result[0].Fingerprint);
        Assert.Equal("archive-1", result[1].Fingerprint);
    }

    [Fact]
    public void GetAutoApplicableMeetingCleanupRecommendations_Excludes_Split_And_Low_Confidence_Items()
    {
        var recommendations = new[]
        {
            new MeetingCleanupRecommendation(
                "archive-1",
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive obvious false start",
                "This looks like a false start.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
            new MeetingCleanupRecommendation(
                "split-1",
                MeetingCleanupAction.Split,
                MeetingCleanupConfidence.High,
                "Split combined meeting",
                "A split point was found.",
                "meeting-2",
                new[] { "meeting-2" },
                false,
                null,
                TimeSpan.FromMinutes(5)),
            new MeetingCleanupRecommendation(
                "rename-1",
                MeetingCleanupAction.Rename,
                MeetingCleanupConfidence.Medium,
                "Rename generic title",
                "A better title is available.",
                "meeting-3",
                new[] { "meeting-3" },
                false,
                "Better Title",
                null),
        };

        var result = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(recommendations);

        Assert.Single(result);
        Assert.Equal("archive-1", result[0].Fingerprint);
    }

    [Fact]
    public void GetAutoApplicableMeetingCleanupRecommendations_Includes_GenerateSpeakerLabels_When_Deterministic()
    {
        var recommendations = new[]
        {
            new MeetingCleanupRecommendation(
                "labels-1",
                MeetingCleanupAction.GenerateSpeakerLabels,
                MeetingCleanupConfidence.High,
                "Add missing speaker labels",
                "Speaker labels can be added safely.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
            new MeetingCleanupRecommendation(
                "rename-1",
                MeetingCleanupAction.Rename,
                MeetingCleanupConfidence.Medium,
                "Rename generic title",
                "A better title is available.",
                "meeting-2",
                new[] { "meeting-2" },
                false,
                "Better Title",
                null),
        };

        var result = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(recommendations);

        Assert.Single(result);
        Assert.Equal("labels-1", result[0].Fingerprint);
    }

    [Fact]
    public void BuildMeetingCleanupSafetyLabel_Matches_Safe_Fix_Rules()
    {
        var safeRecommendation = new MeetingCleanupRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            "Archive obvious false start",
            "This looks like a false start.",
            "meeting-1",
            new[] { "meeting-1" },
            true,
            null,
            null);
        var reviewRecommendation = new MeetingCleanupRecommendation(
            "rename-1",
            MeetingCleanupAction.Rename,
            MeetingCleanupConfidence.Medium,
            "Rename generic title",
            "A better title is available.",
            "meeting-2",
            new[] { "meeting-2" },
            false,
            "Better Title",
            null);
        var speakerLabelRecommendation = new MeetingCleanupRecommendation(
            "labels-1",
            MeetingCleanupAction.GenerateSpeakerLabels,
            MeetingCleanupConfidence.High,
            "Add missing speaker labels",
            "Speaker labels can be added safely.",
            "meeting-3",
            new[] { "meeting-3" },
            true,
            null,
            null);

        Assert.Equal("Safe Fix", MainWindowInteractionLogic.BuildMeetingCleanupSafetyLabel(safeRecommendation));
        Assert.Equal("Safe Fix", MainWindowInteractionLogic.BuildMeetingCleanupSafetyLabel(speakerLabelRecommendation));
        Assert.Equal("Review First", MainWindowInteractionLogic.BuildMeetingCleanupSafetyLabel(reviewRecommendation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    [InlineData(" DELETE ")]
    public void IsValidPermanentDeleteConfirmationText_Returns_False_When_Text_Is_Not_Exact_DELETE(string? confirmationText)
    {
        var result = MainWindowInteractionLogic.IsValidPermanentDeleteConfirmationText(confirmationText);

        Assert.False(result);
    }

    [Fact]
    public void IsValidPermanentDeleteConfirmationText_Returns_True_For_Exact_DELETE()
    {
        var result = MainWindowInteractionLogic.IsValidPermanentDeleteConfirmationText("DELETE");

        Assert.True(result);
    }

    [Fact]
    public void BuildMeetingInspectorState_Uses_Persisted_Attendees_Recommendations_And_Metadata()
    {
        var meeting = new MeetingOutputRecord(
            "meeting-1",
            "Intel CIO Discussion",
            DateTimeOffset.Parse("2026-03-20T16:01:31Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(34) + TimeSpan.FromSeconds(55),
            @"C:\meetings\audio.wav",
            @"C:\meetings\transcript.md",
            @"C:\meetings\transcript.json",
            @"C:\meetings\transcript.ready",
            @"C:\meetings\manifest.json",
            SessionState.Published,
            new[]
            {
                new MeetingAttendee("Pranav Sharma", new[] { MeetingAttendeeSource.OutlookCalendar }),
                new MeetingAttendee("Nick Gallina", new[] { MeetingAttendeeSource.TeamsLiveRoster }),
            },
            true,
            "ggml-small.bin");
        var recommendations = new[]
        {
            new MeetingCleanupRecommendation(
                "rename-1",
                MeetingCleanupAction.Rename,
                MeetingCleanupConfidence.Medium,
                "Rename generic title",
                "A better title is available.",
                "meeting-1",
                new[] { "meeting-1" },
                false,
                "Better Title",
                null),
            new MeetingCleanupRecommendation(
                "retry-1",
                MeetingCleanupAction.RegenerateTranscript,
                MeetingCleanupConfidence.High,
                "Retry transcript with current audio",
                "Transcript can be rebuilt safely.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
        };

        var result = MainWindowInteractionLogic.BuildMeetingInspectorState(
            meeting,
            recommendations,
            CultureInfo.GetCultureInfo("en-US"),
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

        Assert.Equal("Intel CIO Discussion", result.Title);
        Assert.Equal("3/20/2026 12:01 PM", result.StartedAtUtc);
        Assert.Equal("34:55", result.Duration);
        Assert.Equal("Teams", result.Platform);
        Assert.Equal("Published", result.Status);
        Assert.Equal("ggml-small.bin", result.TranscriptionModelFileName);
        Assert.Equal("Speaker labels are present.", result.SpeakerLabelState);
        Assert.Equal(new[] { "Pranav Sharma", "Nick Gallina" }, result.AttendeeNames);
        Assert.Equal(new[] { "Rename", "Retry Transcript" }, result.RecommendationBadges);
    }

    [Fact]
    public void BuildMeetingContextActionState_Enables_Single_Meeting_Actions_For_Focused_Row()
    {
        var result = MainWindowInteractionLogic.BuildMeetingContextActionState(
            selectedMeetingCount: 1,
            hasFocusedMeeting: true,
            canOpenAudio: true,
            canOpenTranscript: true,
            hasRecommendedAction: true,
            canRegenerateTranscript: true,
            canAddSpeakerLabels: true,
            isBusy: false);

        Assert.True(result.ShowSingleMeetingActionGroup);
        Assert.False(result.ShowBulkMeetingActionGroup);
        Assert.True(result.CanOpenAudio);
        Assert.True(result.CanOpenTranscript);
        Assert.True(result.CanOpenContainingFolder);
        Assert.True(result.CanCopyAudioPath);
        Assert.True(result.CanCopyTranscriptPath);
        Assert.True(result.CanApplyRecommendedAction);
        Assert.True(result.CanRename);
        Assert.True(result.CanSuggestTitle);
        Assert.True(result.CanRegenerateTranscript);
        Assert.True(result.CanReTranscribeWithDifferentModel);
        Assert.True(result.CanAddSpeakerLabels);
        Assert.True(result.CanSplit);
        Assert.True(result.CanArchive);
        Assert.True(result.CanDeletePermanently);
        Assert.False(result.CanApplyRecommendationsForSelection);
        Assert.False(result.CanMergeSelected);
    }

    [Fact]
    public void BuildMeetingContextActionState_Enables_Bulk_Actions_For_Multi_Select()
    {
        var result = MainWindowInteractionLogic.BuildMeetingContextActionState(
            selectedMeetingCount: 3,
            hasFocusedMeeting: true,
            canOpenAudio: true,
            canOpenTranscript: true,
            hasRecommendedAction: true,
            canRegenerateTranscript: true,
            canAddSpeakerLabels: true,
            isBusy: false);

        Assert.False(result.ShowSingleMeetingActionGroup);
        Assert.True(result.ShowBulkMeetingActionGroup);
        Assert.False(result.CanOpenAudio);
        Assert.False(result.CanOpenTranscript);
        Assert.False(result.CanRename);
        Assert.False(result.CanSuggestTitle);
        Assert.False(result.CanSplit);
        Assert.True(result.CanApplyRecommendationsForSelection);
        Assert.True(result.CanMergeSelected);
        Assert.True(result.CanReTranscribeSelectedWithModel);
        Assert.True(result.CanAddSpeakerLabelsToSelected);
        Assert.True(result.CanArchiveSelected);
        Assert.True(result.CanDeleteSelectedPermanently);
    }

    [Fact]
    public void BuildMeetingWorkspaceToolState_Shows_Contextual_Single_And_Multi_Meeting_Tools()
    {
        var singleSelection = MainWindowInteractionLogic.BuildMeetingWorkspaceToolState(
            selectedMeetingCount: 1,
            hasSpeakerLabels: true,
            hasCleanupRecommendations: true);

        Assert.True(singleSelection.ShowWorkspaceTools);
        Assert.True(singleSelection.ShowSingleMeetingActions);
        Assert.False(singleSelection.ShowMultiMeetingActions);
        Assert.True(singleSelection.ShowTitleAndTranscriptTool);
        Assert.True(singleSelection.ShowSplitTool);
        Assert.True(singleSelection.ShowSpeakerLabelsTool);
        Assert.False(singleSelection.ShowMergeTool);
        Assert.True(singleSelection.ShowCleanupTray);

        var multiSelection = MainWindowInteractionLogic.BuildMeetingWorkspaceToolState(
            selectedMeetingCount: 3,
            hasSpeakerLabels: false,
            hasCleanupRecommendations: true);

        Assert.True(multiSelection.ShowWorkspaceTools);
        Assert.False(multiSelection.ShowSingleMeetingActions);
        Assert.True(multiSelection.ShowMultiMeetingActions);
        Assert.False(multiSelection.ShowTitleAndTranscriptTool);
        Assert.False(multiSelection.ShowSplitTool);
        Assert.False(multiSelection.ShowSpeakerLabelsTool);
        Assert.True(multiSelection.ShowMergeTool);
        Assert.True(multiSelection.ShowCleanupTray);
    }

    [Fact]
    public void BuildMeetingInspectorState_Uses_Same_Recommendation_Labels_As_Row_And_Cleanup_Workflows()
    {
        var recommendations = new[]
        {
            new MeetingCleanupRecommendation(
                "archive-1",
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive obvious false start",
                "This looks like a false start.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
            new MeetingCleanupRecommendation(
                "labels-1",
                MeetingCleanupAction.GenerateSpeakerLabels,
                MeetingCleanupConfidence.High,
                "Add missing speaker labels",
                "Speaker labels can be added safely.",
                "meeting-1",
                new[] { "meeting-1" },
                true,
                null,
                null),
        };
        var meeting = new MeetingOutputRecord(
            "meeting-1",
            "Weekly Sync",
            DateTimeOffset.Parse("2026-03-20T18:00:00Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(30),
            @"C:\audio.wav",
            @"C:\transcript.md",
            @"C:\transcript.json",
            @"C:\transcript.ready",
            @"C:\manifest.json",
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var inspector = MainWindowInteractionLogic.BuildMeetingInspectorState(meeting, recommendations);
        var badgeText = MainWindowInteractionLogic.BuildMeetingCleanupBadgeText(recommendations);

        Assert.Contains("Archive", inspector.RecommendationBadges);
        Assert.Contains("Add Speaker Labels", inspector.RecommendationBadges);
        Assert.StartsWith("Archive", badgeText, StringComparison.Ordinal);
    }

    [Fact]
    public void MeetingMatchesWorkspaceSearch_Returns_True_For_Attendee_Name()
    {
        var attendees = new[]
        {
            new MeetingAttendee("Pranav Sharma", new[] { MeetingAttendeeSource.OutlookCalendar }),
        };

        var result = MainWindowInteractionLogic.MeetingMatchesWorkspaceSearch(
            "pranav",
            "Intel CIO Discussion",
            "Teams",
            "Published",
            attendees);

        Assert.True(result);
    }

    [Fact]
    public void MeetingMatchesWorkspaceSearch_Returns_True_For_Key_Attendee_Name()
    {
        var result = MainWindowInteractionLogic.MeetingMatchesWorkspaceSearch(
            "pranav",
            "Intel CIO Discussion",
            "Project Atlas",
            "Teams",
            "Published",
            Array.Empty<MeetingAttendee>(),
            ["Pranav Sharma"]);

        Assert.True(result);
    }

    [Theory]
    [InlineData(MeetingsGroupKey.Week, "2026-03-18T14:00:00Z", "Week of 2026-03-16")]
    [InlineData(MeetingsGroupKey.Month, "2026-03-18T14:00:00Z", "March 2026")]
    [InlineData(MeetingsGroupKey.Platform, "2026-03-18T14:00:00Z", "Teams")]
    [InlineData(MeetingsGroupKey.Status, "2026-03-18T14:00:00Z", "Published")]
    public void BuildMeetingWorkspaceGroupLabel_Returns_Expected_Label(
        MeetingsGroupKey groupKey,
        string startedAtUtcText,
        string expectedLabel)
    {
        var result = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
            groupKey,
            DateTimeOffset.Parse(startedAtUtcText),
            "Teams",
            "Published");

        Assert.Equal(expectedLabel, result);
    }

    [Fact]
    public void GetMeetingWorkspaceGroupPropertyName_Returns_Group_Specific_Property()
    {
        var result = MainWindowInteractionLogic.GetMeetingWorkspaceGroupPropertyName(MeetingsGroupKey.Month);

        Assert.Equal("MonthGroupLabel", result);
    }

    [Fact]
    public void BuildMeetingWorkspaceGroupLabel_Returns_Project_Name_For_Client_Project_Grouping()
    {
        var result = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
            MeetingsGroupKey.ClientProject,
            DateTimeOffset.Parse("2026-03-18T14:00:00Z"),
            "Teams",
            "Published",
            projectName: "Project Atlas");

        Assert.Equal("Project Atlas", result);
    }

    [Fact]
    public void BuildMeetingWorkspaceGroupLabel_Merges_Key_And_Persisted_Attendees_For_Attendee_Grouping()
    {
        var result = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
            MeetingsGroupKey.Attendee,
            DateTimeOffset.Parse("2026-03-18T14:00:00Z"),
            "Teams",
            "Published",
            attendees:
            [
                new MeetingAttendee("Pranav", [MeetingAttendeeSource.TeamsLiveRoster]),
                new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar]),
            ],
            keyAttendees: ["Pranav Sharma"]);

        Assert.Equal("Pranav Sharma", result);
    }

    [Theory]
    [InlineData(MeetingsGroupKey.ClientProject, "ClientProjectGroupLabel")]
    [InlineData(MeetingsGroupKey.Attendee, "AttendeeGroupLabel")]
    public void GetMeetingWorkspaceGroupPropertyName_Returns_Group_Specific_Property_For_Client_Project_And_Attendee(
        MeetingsGroupKey groupKey,
        string expectedPropertyName)
    {
        var result = MainWindowInteractionLogic.GetMeetingWorkspaceGroupPropertyName(groupKey);

        Assert.Equal(expectedPropertyName, result);
    }

    [Theory]
    [InlineData(MeetingsGroupKey.ClientProject, "ClientProjectGroupLabel")]
    [InlineData(MeetingsGroupKey.Attendee, "AttendeeGroupLabel")]
    public void GetMeetingWorkspaceGroupSortPropertyName_Returns_Group_Specific_Property_For_Client_Project_And_Attendee(
        MeetingsGroupKey groupKey,
        string expectedPropertyName)
    {
        var result = MainWindowInteractionLogic.GetMeetingWorkspaceGroupSortPropertyName(groupKey);

        Assert.Equal(expectedPropertyName, result);
    }

    [Fact]
    public void PromotePendingUpdateToInstalledReleaseMetadata_Copies_Pending_Metadata_And_Clears_Pending_State()
    {
        var pendingPublishedAtUtc = DateTimeOffset.Parse("2026-03-20T21:51:12Z");
        var config = new AppConfig
        {
            InstalledReleaseVersion = "0.2",
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-03-17T19:28:22Z"),
            InstalledReleaseAssetSizeBytes = 74290000,
            PendingUpdateZipPath = @"C:\Downloads\MeetingRecorder-v0.2-win-x64.zip",
            PendingUpdateVersion = "0.2",
            PendingUpdatePublishedAtUtc = pendingPublishedAtUtc,
            PendingUpdateAssetSizeBytes = 74294567,
        };

        var result = MainWindowInteractionLogic.PromotePendingUpdateToInstalledReleaseMetadata(config, "0.2");

        Assert.Equal("0.2", result.InstalledReleaseVersion);
        Assert.Equal(pendingPublishedAtUtc, result.InstalledReleasePublishedAtUtc);
        Assert.Equal(74294567, result.InstalledReleaseAssetSizeBytes);
        Assert.Equal(string.Empty, result.PendingUpdateZipPath);
        Assert.Equal(string.Empty, result.PendingUpdateVersion);
        Assert.Null(result.PendingUpdatePublishedAtUtc);
        Assert.Null(result.PendingUpdateAssetSizeBytes);
    }

    [Fact]
    public void IsPendingUpdateAlreadyInstalled_Returns_False_When_Same_Version_Metadata_Differs()
    {
        var config = new AppConfig
        {
            InstalledReleaseVersion = "0.3",
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-03-22T12:00:00Z"),
            InstalledReleaseAssetSizeBytes = 100_000_000,
            PendingUpdateVersion = "0.3",
            PendingUpdatePublishedAtUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z"),
            PendingUpdateAssetSizeBytes = 101_000_000,
        };

        var result = MainWindowInteractionLogic.IsPendingUpdateAlreadyInstalled(config, "0.3");

        Assert.False(result);
    }

    [Fact]
    public void IsPendingUpdateAlreadyInstalled_Returns_True_When_Same_Version_Metadata_Matches()
    {
        var publishedAtUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");
        var config = new AppConfig
        {
            InstalledReleaseVersion = "0.3",
            InstalledReleasePublishedAtUtc = publishedAtUtc,
            InstalledReleaseAssetSizeBytes = 101_000_000,
            PendingUpdateVersion = "0.3",
            PendingUpdatePublishedAtUtc = publishedAtUtc,
            PendingUpdateAssetSizeBytes = 101_000_000,
        };

        var result = MainWindowInteractionLogic.IsPendingUpdateAlreadyInstalled(config, "0.3");

        Assert.True(result);
    }
}
