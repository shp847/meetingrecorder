using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class AutoRecordingContinuityPolicy
{
    private static readonly TimeSpan MinimumWeakSignalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumTeamsShellContinuationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RecentAutoStopRecoveryWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan QuietSpecificTeamsAutoStartDelay = TimeSpan.FromSeconds(20);

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

        if (HasTeamsSpecificQuietContinuation(decision, activePlatform, activeSessionTitle) ||
            (decision is not null && HasSuppressedTeamsTitleContinuation(decision, activeSessionTitle)) ||
            (decision is not null && HasTeamsSharingSurfaceContinuation(decision, activeSessionTitle)))
        {
            var scaledTimeout = TimeSpan.FromSeconds(configuredTimeout.TotalSeconds * 3d);
            return scaledTimeout >= MinimumTeamsShellContinuationTimeout
                ? scaledTimeout
                : MinimumTeamsShellContinuationTimeout;
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

    internal bool ShouldAutoStartQuietSpecificTeamsMeeting(
        DetectionDecision? decision,
        DateTimeOffset firstObservedUtc,
        DateTimeOffset nowUtc)
    {
        if (!IsQuietSpecificTeamsMeetingCandidate(decision))
        {
            return false;
        }

        return nowUtc - firstObservedUtc >= QuietSpecificTeamsAutoStartDelay;
    }

    internal bool IsQuietSpecificTeamsMeetingCandidate(DetectionDecision? decision)
    {
        return IsQuietSpecificTeamsMeetingCandidateCore(decision);
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

    public bool ShouldReclassifyActiveSession(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle)
    {
        if (decision is null ||
            !decision.ShouldStart ||
            activePlatform == MeetingPlatform.Unknown ||
            decision.Platform == MeetingPlatform.Unknown)
        {
            return false;
        }

        var normalizedDetectedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        if (IsGenericMeetingTitle(normalizedDetectedTitle, decision.Platform) ||
            !HasMeetingIdentityEvidence(decision))
        {
            return false;
        }

        if (decision.Platform != activePlatform)
        {
            return true;
        }

        var normalizedActiveTitle = NormalizeMeetingTitle(activeSessionTitle ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedActiveTitle) ||
            IsGenericMeetingTitle(normalizedActiveTitle, activePlatform))
        {
            return true;
        }

        return !string.Equals(normalizedDetectedTitle, normalizedActiveTitle, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldRefreshLastPositiveSignal(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle,
        bool hasRecentLoopbackActivity,
        bool hasRecentMicrophoneActivity)
    {
        if (decision is null || activePlatform == MeetingPlatform.Unknown)
        {
            return false;
        }

        if (decision.Platform != activePlatform)
        {
            return HasCrossPlatformBrowserContinuation(
                decision,
                activePlatform,
                activeSessionTitle,
                hasRecentLoopbackActivity);
        }

        if (decision.ShouldStart)
        {
            return true;
        }

        if (decision.Platform == MeetingPlatform.Teams &&
            HasOfficialTeamsNoCurrentMatchSignal(decision))
        {
            return false;
        }

        var hasRecentCaptureActivity = hasRecentLoopbackActivity || hasRecentMicrophoneActivity;
        if (decision.Platform == MeetingPlatform.Teams &&
            hasRecentLoopbackActivity &&
            HasOfficialTeamsMatchSignal(decision))
        {
            return true;
        }

        if (HasSpecificMeetingTitleMatch(decision, activeSessionTitle))
        {
            return decision.Platform != MeetingPlatform.Teams || hasRecentLoopbackActivity;
        }

        if (hasRecentLoopbackActivity &&
            HasSuppressedTeamsTitleContinuation(decision, activeSessionTitle))
        {
            return true;
        }

        if (hasRecentLoopbackActivity &&
            HasTeamsSharingSurfaceContinuation(decision, activeSessionTitle))
        {
            return true;
        }

        if (!hasRecentLoopbackActivity)
        {
            return false;
        }

        return HasWeakSamePlatformSignal(decision, activePlatform);
    }

    private static bool HasTeamsSpecificQuietContinuation(
        DetectionDecision? decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle)
    {
        return activePlatform == MeetingPlatform.Teams &&
            decision is not null &&
            decision.Platform == MeetingPlatform.Teams &&
            HasSpecificMeetingTitleMatch(decision, activeSessionTitle);
    }

    private static bool HasCrossPlatformBrowserContinuation(
        DetectionDecision decision,
        MeetingPlatform activePlatform,
        string? activeSessionTitle,
        bool hasRecentLoopbackActivity)
    {
        if (!hasRecentLoopbackActivity ||
            decision.ShouldStart ||
            HasStrongAttributedAudioMatch(decision))
        {
            return false;
        }

        var normalizedActiveTitle = NormalizeMeetingTitle(activeSessionTitle ?? string.Empty);
        if (IsGenericMeetingTitle(normalizedActiveTitle, activePlatform))
        {
            return false;
        }

        return activePlatform == MeetingPlatform.Teams &&
            decision.Platform == MeetingPlatform.GoogleMeet &&
            HasWindowTitleEvidence(decision) &&
            HasBrowserSurfaceEvidence(decision);
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

    private static bool HasBrowserSurfaceEvidence(DetectionDecision decision)
    {
        foreach (var signal in decision.Signals)
        {
            if (string.Equals(signal.Source, "browser-window", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signal.Source, "browser-tab", StringComparison.OrdinalIgnoreCase))
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

    private static bool HasSupportedMeetingAudioAttribution(DetectionDecision decision)
    {
        return decision.DetectedAudioSource is { MatchKind: not AudioSourceMatchKind.EndpointFallback } detectedAudioSource &&
            decision.Platform switch
            {
                MeetingPlatform.Teams => string.Equals(detectedAudioSource.AppName, "Microsoft Teams", StringComparison.OrdinalIgnoreCase),
                MeetingPlatform.GoogleMeet => string.Equals(detectedAudioSource.AppName, "Google Meet", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
    }

    private static bool HasMeetingIdentityEvidence(DetectionDecision decision)
    {
        if (HasStrongAttributedAudioMatch(decision))
        {
            return true;
        }

        return decision.Platform switch
        {
            MeetingPlatform.Teams => HasWindowTitleEvidence(decision) && HasTeamsHostEvidence(decision),
            MeetingPlatform.GoogleMeet => HasWindowTitleEvidence(decision) || HasBrowserSurfaceEvidence(decision),
            _ => false,
        };
    }

    private static bool IsQuietSpecificTeamsMeetingCandidateCore(DetectionDecision? decision)
    {
        if (decision is null ||
            decision.Platform != MeetingPlatform.Teams ||
            decision.ShouldStart ||
            !decision.ShouldKeepRecording)
        {
            return false;
        }

        var normalizedTitle = NormalizeMeetingTitle(decision.SessionTitle);
        if (IsGenericMeetingTitle(normalizedTitle, MeetingPlatform.Teams) ||
            HasSuppressedTeamsNavigationSignal(decision) ||
            !HasMeetingIdentityEvidence(decision) ||
            !HasSupportedMeetingAudioAttribution(decision) ||
            !HasSilentAudioSignal(decision))
        {
            return false;
        }

        return true;
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
            string.IsNullOrWhiteSpace(normalizedActiveTitle))
        {
            return false;
        }

        if (string.Equals(normalizedDetectedTitle, normalizedActiveTitle, StringComparison.OrdinalIgnoreCase))
        {
            return !IsGenericMeetingTitle(normalizedDetectedTitle, decision.Platform);
        }

        return decision.Platform == MeetingPlatform.GoogleMeet &&
            !IsGenericMeetingTitle(normalizedActiveTitle, MeetingPlatform.GoogleMeet) &&
            HasGoogleMeetSharingSurfaceTitle(decision.SessionTitle) &&
            HasWindowTitleEvidence(decision) &&
            HasBrowserSurfaceEvidence(decision);
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

    private static bool HasSilentAudioSignal(DetectionDecision decision)
    {
        foreach (var signal in decision.Signals)
        {
            if (string.Equals(signal.Source, "audio-silence", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGoogleMeetSharingSurfaceTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalized = title.Trim().ToLowerInvariant();
        return normalized.StartsWith("meet.google.com is sharing ", StringComparison.Ordinal);
    }

    private static bool HasOfficialTeamsMatchSignal(DetectionDecision decision)
    {
        return HasSignal(decision, "official-teams-match");
    }

    private static bool HasOfficialTeamsNoCurrentMatchSignal(DetectionDecision decision)
    {
        return HasSignal(decision, "official-teams-no-current-match");
    }

    private static bool HasSignal(DetectionDecision decision, string source)
    {
        foreach (var signal in decision.Signals)
        {
            if (string.Equals(signal.Source, source, StringComparison.OrdinalIgnoreCase))
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
