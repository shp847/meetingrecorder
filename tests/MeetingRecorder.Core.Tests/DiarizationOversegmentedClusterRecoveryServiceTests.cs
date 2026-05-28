using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationOversegmentedClusterRecoveryServiceTests
{
    [Fact]
    public void TryRecover_Merges_OverSegmented_Clusters_Into_Supported_Range()
    {
        var selectionService = new DiarizationClusterSelectionService();
        var recoveryService = new DiarizationOversegmentedClusterRecoveryService(
            selectionService,
            new SpeakerClusterMergeService());
        var candidate = new DiarizationClusterCandidate(
            0.5f,
            Enumerable.Range(0, 18)
                .Select(index => Turn(SpeakerId(index), index * 60, index * 60 + 60))
                .ToArray());
        var selection = selectionService.SelectBestCandidate([candidate]);
        var samples = Enumerable.Range(0, 18)
            .Select(index => Sample(
                SpeakerId(index),
                index < 9 ? [1f, 0f, 0f] : [0f, 1f, 0f]))
            .ToArray();

        var result = recoveryService.TryRecover(selection, samples);

        Assert.True(DiarizationOversegmentedClusterRecoveryService.ShouldAttemptRecovery(selection));
        Assert.True(result.Recovered);
        Assert.True(result.Selection.IsAutomaticSpeakerCountSupported);
        Assert.Equal(2, result.Selection.SupportedSpeakerCount);
        Assert.Equal(18, result.MergeResult?.OriginalSpeakerCount);
        Assert.Equal(2, result.MergeResult?.MergedSpeakerCount);
        Assert.Equal(["speaker_00", "speaker_09"], result.Selection.Candidate.SpeakerTurns
            .Select(turn => turn.SpeakerId)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        Assert.Equal(["speaker_00", "speaker_09"], result.VoiceSamples
            .Select(sample => sample.SpeakerId)
            .ToArray());
        Assert.Contains("from 18 to 2 speakers", result.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRecover_Keeps_Unsupported_Result_When_Distinct_Clusters_Do_Not_Merge()
    {
        var selectionService = new DiarizationClusterSelectionService();
        var recoveryService = new DiarizationOversegmentedClusterRecoveryService(
            selectionService,
            new SpeakerClusterMergeService());
        var candidate = new DiarizationClusterCandidate(
            0.5f,
            Enumerable.Range(0, 17)
                .Select(index => Turn(SpeakerId(index), index * 60, index * 60 + 60))
                .ToArray());
        var selection = selectionService.SelectBestCandidate([candidate]);
        var samples = Enumerable.Range(0, 17)
            .Select(index => Sample(SpeakerId(index), UnitVector(index, 17)))
            .ToArray();

        var result = recoveryService.TryRecover(selection, samples);

        Assert.False(result.Recovered);
        Assert.Same(selection, result.Selection);
        Assert.Equal(17, result.MergeResult?.MergedSpeakerCount);
        Assert.Contains("left 17 supported speakers", result.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRecover_Does_Not_Run_For_Collapsed_Or_Already_Supported_Selections()
    {
        var selectionService = new DiarizationClusterSelectionService();
        var recoveryService = new DiarizationOversegmentedClusterRecoveryService(
            selectionService,
            new SpeakerClusterMergeService());
        var collapsed = selectionService.SelectBestCandidate([
            new DiarizationClusterCandidate(0.5f, [Turn("speaker_00", 0, 60)])
        ]);
        var supported = selectionService.SelectBestCandidate([
            new DiarizationClusterCandidate(0.5f, [Turn("speaker_00", 0, 60), Turn("speaker_01", 60, 120)])
        ]);

        Assert.False(DiarizationOversegmentedClusterRecoveryService.ShouldAttemptRecovery(collapsed));
        Assert.False(DiarizationOversegmentedClusterRecoveryService.ShouldAttemptRecovery(supported));
        Assert.False(recoveryService.TryRecover(collapsed, [Sample("speaker_00", [1f, 0f])]).Recovered);
        Assert.False(recoveryService.TryRecover(supported, [Sample("speaker_00", [1f, 0f])]).Recovered);
    }

    private static string SpeakerId(int index)
    {
        return $"speaker_{index:00}";
    }

    private static SpeakerTurn Turn(string speakerId, int startSeconds, int endSeconds)
    {
        return new SpeakerTurn(
            speakerId,
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(endSeconds));
    }

    private static SpeakerVoiceSample Sample(string speakerId, IReadOnlyList<float> embedding)
    {
        return new SpeakerVoiceSample(
            speakerId,
            "embedding.onnx",
            embedding.Count,
            embedding,
            TimeSpan.FromSeconds(30),
            DateTimeOffset.Parse("2026-05-24T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private static float[] UnitVector(int index, int dimension)
    {
        var vector = new float[dimension];
        vector[index] = 1f;
        return vector;
    }
}
