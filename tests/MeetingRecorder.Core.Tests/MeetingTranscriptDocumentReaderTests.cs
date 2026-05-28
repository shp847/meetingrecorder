using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingTranscriptDocumentReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public MeetingTranscriptDocumentReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Read_Prefers_Json_Sidecar_Segments()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        var markdownPath = Path.Combine(_root, "meeting.md");
        File.WriteAllText(
            jsonPath,
            """
            {
              "segments": [
                {
                  "start": "00:00:04",
                  "end": "00:00:08",
                  "speakerLabel": "Speaker 1",
                  "text": "What are the objectives?"
                },
                {
                  "start": "00:00:09",
                  "end": "00:00:12",
                  "displaySpeakerLabel": "Speaker 2",
                  "text": "We need the smaller working group."
                }
              ]
            }
            """);
        File.WriteAllText(
            markdownPath,
            """
            # Meeting

            ## Transcript

            [00:00:00 - 00:00:02] **Speaker:** Fallback line.
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath, markdownPath);

        Assert.True(result.HasTranscript);
        Assert.Equal("Showing 2 transcript segment(s) from JSON sidecar.", result.StatusText);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("00:04", result.Segments[0].Timestamp);
        Assert.Equal("Speaker 1", result.Segments[0].SpeakerLabel);
        Assert.Equal("What are the objectives?", result.Segments[0].Text);
        Assert.Equal("Speaker 2", result.Segments[1].SpeakerLabel);
        Assert.True(result.HasStructuredJson);
        Assert.Equal(2, result.StructuredSegments.Count);
        Assert.Equal(TimeSpan.FromSeconds(4), result.StructuredSegments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(8), result.StructuredSegments[0].End);
    }

    [Fact]
    public void Read_Coalesces_Consecutive_Json_Segments_For_The_Same_Speaker_Display()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        File.WriteAllText(
            jsonPath,
            """
            {
              "segments": [
                {
                  "start": "00:10:59",
                  "end": "00:11:01",
                  "speakerLabel": "Speaker 1",
                  "text": "if we were presenting this,"
                },
                {
                  "start": "00:11:01",
                  "end": "00:11:02",
                  "speakerLabel": "Speaker 1",
                  "text": "would we,"
                },
                {
                  "start": "00:11:02",
                  "end": "00:11:08",
                  "speakerLabel": "Speaker 1",
                  "text": "the way I was thinking about it was just creating a script."
                },
                {
                  "start": "00:11:09",
                  "end": "00:11:12",
                  "speakerLabel": "Speaker 2",
                  "text": "Yes, that makes sense."
                }
              ]
            }
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath, markdownPath: null);

        Assert.Equal("Showing 2 transcript paragraph(s) from JSON sidecar (merged from 4 segment(s)).", result.StatusText);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("10:59", result.Segments[0].Timestamp);
        Assert.Equal("Speaker 1", result.Segments[0].SpeakerLabel);
        Assert.Equal(
            "if we were presenting this, would we, the way I was thinking about it was just creating a script.",
            result.Segments[0].Text);
        Assert.Equal(4, result.StructuredSegments.Count);
    }

    [Fact]
    public void Read_Parses_Summary_Status_And_Content_From_Json_Sidecar()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        File.WriteAllText(
            jsonPath,
            """
            {
              "summarizationStatus": {
                "stageName": "summarization",
                "state": 3,
                "updatedAtUtc": "2026-05-22T14:30:00Z",
                "message": "Summary generated."
              },
              "summary": {
                "overview": "The team aligned on launch readiness.",
                "keyPoints": ["Launch remains on track."],
                "decisions": ["Proceed with the pilot."],
                "actionItems": [
                  {
                    "text": "Send pilot checklist.",
                    "owner": "Pranav",
                    "dueDateText": "Friday"
                  }
                ],
                "risksAndOpenQuestions": ["Confirm legal review timing."],
                "provider": {
                  "providerKind": "OpenAi",
                  "providerName": "OpenAI",
                  "model": "gpt-5-mini",
                  "fallbackUsed": true
                },
                "generatedAtUtc": "2026-05-22T14:30:00Z",
                "transcriptFingerprint": "fingerprint-123"
              },
              "segments": [
                {
                  "start": "00:00:04",
                  "end": "00:00:08",
                  "speakerId": "speaker_00",
                  "speakerLabel": "Speaker 1",
                  "text": "What are the objectives?"
                }
              ]
            }
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath, markdownPath: null);

        Assert.True(result.HasTranscript);
        Assert.True(result.HasStructuredJson);
        Assert.Equal(StageExecutionState.Succeeded, result.SummarizationStatus?.State);
        Assert.NotNull(result.Summary);
        Assert.Equal("The team aligned on launch readiness.", result.Summary.Overview);
        Assert.Equal("Launch remains on track.", Assert.Single(result.Summary.KeyPoints));
        Assert.Equal("Proceed with the pilot.", Assert.Single(result.Summary.Decisions));
        var actionItem = Assert.Single(result.Summary.ActionItems);
        Assert.Equal("Send pilot checklist.", actionItem.Text);
        Assert.Equal("Pranav", actionItem.Owner);
        Assert.Equal("Friday", actionItem.DueDateText);
        Assert.Equal("Confirm legal review timing.", Assert.Single(result.Summary.RisksAndOpenQuestions));
        Assert.Equal(SummaryChatProviderKind.OpenAi, result.Summary.Provider.ProviderKind);
        Assert.Equal("OpenAI", result.Summary.Provider.ProviderName);
        Assert.Equal("gpt-5-mini", result.Summary.Provider.Model);
        Assert.True(result.Summary.Provider.FallbackUsed);
        Assert.Equal("fingerprint-123", result.Summary.TranscriptFingerprint);
        Assert.Equal("speaker_00", result.StructuredSegments[0].SpeakerId);
    }

    [Fact]
    public void Read_Preserves_ModelProxy_Routing_Metadata_From_Summary_Provider()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        File.WriteAllText(
            jsonPath,
            """
            {
              "summarizationStatus": {
                "stageName": "summarization",
                "state": 3,
                "updatedAtUtc": "2026-05-22T14:30:00Z",
                "message": "Summary generated."
              },
              "summary": {
                "overview": "The team aligned on launch readiness.",
                "keyPoints": [],
                "decisions": [],
                "actionItems": [],
                "risksAndOpenQuestions": [],
                "provider": {
                  "providerKind": "ModelProxy",
                  "providerName": "ModelProxy",
                  "model": "gpt-5.4-mini",
                  "fallbackUsed": false,
                  "modelProxyRouting": {
                    "requestId": "mp-summary",
                    "requestedBackend": "app-server",
                    "effectiveBackend": "app-server",
                    "appServerWebSearchSupported": false
                  }
                },
                "generatedAtUtc": "2026-05-22T14:30:00Z",
                "transcriptFingerprint": "fingerprint-123"
              },
              "segments": [
                {
                  "start": "00:00:04",
                  "end": "00:00:08",
                  "speakerId": "speaker_00",
                  "speakerLabel": "Speaker 1",
                  "text": "What are the objectives?"
                }
              ]
            }
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath, markdownPath: null);

        Assert.NotNull(result.Summary);
        Assert.Equal("mp-summary", result.Summary.Provider.ModelProxyRouting?.RequestId);
        Assert.Equal("app-server", result.Summary.Provider.ModelProxyRouting?.RequestedBackend);
        Assert.Equal("app-server", result.Summary.Provider.ModelProxyRouting?.EffectiveBackend);
        Assert.False(result.Summary.Provider.ModelProxyRouting?.AppServerWebSearchSupported);
    }

    [Fact]
    public void Read_Ignores_Malformed_Summary_Without_Losing_Transcript()
    {
        var jsonPath = Path.Combine(_root, "meeting.json");
        File.WriteAllText(
            jsonPath,
            """
            {
              "summarizationStatus": {
                "stageName": "summarization",
                "state": 4,
                "updatedAtUtc": "2026-05-22T14:30:00Z",
                "message": "Provider failed safely."
              },
              "summary": {
                "keyPoints": ["Missing overview should not break transcript display."]
              },
              "segments": [
                {
                  "start": "00:00:04",
                  "end": "00:00:08",
                  "speakerLabel": "Speaker 1",
                  "text": "What are the objectives?"
                }
              ]
            }
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath, markdownPath: null);

        Assert.True(result.HasTranscript);
        Assert.Equal(StageExecutionState.Failed, result.SummarizationStatus?.State);
        Assert.Null(result.Summary);
        Assert.Single(result.Segments);
        Assert.Single(result.StructuredSegments);
    }

    [Fact]
    public void Read_Falls_Back_To_App_Markdown_Transcript_Format()
    {
        var markdownPath = Path.Combine(_root, "meeting.md");
        File.WriteAllText(
            markdownPath,
            """
            # Client Sync

            - Platform: Teams

            ## Transcript

            [00:00:00 - 00:00:02] **Speaker 1:** Hello team.
            [00:01:05 - 00:01:07] **Speaker 2:** Let's start.
            """);

        var result = MeetingTranscriptDocumentReader.Read(jsonPath: null, markdownPath);

        Assert.True(result.HasTranscript);
        Assert.Equal("Showing 2 transcript segment(s) from Markdown transcript.", result.StatusText);
        Assert.Equal(new[] { "Hello team.", "Let's start." }, result.Segments.Select(segment => segment.Text));
        Assert.Equal("01:05", result.Segments[1].Timestamp);
        Assert.False(result.HasStructuredJson);
        Assert.Empty(result.StructuredSegments);
        Assert.Null(result.SummarizationStatus);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void Read_Returns_Empty_State_When_No_Transcript_Artifact_Exists()
    {
        var result = MeetingTranscriptDocumentReader.Read(
            Path.Combine(_root, "missing.json"),
            Path.Combine(_root, "missing.md"));

        Assert.False(result.HasTranscript);
        Assert.Equal("No transcript artifact is available for this meeting yet.", result.StatusText);
        Assert.Empty(result.Segments);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
