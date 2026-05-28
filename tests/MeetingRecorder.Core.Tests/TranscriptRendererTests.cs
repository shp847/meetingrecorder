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
    public void RenderMarkdown_Coalesces_Consecutive_Segments_For_The_Same_Speaker()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest();

        var markdown = renderer.RenderMarkdown(
            manifest,
            [
                new TranscriptSegment(TimeSpan.FromSeconds(659), TimeSpan.FromSeconds(661), "Speaker 1", "if we were presenting this,"),
                new TranscriptSegment(TimeSpan.FromSeconds(661), TimeSpan.FromSeconds(662), "Speaker 1", "would we,"),
                new TranscriptSegment(TimeSpan.FromSeconds(662), TimeSpan.FromSeconds(668), "Speaker 1", "the way I was thinking about it was just creating a script."),
                new TranscriptSegment(TimeSpan.FromSeconds(669), TimeSpan.FromSeconds(672), "Speaker 2", "Yes, that makes sense."),
            ]);

        Assert.Contains(
            "[00:10:59 - 00:11:08] **Speaker 1:** if we were presenting this, would we, the way I was thinking about it was just creating a script.",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "[00:11:09 - 00:11:12] **Speaker 2:** Yes, that makes sense.",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[00:11:01 - 00:11:02]", markdown, StringComparison.Ordinal);
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

    [Fact]
    public void RenderMarkdown_Includes_Summary_Before_Transcript_When_Summarization_Succeeds()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Succeeded,
                DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "Summary generated."),
            Summary = CreateSummary(),
        };

        var markdown = renderer.RenderMarkdown(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), "speaker_00", null, "Hello team")]);

        var summaryIndex = markdown.IndexOf("## Summary", StringComparison.Ordinal);
        var transcriptIndex = markdown.IndexOf("## Transcript", StringComparison.Ordinal);
        Assert.True(summaryIndex >= 0);
        Assert.True(summaryIndex < transcriptIndex);
        Assert.Contains("The team aligned on launch readiness.", markdown, StringComparison.Ordinal);
        Assert.Contains("- Launch remains on track.", markdown, StringComparison.Ordinal);
        Assert.Contains("- Send pilot checklist. Owner: Pranav. Due: Friday.", markdown, StringComparison.Ordinal);
        Assert.Contains("- Provider: OpenAI (gpt-5-mini)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_Omits_Summary_When_Summarization_Did_Not_Succeed()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Failed,
                DateTimeOffset.UtcNow,
                "ModelProxy returned HTTP 503 Failure."),
            Summary = CreateSummary(),
        };

        var markdown = renderer.RenderMarkdown(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), null, null, "Hello team")]);

        Assert.DoesNotContain("## Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("## Transcript", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderJson_Includes_Summarization_Status_And_Nullable_Summary()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Succeeded,
                DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "Summary generated."),
            Summary = CreateSummary(),
        };

        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), null, null, "Hello team")]);
        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        Assert.Equal("summarization", document["summarizationStatus"]?["StageName"]?.GetValue<string>());
        Assert.Equal((int)StageExecutionState.Succeeded, document["summarizationStatus"]?["State"]?.GetValue<int>());
        Assert.Equal("The team aligned on launch readiness.", document["summary"]?["overview"]?.GetValue<string>());
        Assert.Equal("OpenAI", document["summary"]?["provider"]?["providerName"]?.GetValue<string>());
    }

    [Fact]
    public void RenderJson_Preserves_ModelProxy_Routing_Metadata()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Succeeded,
                DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "Summary generated."),
            Summary = CreateSummary() with
            {
                Provider = new MeetingSummaryProviderInfo(
                    SummaryChatProviderKind.ModelProxy,
                    "ModelProxy",
                    "gpt-5.4-mini",
                    false,
                    new ModelProxyRoutingInfo("mp-summary", "app-server", "app-server", null, false, null)),
            },
        };

        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), null, null, "Hello team")]);
        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        var routing = document["summary"]?["provider"]?["modelProxyRouting"];
        Assert.Equal("mp-summary", routing?["requestId"]?.GetValue<string>());
        Assert.Equal("app-server", routing?["requestedBackend"]?.GetValue<string>());
        Assert.Equal("app-server", routing?["effectiveBackend"]?.GetValue<string>());
        Assert.False(routing?["appServerWebSearchSupported"]?.GetValue<bool>());
    }

    [Fact]
    public void RenderJson_Writes_Null_Summary_For_Failed_Summarization()
    {
        var renderer = new TranscriptRenderer();
        var manifest = CreateManifest() with
        {
            SummarizationStatus = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Failed,
                DateTimeOffset.UtcNow,
                "ModelProxy returned HTTP 503 Failure."),
        };

        var json = renderer.RenderJson(
            manifest,
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), null, null, "Hello team")]);
        var document = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Transcript JSON was not an object.");

        Assert.Equal((int)StageExecutionState.Failed, document["summarizationStatus"]?["State"]?.GetValue<int>());
        Assert.Null(document["summary"]);
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

    private static MeetingSummary CreateSummary()
    {
        var generatedAtUtc = DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        return new MeetingSummary(
            "The team aligned on launch readiness.",
            ["Launch remains on track."],
            ["Proceed with the pilot."],
            [new MeetingSummaryActionItem("Send pilot checklist.", "Pranav", "Friday")],
            ["Confirm legal review timing."],
            new MeetingSummaryProviderInfo(SummaryChatProviderKind.OpenAi, "OpenAI", "gpt-5-mini", false),
            generatedAtUtc,
            "fingerprint-123");
    }
}
