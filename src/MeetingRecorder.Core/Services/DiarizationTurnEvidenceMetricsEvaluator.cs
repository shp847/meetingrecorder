namespace MeetingRecorder.Core.Services;

/// <summary>
/// An opaque, timestamp-only speaker turn used by private fixture evidence.
/// Speaker ids must be fixture-local tokens; never pass source transcript labels here.
/// </summary>
public sealed record DiarizationEvidenceTurn(string SpeakerId, TimeSpan Start, TimeSpan End);

public sealed record DiarizationTurnEvidenceMetrics(
    int ExpectedSpeakerCount,
    int DetectedSpeakerCount,
    int ExpectedTurnCount,
    int DetectedTurnCount,
    long AlignedMilliseconds,
    long ExpectedSpeechMilliseconds,
    long DetectedSpeechMilliseconds,
    int OverSegmentationCount,
    int UnderSegmentationCount,
    string Status)
{
    public double ExpectedCoverage => ExpectedSpeechMilliseconds == 0
        ? 0d
        : (double)AlignedMilliseconds / ExpectedSpeechMilliseconds;

    public double DetectedCoverage => DetectedSpeechMilliseconds == 0
        ? 0d
        : (double)AlignedMilliseconds / DetectedSpeechMilliseconds;
}

/// <summary>
/// Scores diarization against restricted, opaque turn truth after a fixture worker has finished.
/// This evaluator has no audio, transcript text, names, or production configuration inputs.
/// </summary>
public static class DiarizationTurnEvidenceMetricsEvaluator
{
    private const int MaximumTruthSpeakerCount = 16;

    public static DiarizationTurnEvidenceMetrics Evaluate(
        IEnumerable<DiarizationEvidenceTurn>? expectedTurns,
        IEnumerable<DiarizationEvidenceTurn>? detectedTurns)
    {
        var expected = Normalize(expectedTurns);
        var detected = Normalize(detectedTurns);
        var expectedSpeakers = expected.Select(static turn => turn.SpeakerId).Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        var detectedSpeakers = detected.Select(static turn => turn.SpeakerId).Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal).ToArray();

        if (expectedSpeakers.Length > MaximumTruthSpeakerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedTurns), $"Fixture truth may contain at most {MaximumTruthSpeakerCount} speakers.");
        }

        var expectedMilliseconds = SumMilliseconds(expected);
        var detectedMilliseconds = SumMilliseconds(detected);
        var alignedMilliseconds = FindMaximumAlignedMilliseconds(expected, detected, expectedSpeakers, detectedSpeakers);
        var status = expected.Count == 0
            ? "not_asserted"
            : detected.Count == 0
                ? "missing_detected_turns"
                : "completed";

        return new DiarizationTurnEvidenceMetrics(
            expectedSpeakers.Length,
            detectedSpeakers.Length,
            expected.Count,
            detected.Count,
            alignedMilliseconds,
            expectedMilliseconds,
            detectedMilliseconds,
            Math.Max(0, detectedSpeakers.Length - expectedSpeakers.Length),
            Math.Max(0, expectedSpeakers.Length - detectedSpeakers.Length),
            status);
    }

    private static IReadOnlyList<DiarizationEvidenceTurn> Normalize(IEnumerable<DiarizationEvidenceTurn>? turns) =>
        (turns ?? Array.Empty<DiarizationEvidenceTurn>())
            .Where(static turn => !string.IsNullOrWhiteSpace(turn.SpeakerId) && turn.End > turn.Start)
            .OrderBy(static turn => turn.Start)
            .ThenBy(static turn => turn.End)
            .ThenBy(static turn => turn.SpeakerId, StringComparer.Ordinal)
            .ToArray();

    private static long FindMaximumAlignedMilliseconds(
        IReadOnlyList<DiarizationEvidenceTurn> expected,
        IReadOnlyList<DiarizationEvidenceTurn> detected,
        IReadOnlyList<string> expectedSpeakers,
        IReadOnlyList<string> detectedSpeakers)
    {
        if (expectedSpeakers.Count == 0 || detectedSpeakers.Count == 0)
        {
            return 0;
        }

        var overlap = new long[detectedSpeakers.Count, expectedSpeakers.Count];
        for (var detectedIndex = 0; detectedIndex < detectedSpeakers.Count; detectedIndex++)
        {
            foreach (var detectedTurn in detected.Where(turn => turn.SpeakerId == detectedSpeakers[detectedIndex]))
            {
                for (var expectedIndex = 0; expectedIndex < expectedSpeakers.Count; expectedIndex++)
                {
                    foreach (var expectedTurn in expected.Where(turn => turn.SpeakerId == expectedSpeakers[expectedIndex]))
                    {
                        overlap[detectedIndex, expectedIndex] += OverlapMilliseconds(expectedTurn, detectedTurn);
                    }
                }
            }
        }

        // Multiple detected clusters may legitimately map to one truth speaker: that is the
        // evidence of fragmentation this metric must retain. Each observed cluster therefore
        // takes its best opaque truth overlap independently; count metrics report the split.
        return Enumerable.Range(0, detectedSpeakers.Count)
            .Sum(detectedIndex => Enumerable.Range(0, expectedSpeakers.Count)
                .Select(expectedIndex => overlap[detectedIndex, expectedIndex])
                .Max());
    }

    private static long SumMilliseconds(IEnumerable<DiarizationEvidenceTurn> turns) =>
        turns.Sum(static turn => (long)Math.Round((turn.End - turn.Start).TotalMilliseconds, MidpointRounding.AwayFromZero));

    private static long OverlapMilliseconds(DiarizationEvidenceTurn left, DiarizationEvidenceTurn right)
    {
        var start = left.Start > right.Start ? left.Start : right.Start;
        var end = left.End < right.End ? left.End : right.End;
        return end <= start
            ? 0
            : (long)Math.Round((end - start).TotalMilliseconds, MidpointRounding.AwayFromZero);
    }
}
