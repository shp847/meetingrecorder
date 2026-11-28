using System.Text.Json.Nodes;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptSelectionCorrectionTests
{
    [Fact]
    public void CorrectsRangeAcrossPassagesAndUndoRestoresSource()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("a", "Hello there"), Segment("b", "good friend") };
        var slices = new[] { Slice(source[0], 6, 5), Slice(source[1], 0, 4) };

        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, slices,
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        var parts = TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray);
        Assert.Equal(new[] { "Hello ", "there", "good", " friend" }, parts.Select(x => x.Text));
        Assert.Equal(new[] { parts[1].SegmentId, parts[2].SegmentId },
            ((JsonArray)document["speakerAttributionOverrides"]!).Select(x => x!["segmentId"]!.GetValue<string>()));
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "op", "rev", out reason), reason);
        Assert.Equal(source, TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray));
        Assert.All(((JsonArray)document["speakerAttributionOverrides"]!), x => Assert.NotNull(x!["revertedAtUtc"]));
    }

    [Fact]
    public void SplittingPreviouslyCorrectedPassagePreservesPriorCorrectionAndUndo()
    {
        var document = JsonNode.Parse("""{"segments":[],"speakerAttributionOverrides":[{"id":"prior","artifactRevision":"rev","segmentId":"a","originalSpeakerId":"speaker","targetKind":0,"targetValue":"old","actorSource":"UserOverride","createdAtUtc":"2026-01-01T00:00:00Z","updatedAtUtc":"2026-01-01T00:00:00Z"}]}""")!.AsObject();
        var source = new[] { Segment("a", "one two three") };
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 4, 3)],
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "new", out var reason), reason);
        var parts = TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray);
        Assert.Equal(new[] { "one ", "two", " three" }, parts.Select(x => x.Text));
        var overrides = document["speakerAttributionOverrides"]!.AsArray();
        Assert.Equal("old", overrides.OfType<JsonObject>().Last(x => x["segmentId"]!.GetValue<string>() == parts[0].SegmentId)["targetValue"]!.GetValue<string>());
        Assert.Equal("new", overrides.OfType<JsonObject>().Last(x => x["segmentId"]!.GetValue<string>() == parts[1].SegmentId)["targetValue"]!.GetValue<string>());
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "op", "rev", out reason), reason);
        Assert.Null(overrides[0]!["revertedAtUtc"]);
        Assert.Equal(source, TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray));
    }

    [Fact]
    public void InvalidOrDependentEditsCannotPartiallyChangeTheDocument()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("a", "Hello world") };
        var before = document.ToJsonString();
        Assert.False(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 3, 999)],
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out _));
        Assert.Equal(before, document.ToJsonString());
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 6, 5)],
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        var selected = TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray)[1];
        document["speakerAttributionOverrides"]!.AsArray().Add(new JsonObject { ["id"] = "later", ["segmentId"] = selected.SegmentId });
        before = document.ToJsonString();
        Assert.False(TranscriptSelectionCorrectionService.TryRevert(document, "op", "rev", out _));
        Assert.Equal(before, document.ToJsonString());
    }

    [Fact]
    public void UnicodeTextElementsCannotBeSplitAndWholeCorrectionCannotBePartiallyUndone()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("a", "A e\u0301 emoji 😀 end") };
        var original = document.ToJsonString();
        Assert.False(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 3, 1)],
            "bad", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out _));
        Assert.Equal(original, document.ToJsonString());
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 2, 2)],
            "good", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        var overrideId = document["speakerSelectionCorrections"]![0]!["overrideIds"]!.AsArray().Last()!.GetValue<string>();
        Assert.False(SegmentAttributionOverrideDocumentMutator.TryRevert(document, overrideId, "rev", out reason));
        Assert.Contains("complete", reason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "good", "rev", out reason), reason);
    }

    [Fact]
    public void UndoRestoresPreviousOverrideWhenSeveralHistoricalOverridesAreActive()
    {
        var document = JsonNode.Parse("""{"segments":[],"speakerAttributionOverrides":[{"id":"old1","artifactRevision":"rev","segmentId":"a","originalSpeakerId":"speaker","targetKind":0,"targetValue":"first","actorSource":"UserOverride","createdAtUtc":"2026-01-01T00:00:00Z","updatedAtUtc":"2026-01-01T00:00:00Z"},{"id":"old2","artifactRevision":"rev","segmentId":"a","originalSpeakerId":"speaker","targetKind":0,"targetValue":"second","actorSource":"UserOverride","createdAtUtc":"2026-01-01T00:00:00Z","updatedAtUtc":"2026-01-01T00:00:00Z"}]}""")!.AsObject();
        var source = new[] { Segment("a", "one two") };
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 4, 3)],
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "third", out var reason), reason);
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "op", "rev", out reason), reason);
        var saved = document["speakerAttributionOverrides"]!.AsArray();
        Assert.Null(saved[0]!["revertedAtUtc"]);
        Assert.Null(saved[1]!["revertedAtUtc"]);
    }

    [Fact]
    public void LaterSplitOfWholePassageCorrectionBlocksUndoUntilLaterCorrectionIsUndone()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("a", "one two") };
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 0, source[0].Text.Length)],
            "first", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 4, 3)],
            "second", "rev", SegmentAttributionOverrideTargetKind.Speaker, "third", out reason), reason);
        var before = document.ToJsonString();
        Assert.False(TranscriptSelectionCorrectionService.TryRevert(document, "first", "rev", out _));
        Assert.Equal(before, document.ToJsonString());
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "second", "rev", out reason), reason);
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "first", "rev", out reason), reason);
    }

    [Fact]
    public void DuplicateSegmentIdentityCannotApplyAnAmbiguousCorrection()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("same", "words"), Segment("same", "words") };
        var before = document.ToJsonString();
        Assert.False(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 0, 5)],
            "op", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason));
        Assert.Contains("ambiguous", reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, document.ToJsonString());
    }

    [Fact]
    public void NestedCorrectionRestoresEarlierSelectionAndPreservesUndoOrder()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = new[] { Segment("a", "one two three") };
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(source[0], 4, 3)],
            "first", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        var middle = TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray)[1];
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, [Slice(middle, 1, 1)],
            "second", "rev", SegmentAttributionOverrideTargetKind.Speaker, "third", out reason), reason);
        Assert.False(TranscriptSelectionCorrectionService.TryRevert(document, "first", "rev", out _));
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "second", "rev", out reason), reason);
        var restored = TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray);
        Assert.Equal(new[] { "one ", "two", " three" }, restored.Select(part => part.Text));
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "first", "rev", out reason), reason);
        Assert.Equal(source, TranscriptPassageSplitService.Project(source, document["manualTranscriptSplits"] as JsonArray));
    }

    [Fact]
    public void LongSelectionRemainsOneUndoableChange()
    {
        var document = JsonNode.Parse("""{"segments":[]}""")!.AsObject();
        var source = Enumerable.Range(0, 1000).Select(index => Segment($"s-{index}", $"Sentence {index}.")).ToArray();
        var slices = source.Select(segment => Slice(segment, 0, segment.Text.Length)).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(TranscriptSelectionCorrectionService.TryApply(document, source, slices,
            "long", "rev", SegmentAttributionOverrideTargetKind.Speaker, "other", out var reason), reason);
        Assert.True(TranscriptSelectionCorrectionService.TryRevert(document, "long", "rev", out reason), reason);
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Long correction and undo took {watch.Elapsed.TotalSeconds:0.0} seconds.");
    }

    private static TranscriptSegment Segment(string id, string text) => new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker", "Alex", text, id);
    private static TranscriptSelectionSlice Slice(TranscriptSegment part, int offset, int length) => new(part.SegmentId!, part.SpeakerId, part.Text, offset, length);
}
