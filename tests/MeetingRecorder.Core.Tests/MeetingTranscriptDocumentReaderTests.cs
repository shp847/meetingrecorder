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
