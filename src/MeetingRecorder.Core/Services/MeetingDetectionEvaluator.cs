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
        var hasAttributedAudioActivity = false;
        var hasUnverifiedBrowserAudio = false;
        var suppressedTeamsWindowDetected = false;
        var genericTeamsShellDetected = false;

        foreach (var signal in signals)
        {
            if (IsActiveAudioSignal(signal))
            {
                hasAudioActivity = true;
                hasAttributedAudioActivity |= IsAttributedAudioSignal(signal);
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

        var sessionTitle = title ?? "Detected meeting";
        genericTeamsShellDetected |=
            platform == MeetingPlatform.Teams &&
            LooksLikeEmailAddress(sessionTitle) &&
            !hasAttributedAudioActivity;
        var confidence = Math.Min(1d, Math.Max(meetConfidence, teamsConfidence));
        if (platform == MeetingPlatform.Teams && genericTeamsShellDetected)
        {
            confidence = 0.74d;
        }

        var requiresAttributedAudioForStart = IsGenericMeetingTitle(platform, sessionTitle);
        var shouldKeepRecording = confidence >= 0.75d &&
            platform != MeetingPlatform.Unknown &&
            !suppressedTeamsWindowDetected &&
            !genericTeamsShellDetected;
        var shouldStart = shouldKeepRecording &&
            hasAudioActivity &&
            (!requiresAttributedAudioForStart || hasAttributedAudioActivity);

        var reason = shouldStart
            ? BuildStartReason(platform, hasAudioActivity)
            : BuildReason(
                confidence,
                platform,
                hasAudioActivity,
                hasAttributedAudioActivity,
                hasUnverifiedBrowserAudio,
                suppressedTeamsWindowDetected,
                genericTeamsShellDetected,
                requiresAttributedAudioForStart);

        return new DetectionDecision(
            platform,
            shouldStart,
            shouldKeepRecording,
            confidence,
            sessionTitle,
            signals,
            reason);
    }

    private static string BuildReason(
        double confidence,
        MeetingPlatform platform,
        bool hasAudioActivity,
        bool hasAttributedAudioActivity,
        bool hasUnverifiedBrowserAudio,
        bool suppressedTeamsWindowDetected,
        bool genericTeamsShellDetected,
        bool requiresAttributedAudioForStart)
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

        if (requiresAttributedAudioForStart &&
            hasAudioActivity &&
            !hasAttributedAudioActivity)
        {
            return "Generic meeting window detected, but the active system audio could not be attributed to that meeting.";
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

    private static string BuildStartReason(MeetingPlatform platform, bool hasAudioActivity)
    {
        if (hasAudioActivity)
        {
            return "Detection confidence met the recording threshold and active system audio was present.";
        }

        return platform == MeetingPlatform.GoogleMeet
            ? "Specific Google Meet identity evidence was present, so auto-start proceeded before render audio became active."
            : "Detection confidence met the recording threshold.";
    }

    private static bool IsActiveAudioSignal(DetectionSignal signal)
    {
        return signal.Source.StartsWith("audio-", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signal.Source, "audio-silence", StringComparison.OrdinalIgnoreCase) &&
            signal.Weight > 0d;
    }

    private static bool IsAttributedAudioSignal(DetectionSignal signal)
    {
        return IsActiveAudioSignal(signal) &&
            !string.Equals(signal.Source, "audio-activity", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signal.Source, "audio-browser-unverified", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericMeetingTitle(MeetingPlatform platform, string title)
    {
        var normalized = MeetingTitleNormalizer.NormalizeForComparison(title);
        return platform switch
        {
            MeetingPlatform.Teams => normalized is
                "microsoft teams" or
                "teams" or
                "ms teams" or
                "search" ||
                normalized.StartsWith("sharing control bar", StringComparison.Ordinal),
            MeetingPlatform.GoogleMeet =>
                normalized is "google meet" or "meet" ||
                normalized.StartsWith("google meet ", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string CleanTitle(string value, string suffix)
    {
        var cleaned = value
            .Replace($"- {suffix}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"| {suffix}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (!suffix.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
        {
            return cleaned;
        }

        var parts = cleaned.Split('|', StringSplitOptions.TrimEntries);
        var account = parts.Length >= 3 ? parts[^1] : string.Empty;
        return LooksLikeEmailAddress(account)
            ? string.Join(" | ", parts[..^2])
            : cleaned;
    }

    private static bool LooksLikeEmailAddress(string value)
    {
        return value.IndexOf('@') > 0 &&
               value.IndexOf('@') == value.LastIndexOf('@') &&
               value[^1] != '@' &&
               !value.Any(char.IsWhiteSpace);
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
            normalized.Equals("ms-teams", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Sharing control bar", StringComparison.OrdinalIgnoreCase);
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

        if (trimmed.Length <= chatPrefix.Length + teamsSuffix.Length)
        {
            return null;
        }

        var attendeeTitle = trimmed
            .Substring(chatPrefix.Length, trimmed.Length - chatPrefix.Length - teamsSuffix.Length)
            .Trim()
            .Trim('|', ' ');
        return string.IsNullOrWhiteSpace(attendeeTitle) ? null : attendeeTitle;
    }
}
