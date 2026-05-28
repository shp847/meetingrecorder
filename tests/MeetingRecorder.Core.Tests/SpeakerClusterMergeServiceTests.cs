using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerClusterMergeServiceTests
{
    [Fact]
    public void MergeSimilarClusters_Merges_OverSplit_Embedding_Clusters_Without_A_Speaker_Count_Hint()
    {
        var service = new SpeakerClusterMergeService();
        var speakerTurns = new[]
        {
            Turn("speaker_00", 0, 500),
            Turn("speaker_01", 500, 760),
            Turn("speaker_06", 760, 830),
            Turn("speaker_05", 830, 855),
            Turn("speaker_02", 855, 875),
            Turn("speaker_10", 875, 884),
            Turn("speaker_21", 884, 889),
        };
        var samples = new[]
        {
            Sample("speaker_00", [1f, 0f, 0f]),
            Sample("speaker_05", [0.90f, 0.4358899f, 0f]),
            Sample("speaker_01", [0f, 1f, 0f]),
            Sample("speaker_06", [0.4358899f, 0.90f, 0f]),
            Sample("speaker_02", [0.20f, 0.80f, 0.5656854f]),
        };

        var result = service.MergeSimilarClusters(speakerTurns, samples);

        Assert.Equal(7, result.OriginalSpeakerCount);
        Assert.Equal(2, result.MergedSpeakerCount);
        Assert.Equal(["speaker_00", "speaker_01"], result.SpeakerTurns.Select(turn => turn.SpeakerId).Distinct(StringComparer.Ordinal).ToArray());
        Assert.Equal("speaker_00", result.SpeakerIdMap["speaker_05"]);
        Assert.Equal("speaker_01", result.SpeakerIdMap["speaker_06"]);
        Assert.Equal("speaker_01", result.SpeakerIdMap["speaker_02"]);
        Assert.Contains("from 7 to 2 speakers", result.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeSimilarClusters_Keeps_Distinct_Long_Speakers_When_Embeddings_Do_Not_Match()
    {
        var service = new SpeakerClusterMergeService();
        var speakerTurns = new[]
        {
            Turn("speaker_00", 0, 120),
            Turn("speaker_01", 120, 240),
            Turn("speaker_02", 240, 360),
        };
        var samples = new[]
        {
            Sample("speaker_00", [1f, 0f, 0f]),
            Sample("speaker_01", [0f, 1f, 0f]),
            Sample("speaker_02", [0f, 0f, 1f]),
        };

        var result = service.MergeSimilarClusters(speakerTurns, samples);

        Assert.Equal(3, result.MergedSpeakerCount);
        Assert.Null(result.DiagnosticMessage);
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
            DateTimeOffset.Parse("2026-05-23T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
    }
}
