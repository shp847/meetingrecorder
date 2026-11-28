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

    [Fact]
    public void Apply_Unknown_Speaker_Only_Corrects_Selected_Segment()
    {
        var doc = JsonNode.Parse("""{ "segments":[{"segmentId":"one","speakerLabel":"Speaker","text":"First"},{"segmentId":"two","speakerLabel":"Speaker","text":"Second"}] }""")!.AsObject();
        var correction = new SegmentAttributionOverride("o1", "r1", "one", null,
            SegmentAttributionOverrideTargetKind.DisplayName, "Alex", "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.True(SegmentAttributionOverrideDocumentMutator.TryApply(doc, correction, out _));
        Assert.Equal("Speaker", doc["segments"]![0]!["displaySpeakerLabel"]!.GetValue<string>());
        Assert.Equal("Alex", doc["segments"]![0]!["speakerLabel"]!.GetValue<string>());
        Assert.Equal("Speaker", doc["segments"]![1]!["speakerLabel"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"segmentId\":\"one\",\"speakerId\":\"a\",\"SpeakerLabel\":\"Speaker 1\",\"text\":\"Words\"}")]
    [InlineData("{\"segmentId\":\"one\",\"speakerId\":\"a\",\"text\":\"Words\"}")]
    public async Task Apply_Legacy_Label_Shapes_Keep_Stable_Source_Revision(string segmentJson)
    {
        var path = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"segments\":[" + segmentJson + "]}");
        var record = new MeetingOutputRecord("legacy", "Legacy", DateTimeOffset.UtcNow, MeetingPlatform.Teams,
            null, null, null, path, null, null, SessionState.Published, Array.Empty<MeetingAttendee>(), true, "model.bin");
        var before = MeetingOutputCatalogService.GetSegmentAttributionRevision(record);
        var doc = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var item = new SegmentAttributionOverride("o1", before, "one", "a", SegmentAttributionOverrideTargetKind.DisplayName,
            "Alex", "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.True(SegmentAttributionOverrideDocumentMutator.TryApply(doc, item, out var reason), reason);
        await File.WriteAllTextAsync(path, doc.ToJsonString());
        Assert.Equal(before, MeetingOutputCatalogService.GetSegmentAttributionRevision(record));
        var transcript = MeetingTranscriptDocumentReader.Read(path, null);
        Assert.NotEqual("Alex", Assert.Single(transcript.StructuredSegments).SourceSpeakerLabel);
    }
}
