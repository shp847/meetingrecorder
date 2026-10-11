using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerEvidenceServiceTests
{
    private readonly SpeakerEvidenceService _service = new();

    [Fact]
    public void Select_Spreads_Bounded_Cues_And_Deduplicates_Text()
    {
        var result = _service.Select(Request(
        [
            Segment("speaker-1", 0, 4, "Early context makes this a readable local transcript cue."),
            Segment("speaker-1", 10, 14, "Early context makes this a readable local transcript cue."),
            Segment("speaker-1", 50, 55, "Middle context has separate enough words for safe review."),
            Segment("speaker-1", 100, 105, "Late context is another representative speaker contribution."),
            Segment("speaker-1", 102, 108, "Overlapping context must not become a second selected cue."),
        ]));

        Assert.Equal(SpeakerEvidenceState.Available, result.State);
        Assert.Equal(3, result.Cues.Count);
        Assert.Equal([0d, 50d, 100d], result.Cues.Select(cue => cue.Start.TotalSeconds));
        Assert.DoesNotContain(result.Cues, cue => cue.Excerpt.Contains("Overlapping", StringComparison.Ordinal));
    }

    [Fact]
    public void Select_Bounds_Unicode_Excerpts_Without_Splitting_Text_Elements()
    {
        var source = string.Concat(Enumerable.Repeat("e\u0301", 220));
        var result = _service.Select(Request([Segment("speaker-1", 0, 4, source)]));

        Assert.Equal(SpeakerEvidenceState.Limited, result.State);
        Assert.Single(result.Cues);
        Assert.EndsWith("…", result.Cues[0].Excerpt, StringComparison.Ordinal);
        Assert.Equal(181, System.Globalization.StringInfo.ParseCombiningCharacters(result.Cues[0].Excerpt).Length);
    }

    [Theory]
    [InlineData(false, SpeakerEvidenceState.TranscriptUnavailable, SpeakerEvidenceAction.ReadTranscript)]
    [InlineData(true, SpeakerEvidenceState.NoTurnsForSpeaker, SpeakerEvidenceAction.RepairSpeakerLabels)]
    public void Select_Returns_Explicit_Unavailable_State(bool hasTranscript, SpeakerEvidenceState expectedState, SpeakerEvidenceAction expectedAction)
    {
        var segments = hasTranscript
            ? new[] { Segment("speaker-2", 0, 4, "Different speaker has readable transcript context here.") }
            : Array.Empty<TranscriptSegment>();

        var result = _service.Select(Request(segments, hasTranscript));

        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedAction, result.Action);
        Assert.Empty(result.Cues);
    }

    [Fact]
    public void Select_Uses_Markdown_Fallback_Only_With_Valid_Timestamps()
    {
        var request = Request(
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.Zero, null, "Speaker 1", "Readable fallback context with enough words to safely show.")],
            sourceKind: SpeakerEvidenceSourceKind.MarkdownFallback);

        var result = _service.Select(request);

        Assert.Equal(SpeakerEvidenceState.TimestampsUnavailable, result.State);
        Assert.Equal(SpeakerEvidenceAction.ReadTranscript, result.Action);
    }

    [Fact]
    public void Select_Stale_Artifact_Never_Returns_Cues()
    {
        var result = _service.Select(Request(
            [Segment("speaker-1", 0, 4, "Readable local transcript context that must not outlive its artifact.")],
            isStale: true));

        Assert.Equal(SpeakerEvidenceState.ArtifactStale, result.State);
        Assert.Empty(result.Cues);
    }

    private static SpeakerEvidenceRequest Request(
        IReadOnlyList<TranscriptSegment> segments,
        bool hasTranscript = true,
        bool isStale = false,
        SpeakerEvidenceSourceKind sourceKind = SpeakerEvidenceSourceKind.StructuredTranscript) =>
        new("meeting", "revision", "speaker-1", "Speaker 1", segments, hasTranscript, true, isStale, sourceKind);

    private static TranscriptSegment Segment(string speakerId, int startSeconds, int endSeconds, string text) =>
        new(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds), speakerId, "Speaker 1", text);
}
