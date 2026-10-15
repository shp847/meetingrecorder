using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationTurnEvidenceMetricsEvaluatorTests
{
    [Fact]
    public void Evaluate_MapsEachObservedClusterToItsBestOpaqueTruth()
    {
        var result = DiarizationTurnEvidenceMetricsEvaluator.Evaluate(
        [
            Turn("truth-01", 0, 10),
            Turn("truth-02", 10, 19)
        ],
        [
            Turn("detected-a", 0, 19),
            Turn("detected-b", 0, 8)
        ]);

        Assert.Equal(18_000, result.AlignedMilliseconds);
        Assert.Equal(0, result.OverSegmentationCount);
        Assert.Equal(0, result.UnderSegmentationCount);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public void Evaluate_ReportsFragmentationWithoutLeakingSpeakerLabels()
    {
        var result = DiarizationTurnEvidenceMetricsEvaluator.Evaluate(
        [Turn("truth-01", 0, 5), Turn("truth-02", 5, 10)],
        [Turn("detected-a", 0, 5), Turn("detected-b", 5, 7), Turn("detected-c", 7, 10)]);

        Assert.Equal(2, result.ExpectedSpeakerCount);
        Assert.Equal(3, result.DetectedSpeakerCount);
        Assert.Equal(1, result.OverSegmentationCount);
        Assert.Equal(10_000, result.AlignedMilliseconds);
        Assert.Equal(1d, result.ExpectedCoverage);
    }

    [Fact]
    public void Evaluate_HandlesMissingOrInvalidTurnsAsMetricsNotFailures()
    {
        var result = DiarizationTurnEvidenceMetricsEvaluator.Evaluate(
        [Turn("truth-01", 2, 2)],
        [Turn("detected-a", 0, 1)]);

        Assert.Equal("not_asserted", result.Status);
        Assert.Equal(0, result.ExpectedTurnCount);
        Assert.Equal(0, result.AlignedMilliseconds);
    }

    private static DiarizationEvidenceTurn Turn(string speakerId, int startSeconds, int endSeconds) =>
        new(speakerId, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds));
}
