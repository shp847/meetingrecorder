using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptRendererTests
{
    [Fact]
    public void RenderMarkdown_Uses_Default_Speaker_Label_When_Diarization_Is_Missing()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest();

        var markdown = renderer.RenderMarkdown(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), null, "Hello team")]);

        Assert.Contains("**Speaker:** Hello team", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderJson_Preserves_Speaker_Labels_And_Status_Metadata()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "ok"),
        };

        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Pranav", "Hello team")]);

        Assert.Contains("\"SpeakerLabel\": \"Pranav\"", json, StringComparison.Ordinal);
        Assert.Contains("\"StageName\": \"diarization\"", json, StringComparison.Ordinal);
        Assert.Contains("\"State\": 3", json, StringComparison.Ordinal);
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
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "done"),
        };
    }
}
