using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MainWindowInteractionLogicTests
{
    [Fact]
    public void BuildDashboardPrimaryAction_Prioritizes_Model_Setup_When_No_Valid_Model_Is_Configured()
    {
        var result = MainWindowInteractionLogic.BuildDashboardPrimaryAction(
            hasValidModel: false,
            isRecording: false,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: true,
            updateResult: null);

        Assert.Equal(DashboardPrimaryActionTarget.Models, result.Target);
        Assert.Equal("Open Models", result.ActionLabel);
    }

    [Fact]
    public void BuildDashboardPrimaryAction_Points_To_Updates_When_Manual_Update_Is_Available()
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

        var result = MainWindowInteractionLogic.BuildDashboardPrimaryAction(
            hasValidModel: true,
            isRecording: false,
            updateChecksEnabled: true,
            autoInstallUpdatesEnabled: false,
            updateResult: updateResult);

        Assert.Equal(DashboardPrimaryActionTarget.Updates, result.Target);
        Assert.Equal("Open Updates", result.ActionLabel);
    }

    [Fact]
    public void BuildDashboardPrimaryAction_Points_To_Config_When_Update_Checks_Are_Disabled()
    {
        var result = MainWindowInteractionLogic.BuildDashboardPrimaryAction(
            hasValidModel: true,
            isRecording: false,
            updateChecksEnabled: false,
            autoInstallUpdatesEnabled: false,
            updateResult: null);

        Assert.Equal(DashboardPrimaryActionTarget.Config, result.Target);
        Assert.Equal("Open Config", result.ActionLabel);
    }

    [Fact]
    public void BuildDashboardPrimaryAction_Returns_Ready_State_When_Core_Setup_Is_Healthy()
    {
        var result = MainWindowInteractionLogic.BuildDashboardPrimaryAction(
            hasValidModel: true,
            isRecording: false,
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

        Assert.Equal(DashboardPrimaryActionTarget.None, result.Target);
        Assert.Null(result.ActionLabel);
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

        var result = MainWindowInteractionLogic.BuildDetectionSummary(decision);

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

        var result = MainWindowInteractionLogic.BuildDetectionSummary(decision);

        Assert.Contains("Possible Teams meeting", result, StringComparison.Ordinal);
        Assert.Contains("no active system audio", result, StringComparison.OrdinalIgnoreCase);
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
    public void BuildAutoDetectSettingsHint_Explains_When_Auto_Detect_Is_Off()
    {
        var result = MainWindowInteractionLogic.BuildAutoDetectSettingsHint(autoDetectEnabled: false);

        Assert.Contains("ignored", result, StringComparison.OrdinalIgnoreCase);
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
        };
        var editor = new ConfigEditorSnapshot(
            @" C:\Audio ",
            @" C:\Transcripts ",
            @" C:\Work ",
            @" C:\Models ",
            @" C:\Models\ggml-base.en-q8_0.bin ",
            @" C:\Models\diarizer.onnx ",
            "0.125",
            "15",
            true,
            true,
            true,
            false,
            true,
            false,
            " https://example.com/releases/latest ");

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
        };
        var editor = new ConfigEditorSnapshot(
            @"C:\Audio",
            @"C:\Transcripts",
            @"C:\Work",
            @"C:\Models",
            @"C:\Models\ggml-base.en-q8_0.bin",
            @"C:\Models\diarizer.onnx",
            "0.125",
            "15",
            true,
            true,
            true,
            false,
            true,
            false,
            "https://example.com/releases/latest");

        var result = MainWindowInteractionLogic.HasPendingConfigChanges(config, editor);

        Assert.True(result);
    }
}
