using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSchemaTests
{
    [Fact]
    public void RenderJson_Includes_Speakers_Turns_And_Diarization_Metadata()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            ProcessingMetadata = new MeetingProcessingMetadata(
                "ggml-small.bin",
                true,
                [
                    new SpeakerIdentity(
                        "speaker_00",
                        "Pranav Sharma",
                        false,
                        "voice_pranav",
                        SpeakerNameSource.AutoAppliedVoiceProfile,
                        0.91d),
                    new SpeakerIdentity("speaker_01", "Speaker 2", true),
                ],
                [
                    new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(2)),
                    new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
                ],
                new DiarizationMetadata(
                    "sherpa-onnx",
                    "model.int8.onnx",
                    "nemo_en_titanet_small.onnx",
                    "2026.03.21",
                    DiarizationAttributionMode.SegmentOverlap,
                    DiarizationExecutionProvider.Cpu,
                    gpuAccelerationRequested: true,
                    gpuAccelerationAvailable: false,
                    diagnosticMessage: "DirectML probe failed."),
                [
                    new SpeakerVoiceSample(
                        "speaker_00",
                        "nemo_en_titanet_small.onnx",
                        3,
                        [1f, 0f, 0f],
                        TimeSpan.FromSeconds(9),
                        DateTimeOffset.Parse("2026-03-17T12:02:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)),
                ])
        };

        var json = renderer.RenderJson(
            manifest,
            [
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker_00", null, "Hello team"),
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "speaker_01", "Pranav", "Thanks everyone"),
            ]);

        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        Assert.Equal("ggml-small.bin", document["transcriptionModelFileName"]?.GetValue<string>());
        Assert.True(document["hasSpeakerLabels"]?.GetValue<bool>());
        Assert.Equal(2, document["speakers"]?.AsArray().Count);
        Assert.Equal("voice_pranav", document["speakers"]?[0]?["profileId"]?.GetValue<string>());
        Assert.Equal("AutoAppliedVoiceProfile", document["speakers"]?[0]?["nameSource"]?.GetValue<string>());
        Assert.Equal(0.91d, document["speakers"]?[0]?["confidence"]?.GetValue<double>());
        Assert.Equal(2, document["speakerTurns"]?.AsArray().Count);
        Assert.False(document.ContainsKey("speakerVoiceSamples"));
        Assert.Equal("sherpa-onnx", document["diarizationMetadata"]?["provider"]?.GetValue<string>());
        Assert.Equal("directml probe failed.", document["diarizationMetadata"]?["diagnosticMessage"]?.GetValue<string>().ToLowerInvariant());
        Assert.Equal("Pranav Sharma", document["segments"]?[0]?["speakerLabel"]?.GetValue<string>());
        Assert.Equal("Pranav", document["segments"]?[1]?["speakerLabel"]?.GetValue<string>());
    }

    private static MeetingSessionManifest CreateManifest()
    {
        return new MeetingSessionManifest
        {
            SessionId = "session-1",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Weekly Sync",
            StartedAtUtc = DateTimeOffset.Parse("2026-03-17T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            State = SessionState.Published,
            DetectionEvidence = Array.Empty<DetectionSignal>(),
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "ok"),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "ok"),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "done"),
        };
    }
}
