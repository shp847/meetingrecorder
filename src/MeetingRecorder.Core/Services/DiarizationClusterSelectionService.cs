using MeetingRecorder.Core.Domain;
using System.Globalization;

namespace MeetingRecorder.Core.Services;

public sealed record DiarizationClusterCandidate(
    float Threshold,
    IReadOnlyList<SpeakerTurn> SpeakerTurns);

public sealed record DiarizationClusterSelection(
    DiarizationClusterCandidate Candidate,
    int SupportedSpeakerCount,
    string DiagnosticMessage,
    bool IsAutomaticSpeakerCountSupported);

public sealed class DiarizationClusterSelectionService
{
    public const int MinimumAutomaticSpeakerCount = 2;
    public const int MaximumAutomaticSpeakerCount = 16;

    public DiarizationClusterSelection SelectBestCandidate(IReadOnlyList<DiarizationClusterCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one diarization cluster candidate is required.", nameof(candidates));
        }

        var options = DiarizationCalibrationEnvironment.LoadClusterSelectionOptions();
        var evaluations = candidates
            .Select((candidate, index) => EvaluateCandidate(candidate, index, options))
            .ToArray();
        var defaultEvaluation = evaluations[0];
        if (defaultEvaluation.IsAutomaticSpeakerCountSupported &&
            defaultEvaluation.SupportedSpeakerCount <= options.MaximumPreferredAutomaticSpeakerCount &&
            defaultEvaluation.UnsupportedSpeakerCount == 0)
        {
            return BuildSelection(defaultEvaluation, defaultEvaluation);
        }

        if (defaultEvaluation.SupportedSpeakerCount < MinimumAutomaticSpeakerCount)
        {
            var collapsedRecovery = evaluations
                .Skip(1)
                .FirstOrDefault(static evaluation => evaluation.IsAutomaticSpeakerCountSupported);
            return collapsedRecovery is null
                ? BuildSelection(defaultEvaluation, defaultEvaluation)
                : BuildSelection(collapsedRecovery, defaultEvaluation);
        }

        var preferredRecovery = evaluations
            .Where(static evaluation =>
                evaluation.IsAutomaticSpeakerCountSupported)
            .Where(evaluation => evaluation.SupportedSpeakerCount <= options.MaximumPreferredAutomaticSpeakerCount)
            .OrderBy(static evaluation => evaluation.UnsupportedSpeakerCount)
            .ThenBy(static evaluation => evaluation.SupportedSpeakerCount)
            .ThenBy(static evaluation => evaluation.RawSpeakerCount)
            .ThenBy(static evaluation => evaluation.Index)
            .FirstOrDefault();
        if (preferredRecovery is not null)
        {
            return BuildSelection(preferredRecovery, defaultEvaluation);
        }

        if (defaultEvaluation.IsAutomaticSpeakerCountSupported)
        {
            return BuildSelection(defaultEvaluation, defaultEvaluation);
        }

