using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.CoreAudioApi;
using System.Globalization;
using System.Diagnostics;
using System.Threading;

namespace MeetingRecorder.App.Services;

internal readonly record struct MeetingWindowCandidate(
    string ProcessName,
    string MainWindowTitle,
    string WindowClassName = "",
    nint WindowHandle = default,
    int ProcessId = 0);

internal readonly record struct CandidateDetectionTitle(
    string Title,
    bool FromBrowserTab);

internal sealed class WindowMeetingDetector
{
    private static readonly string[] SupportedMeetingWindowClasses =
    [
        "Chrome_WidgetWin_0",
        "Chrome_WidgetWin_1",
        "TeamsWebView",
    ];

    private readonly LiveAppConfig _config;
    private readonly MeetingDetectionEvaluator _evaluator;
    private readonly IAudioActivityProbe _audioActivityProbe;
    private readonly MeetingTitleEnricher _meetingTitleEnricher;
    private readonly Func<IReadOnlyList<MeetingWindowCandidate>> _enumerateCandidates;
    private readonly TimeSpan _audioProbeTimeout;
    private readonly TimeSpan _audioProbeBackoff;
    private readonly Action<string>? _log;
    private long _audioProbeDisabledUntilUtcTicks;
    private readonly object _audioProbeTaskGate = new();
    private Task<AudioSourceAttributionSnapshot>? _activeAudioProbeTask;

    public WindowMeetingDetector(
        LiveAppConfig config,
        MeetingDetectionEvaluator evaluator,
        IAudioActivityProbe audioActivityProbe,
        MeetingTitleEnricher meetingTitleEnricher)
        : this(
            config,
            evaluator,
            audioActivityProbe,
            meetingTitleEnricher,
            EnumerateCandidateWindows,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMinutes(2),
            null)
    {
    }

    internal WindowMeetingDetector(
        LiveAppConfig config,
        MeetingDetectionEvaluator evaluator,
        IAudioActivityProbe audioActivityProbe,
        MeetingTitleEnricher meetingTitleEnricher,
        Func<IReadOnlyList<MeetingWindowCandidate>> enumerateCandidates,
        TimeSpan audioProbeTimeout,
        TimeSpan audioProbeBackoff,
        Action<string>? log = null)
    {
        _config = config;
        _evaluator = evaluator;
        _audioActivityProbe = audioActivityProbe;
        _meetingTitleEnricher = meetingTitleEnricher;
        _enumerateCandidates = enumerateCandidates ?? throw new ArgumentNullException(nameof(enumerateCandidates));
        _audioProbeTimeout = audioProbeTimeout > TimeSpan.Zero
            ? audioProbeTimeout
            : throw new ArgumentOutOfRangeException(nameof(audioProbeTimeout), "The audio probe timeout must be greater than zero.");
        _audioProbeBackoff = audioProbeBackoff > TimeSpan.Zero
            ? audioProbeBackoff
            : throw new ArgumentOutOfRangeException(nameof(audioProbeBackoff), "The audio probe backoff must be greater than zero.");
        _log = log;
    }

    public DetectionDecision? DetectBestCandidate()
    {
        var candidates = new List<DetectionDecision>();
        var audioAttribution = TryCaptureAudioAttribution();

        foreach (var candidateWindow in _enumerateCandidates())
        {
            if (!LooksLikeSupportedMeetingWindowClass(candidateWindow.WindowClassName))
            {
                continue;
            }

            foreach (var detectionTitle in EnumerateDetectionTitles(candidateWindow, audioAttribution))
            {
                var signals = BuildSignals(candidateWindow, audioAttribution, detectionTitle, out var audioMatch);
                if (signals.Count == 0)
                {
                    continue;
                }

                var decision = _evaluator.Evaluate(signals);
                if (decision.Platform == MeetingPlatform.Unknown)
                {
                    continue;
                }

                var decisionTimestamp = DateTimeOffset.UtcNow;
                decision = decision with
                {
                    SessionTitle = string.IsNullOrWhiteSpace(decision.SessionTitle)
                        ? detectionTitle.Title
                        : decision.SessionTitle,
                    DetectedAudioSource = audioMatch?.Source,
                };
                decision = _meetingTitleEnricher.Enrich(
                    decision,
                    _config.Current.CalendarTitleFallbackEnabled,
                    decisionTimestamp);

                candidates.Add(decision);
            }
        }

        var bestCandidate = default(DetectionDecision);
        foreach (var candidate in candidates)
        {
            var adjustedCandidate = ApplyTeamsPlaybackHeuristic(candidate, candidates);
            if (bestCandidate is null || IsBetterCandidate(adjustedCandidate, bestCandidate))
            {
                bestCandidate = adjustedCandidate;
            }
        }

        return bestCandidate;
    }

    public Task<DetectionDecision?> DetectBestCandidateAsync(CancellationToken cancellationToken)
    {
        return Task.Run(DetectBestCandidate, cancellationToken);
    }

    internal static DetectionDecision ApplyTeamsPlaybackHeuristic(
        DetectionDecision candidate,
        IReadOnlyList<DetectionDecision> allCandidates)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(allCandidates);

        if (!IsAmbiguousPlainTeamsContentWindow(candidate) ||
            HasTeamsRenderEvidence(candidate) ||
            HasUnavailableAudioProbeSignal(candidate))
        {
            return candidate;
        }

        var hasMatchingSuppressedChatCandidate = HasMatchingSuppressedTeamsChatCandidate(candidate, allCandidates);

