using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        Assert.Contains("\"speakerLabel\": \"Pranav\"", json, StringComparison.Ordinal);
        Assert.Contains("\"StageName\": \"diarization\"", json, StringComparison.Ordinal);
        Assert.Contains("\"State\": 3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderJson_Includes_Attendees_And_Processing_Metadata()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            Attendees =
            [
                new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar]),
                new MeetingAttendee("John Doe", [MeetingAttendeeSource.TeamsLiveRoster]),
            ],
            ProcessingMetadata = new MeetingProcessingMetadata("ggml-small.bin", true),
        };

        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Jane Smith", "Hello team")]);

        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");
        var attendees = document["attendees"]?.AsArray()
            ?? throw new InvalidOperationException("attendees was missing.");

        Assert.Equal("ggml-small.bin", document["transcriptionModelFileName"]?.GetValue<string>());
        Assert.True(document["hasSpeakerLabels"]?.GetValue<bool>());
        Assert.Equal(2, attendees.Count);
        Assert.Equal("Jane Smith", attendees[0]?["name"]?.GetValue<string>());
        Assert.Equal("OutlookCalendar", attendees[0]?["sources"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void RenderMarkdown_And_Json_Include_Detected_Audio_Source_Metadata()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            DetectedAudioSource = new DetectedAudioSource(
                "Microsoft Teams",
                "GF/Bharat | AI workshop Sync Sourcing",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.High,
                DateTimeOffset.Parse("2026-03-23T16:18:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)),
        };

        var markdown = renderer.RenderMarkdown(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Speaker", "Hello team")]);
        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Speaker", "Hello team")]);
        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        Assert.Contains("- Detected audio source: Microsoft Teams", markdown, StringComparison.Ordinal);
        Assert.Equal("Microsoft Teams", document["detectedAudioSource"]?["appName"]?.GetValue<string>());
        Assert.Equal("Window", document["detectedAudioSource"]?["matchKind"]?.GetValue<string>());
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
