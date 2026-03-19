using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class AutoRecordingContinuityPolicy
{
    private static readonly TimeSpan MinimumWeakSignalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RecentAutoStopRecoveryWindow = TimeSpan.FromMinutes(2);

    public TimeSpan GetAutoStopTimeout(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        TimeSpan configuredTimeout)
    {
        if (configuredTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredTimeout), "The configured timeout must be greater than zero.");
        }

        if (decision is not null &&
            HasWeakSamePlatformSignal(decision, activePlatform) &&
            !HasSuppressedTeamsNavigationSignal(decision))
        {
            var scaledTimeout = TimeSpan.FromSeconds(configuredTimeout.TotalSeconds * 6d);
            return scaledTimeout >= MinimumWeakSignalTimeout
                ? scaledTimeout
                : MinimumWeakSignalTimeout;
        }

        return configuredTimeout;
    }

    public bool ShouldRecoverFromRecentAutoStop(
        DetectionDecision? decision,
        RecentAutoStopContext? recentAutoStop,
        DateTimeOffset nowUtc)
    {
        if (decision is null || recentAutoStop is null)
        {
            return false;
        }

        if (!decision.ShouldKeepRecording || decision.Platform == MeetingPlatform.Unknown)
        {
            return false;
        }

        if (decision.Platform != recentAutoStop.Platform)
        {
            return false;
        }

        return nowUtc - recentAutoStop.StoppedAtUtc <= RecentAutoStopRecoveryWindow;
    }

    public bool ShouldRefreshLastPositiveSignal(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        bool hasRecentCaptureActivity)
    {
        return ShouldRefreshLastPositiveSignal(
            decision,
            activePlatform,
            activeSessionTitle: null,
            hasRecentLoopbackActivity: hasRecentCaptureActivity,
            hasRecentMicrophoneActivity: false);
    }

    public bool ShouldRefreshLastPositiveSignal(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle,
        bool hasRecentLoopbackActivity,
        bool hasRecentMicrophoneActivity)
    {
        if (decision is null || activePlatform == MeetingPlatform.Unknown || decision.Platform != activePlatform)
        {
            return false;
        }

        if (decision.ShouldStart)
        {
            return true;
        }

        if (HasSpecificMeetingTitleMatch(decision, activeSessionTitle))
        {
            return true;
        }

        if (HasTeamsSharingSurfaceContinuation(decision, activeSessionTitle))
        {
            return true;
        }

        if (!hasRecentLoopbackActivity)
        {
            return false;
        }

        return HasWeakSamePlatformSignal(decision, activePlatform);
    }

    private static bool HasWeakSamePlatformSignal(DetectionDecision? decision, MeetingPlatform activePlatform)
    {
        return decision is not null &&
            decision.Platform == activePlatform &&
            decision.Platform != MeetingPlatform.Unknown &&
            !decision.ShouldKeepRecording;
    }

    private static bool HasSpecificMeetingTitleMatch(DetectionDecision decision, string? activeSessionTitle)
    {
        if (!decision.ShouldKeepRecording)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(activeSessionTitle) || string.IsNullOrWhiteSpace(decision.SessionTitle))
        {
            return false;
        }

        var normalizedDetectedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        var normalizedActiveTitle = NormalizeMeetingTitle(activeSessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedDetectedTitle) ||
            string.IsNullOrWhiteSpace(normalizedActiveTitle) ||
            !string.Equals(normalizedDetectedTitle, normalizedActiveTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsGenericMeetingTitle(normalizedDetectedTitle, decision.Platform);
    }

    private static string NormalizeMeetingTitle(string title)
    {
        var normalized = title.Trim();
        normalized = CollapseWhitespace(normalized);

        if (normalized.StartsWith("Meeting compact view |", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Meeting compact view |".Length..].Trim();
        }

        if (normalized.EndsWith("| Pinned window", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"| Pinned window".Length].Trim();
        }

        return CollapseWhitespace(normalized.Trim('|', ' '));
    }

    private static bool IsGenericMeetingTitle(string title, MeetingPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var normalized = title.Trim().ToLowerInvariant();
        if (normalized is "detected meeting" or "meeting")
        {
            return true;
        }

        return platform switch
        {
            MeetingPlatform.Teams => normalized is "microsoft teams" or "teams" or "ms-teams" or "sharing control bar",
            MeetingPlatform.GoogleMeet => normalized is "google meet" or "meet",
            _ => false,
        };
    }

    private static bool HasTeamsSharingSurfaceContinuation(DetectionDecision decision, string? activeSessionTitle)
    {
        if (decision.Platform != MeetingPlatform.Teams || !decision.ShouldKeepRecording)
        {
            return false;
        }

        var normalizedActiveTitle = NormalizeMeetingTitle(activeSessionTitle ?? string.Empty);
        if (IsGenericMeetingTitle(normalizedActiveTitle, MeetingPlatform.Teams))
        {
            return false;
        }

        var normalizedDetectedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        return normalizedDetectedTitle.StartsWith("sharing control bar", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasSuppressedTeamsNavigationSignal(DetectionDecision decision)
    {
        if (decision.Platform != MeetingPlatform.Teams)
        {
            return false;
        }

        foreach (var signal in decision.Signals)
        {
            if (!string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsSuppressedTeamsWindowTitle(signal.Value))
            {
                return true;
            }
        }

        return false;
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

public sealed record RecentAutoStopContext(
    MeetingPlatform Platform,
    DateTimeOffset StoppedAtUtc);
