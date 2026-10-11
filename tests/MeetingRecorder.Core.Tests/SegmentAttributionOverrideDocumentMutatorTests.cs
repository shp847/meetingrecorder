using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Tests;

public sealed class SegmentAttributionOverrideDocumentMutatorTests
{
    [Fact]
    public void Apply_Preserves_Source_And_Adds_Override()
    {
        var doc = JsonNode.Parse("""{ "segments":[{"segmentId":"seg_1","speakerId":"speaker_0","speakerLabel":"Speaker 1","text":"Keep","start":"00:00:00","end":"00:00:02"}] }""")!.AsObject();
        var item = new SegmentAttributionOverride("o1", "r1", "seg_1", "speaker_0", SegmentAttributionOverrideTargetKind.DisplayName, "Alex", "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.True(SegmentAttributionOverrideDocumentMutator.TryApply(doc, item, out _));
        Assert.Equal("Keep", doc["segments"]![0]! ["text"]!.GetValue<string>());
        Assert.Equal("speaker_0", doc["segments"]![0]!["speakerId"]!.GetValue<string>());
        Assert.Equal("Alex", doc["segments"]![0]!["speakerLabel"]!.GetValue<string>());
        Assert.Single(doc["speakerAttributionOverrides"]!.AsArray());
    }

    [Fact]
    public void Apply_Rejects_Changed_Source_Speaker()
    {
        var doc = JsonNode.Parse("""{ "segments":[{"segmentId":"seg_1","speakerId":"speaker_new"}] }""")!.AsObject();
        var item = new SegmentAttributionOverride("o1", "r1", "seg_1", "speaker_old", SegmentAttributionOverrideTargetKind.DisplayName, "Alex", "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.False(SegmentAttributionOverrideDocumentMutator.TryApply(doc, item, out var reason));
        Assert.Equal("The transcript segment changed.", reason);
        Assert.Null(doc["speakerAttributionOverrides"]);
    }
}
