using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSpeakerAttributionServiceTests
{
    [Fact]
    public void ApplySpeakerTurns_Uses_Maximum_Overlap()
    {
        var service = new TranscriptSpeakerAttributionService();

        var result = service.ApplySpeakerTurns(
            [new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), null, "Hello team")],
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(2)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["speaker_00"] = "Speaker 1",
                ["speaker_01"] = "Speaker 2",
            });

        var segment = Assert.Single(result);
        Assert.Equal("speaker_01", segment.SpeakerId);
        Assert.Equal("Speaker 2", segment.SpeakerLabel);
    }

    [Fact]
    public void ApplySpeakerTurns_Breaks_Ties_By_Segment_Midpoint()
    {
        var service = new TranscriptSpeakerAttributionService();

        var result = service.ApplySpeakerTurns(
            [new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), null, "Hello team")],
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(3)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6)),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["speaker_00"] = "Speaker 1",
                ["speaker_01"] = "Speaker 2",
            });

        var segment = Assert.Single(result);
        Assert.Equal("speaker_00", segment.SpeakerId);
        Assert.Equal("Speaker 1", segment.SpeakerLabel);
    }

    [Fact]
    public void ApplySpeakerTurns_Leaves_Segment_Unlabeled_When_There_Is_No_Overlap()
    {
        var service = new TranscriptSpeakerAttributionService();
        var originalSegment = new TranscriptSegment(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), null, "Hello team");

        var result = service.ApplySpeakerTurns(
            [originalSegment],
            [new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(2))],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["speaker_00"] = "Speaker 1",
            });

        var segment = Assert.Single(result);
        Assert.Null(segment.SpeakerId);
        Assert.Null(segment.SpeakerLabel);
        Assert.Equal(originalSegment.Text, segment.Text);
    }
}
