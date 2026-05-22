using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Tests;

public sealed class PublishedMeetingSummaryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    public PublishedMeetingSummaryServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task GenerateAsync_Updates_Published_Artifacts_From_Transcript_Json_Without_Reprocessing()
    {
        var jsonPath = Path.Combine(_root, "transcripts", "2026-05-22_100000_teams_client-sync.json");
        var markdownPath = Path.Combine(_root, "transcripts", "2026-05-22_100000_teams_client-sync.md");
        var readyPath = Path.Combine(_root, "transcripts", "2026-05-22_100000_teams_client-sync.ready");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await WriteTranscriptArtifactsAsync(jsonPath, markdownPath);
        await File.WriteAllTextAsync(readyPath, "ready");

        var manifestPath = Path.Combine(_root, "work", "session-1", "manifest.json");
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        await manifestStore.SaveAsync(CreateManifest(), manifestPath);

        var provider = new TrackingSummaryProvider();
        var service = new PublishedMeetingSummaryService(provider, manifestStore);
        var record = CreateRecord(jsonPath, markdownPath, readyPath, manifestPath);

        var result = await service.GenerateAsync(record, CreateEnabledConfig());

        Assert.Equal(StageExecutionState.Succeeded, result.Status.State);
        Assert.True(result.UpdatedJson);
        Assert.True(result.UpdatedMarkdown);
        Assert.True(result.UpdatedManifest);
        Assert.True(result.UpdatedSnapshot);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal("Client Sync", provider.LastRequest?.Manifest.DetectedTitle);
        Assert.Equal("What are the objectives?", Assert.Single(provider.LastRequest!.Segments).Text);
        Assert.True(File.Exists(readyPath));

        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        Assert.Equal((int)StageExecutionState.Succeeded, json["summarizationStatus"]?["State"]?.GetValue<int>());
        Assert.Equal("The team aligned on launch readiness.", json["summary"]?["overview"]?.GetValue<string>());
        Assert.Equal("OpenAI", json["summary"]?["provider"]?["providerName"]?.GetValue<string>());
        Assert.Equal("OpenAi", json["summary"]?["provider"]?["providerKind"]?.GetValue<string>());

        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("## Summary", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("## Summary", StringComparison.Ordinal) < markdown.IndexOf("## Transcript", StringComparison.Ordinal));
        Assert.Contains("- Provider: OpenAI (gpt-5-mini)", markdown, StringComparison.Ordinal);

        var savedManifest = await manifestStore.LoadAsync(manifestPath);
        Assert.Equal(StageExecutionState.Succeeded, savedManifest.SummarizationStatus.State);
        Assert.Equal("The team aligned on launch readiness.", savedManifest.Summary?.Overview);
        Assert.True(File.Exists(Path.Combine(_root, "work", "session-1", "processing", "summary.snapshot.json")));
        Assert.DoesNotContain("sk-secret", await File.ReadAllTextAsync(jsonPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-secret", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_Skips_When_Disabled_Without_Calling_Provider()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        var markdownPath = Path.Combine(_root, "meeting.md");
        await WriteTranscriptArtifactsAsync(jsonPath, markdownPath);
        var provider = new TrackingSummaryProvider();
        var service = new PublishedMeetingSummaryService(provider);

        var result = await service.GenerateAsync(
            CreateRecord(jsonPath, markdownPath, readyPath: null, manifestPath: null),
            new AppConfig { SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled });

        Assert.Equal(StageExecutionState.Skipped, result.Status.State);
        Assert.Equal(0, provider.CallCount);
        Assert.False(result.UpdatedJson);
        Assert.DoesNotContain("## Summary", await File.ReadAllTextAsync(markdownPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_Skips_When_Transcript_Json_Is_Missing()
    {
        var markdownPath = Path.Combine(_root, "meeting.md");
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
        await File.WriteAllTextAsync(
            markdownPath,
            """
            # Client Sync

            ## Transcript

            [00:00:00 - 00:00:02] **Speaker:** Hello team.
            """);
        var provider = new TrackingSummaryProvider();
        var service = new PublishedMeetingSummaryService(provider);

        var result = await service.GenerateAsync(
            CreateRecord(jsonPath: null, markdownPath, readyPath: null, manifestPath: null),
            CreateEnabledConfig());

        Assert.Equal(StageExecutionState.Skipped, result.Status.State);
        Assert.Contains("JSON", result.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Writes_Safe_Failed_Status_And_Preserves_Ready_Marker()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        var markdownPath = Path.Combine(_root, "meeting.md");
        var readyPath = Path.Combine(_root, "meeting.ready");
        await WriteTranscriptArtifactsAsync(jsonPath, markdownPath);
        await File.WriteAllTextAsync(readyPath, "ready");
        var provider = new TrackingSummaryProvider
        {
            ResultFactory = request => new MeetingSummaryResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Failed,
                    DateTimeOffset.Parse("2026-05-22T15:00:00Z"),
                    "OpenAI HTTP 503 Service Unavailable."),
                null),
        };
        var service = new PublishedMeetingSummaryService(provider);

        var result = await service.GenerateAsync(
            CreateRecord(jsonPath, markdownPath, readyPath, manifestPath: null),
            CreateEnabledConfig());

        Assert.Equal(StageExecutionState.Failed, result.Status.State);
        Assert.True(result.UpdatedJson);
        Assert.False(result.UpdatedMarkdown);
        Assert.True(File.Exists(readyPath));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        Assert.Equal((int)StageExecutionState.Failed, json["summarizationStatus"]?["State"]?.GetValue<int>());
        Assert.Null(json["summary"]);
        Assert.DoesNotContain("raw response body", await File.ReadAllTextAsync(jsonPath), StringComparison.OrdinalIgnoreCase);
    }

    private static AppConfig CreateEnabledConfig()
    {
        return new AppConfig
        {
            SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
            SummaryOpenAiModel = "gpt-5-mini",
        };
    }

    private static MeetingSessionManifest CreateManifest()
    {
        return new MeetingSessionManifest
        {
            SessionId = "session-1",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Client Sync",
            StartedAtUtc = DateTimeOffset.Parse("2026-05-22T10:00:00Z"),
            State = SessionState.Published,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.Parse("2026-05-22T10:05:00Z"), "done"),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Succeeded, DateTimeOffset.Parse("2026-05-22T10:06:00Z"), "done"),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.Parse("2026-05-22T10:07:00Z"), "published"),
        };
    }

    private static MeetingOutputRecord CreateRecord(
        string? jsonPath,
        string? markdownPath,
        string? readyPath,
        string? manifestPath)
    {
        return new MeetingOutputRecord(
            "2026-05-22_100000_teams_client-sync",
            "Client Sync",
            DateTimeOffset.Parse("2026-05-22T10:00:00Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(30),
            AudioPath: null,
            MarkdownPath: markdownPath,
            JsonPath: jsonPath,
            ReadyMarkerPath: readyPath,
            ManifestPath: manifestPath,
            ManifestState: SessionState.Published,
            Attendees: Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: true,
            TranscriptionModelFileName: "ggml-small.bin");
    }

    private static async Task WriteTranscriptArtifactsAsync(string jsonPath, string markdownPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
        await File.WriteAllTextAsync(
            jsonPath,
            """
            {
              "sessionId": "session-1",
              "platform": "Teams",
              "title": "Client Sync",
              "startedAtUtc": "2026-05-22T10:00:00Z",
              "summarizationStatus": {
                "StageName": "summarization",
                "State": 0,
                "UpdatedAtUtc": "2026-05-22T10:00:00Z",
                "Message": null
              },
              "summary": null,
              "segments": [
                {
                  "start": "00:00:00",
                  "end": "00:00:04",
                  "speakerId": "speaker_00",
                  "speakerLabel": "Speaker 1",
                  "text": "What are the objectives?"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            markdownPath,
            """
            # Client Sync

            - Session ID: session-1
            - Platform: Teams
            - Started (UTC): 2026-05-22T10:00:00.0000000+00:00

            ## Transcript

            [00:00:00 - 00:00:04] **Speaker 1:** What are the objectives?
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TrackingSummaryProvider : IMeetingSummarizationProvider
    {
        public int CallCount { get; private set; }

        public MeetingSummaryRequest? LastRequest { get; private set; }

        public Func<MeetingSummaryRequest, MeetingSummaryResult>? ResultFactory { get; init; }

        public Task<MeetingSummaryResult> SummarizeAsync(
            MeetingSummaryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            var generatedAtUtc = DateTimeOffset.Parse("2026-05-22T14:30:00Z");
            return Task.FromResult(ResultFactory?.Invoke(request) ?? new MeetingSummaryResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Succeeded,
                    generatedAtUtc,
                    "Summary generated."),
                new MeetingSummary(
                    "The team aligned on launch readiness.",
                    ["Launch remains on track."],
                    ["Proceed with the pilot."],
                    [new MeetingSummaryActionItem("Send pilot checklist.", "Pranav", "Friday")],
                    ["Confirm legal review timing."],
                    new MeetingSummaryProviderInfo(SummaryChatProviderKind.OpenAi, "OpenAI", "gpt-5-mini", false),
                    generatedAtUtc,
                    MeetingSummaryTranscriptFingerprint.Compute(request.Segments))));
        }
    }
}
