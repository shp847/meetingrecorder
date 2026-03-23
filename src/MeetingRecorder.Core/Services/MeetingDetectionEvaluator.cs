using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class MeetingDetectionEvaluator
{
    public DetectionDecision Evaluate(IReadOnlyList<DetectionSignal> signals)
    {
        if (signals.Count == 0)
        {
            return new DetectionDecision(
                MeetingPlatform.Unknown,
                false,
                false,
                0d,
                string.Empty,
                signals,
                "No detection signals were provided.");
        }

        var teamsConfidence = 0d;
        var meetConfidence = 0d;
        string? title = null;
        var hasAudioActivity = false;
        var hasUnverifiedBrowserAudio = false;
        var suppressedTeamsWindowDetected = false;
        var genericTeamsShellDetected = false;

        foreach (var signal in signals)
        {
            if (IsActiveAudioSignal(signal))
            {
                hasAudioActivity = true;
            }

            if (string.Equals(signal.Source, "audio-browser-unverified", StringComparison.OrdinalIgnoreCase))
            {
                hasUnverifiedBrowserAudio = true;
            }

            var normalized = signal.Value.ToLowerInvariant();
            if (normalized.Contains("teams", StringComparison.Ordinal))
            {
                if (string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase) &&
                    IsSuppressedTeamsWindowTitle(signal.Value))
                {
                    title ??= TryExtractTeamsAttendeeTitle(signal.Value);
                    suppressedTeamsWindowDetected = true;
                    continue;
                }

                if (string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase) &&
                    IsGenericTeamsShellTitle(signal.Value))
                {
                    genericTeamsShellDetected = true;
                }

                teamsConfidence += signal.Weight;
                title ??= CleanTitle(signal.Value, "Microsoft Teams");
            }

            if (normalized.Contains("google meet", StringComparison.Ordinal) ||
                normalized.Contains("meet.google.com", StringComparison.Ordinal) ||
                normalized.StartsWith("meet -", StringComparison.Ordinal))
            {
                meetConfidence += signal.Weight;
                title ??= CleanTitle(signal.Value, "Google Meet");
            }
        }

        var platform = meetConfidence >= teamsConfidence && meetConfidence > 0d
            ? MeetingPlatform.GoogleMeet
                : teamsConfidence > 0d
                    ? MeetingPlatform.Teams
                    : MeetingPlatform.Unknown;

        var confidence = Math.Min(1d, Math.Max(meetConfidence, teamsConfidence));
        if (platform == MeetingPlatform.Teams && genericTeamsShellDetected)
        {
            confidence = 0.74d;
        }

        var shouldKeepRecording = confidence >= 0.75d &&
            platform != MeetingPlatform.Unknown &&
            !suppressedTeamsWindowDetected &&
            !genericTeamsShellDetected;
        var shouldStart = shouldKeepRecording && hasAudioActivity;
        var reason = shouldStart
            ? "Detection confidence met the recording threshold and active system audio was present."
            : BuildReason(confidence, platform, hasAudioActivity, hasUnverifiedBrowserAudio, suppressedTeamsWindowDetected, genericTeamsShellDetected);

        return new DetectionDecision(
            platform,
            shouldStart,
            shouldKeepRecording,
            confidence,
            title ?? "Detected meeting",
            signals,
            reason);
    }

    private static string BuildReason(
        double confidence,
        MeetingPlatform platform,
        bool hasAudioActivity,
        bool hasUnverifiedBrowserAudio,
        bool suppressedTeamsWindowDetected,
        bool genericTeamsShellDetected)
    {
        if (suppressedTeamsWindowDetected)
        {
            return "The detected Teams window appears to be a chat or navigation view, not an active meeting.";
        }

        if (genericTeamsShellDetected)
        {
            return "The detected Teams window appears to be a generic Teams shell, not a specific active meeting.";
        }

        if (platform == MeetingPlatform.Unknown || confidence < 0.75d)
        {
            return "Detection confidence did not meet the recording threshold.";
        }

        if (platform == MeetingPlatform.GoogleMeet && hasUnverifiedBrowserAudio)
        {
            return "Google Meet-like window detected, but the active browser audio could not be attributed to the Meet tab.";
        }

        if (!hasAudioActivity)
        {
            return "Meeting-like window detected, but no active system audio was observed.";
        }

        return "Detection did not meet the recording criteria.";
    }

    private static bool IsActiveAudioSignal(DetectionSignal signal)
    {
        return signal.Source.StartsWith("audio-", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signal.Source, "audio-silence", StringComparison.OrdinalIgnoreCase) &&
            signal.Weight > 0d;
    }

    private static string CleanTitle(string value, string suffix)
    {
        return value
            .Replace($"- {suffix}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"| {suffix}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool IsSuppressedTeamsWindowTitle(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.StartsWith("chat |", StringComparison.Ordinal) ||
            normalized.StartsWith("activity |", StringComparison.Ordinal) ||
            normalized.StartsWith("calendar |", StringComparison.Ordinal) ||
            normalized.StartsWith("files |", StringComparison.Ordinal) ||
            normalized.StartsWith("approvals |", StringComparison.Ordinal) ||
            normalized.StartsWith("assignments |", StringComparison.Ordinal) ||
            normalized.StartsWith("calls |", StringComparison.Ordinal) ||
            normalized.StartsWith("search |", StringComparison.Ordinal);
    }

    private static bool IsGenericTeamsShellTitle(string value)
    {
        var normalized = CleanTitle(value, "Microsoft Teams").Trim();
        return normalized.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ms-teams", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractTeamsAttendeeTitle(string value)
    {
        const string chatPrefix = "Chat |";
        const string teamsSuffix = "| Microsoft Teams";

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(chatPrefix, StringComparison.OrdinalIgnoreCase) ||
            !trimmed.EndsWith(teamsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var attendeeTitle = trimmed[chatPrefix.Length..^teamsSuffix.Length].Trim().Trim('|', ' ');
        return string.IsNullOrWhiteSpace(attendeeTitle) ? null : attendeeTitle;
    }
}
