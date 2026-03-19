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
    string AutoDetectThresholdText,
    string MeetingStopTimeoutText,
    bool MicCaptureEnabled,
    bool LaunchOnLoginEnabled,
    bool AutoDetectEnabled,
    bool CalendarTitleFallbackEnabled,
    bool UpdateCheckEnabled,
    bool AutoInstallUpdatesEnabled,
    string UpdateFeedUrl);

internal sealed record SpeakerLabelDraft(string OriginalLabel, string EditedLabel);

internal enum DashboardPrimaryActionTarget
{
    None = 0,
    Models = 1,
    Updates = 2,
    Config = 3,
}

internal sealed record DashboardPrimaryActionState(
    string Headline,
    string Body,
    DashboardPrimaryActionTarget Target,
    string? ActionLabel);

internal static class MainWindowInteractionLogic
{
    public static DashboardPrimaryActionState BuildDashboardPrimaryAction(
        bool hasValidModel,
        bool isRecording,
        bool updateChecksEnabled,
        bool autoInstallUpdatesEnabled,
        AppUpdateCheckResult? updateResult)
    {
        if (!hasValidModel)
        {
            return new DashboardPrimaryActionState(
                "Set up transcription first",
                "Download or import a valid Whisper model so new recordings can be transcribed without extra cleanup later.",
                DashboardPrimaryActionTarget.Models,
                "Open Models");
        }

        if (updateResult?.Status == AppUpdateStatusKind.UpdateAvailable && !autoInstallUpdatesEnabled)
        {
            return new DashboardPrimaryActionState(
                "A newer app version is ready",
                "Open the Updates tab to review the release details and install it when the app is idle.",
                DashboardPrimaryActionTarget.Updates,
                "Open Updates");
        }

        if (!updateChecksEnabled)
        {
            return new DashboardPrimaryActionState(
                "Turn on daily update checks",
                "Enable automatic GitHub checks so the app can keep itself current and reduce manual maintenance.",
                DashboardPrimaryActionTarget.Config,
                "Open Config");
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
            "Core setup looks healthy. Start from the Dashboard when you need a manual recording, or let auto-detection watch for supported meetings.",
            DashboardPrimaryActionTarget.None,
            null);
    }

    public static string BuildAutoInstallUpdatesHint(bool updateChecksEnabled)
    {
        return updateChecksEnabled
            ? "If enabled, the app will install newer releases automatically once recording and background processing are idle."
            : "Turn on daily GitHub checks first. Automatic install only works after update checks are enabled.";
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
            !string.Equals(FormatThreshold(currentConfig.AutoDetectAudioPeakThreshold), NormalizeText(editor.AutoDetectThresholdText), StringComparison.Ordinal) ||
            !string.Equals(currentConfig.MeetingStopTimeoutSeconds.ToString(CultureInfo.InvariantCulture), NormalizeText(editor.MeetingStopTimeoutText), StringComparison.Ordinal) ||
            currentConfig.MicCaptureEnabled != editor.MicCaptureEnabled ||
            currentConfig.LaunchOnLoginEnabled != editor.LaunchOnLoginEnabled ||
            currentConfig.AutoDetectEnabled != editor.AutoDetectEnabled ||
            currentConfig.CalendarTitleFallbackEnabled != editor.CalendarTitleFallbackEnabled ||
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

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