        return candidate with
        {
            ShouldStart = false,
            ShouldKeepRecording = false,
            Confidence = Math.Min(candidate.Confidence, 0.15d),
            Reason = hasMatchingSuppressedChatCandidate
                ? "The detected Teams window appears to be playback or chat-thread media, not a live meeting."
                : "The detected Teams window appears to be Teams content without recent Teams render audio, not a live meeting.",
        };
    }

    internal static bool IsBetterCandidate(DetectionDecision candidate, DetectionDecision currentBest)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(currentBest);

        if (ShouldPreferSpecificTeamsCandidateOverUnattributedGoogleMeet(candidate, currentBest))
        {
            return true;
        }

        if (ShouldPreferSpecificTeamsCandidateOverUnattributedGoogleMeet(currentBest, candidate))
        {
            return false;
        }

        var candidatePriority = GetCandidatePriority(candidate);
        var currentBestPriority = GetCandidatePriority(currentBest);

        if (candidatePriority.ShouldStartScore != currentBestPriority.ShouldStartScore)
        {
            return candidatePriority.ShouldStartScore > currentBestPriority.ShouldStartScore;
        }

        if (candidatePriority.Confidence != currentBestPriority.Confidence)
        {
            return candidatePriority.Confidence > currentBestPriority.Confidence;
        }

        if (candidatePriority.SpecificityScore != currentBestPriority.SpecificityScore)
        {
            return candidatePriority.SpecificityScore > currentBestPriority.SpecificityScore;
        }

        return candidatePriority.SignalCount > currentBestPriority.SignalCount;
    }

    private static bool ShouldPreferSpecificTeamsCandidateOverUnattributedGoogleMeet(
        DetectionDecision candidate,
        DetectionDecision other)
    {
        return candidate.Platform == MeetingPlatform.Teams &&
            other.Platform == MeetingPlatform.GoogleMeet &&
            candidate.ShouldKeepRecording &&
            HasSpecificSessionTitle(candidate) &&
            HasTeamsRenderEvidence(candidate) &&
            !HasAttributedGoogleMeetAudio(other);
    }

    private static bool HasAttributedGoogleMeetAudio(DetectionDecision decision)
    {
        return decision.Platform == MeetingPlatform.GoogleMeet &&
            decision.DetectedAudioSource is
            {
                AppName: var appName,
                MatchKind: not AudioSourceMatchKind.EndpointFallback,
            } &&
            string.Equals(appName, "Google Meet", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<MeetingWindowCandidate> EnumerateCandidateWindows()
    {
        return TopLevelWindowEnumerator.EnumerateVisibleWindowsByClass(SupportedMeetingWindowClasses)
            .Select(window => new MeetingWindowCandidate(
                TryGetProcessName(window.ProcessId),
                window.WindowTitle,
                window.WindowClassName,
                window.WindowHandle,
                window.ProcessId))
            .ToArray();
    }

    private static bool LooksLikeSupportedMeetingWindowClass(string windowClassName)
    {
        return SupportedMeetingWindowClasses.Any(supportedClass =>
            string.Equals(windowClassName, supportedClass, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeBrowserWindowClass(string windowClassName)
    {
        return string.Equals(windowClassName, "Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(windowClassName, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DetectionSignal> BuildSignals(
        MeetingWindowCandidate candidateWindow,
        AudioSourceAttributionSnapshot audioAttribution,
        CandidateDetectionTitle detectionTitle,
        out AudioSourceAttributionMatch? audioMatch)
    {
        var title = detectionTitle.Title;
        var now = DateTimeOffset.UtcNow;
        var signals = new List<DetectionSignal>();
        var isGoogleMeetCandidate = LooksLikeGoogleMeetWindowTitle(title);
        audioMatch = MatchAudioSource(candidateWindow, detectionTitle, audioAttribution);

        if (isGoogleMeetCandidate)
        {
            signals.Add(new DetectionSignal(
                "window-title",
                title,
                detectionTitle.FromBrowserTab ? 0.70d : 0.85d,
                now));
            if (LooksLikeSupportedMeetingWindowClass(candidateWindow.WindowClassName))
            {
                signals.Add(new DetectionSignal(
                    detectionTitle.FromBrowserTab ? "browser-tab" : "browser-window",
                    title,
                    detectionTitle.FromBrowserTab ? 0.05d : 0.15d,
                    now));
            }
        }

        if (LooksLikeTeamsMeetingSurface(candidateWindow, title))
        {
            var hasExplicitTeamsTitle = LooksLikeTeamsWindowTitle(title);
            signals.Add(new DetectionSignal(
                "window-title",
                hasExplicitTeamsTitle ? title : $"{title} | Microsoft Teams",
                hasExplicitTeamsTitle ? 0.85d : 0.70d,
                now));

            if (!string.IsNullOrWhiteSpace(candidateWindow.ProcessName))
            {
                signals.Add(new DetectionSignal("process-name", candidateWindow.ProcessName, 0.05d, now));
            }

            if (LooksLikeSupportedMeetingWindowClass(candidateWindow.WindowClassName))
            {
                signals.Add(new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now));
            }
        }

        if (audioMatch is not null)
        {
            signals.Add(new DetectionSignal(
                audioAttribution.IsActive
                    ? GetAudioSignalSource(audioMatch.Source.MatchKind)
                    : "audio-session-match",
                BuildAudioSignalValue(audioMatch),
                audioAttribution.IsActive
                    ? GetAudioSignalWeight(audioMatch.Source.MatchKind, audioMatch.Source.Confidence)
                    : 0d,
                now));
        }
        else
        {
            var shouldTrustExplicitGoogleMeetBrowserAudio = ShouldTrustExplicitGoogleMeetBrowserAudio(
                candidateWindow,
                detectionTitle,
                audioAttribution);
            var audioSource = shouldTrustExplicitGoogleMeetBrowserAudio
                ? "audio-browser-unverified"
                : ShouldRequireGoogleMeetAudioAttribution(isGoogleMeetCandidate, audioAttribution)
                ? "audio-browser-unverified"
                : audioAttribution.IsActive
                    ? "audio-activity"
                    : "audio-silence";
            var audioWeight = shouldTrustExplicitGoogleMeetBrowserAudio
                ? 0.20d
                : audioSource == "audio-activity"
                    ? 0.1d
                    : 0d;
            var audioValue = string.IsNullOrWhiteSpace(audioAttribution.DeviceName)
                ? $"peak={audioAttribution.PeakLevel:0.000}; status={audioAttribution.StatusDetail ?? "unknown"}"
                : $"{audioAttribution.DeviceName}; peak={audioAttribution.PeakLevel:0.000}; status={audioAttribution.StatusDetail ?? "ok"}";
            signals.Add(new DetectionSignal(audioSource, audioValue, audioWeight, now));
        }

        return signals;
    }

    private static IReadOnlyList<CandidateDetectionTitle> EnumerateDetectionTitles(
        MeetingWindowCandidate candidateWindow,
        AudioSourceAttributionSnapshot audioAttribution)
    {
        var titles = new List<CandidateDetectionTitle>();

        if (LooksLikeGoogleMeetWindowTitle(candidateWindow.MainWindowTitle) ||
            LooksLikeTeamsMeetingSurface(candidateWindow, candidateWindow.MainWindowTitle))
        {
            titles.Add(new CandidateDetectionTitle(candidateWindow.MainWindowTitle, false));
        }

        if (!LooksLikeBrowserWindowClass(candidateWindow.WindowClassName))
        {
            return titles;
        }

        foreach (var tabTitle in EnumerateAudioDerivedGoogleMeetTitles(candidateWindow, audioAttribution))
        {
            if (titles.Any(existing =>
                string.Equals(existing.Title, tabTitle, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            titles.Add(new CandidateDetectionTitle(tabTitle, true));
        }

        return titles;
    }

    private AudioSourceAttributionSnapshot TryCaptureAudioAttribution()
    {
        var disabledUntilUtcTicks = Interlocked.Read(ref _audioProbeDisabledUntilUtcTicks);
        if (disabledUntilUtcTicks > 0 && DateTimeOffset.UtcNow.UtcTicks < disabledUntilUtcTicks)
        {
            return CreateUnavailableAudioSnapshot("audio probe temporarily skipped after a previous timeout");
        }

        var threshold = _config.Current.AutoDetectAudioPeakThreshold;
        var audioProbeTask = GetOrStartAudioProbeTask(threshold, out var startedNewProbe);
        if (!startedNewProbe)
        {
            var disabledUntilUtc = DateTimeOffset.UtcNow + _audioProbeBackoff;
            Interlocked.Exchange(ref _audioProbeDisabledUntilUtcTicks, disabledUntilUtc.UtcTicks);
            _log?.Invoke(
                $"Skipped audio activity probe because the previous probe is still running. " +
                $"Skipping audio attribution until {disabledUntilUtc:O}.");
            return CreateUnavailableAudioSnapshot("audio probe still running");
        }

        if (!audioProbeTask.Wait(_audioProbeTimeout))
        {
            var disabledUntilUtc = DateTimeOffset.UtcNow + _audioProbeBackoff;
            Interlocked.Exchange(ref _audioProbeDisabledUntilUtcTicks, disabledUntilUtc.UtcTicks);
            _log?.Invoke(
                $"Audio activity probe timed out after {_audioProbeTimeout.TotalMilliseconds:0}ms. " +
                $"Skipping audio attribution until {disabledUntilUtc:O}.");
            return CreateUnavailableAudioSnapshot("audio probe timed out");
        }

        Interlocked.Exchange(ref _audioProbeDisabledUntilUtcTicks, 0);
        try
        {
            return audioProbeTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _log?.Invoke($"Audio activity probe failed: {exception.Message}");
            return CreateUnavailableAudioSnapshot(exception.Message);
        }
    }

    private Task<AudioSourceAttributionSnapshot> GetOrStartAudioProbeTask(double threshold, out bool startedNewProbe)
    {
        lock (_audioProbeTaskGate)
        {
            if (_activeAudioProbeTask is null || _activeAudioProbeTask.IsCompleted)
            {
                _activeAudioProbeTask = Task.Run(() => _audioActivityProbe.Capture(threshold));
                startedNewProbe = true;
                return _activeAudioProbeTask;
            }

            startedNewProbe = false;
            return _activeAudioProbeTask;
        }
    }

    private static AudioSourceAttributionSnapshot CreateUnavailableAudioSnapshot(string statusDetail)
    {
        return new AudioSourceAttributionSnapshot(
            null,
            0d,
            false,
            statusDetail,
            Array.Empty<AudioSourceSessionSnapshot>(),
            null);
    }

    private static IReadOnlyList<string> EnumerateAudioDerivedGoogleMeetTitles(
        MeetingWindowCandidate candidateWindow,
        AudioSourceAttributionSnapshot audioAttribution)
    {
        if (!LooksLikeBrowserWindowClass(candidateWindow.WindowClassName))
        {
            return Array.Empty<string>();
        }

        var candidateProcessName = NormalizeProcessName(candidateWindow.ProcessName);
        if (string.IsNullOrWhiteSpace(candidateProcessName))
        {
            return Array.Empty<string>();
        }

        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in audioAttribution.Sessions)
        {
            if (!session.IsActive || session.IsCurrentProcess || session.IsSystemSounds)
            {
                continue;
            }

            if (!IsBrowserFamilyMatch(candidateProcessName, NormalizeProcessName(session.ProcessName)))
            {
                continue;
            }

            var title = TryExtractGoogleMeetTitleFromSession(session);
            if (string.IsNullOrWhiteSpace(title) || !LooksLikeGoogleMeetWindowTitle(title))
            {
                continue;
            }

            if (seen.Add(title))
            {
                titles.Add(title);
            }
        }

        return titles;
    }

    private static AudioSourceAttributionMatch? MatchAudioSource(
        MeetingWindowCandidate candidateWindow,
        CandidateDetectionTitle detectionTitle,
        AudioSourceAttributionSnapshot audioAttribution)
    {
        if (audioAttribution.Sessions.Count == 0)
        {
            return null;
        }

        AudioSourceAttributionMatch? bestMatch = null;
        foreach (var session in audioAttribution.Sessions)
        {
            if (!session.IsActive || session.IsCurrentProcess || session.IsSystemSounds)
            {
                continue;
            }

            var currentMatch = TryMatchAudioSource(candidateWindow, detectionTitle, session);
            if (currentMatch is null)
            {
                continue;
            }

            if (bestMatch is null || IsBetterAudioMatch(currentMatch, bestMatch))
            {
                bestMatch = currentMatch;
            }
        }

        return bestMatch;
    }

    private static AudioSourceAttributionMatch? TryMatchAudioSource(
        MeetingWindowCandidate candidateWindow,
        CandidateDetectionTitle detectionTitle,
        AudioSourceSessionSnapshot session)
    {
        var sessionProcessName = NormalizeProcessName(session.ProcessName);
        var candidateProcessName = NormalizeProcessName(candidateWindow.ProcessName);
        var isGoogleMeetCandidate = LooksLikeGoogleMeetWindowTitle(detectionTitle.Title);
        var isTeamsCandidate = LooksLikeTeamsMeetingSurface(candidateWindow, detectionTitle.Title);

        if (isGoogleMeetCandidate &&
            LooksLikeBrowserWindowClass(candidateWindow.WindowClassName) &&
            CanMatchGoogleMeetAudioSource(candidateWindow, detectionTitle, session, candidateProcessName, sessionProcessName))
        {
            var matchKind = detectionTitle.FromBrowserTab
                ? AudioSourceMatchKind.BrowserTab
                : AudioSourceMatchKind.BrowserWindow;
            var confidence = HasGoogleMeetSessionMetadata(session, detectionTitle) ||
                candidateWindow.ProcessId == session.ProcessId
                ? AudioSourceConfidence.High
                : AudioSourceConfidence.Medium;
            return BuildAudioMatch(
                candidateWindow,
                detectionTitle,
                session,
                "Google Meet",
                matchKind,
                confidence);
        }

        if (isTeamsCandidate)
        {
            if (candidateWindow.ProcessId > 0 && candidateWindow.ProcessId == session.ProcessId)
            {
                return BuildAudioMatch(
                    candidateWindow,
                    detectionTitle,
                    session,
                    "Microsoft Teams",
                    AudioSourceMatchKind.Window,
                    AudioSourceConfidence.High);
            }

            if (LooksLikeTeamsProcessName(sessionProcessName) &&
                (LooksLikeTeamsProcessName(candidateProcessName) ||
                 string.Equals(candidateWindow.WindowClassName, "TeamsWebView", StringComparison.OrdinalIgnoreCase)))
            {
                return BuildAudioMatch(
                    candidateWindow,
                    detectionTitle,
                    session,
                    "Microsoft Teams",
                    AudioSourceMatchKind.Process,
                    AudioSourceConfidence.Medium);
            }
        }

        if (!isGoogleMeetCandidate &&
            !string.IsNullOrWhiteSpace(candidateProcessName) &&
            string.Equals(candidateProcessName, sessionProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return BuildAudioMatch(
                candidateWindow,
                detectionTitle,
                session,
                candidateWindow.ProcessName,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium);
        }

        return null;
    }

    private static bool ShouldRequireGoogleMeetAudioAttribution(
        bool isGoogleMeetCandidate,
        AudioSourceAttributionSnapshot audioAttribution)
    {
        return isGoogleMeetCandidate &&
            audioAttribution.IsActive &&
            audioAttribution.Sessions.Count > 0;
    }

    private static bool ShouldTrustExplicitGoogleMeetBrowserAudio(
        MeetingWindowCandidate candidateWindow,
        CandidateDetectionTitle detectionTitle,
        AudioSourceAttributionSnapshot audioAttribution)
    {
        if (!audioAttribution.IsActive ||
            detectionTitle.FromBrowserTab ||
            !LooksLikeBrowserWindowClass(candidateWindow.WindowClassName) ||
            !LooksLikeGoogleMeetWindowTitle(candidateWindow.MainWindowTitle) ||
            !string.Equals(candidateWindow.MainWindowTitle, detectionTitle.Title, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateProcessName = NormalizeProcessName(candidateWindow.ProcessName);
        if (string.IsNullOrWhiteSpace(candidateProcessName))
        {
            return false;
        }

        foreach (var session in audioAttribution.Sessions)
        {
            if (!session.IsActive || session.IsCurrentProcess || session.IsSystemSounds)
            {
                continue;
            }

            if (IsBrowserFamilyMatch(candidateProcessName, NormalizeProcessName(session.ProcessName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanMatchGoogleMeetAudioSource(
        MeetingWindowCandidate candidateWindow,
        CandidateDetectionTitle detectionTitle,
        AudioSourceSessionSnapshot session,
        string candidateProcessName,
        string sessionProcessName)
    {
        if (!IsBrowserFamilyMatch(candidateProcessName, sessionProcessName))
        {
            return false;
        }

        if (HasGoogleMeetSessionMetadata(session, detectionTitle))
        {
            return true;
        }

        return !detectionTitle.FromBrowserTab &&
            candidateWindow.ProcessId > 0 &&
            candidateWindow.ProcessId == session.ProcessId &&
            LooksLikeGoogleMeetWindowTitle(candidateWindow.MainWindowTitle);
    }

    private static bool HasGoogleMeetSessionMetadata(
        AudioSourceSessionSnapshot session,
        CandidateDetectionTitle detectionTitle)
    {
        return IsGoogleMeetSessionMetadataMatch(session.DisplayName, detectionTitle.Title) ||
            IsGoogleMeetSessionMetadataMatch(session.SessionIdentifier, detectionTitle.Title);
    }

    private static bool IsGoogleMeetSessionMetadataMatch(string? metadataValue, string detectionTitle)
    {
        if (string.IsNullOrWhiteSpace(metadataValue))
        {
            return false;
        }

        var trimmedMetadata = metadataValue.Trim();
        if (trimmedMetadata.Contains("google meet", StringComparison.OrdinalIgnoreCase) ||
            trimmedMetadata.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedTitle = detectionTitle.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            trimmedMetadata.Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var meetCode = TryExtractGoogleMeetCode(normalizedTitle);
        return !string.IsNullOrWhiteSpace(meetCode) &&
            trimmedMetadata.Contains(meetCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractGoogleMeetCode(string title)
    {
        const string shortTitlePrefix = "Meet -";

        var trimmed = title.Trim();
        if (trimmed.StartsWith(shortTitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = trimmed[shortTitlePrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
        }

        var urlMarker = "meet.google.com/";
        var markerIndex = trimmed.IndexOf(urlMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var codeStart = markerIndex + urlMarker.Length;
        if (codeStart >= trimmed.Length)
        {
            return null;
        }

        var candidateCode = trimmed[codeStart..]
            .Split(['/', '?', '&', ' ', '|'], 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(candidateCode) ? null : candidateCode;
    }

    private static AudioSourceAttributionMatch BuildAudioMatch(
        MeetingWindowCandidate candidateWindow,
        CandidateDetectionTitle detectionTitle,
        AudioSourceSessionSnapshot session,
        string appName,
        AudioSourceMatchKind matchKind,
        AudioSourceConfidence confidence)
    {
        var browserTabTitle = matchKind == AudioSourceMatchKind.BrowserTab
            ? detectionTitle.Title
            : null;
        var windowTitle = matchKind == AudioSourceMatchKind.Process && detectionTitle.FromBrowserTab
            ? candidateWindow.MainWindowTitle
            : detectionTitle.FromBrowserTab && string.IsNullOrWhiteSpace(candidateWindow.MainWindowTitle)
                ? null
                : candidateWindow.MainWindowTitle;
        var source = new DetectedAudioSource(
            appName,
            string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
            browserTabTitle,
            matchKind,
            confidence,
            DateTimeOffset.UtcNow);
        return new AudioSourceAttributionMatch(
            source,
            session.ProcessId,
            session.ProcessName,
            session.PeakLevel);
    }

    private static bool IsBetterAudioMatch(AudioSourceAttributionMatch candidate, AudioSourceAttributionMatch currentBest)
    {
        var candidateScore = GetAudioMatchPriority(candidate.Source.MatchKind, candidate.Source.Confidence);
        var currentScore = GetAudioMatchPriority(currentBest.Source.MatchKind, currentBest.Source.Confidence);
        return candidateScore != currentScore
            ? candidateScore > currentScore
            : candidate.SessionPeakLevel > currentBest.SessionPeakLevel;
    }

    private static int GetAudioMatchPriority(AudioSourceMatchKind matchKind, AudioSourceConfidence confidence)
    {
        var kindScore = matchKind switch
        {
            AudioSourceMatchKind.BrowserTab => 40,
            AudioSourceMatchKind.Window => 35,
            AudioSourceMatchKind.BrowserWindow => 30,
            AudioSourceMatchKind.Process => 20,
            _ => 10,
        };
        var confidenceScore = confidence switch
        {
            AudioSourceConfidence.High => 3,
            AudioSourceConfidence.Medium => 2,
            _ => 1,
        };
        return kindScore + confidenceScore;
    }

    private static string GetAudioSignalSource(AudioSourceMatchKind matchKind)
    {
        return matchKind switch
        {
            AudioSourceMatchKind.BrowserTab => "audio-browser-tab",
            AudioSourceMatchKind.BrowserWindow => "audio-browser-window",
            AudioSourceMatchKind.Window => "audio-window",
            AudioSourceMatchKind.Process => "audio-process",
            _ => "audio-activity",
        };
    }

    private static double GetAudioSignalWeight(AudioSourceMatchKind matchKind, AudioSourceConfidence confidence)
    {
        var baseWeight = matchKind switch
        {
            AudioSourceMatchKind.BrowserTab => 0.35d,
            AudioSourceMatchKind.Window => 0.35d,
            AudioSourceMatchKind.BrowserWindow => 0.25d,
            AudioSourceMatchKind.Process => 0.20d,
            _ => 0.10d,
        };
        return confidence switch
        {
            AudioSourceConfidence.High => baseWeight,
            AudioSourceConfidence.Medium => Math.Max(0.15d, baseWeight - 0.05d),
            _ => 0.10d,
        };
    }

    private static string BuildAudioSignalValue(AudioSourceAttributionMatch match)
    {
        var details = new List<string>
        {
            match.Source.AppName,
        };

        if (!string.IsNullOrWhiteSpace(match.Source.WindowTitle))
        {
            details.Add($"window={match.Source.WindowTitle}");
        }

        if (!string.IsNullOrWhiteSpace(match.Source.BrowserTabTitle))
        {
            details.Add($"tab={match.Source.BrowserTabTitle}");
        }

        if (!string.IsNullOrWhiteSpace(match.MatchedProcessName))
        {
            details.Add($"process={match.MatchedProcessName}");
        }

        details.Add($"peak={match.SessionPeakLevel:0.000}");
        details.Add($"confidence={match.Source.Confidence}");
        return string.Join("; ", details);
    }

    private static string? TryExtractGoogleMeetTitleFromSession(AudioSourceSessionSnapshot session)
    {
        return TryExtractGoogleMeetTitle(session.DisplayName) ??
            TryExtractGoogleMeetTitle(session.SessionIdentifier);
    }

    internal static bool LooksLikeGoogleMeetWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalized = title.Trim().ToLowerInvariant();
        return normalized.Contains("google meet", StringComparison.Ordinal) ||
            normalized.Contains("meet.google.com", StringComparison.Ordinal) ||
            normalized.StartsWith("meet -", StringComparison.Ordinal);
    }

    internal static bool LooksLikeTeamsWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalized = title.Trim().ToLowerInvariant();
        return normalized.Contains("microsoft teams", StringComparison.Ordinal) ||
            normalized.StartsWith("chat |", StringComparison.Ordinal) ||
            normalized.StartsWith("activity |", StringComparison.Ordinal) ||
            normalized.StartsWith("calendar |", StringComparison.Ordinal) ||
            normalized.StartsWith("files |", StringComparison.Ordinal) ||
            normalized.StartsWith("approvals |", StringComparison.Ordinal) ||
            normalized.StartsWith("assignments |", StringComparison.Ordinal) ||
            normalized.StartsWith("calls |", StringComparison.Ordinal) ||
            normalized.StartsWith("search |", StringComparison.Ordinal) ||
            normalized.Contains("meeting compact view", StringComparison.Ordinal) ||
            normalized.Contains("sharing control bar", StringComparison.Ordinal);
    }

    private static bool LooksLikeTeamsMeetingSurface(MeetingWindowCandidate candidateWindow, string title)
    {
        if (LooksLikeTeamsWindowTitle(title))
        {
            return true;
        }

        if (!LooksLikeTeamsProcessName(candidateWindow.ProcessName))
        {
            return false;
        }

        var normalized = title.Trim();
        return !string.IsNullOrWhiteSpace(normalized) &&
            (normalized.Contains('|', StringComparison.Ordinal) ||
             LooksLikeSpecificPlainTeamsMeetingTitle(normalized));
    }

    private static bool LooksLikeSpecificPlainTeamsMeetingTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalized = title.Trim();
        var lower = normalized.ToLowerInvariant();
        if (lower is "microsoft teams" or "teams" or "ms teams" or
            "chat" or "activity" or "calendar" or "files" or "approvals" or
            "assignments" or "calls" or "search" or "people" or "view" or
            "copilot" or "apps" or "more" or "camera")
        {
            return false;
        }

        if (LooksLikeGoogleMeetWindowTitle(normalized) ||
            !normalized.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        if (normalized.Contains(',', StringComparison.Ordinal) ||
            normalized.Contains('&', StringComparison.Ordinal) ||
            normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('-', StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Contains('(', StringComparison.Ordinal) ||
            normalized.Contains(')', StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static bool LooksLikeTeamsProcessName(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized.Equals("ms-teams", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("teams", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("msteams", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserFamilyMatch(string candidateProcessName, string sessionProcessName)
    {
        if (string.IsNullOrWhiteSpace(candidateProcessName) || string.IsNullOrWhiteSpace(sessionProcessName))
        {
            return false;
        }

        return candidateProcessName switch
        {
            "msedge" or "chrome" or "brave" or "vivaldi" or "opera" => string.Equals(candidateProcessName, sessionProcessName, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? TryExtractGoogleMeetTitle(string? metadataValue)
    {
        if (string.IsNullOrWhiteSpace(metadataValue))
        {
            return null;
        }

        var trimmedMetadata = metadataValue.Trim();
        if (trimmedMetadata.StartsWith("Meet -", StringComparison.OrdinalIgnoreCase) ||
            trimmedMetadata.Contains("google meet", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedMetadata;
        }

        var meetCode = TryExtractGoogleMeetCode(trimmedMetadata);
        return string.IsNullOrWhiteSpace(meetCode)
            ? null
            : $"Meet - {meetCode}";
    }

    private static string NormalizeProcessName(string? processName)
    {
        return string.IsNullOrWhiteSpace(processName)
            ? string.Empty
            : processName.Trim();
    }

    private static bool IsAmbiguousPlainTeamsContentWindow(DetectionDecision candidate)
    {
        if (candidate.Platform != MeetingPlatform.Teams)
        {
            return false;
        }

        foreach (var signal in candidate.Signals)
        {
            if (!string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedTitle = signal.Value.Trim();
            if (normalizedTitle.StartsWith("Chat |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.StartsWith("Activity |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.StartsWith("Calendar |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.StartsWith("Files |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.StartsWith("Calls |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.StartsWith("Search |", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.Contains("Meeting compact view", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.Contains("Sharing control bar", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (normalizedTitle.EndsWith("| Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
                normalizedTitle.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMatchingSuppressedTeamsChatCandidate(
        DetectionDecision candidate,
        IReadOnlyList<DetectionDecision> allCandidates)
    {
        var normalizedCandidateTitle = MeetingTitleNormalizer.NormalizeForComparison(candidate.SessionTitle);
        if (string.IsNullOrWhiteSpace(normalizedCandidateTitle))
        {
            return false;
        }

        foreach (var other in allCandidates)
        {
            if (ReferenceEquals(other, candidate) || other.Platform != MeetingPlatform.Teams)
            {
                continue;
            }

            foreach (var signal in other.Signals)
            {
                if (!string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!signal.Value.TrimStart().StartsWith("Chat |", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedOtherTitle = MeetingTitleNormalizer.NormalizeForComparison(other.SessionTitle);
                if (string.Equals(normalizedCandidateTitle, normalizedOtherTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasTeamsRenderEvidence(DetectionDecision candidate)
    {
        if (candidate.Platform != MeetingPlatform.Teams)
        {
            return false;
        }

        if (candidate.DetectedAudioSource is
            {
                AppName: var appName,
                MatchKind: not AudioSourceMatchKind.EndpointFallback,
            } &&
            string.Equals(appName, "Microsoft Teams", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var signal in candidate.Signals)
        {
            if (!signal.Source.StartsWith("audio-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!signal.Value.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return signal.Source.Equals("audio-session-match", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("audio-window", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("audio-process", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("audio-browser-window", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("audio-browser-tab", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool HasUnavailableAudioProbeSignal(DetectionDecision candidate)
    {
        foreach (var signal in candidate.Signals)
        {
            if (!signal.Source.StartsWith("audio-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (signal.Value.Contains("audio probe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static CandidatePriority GetCandidatePriority(DetectionDecision decision)
    {
        var specificityScore = 0d;
        if (HasSpecificSessionTitle(decision))
        {
            specificityScore += 1d;
        }

        if (HasBrowserMeetingEvidence(decision))
        {
            specificityScore += 1d;
        }

        if (decision.DetectedAudioSource is { } detectedAudioSource)
        {
            specificityScore += detectedAudioSource.MatchKind switch
            {
                AudioSourceMatchKind.BrowserTab => 1.5d,
                AudioSourceMatchKind.Window => 1.25d,
                AudioSourceMatchKind.BrowserWindow => 1d,
                AudioSourceMatchKind.Process => 0.75d,
                _ => 0.5d,
            };
        }

        if (IsGenericShellTitle(decision))
        {
            specificityScore -= 1d;
        }

        return new CandidatePriority(
            decision.ShouldStart ? 1 : 0,
            decision.Confidence,
            specificityScore,
            decision.Signals.Count);
    }

    private static bool HasSpecificSessionTitle(DetectionDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.SessionTitle))
        {
            return false;
        }

        var normalized = decision.SessionTitle.Trim();
        return decision.Platform switch
        {
            MeetingPlatform.Teams => !normalized.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Equals("ms-teams", StringComparison.OrdinalIgnoreCase),
            MeetingPlatform.GoogleMeet => !normalized.Equals("Google Meet", StringComparison.OrdinalIgnoreCase),
            _ => !normalized.Equals("Detected meeting", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool HasBrowserMeetingEvidence(DetectionDecision decision)
    {
        if (decision.Platform != MeetingPlatform.GoogleMeet)
        {
            return false;
        }

        foreach (var signal in decision.Signals)
        {
            if (signal.Source.Equals("browser-window", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("browser-tab", StringComparison.OrdinalIgnoreCase) ||
                signal.Source.Equals("browser-url", StringComparison.OrdinalIgnoreCase) ||
                signal.Value.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericShellTitle(DetectionDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.SessionTitle))
        {
            return true;
        }

        var normalized = decision.SessionTitle.Trim();
        return decision.Platform switch
        {
            MeetingPlatform.Teams => normalized.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("ms-teams", StringComparison.OrdinalIgnoreCase),
            MeetingPlatform.GoogleMeet => normalized.Equals("Google Meet", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string TryGetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

internal readonly record struct CandidatePriority(
    int ShouldStartScore,
    double Confidence,
    double SpecificityScore,
    int SignalCount);

internal interface IAudioActivityProbe
{
    AudioSourceAttributionSnapshot Capture(double threshold);
}

internal sealed class SystemAudioActivityProbe : IAudioActivityProbe
{
    private readonly Func<DataFlow, Role, double, AudioSourceAttributionSnapshot> _captureDefaultEndpoint;

    public SystemAudioActivityProbe()
        : this(AudioActivityProbeSupport.CaptureDefaultEndpoint)
    {
    }

    internal SystemAudioActivityProbe(Func<DataFlow, Role, double, AudioSourceAttributionSnapshot> captureDefaultEndpoint)
    {
        _captureDefaultEndpoint = captureDefaultEndpoint ?? throw new ArgumentNullException(nameof(captureDefaultEndpoint));
    }

    public AudioSourceAttributionSnapshot Capture(double threshold)
    {
        var multimediaSnapshot = _captureDefaultEndpoint(DataFlow.Render, Role.Multimedia, threshold);
        var communicationsSnapshot = _captureDefaultEndpoint(DataFlow.Render, Role.Communications, threshold);
        return AudioActivityProbeSupport.MergeRenderSnapshots(multimediaSnapshot, communicationsSnapshot);
    }
}

internal sealed class SystemMicrophoneActivityProbe : IAudioActivityProbe
{
    public AudioSourceAttributionSnapshot Capture(double threshold)
    {
        var communicationsSnapshot = AudioActivityProbeSupport.CaptureDefaultEndpoint(DataFlow.Capture, Role.Communications, threshold);
        if (!string.IsNullOrWhiteSpace(communicationsSnapshot.DeviceName) ||
            string.IsNullOrWhiteSpace(communicationsSnapshot.StatusDetail))
        {
            return communicationsSnapshot;
        }

        return AudioActivityProbeSupport.CaptureDefaultEndpoint(DataFlow.Capture, Role.Multimedia, threshold);
    }
}

internal static class AudioActivityProbeSupport
{
    internal static AudioSourceAttributionSnapshot MergeRenderSnapshots(
        AudioSourceAttributionSnapshot multimediaSnapshot,
        AudioSourceAttributionSnapshot communicationsSnapshot)
    {
        ArgumentNullException.ThrowIfNull(multimediaSnapshot);
        ArgumentNullException.ThrowIfNull(communicationsSnapshot);

        var preferredSnapshot = ShouldPreferRenderSnapshot(communicationsSnapshot, multimediaSnapshot)
            ? communicationsSnapshot
            : multimediaSnapshot;
        var mergedSessions = MergeSessions(multimediaSnapshot.Sessions, communicationsSnapshot.Sessions);
        var mergedEndpointPeakLevel = Math.Max(multimediaSnapshot.EndpointPeakLevel, communicationsSnapshot.EndpointPeakLevel);
        var mergedIsEndpointActive = multimediaSnapshot.IsEndpointActive || communicationsSnapshot.IsEndpointActive;
        var mergedStatusDetail = !string.IsNullOrWhiteSpace(preferredSnapshot.StatusDetail)
            ? preferredSnapshot.StatusDetail
            : string.IsNullOrWhiteSpace(multimediaSnapshot.StatusDetail)
                ? communicationsSnapshot.StatusDetail
                : multimediaSnapshot.StatusDetail;
        var mergedMatch = preferredSnapshot.Match ?? multimediaSnapshot.Match ?? communicationsSnapshot.Match;

        return new AudioSourceAttributionSnapshot(
            preferredSnapshot.DeviceName,
            mergedEndpointPeakLevel,
            mergedIsEndpointActive,
            mergedStatusDetail,
            mergedSessions,
            mergedMatch);
    }

    public static AudioSourceAttributionSnapshot CaptureDefaultEndpoint(DataFlow flow, Role role, double threshold)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            var peakLevel = device.AudioMeterInformation.MasterPeakValue;
            var sessions = CaptureSessions(device, threshold);
            return new AudioSourceAttributionSnapshot(
                device.FriendlyName,
                peakLevel,
                peakLevel >= threshold,
                peakLevel >= threshold ? "active" : "below-threshold",
                sessions,
                null);
        }
        catch (Exception exception)
        {
            return new AudioSourceAttributionSnapshot(
                null,
                0d,
                false,
                exception.Message,
                Array.Empty<AudioSourceSessionSnapshot>(),
                null);
        }
    }

    private static IReadOnlyList<AudioSourceSessionSnapshot> CaptureSessions(MMDevice device, double threshold)
    {
        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            var results = new List<AudioSourceSessionSnapshot>(sessions.Count);
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                var processId = unchecked((int)session.GetProcessID);
                var processName = TryGetSessionProcessName(processId);
                var peakLevel = session.AudioMeterInformation.MasterPeakValue;
                var isSystemSounds = TryGetSystemSoundsFlag(session);
                var isCurrentProcess = processId == Environment.ProcessId;
                var displayName = TryGetSessionDisplayName(session);
                var sessionIdentifier = TryGetSessionIdentifier(session);
                var stateText = TryGetSessionStateText(session);
                var isActive = peakLevel >= threshold ||
                    string.Equals(stateText, "AudioSessionStateActive", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(stateText, "Active", StringComparison.OrdinalIgnoreCase);

                results.Add(new AudioSourceSessionSnapshot(
                    processId,
                    processName,
                    peakLevel,
                    isActive,
                    isSystemSounds,
                    isCurrentProcess,
                    displayName,
                    sessionIdentifier));
            }

            return results;
        }
        catch
        {
            return Array.Empty<AudioSourceSessionSnapshot>();
        }
    }

    private static string TryGetSessionProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryGetSystemSoundsFlag(AudioSessionControl session)
    {
        try
        {
            return session.IsSystemSoundsSession;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetSessionDisplayName(AudioSessionControl session)
    {
        try
        {
            return string.IsNullOrWhiteSpace(session.DisplayName)
                ? null
                : session.DisplayName.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSessionIdentifier(AudioSessionControl session)
    {
        try
        {
            return string.IsNullOrWhiteSpace(session.GetSessionIdentifier)
                ? null
                : session.GetSessionIdentifier;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSessionStateText(AudioSessionControl session)
    {
        try
        {
            return session.State.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldPreferRenderSnapshot(
        AudioSourceAttributionSnapshot candidate,
        AudioSourceAttributionSnapshot current)
    {
        var candidateActiveSessionCount = CountRelevantActiveSessions(candidate);
        var currentActiveSessionCount = CountRelevantActiveSessions(current);
        if (candidateActiveSessionCount != currentActiveSessionCount)
        {
            return candidateActiveSessionCount > currentActiveSessionCount;
        }

        if (candidate.IsEndpointActive != current.IsEndpointActive)
        {
            return candidate.IsEndpointActive;
        }

        if (Math.Abs(candidate.EndpointPeakLevel - current.EndpointPeakLevel) > 0.0001d)
        {
            return candidate.EndpointPeakLevel > current.EndpointPeakLevel;
        }

        if (candidate.Match is not null && current.Match is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.DeviceName) && string.IsNullOrWhiteSpace(current.DeviceName))
        {
            return true;
        }

        return false;
    }

    private static int CountRelevantActiveSessions(AudioSourceAttributionSnapshot snapshot)
    {
        var count = 0;
        foreach (var session in snapshot.Sessions)
        {
            if (session.IsActive && !session.IsCurrentProcess && !session.IsSystemSounds)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<AudioSourceSessionSnapshot> MergeSessions(
        IReadOnlyList<AudioSourceSessionSnapshot> first,
        IReadOnlyList<AudioSourceSessionSnapshot> second)
    {
        var merged = new List<AudioSourceSessionSnapshot>(first.Count + second.Count);

        foreach (var session in first)
        {
            merged.Add(session);
        }

        foreach (var session in second)
        {
            var replaced = false;
            for (var index = 0; index < merged.Count; index++)
            {
                if (!AreEquivalentSessions(merged[index], session))
                {
                    continue;
                }

                if (ShouldPreferSession(session, merged[index]))
                {
                    merged[index] = session;
                }

                replaced = true;
                break;
            }

            if (!replaced)
            {
                merged.Add(session);
            }
        }

        return merged;
    }

    private static bool AreEquivalentSessions(AudioSourceSessionSnapshot left, AudioSourceSessionSnapshot right)
    {
        return left.ProcessId == right.ProcessId &&
            string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.SessionIdentifier, right.SessionIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPreferSession(AudioSourceSessionSnapshot candidate, AudioSourceSessionSnapshot current)
    {
        if (candidate.IsActive != current.IsActive)
        {
            return candidate.IsActive;
        }

        if (Math.Abs(candidate.PeakLevel - current.PeakLevel) > 0.0001d)
        {
            return candidate.PeakLevel > current.PeakLevel;
        }

        if (!string.IsNullOrWhiteSpace(candidate.SessionIdentifier) &&
            string.IsNullOrWhiteSpace(current.SessionIdentifier))
        {
            return true;
        }

        return false;
    }
}