        return BuildSelection(defaultEvaluation, defaultEvaluation);
    }

    public int CountSupportedSpeakers(DiarizationClusterCandidate candidate)
    {
        return GetSupportedSpeakerIds(candidate, DiarizationCalibrationEnvironment.LoadClusterSelectionOptions()).Count;
    }

    public static bool IsAutomaticSpeakerCountSupported(int speakerCount)
    {
        return speakerCount is >= MinimumAutomaticSpeakerCount and <= MaximumAutomaticSpeakerCount;
    }

    private static CandidateEvaluation EvaluateCandidate(
        DiarizationClusterCandidate candidate,
        int index,
        DiarizationClusterSelectionOptions options)
    {
        var rawSpeakerCount = CountRawSpeakers(candidate);
        var supportedSpeakerIds = GetSupportedSpeakerIds(candidate, options);
        return new CandidateEvaluation(
            candidate,
            index,
            rawSpeakerCount,
            supportedSpeakerIds);
    }

    private static int CountRawSpeakers(DiarizationClusterCandidate candidate)
    {
        return candidate.SpeakerTurns
            .Select(static turn => turn.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static IReadOnlySet<string> GetSupportedSpeakerIds(
        DiarizationClusterCandidate candidate,
        DiarizationClusterSelectionOptions options)
    {
        var speakerDurations = candidate.SpeakerTurns
            .Where(static turn => turn.End > turn.Start && !string.IsNullOrWhiteSpace(turn.SpeakerId))
            .GroupBy(static turn => turn.SpeakerId, StringComparer.Ordinal)
            .Select(static group => (
                SpeakerId: group.Key,
                Duration: TimeSpan.FromTicks(group.Sum(turn => (turn.End - turn.Start).Ticks))))
            .ToArray();

        if (speakerDurations.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var totalDurationTicks = speakerDurations.Sum(speaker => speaker.Duration.Ticks);
        if (totalDurationTicks <= 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var minimumSupportedDuration = CalculateMinimumSupportedDuration(TimeSpan.FromTicks(totalDurationTicks), options);
        return speakerDurations
            .Where(speaker => speaker.Duration >= minimumSupportedDuration)
            .Select(static speaker => speaker.SpeakerId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static TimeSpan CalculateMinimumSupportedDuration(
        TimeSpan totalVoicedDuration,
        DiarizationClusterSelectionOptions options)
    {
        var proportionalMinimum = TimeSpan.FromTicks((long)(totalVoicedDuration.Ticks * options.MinimumSpeakerDurationShare));
        var cappedMinimum = proportionalMinimum < options.MaximumMinimumSpeakerDuration
            ? proportionalMinimum
            : options.MaximumMinimumSpeakerDuration;
        return cappedMinimum > options.AbsoluteMinimumSpeakerDuration
            ? cappedMinimum
            : options.AbsoluteMinimumSpeakerDuration;
    }

    private static DiarizationClusterSelection BuildSelection(
        CandidateEvaluation selectedEvaluation,
        CandidateEvaluation defaultEvaluation)
    {
        var selectedCandidate = FilterToSupportedSpeakerTurns(
            selectedEvaluation.Candidate,
            selectedEvaluation.SupportedSpeakerIds);
        var selectedThreshold = FormatThreshold(selectedEvaluation.Candidate.Threshold);
        var supportedSpeakerCount = selectedEvaluation.SupportedSpeakerCount;
        var speakerText = supportedSpeakerCount == 1 ? "speaker" : "speakers";
        var isSupported = IsAutomaticSpeakerCountSupported(supportedSpeakerCount);
        var rangeMessage = isSupported
            ? string.Empty
            : $" This is outside the supported automatic range of {MinimumAutomaticSpeakerCount}-{MaximumAutomaticSpeakerCount} speakers.";
        var diagnosticMessage = ReferenceEquals(selectedEvaluation.Candidate, defaultEvaluation.Candidate)
            ? $"Speaker clustering used threshold {selectedThreshold} and detected {supportedSpeakerCount} supported {speakerText}."
            : $"Speaker clustering adapted from threshold {FormatThreshold(defaultEvaluation.Candidate.Threshold)} to {selectedThreshold} and detected {supportedSpeakerCount} supported {speakerText}.";

        return new DiarizationClusterSelection(selectedCandidate, supportedSpeakerCount, diagnosticMessage + rangeMessage, isSupported);
    }

    private static DiarizationClusterCandidate FilterToSupportedSpeakerTurns(
        DiarizationClusterCandidate candidate,
        IReadOnlySet<string> supportedSpeakerIds)
    {
        if (supportedSpeakerIds.Count == 0 || supportedSpeakerIds.Count == CountRawSpeakers(candidate))
        {
            return candidate;
        }

        var filteredSpeakerTurns = candidate.SpeakerTurns
            .Where(turn => !string.IsNullOrWhiteSpace(turn.SpeakerId) && supportedSpeakerIds.Contains(turn.SpeakerId))
            .ToArray();
        return filteredSpeakerTurns.Length == candidate.SpeakerTurns.Count
            ? candidate
            : new DiarizationClusterCandidate(candidate.Threshold, filteredSpeakerTurns);
    }

    private static string FormatThreshold(float threshold)
    {
        return threshold.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private sealed record CandidateEvaluation(
        DiarizationClusterCandidate Candidate,
        int Index,
        int RawSpeakerCount,
        IReadOnlySet<string> SupportedSpeakerIds)
    {
        public int SupportedSpeakerCount => SupportedSpeakerIds.Count;

        public int UnsupportedSpeakerCount => RawSpeakerCount - SupportedSpeakerCount;

        public bool IsAutomaticSpeakerCountSupported =>
            DiarizationClusterSelectionService.IsAutomaticSpeakerCountSupported(SupportedSpeakerCount);
    }
}
