using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class AutoRecordingContinuityPolicy
{
    private static readonly TimeSpan MinimumWeakSignalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumTeamsShellContinuationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RecentAutoStopRecoveryWindow = TimeSpan.FromMinutes(2);

    public TimeSpan GetAutoStopTimeout(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        TimeSpan configuredTimeout)
    {
        return GetAutoStopTimeout(
            decision,
            activePlatform,
            activeSessionTitle: null,
            configuredTimeout);
    }

    public TimeSpan GetAutoStopTimeout(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle,
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

        if (HasTeamsShellContinuation(decision, activePlatform, activeSessionTitle))
        {
            var scaledTimeout = TimeSpan.FromSeconds(configuredTimeout.TotalSeconds * 3d);
            return scaledTimeout >= MinimumTeamsShellContinuationTimeout
                ? scaledTimeout
                : MinimumTeamsShellContinuationTimeout;
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

        if (!decision.ShouldStart || decision.Platform == MeetingPlatform.Unknown)
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

    public bool ShouldReclassifyAutoStartedSession(
        DetectionDecision? decision,
        MeetingPlatform activePlatform)
    {
        if (decision is null ||
            !decision.ShouldStart ||
            activePlatform == MeetingPlatform.Unknown ||
            decision.Platform == MeetingPlatform.Unknown ||
            decision.Platform == activePlatform)
        {
            return false;
        }

        var normalizedDetectedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        if (HasStrongAttributedAudioMatch(decision) &&
            !IsGenericMeetingTitle(normalizedDetectedTitle, decision.Platform))
        {
            return true;
        }

        if (activePlatform != MeetingPlatform.GoogleMeet || decision.Platform != MeetingPlatform.Teams)
        {
            return false;
        }

        if (!HasWindowTitleEvidence(decision) || !HasTeamsHostEvidence(decision))
        {
            return false;
        }

        return !IsGenericMeetingTitle(normalizedDetectedTitle, MeetingPlatform.Teams);
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

        if (HasSuppressedTeamsTitleContinuation(decision, activeSessionTitle))
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
        if (decision is null ||
            decision.Platform != activePlatform ||
            decision.Platform == MeetingPlatform.Unknown ||
            decision.ShouldKeepRecording)
        {
            return false;
        }

        foreach (var signal in decision.Signals)
        {
            if (!string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(signal.Source, "browser-window", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(signal.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStrongAttributedAudioMatch(DetectionDecision decision)
    {
        return decision.DetectedAudioSource is
        {
            Confidence: AudioSourceConfidence.High,
            MatchKind: not AudioSourceMatchKind.EndpointFallback,
        };
    }

    private static bool HasTeamsShellContinuation(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle)
    {
        if (decision is null ||
            activePlatform != MeetingPlatform.Teams ||
            decision.Platform != MeetingPlatform.Teams)
        {
            return false;
        }

        var normalizedActiveTitle = NormalizeMeetingTitle(activeSessionTitle ?? string.Empty);
        if (IsGenericMeetingTitle(normalizedActiveTitle, MeetingPlatform.Teams))
        {
            return false;
        }

        if (!HasWindowTitleEvidence(decision))
        {
            return false;
        }

        var normalizedDetectedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        if (IsGenericMeetingTitle(normalizedDetectedTitle, MeetingPlatform.Teams))
        {
            return true;
        }

        return HasSuppressedTeamsNavigationSignal(decision);
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

    private static bool HasSuppressedTeamsTitleContinuation(DetectionDecision decision, string? activeSessionTitle)
    {
        if (decision.Platform != MeetingPlatform.Teams || !HasSuppressedTeamsNavigationSignal(decision))
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

        return !IsGenericMeetingTitle(normalizedDetectedTitle, MeetingPlatform.Teams);
    }

    private static string NormalizeMeetingTitle(string title) => MeetingTitleNormalizer.NormalizeForComparison(title);

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
            MeetingPlatform.Teams => normalized is "microsoft teams" or "teams" or "ms teams" or "sharing control bar",
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

    private static bool HasWindowTitleEvidence(DetectionDecision decision)
    {
        foreach (var signal in decision.Signals)
        {
            if (string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTeamsHostEvidence(DetectionDecision decision)
    {
        foreach (var signal in decision.Signals)
        {
            if (string.Equals(signal.Source, "teams-host", StringComparison.OrdinalIgnoreCase))
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
