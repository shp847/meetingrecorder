using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSpeakerBlockTests
{
    [Fact]
    public void ConsecutiveResolvedSpeakersFormOneBlockAndKeepSourceOffsets()
    {
        var rows = new[]
        {
            Row("one", "a", "Hello"), Row("two", "a", "world!"),
            Row("three", "b", "Yes."), Row("four", "a", "Again.")
        };

        var blocks = TranscriptSpeakerBlockBuilder.Build(rows);

        Assert.Equal(3, blocks.Count);
        Assert.Equal("Hello world!", blocks[0].Text);
        Assert.Equal(new[] { "one", "two" }, blocks[0].Parts.Select(part => part.Row.SegmentId));
        Assert.Equal(new[] { 0, 6 }, blocks[0].Parts.Select(part => part.Start));
        Assert.Equal("Yes.", blocks[1].Text);
        Assert.Equal("Again.", blocks[2].Text);
    }

    [Fact]
    public void UnknownOrReviewNeededIdentitiesNeverMergeAndPunctuationStaysAttached()
    {
        var rows = new[]
        {
            Row("one", null, "Who"), Row("two", null, "knows"),
            Row("three", "a", "Hello"), Row("four", "a", ", friend"),
            Row("five", "a", "untrusted") with { AttributionState = EffectiveSegmentAttributionState.ReviewNeeded }
        };

        var blocks = TranscriptSpeakerBlockBuilder.Build(rows);

        Assert.Equal(4, blocks.Count);
        Assert.Equal("Hello, friend", blocks[2].Text);
    }

    [Fact]
    public void SameNamesDoNotMergeDifferentSpeakersAndExistingSplitTextIsNotChanged()
    {
        var rows = new[]
        {
            Row("left", "a", "un") with { ManualSplitId = "split" },
            Row("right", "a", "broken") with { ManualSplitId = "split" },
            Row("other", "b", "word")
        };
        var blocks = TranscriptSpeakerBlockBuilder.Build(rows);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("unbroken", blocks[0].Text);
    }

    [Fact]
    public void SelectionMapsAcrossOriginalSegmentsAndSkipsJoinSpacing()
    {
        var block = Assert.Single(TranscriptSpeakerBlockBuilder.Build([Row("one", "a", "Hello there"), Row("two", "a", "good friend")])).AsRow();
        var slices = TranscriptSpeakerBlockBuilder.GetSelection(block, 6, 10);
        Assert.Equal(new[] { "one", "two" }, slices.Select(slice => slice.SegmentId));
        Assert.Equal(new[] { (6, 5), (0, 4) }, slices.Select(slice => (slice.Start, slice.Length)));
        Assert.Empty(TranscriptSpeakerBlockBuilder.GetSelection(block, 11, 1));
    }

    [Fact]
    public void EffectiveTargetIdentityGroupsCorrectionsWithTheirActualSpeaker()
    {
        var segments = new[]
        {
            new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a", "Alex", "Hello", "one"),
            new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "b", "Blair", "again", "two"),
            new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "c", "Blair", "different person", "three"),
        };
        var speakers = new[] { new SpeakerIdentity("a", "Alex", false), new SpeakerIdentity("b", "Blair", false), new SpeakerIdentity("c", "Blair", false) }
            .ToDictionary(s => s.Id, StringComparer.Ordinal);
        var correction = new SegmentAttributionOverride("override", "rev", "one", "a", SegmentAttributionOverrideTargetKind.Speaker,
            "b", "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var projection = SegmentAttributionOverrideProjectionService.Project(segments, "rev", speakers, [correction]);
        var blocks = TranscriptSpeakerBlockBuilder.Build(projection.Rows);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("Hello again", blocks[0].Text);
        Assert.Equal("b", blocks[0].AsRow().InlineRenameSpeakerId);
    }

    [Fact]
    public void NestedSplitsKeepExactTextAndOffsetsWhenGrouped()
    {
        var document = new System.Text.Json.Nodes.JsonObject();
        var source = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a", "Alex", "unbroken", "one");
        Assert.True(TranscriptPassageSplitService.TryApply(document, [source], "one", 2, "first", "rev", out var reason), reason);
        Assert.True(TranscriptPassageSplitService.TryApply(document, [source], "first:right", 3, "second", "rev", out reason), reason);
        var parts = TranscriptPassageSplitService.Project([source], document["manualTranscriptSplits"] as System.Text.Json.Nodes.JsonArray);
        var speakers = new[] { new SpeakerIdentity("a", "Alex", false) }.ToDictionary(s => s.Id);
        var rows = SegmentAttributionOverrideProjectionService.Project(parts, "rev", speakers, []).Rows;
        var block = Assert.Single(TranscriptSpeakerBlockBuilder.Build(rows));
        Assert.Equal("unbroken", block.Text);
        Assert.Equal(new[] { 0, 2, 5 }, block.Parts.Select(part => part.Row.SourceTextOffset));
    }

    private static MeetingTranscriptSegmentRow Row(string segmentId, string? identity, string text) =>
        new("00:00:00", "Alex", text, identity, segmentId, EffectiveSpeakerKey: identity);
}
