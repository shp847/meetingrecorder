using System.Text.Json.Nodes;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptPassageSplitTests
{
    [Fact]
    public void SplitsPreserveTextTimingAndSourceAndCanBeUndoneInOrder()
    {
        var doc = JsonNode.Parse("""{"segments":[{"segmentId":"one","speakerId":"speaker-a","speakerLabel":"Alex","text":"Hello there friend"}]}""")!.AsObject();
        var source = new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), "speaker-a", "Alex", "Hello there friend", "one");

        Assert.True(TranscriptPassageSplitService.TryApply(doc, [source], "one", 6, "split1", "rev", out var reason), reason);
        var first = TranscriptPassageSplitService.Project([source], doc["manualTranscriptSplits"] as JsonArray);
        Assert.Equal(new[] { "Hello ", "there friend" }, first.Select(part => part.Text));
        Assert.All(first, part => { Assert.Equal(source.Start, part.Start); Assert.Equal(source.End, part.End); Assert.Equal(source.SpeakerId, part.SpeakerId); });
        Assert.True(TranscriptPassageSplitService.TryApply(doc, [source], first[1].SegmentId!, 6, "split2", "rev", out reason), reason);
        var parts = TranscriptPassageSplitService.Project([source], doc["manualTranscriptSplits"] as JsonArray);
        Assert.Equal(source.Text, string.Concat(parts.Select(part => part.Text)));
        Assert.False(TranscriptPassageSplitService.TryRevert(doc, "split1", out _));
        Assert.True(TranscriptPassageSplitService.TryRevert(doc, "split2", out reason), reason);
        Assert.True(TranscriptPassageSplitService.TryRevert(doc, "split1", out reason), reason);
        Assert.Equal(source, Assert.Single(TranscriptPassageSplitService.Project([source], doc["manualTranscriptSplits"] as JsonArray)));
    }

    [Fact]
    public void SplitRejectsInvalidBoundariesAndUndoProtectsChildCorrections()
    {
        var doc = JsonNode.Parse("""{"segments":[{"segmentId":"one","speakerId":"a","text":"Hi😀 there"}]}""")!.AsObject();
        var source = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a", "A", "Hi😀 there", "one");
        Assert.False(TranscriptPassageSplitService.TryApply(doc, [source], "one", 3, "bad", "rev", out _));
        Assert.False(TranscriptPassageSplitService.TryApply(doc, [source], "one", 0, "bad", "rev", out _));
        Assert.True(TranscriptPassageSplitService.TryApply(doc, [source], "one", 4, "good", "rev", out var reason), reason);
        var child = TranscriptPassageSplitService.Project([source], doc["manualTranscriptSplits"] as JsonArray)[1];
        doc["speakerAttributionOverrides"] = new JsonArray(new JsonObject { ["segmentId"] = child.SegmentId });
        Assert.False(TranscriptPassageSplitService.TryRevert(doc, "good", out _));
    }

    [Fact]
    public void InvalidStoredSplitIsReportedAndBlocksNewSplits()
    {
        var doc = JsonNode.Parse("""{"manualTranscriptSplits":[{"id":"old","parentSegmentId":"one","leftSegmentId":"left","rightSegmentId":"right","offset":2,"textHash":"stale"}]}""")!.AsObject();
        var source = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a", "A", "New words", "one");
        var projected = TranscriptPassageSplitService.Project([source], doc["manualTranscriptSplits"] as JsonArray, out var invalid);
        Assert.Equal(1, invalid);
        Assert.Equal(source, Assert.Single(projected));
        Assert.False(TranscriptPassageSplitService.TryApply(doc, [source], "one", 4, "new", "rev", out var reason));
        Assert.Contains("need review", reason, StringComparison.OrdinalIgnoreCase);
    }
}
