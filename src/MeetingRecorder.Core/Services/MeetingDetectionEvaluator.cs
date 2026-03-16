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
                0d,
                string.Empty,
                signals,
                "No detection signals were provided.");
        }

        var teamsConfidence = 0d;
        var meetConfidence = 0d;
        string? title = null;
        var hasAudioActivity = false;
        var suppressedTeamsWindowDetected = false;

        foreach (var signal in signals)
        {
            if (string.Equals(signal.Source, "audio-activity", StringComparison.OrdinalIgnoreCase))
            {
                hasAudioActivity = true;
            }

            var normalized = signal.Value.ToLowerInvariant();
            if (normalized.Contains("teams", StringComparison.Ordinal))
            {
                if (string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase) &&
                    IsSuppressedTeamsWindowTitle(signal.Value))
                {
                    suppressedTeamsWindowDetected = true;
                    continue;
                }

                teamsConfidence += signal.Weight;
                title ??= CleanTitle(signal.Value, "Microsoft Teams");
            }

            if (normalized.Contains("google meet", StringComparison.Ordinal) ||
                normalized.Contains("meet.google.com", StringComparison.Ordinal))
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
        var shouldStart = confidence >= 0.75d &&
            platform != MeetingPlatform.Unknown &&
            hasAudioActivity &&
            !suppressedTeamsWindowDetected;
        var reason = shouldStart
            ? "Detection confidence met the recording threshold and active system audio was present."
            : BuildReason(confidence, platform, hasAudioActivity, suppressedTeamsWindowDetected);

        return new DetectionDecision(
            platform,
            shouldStart,
            confidence,
            title ?? "Detected meeting",
            signals,
            reason);
    }

    private static string BuildReason(
        double confidence,
        MeetingPlatform platform,
        bool hasAudioActivity,
        bool suppressedTeamsWindowDetected)
    {
        if (suppressedTeamsWindowDetected)
        {
            return "The detected Teams window appears to be a chat or navigation view, not an active meeting.";
        }

        if (platform == MeetingPlatform.Unknown || confidence < 0.75d)
        {
            return "Detection confidence did not meet the recording threshold.";
        }

        if (!hasAudioActivity)
        {
            return "Meeting-like window detected, but no active system audio was observed.";
        }

        return "Detection did not meet the recording criteria.";
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
            normalized.StartsWith("calls |", StringComparison.Ordinal);
    }
}
