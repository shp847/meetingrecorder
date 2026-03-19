using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace MeetingRecorder.App.Services;

internal sealed class WindowMeetingDetector
{
    private readonly LiveAppConfig _config;
    private readonly MeetingDetectionEvaluator _evaluator;
    private readonly IAudioActivityProbe _audioActivityProbe;
    private readonly MeetingTitleEnricher _meetingTitleEnricher;

    public WindowMeetingDetector(
        LiveAppConfig config,
        MeetingDetectionEvaluator evaluator,
        IAudioActivityProbe audioActivityProbe,
        MeetingTitleEnricher meetingTitleEnricher)
    {
        _config = config;
        _evaluator = evaluator;
        _audioActivityProbe = audioActivityProbe;
        _meetingTitleEnricher = meetingTitleEnricher;
    }

    public DetectionDecision? DetectBestCandidate()
    {
        var bestCandidate = default(DetectionDecision);
        var audioActivity = _audioActivityProbe.Capture(_config.Current.AutoDetectAudioPeakThreshold);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }

                var signals = BuildSignals(process, audioActivity);
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
                        ? process.MainWindowTitle
                        : decision.SessionTitle,
                };
                decision = _meetingTitleEnricher.Enrich(
                    decision,
                    _config.Current.CalendarTitleFallbackEnabled,
                    decisionTimestamp);

                if (bestCandidate is null || IsBetterCandidate(decision, bestCandidate))
                {
                    bestCandidate = decision;
                }
            }
            catch
            {
                // Some managed processes throw when their main window data is not accessible.
            }
            finally
            {
                process.Dispose();
            }
        }

        return bestCandidate;
    }

    internal static bool IsBetterCandidate(DetectionDecision candidate, DetectionDecision currentBest)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(currentBest);

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

    private static IReadOnlyList<DetectionSignal> BuildSignals(Process process, AudioActivitySnapshot audioActivity)
    {
        var title = process.MainWindowTitle;
        var now = DateTimeOffset.UtcNow;
        var signals = new List<DetectionSignal>();
        var lowerTitle = title.ToLowerInvariant();
        var lowerProcessName = process.ProcessName.ToLowerInvariant();

        if (lowerTitle.Contains("google meet", StringComparison.Ordinal) ||
            lowerTitle.Contains("meet -", StringComparison.Ordinal) ||
            lowerTitle.Contains("meet ", StringComparison.Ordinal))
        {
            signals.Add(new DetectionSignal("window-title", title, 0.85d, now));
        }

        if (lowerProcessName.Contains("chrome", StringComparison.Ordinal) ||
            lowerProcessName.Contains("msedge", StringComparison.Ordinal))
        {
            if (lowerTitle.Contains("meet", StringComparison.Ordinal))
            {
                signals.Add(new DetectionSignal("browser-window", title, 0.15d, now));
            }
        }

        if (lowerTitle.Contains("microsoft teams", StringComparison.Ordinal) ||
            lowerTitle.Contains("teams", StringComparison.Ordinal))
        {
            signals.Add(new DetectionSignal("window-title", title, 0.85d, now));
        }

        if (lowerProcessName.Contains("teams", StringComparison.Ordinal))
        {
            signals.Add(new DetectionSignal("process-name", process.ProcessName, 0.15d, now));
        }

        var audioSource = audioActivity.IsActive ? "audio-activity" : "audio-silence";
        var audioWeight = audioActivity.IsActive ? 0.2d : 0d;
        var audioValue = string.IsNullOrWhiteSpace(audioActivity.DeviceName)
            ? $"peak={audioActivity.PeakLevel:0.000}; status={audioActivity.StatusDetail ?? "unknown"}"
            : $"{audioActivity.DeviceName}; peak={audioActivity.PeakLevel:0.000}; status={audioActivity.StatusDetail ?? "ok"}";
        signals.Add(new DetectionSignal(audioSource, audioValue, audioWeight, now));

        return signals;
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
}

internal readonly record struct CandidatePriority(
    int ShouldStartScore,
    double Confidence,
    double SpecificityScore,
    int SignalCount);

internal interface IAudioActivityProbe
{
    AudioActivitySnapshot Capture(double threshold);
}

internal sealed record AudioActivitySnapshot(
    string? DeviceName,
    double PeakLevel,
    bool IsActive,
    string? StatusDetail);

internal sealed class SystemAudioActivityProbe : IAudioActivityProbe
{
    public AudioActivitySnapshot Capture(double threshold)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var peakLevel = device.AudioMeterInformation.MasterPeakValue;
            return new AudioActivitySnapshot(
                device.FriendlyName,
                peakLevel,
                peakLevel >= threshold,
                peakLevel >= threshold ? "active" : "below-threshold");
        }
        catch (Exception exception)
        {
            return new AudioActivitySnapshot(null, 0d, false, exception.Message);
        }
    }
}
