using MeetingRecorder.Core.Domain;
using System.Globalization;

namespace MeetingRecorder.Core.Services;

public sealed record DiarizationClusterCandidate(
    float Threshold,
    IReadOnlyList<SpeakerTurn> SpeakerTurns);

public sealed record DiarizationClusterSelection(
    DiarizationClusterCandidate Candidate,
    int SupportedSpeakerCount,
    string DiagnosticMessage);

public sealed class DiarizationClusterSelectionService
{
    private static readonly TimeSpan AbsoluteMinimumSpeakerDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumMinimumSpeakerDuration = TimeSpan.FromSeconds(15);
    private const double MinimumSpeakerDurationShare = 0.005d;
    private const int MinimumMultiSpeakerCount = 2;
    private const int MaximumAutomaticSpeakerCount = 16;

    public DiarizationClusterSelection SelectBestCandidate(IReadOnlyList<DiarizationClusterCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one diarization cluster candidate is required.", nameof(candidates));
        }

        var defaultCandidate = candidates[0];
        var defaultSupportedSpeakerCount = CountSupportedSpeakers(defaultCandidate);
        if (IsSupportedMultiSpeakerCount(defaultSupportedSpeakerCount))
        {
            return BuildSelection(defaultCandidate, defaultSupportedSpeakerCount, defaultCandidate);
        }

        foreach (var candidate in candidates.Skip(1))
        {
            var supportedSpeakerCount = CountSupportedSpeakers(candidate);
            if (IsSupportedMultiSpeakerCount(supportedSpeakerCount))
            {
                return BuildSelection(candidate, supportedSpeakerCount, defaultCandidate);
            }
        }

        return BuildSelection(defaultCandidate, defaultSupportedSpeakerCount, defaultCandidate);
    }

    public int CountSupportedSpeakers(DiarizationClusterCandidate candidate)
    {
        var speakerDurations = candidate.SpeakerTurns
            .Where(static turn => turn.End > turn.Start && !string.IsNullOrWhiteSpace(turn.SpeakerId))
            .GroupBy(static turn => turn.SpeakerId, StringComparer.Ordinal)
            .Select(static group => new
            {
                SpeakerId = group.Key,
                Duration = TimeSpan.FromTicks(group.Sum(turn => (turn.End - turn.Start).Ticks)),
            })
            .ToArray();

        if (speakerDurations.Length == 0)
        {
            return 0;
        }

        var totalDurationTicks = speakerDurations.Sum(speaker => speaker.Duration.Ticks);
        if (totalDurationTicks <= 0)
        {
            return 0;
        }

        var minimumSupportedDuration = CalculateMinimumSupportedDuration(TimeSpan.FromTicks(totalDurationTicks));
        return speakerDurations.Count(speaker => speaker.Duration >= minimumSupportedDuration);
    }

    private static bool IsSupportedMultiSpeakerCount(int speakerCount)
    {
        return speakerCount is >= MinimumMultiSpeakerCount and <= MaximumAutomaticSpeakerCount;
    }

    private static TimeSpan CalculateMinimumSupportedDuration(TimeSpan totalVoicedDuration)
    {
        var proportionalMinimum = TimeSpan.FromTicks((long)(totalVoicedDuration.Ticks * MinimumSpeakerDurationShare));
        var cappedMinimum = proportionalMinimum < MaximumMinimumSpeakerDuration
            ? proportionalMinimum
            : MaximumMinimumSpeakerDuration;
        return cappedMinimum > AbsoluteMinimumSpeakerDuration
            ? cappedMinimum
            : AbsoluteMinimumSpeakerDuration;
    }

    private static DiarizationClusterSelection BuildSelection(
        DiarizationClusterCandidate selectedCandidate,
        int supportedSpeakerCount,
        DiarizationClusterCandidate defaultCandidate)
    {
        var selectedThreshold = FormatThreshold(selectedCandidate.Threshold);
        var speakerText = supportedSpeakerCount == 1 ? "speaker" : "speakers";
        var diagnosticMessage = ReferenceEquals(selectedCandidate, defaultCandidate)
            ? $"Speaker clustering used threshold {selectedThreshold} and detected {supportedSpeakerCount} supported {speakerText}."
            : $"Speaker clustering adapted from threshold {FormatThreshold(defaultCandidate.Threshold)} to {selectedThreshold} and detected {supportedSpeakerCount} supported {speakerText}.";

        return new DiarizationClusterSelection(selectedCandidate, supportedSpeakerCount, diagnosticMessage);
    }

    private static string FormatThreshold(float threshold)
    {
        return threshold.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
