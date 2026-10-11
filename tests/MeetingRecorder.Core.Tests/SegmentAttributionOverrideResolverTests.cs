using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SegmentAttributionOverrideResolverTests
{
    [Fact]
    public void Resolve_Uses_Explicit_Display_Name_Without_Mutating_Source()
    {
        var segment = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker_00", "Speaker 1", "Hello", "seg_1");
        var result = SegmentAttributionOverrideResolver.Resolve(segment, "rev_1", Speakers(), [Override("seg_1", SegmentAttributionOverrideTargetKind.DisplayName, "Alex")]);

        Assert.Equal("Alex", result.DisplayLabel);
        Assert.Equal(EffectiveSegmentAttributionState.Override, result.State);
        Assert.Equal("speaker_00", segment.SpeakerId);
        Assert.Equal("Speaker 1", segment.SpeakerLabel);
    }

    [Fact]
    public void Resolve_Requires_Current_Revision_Source_And_Target()
    {
        var segment = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker_00", "Speaker 1", "Hello", "seg_1");
        var stale = Override("seg_1", SegmentAttributionOverrideTargetKind.Speaker, "speaker_01") with { ArtifactRevision = "rev_old" };
        var missing = Override("seg_1", SegmentAttributionOverrideTargetKind.Speaker, "missing");

        Assert.Equal(EffectiveSegmentAttributionState.Source, SegmentAttributionOverrideResolver.Resolve(segment, "rev_1", Speakers(), [stale]).State);
        Assert.Equal(EffectiveSegmentAttributionState.ReviewNeeded, SegmentAttributionOverrideResolver.Resolve(segment, "rev_1", Speakers(), [missing]).State);
    }

    [Fact]
    public void Project_Uses_Effective_Label_And_Preserves_Original_Attribution()
    {
        var segment = new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker_00", "Alex", "Hello", "seg_1")
        {
            SourceSpeakerLabel = "Speaker 1",
        };

        var result = SegmentAttributionOverrideProjectionService.Project(
            [segment],
            "rev_1",
            Speakers(),
            [Override("seg_1", SegmentAttributionOverrideTargetKind.Speaker, "speaker_01")]);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Blair", row.SpeakerLabel);
        Assert.Equal("Speaker 1", row.OriginalSpeakerLabel);
        Assert.Equal("seg_1", row.SegmentId);
        Assert.True(row.HasManualAttributionOverride);
        Assert.Equal("speaker_00", Assert.Single(result.EffectiveSegments).SpeakerId);
    }

    private static IReadOnlyDictionary<string, SpeakerIdentity> Speakers() => new Dictionary<string, SpeakerIdentity> { ["speaker_01"] = new("speaker_01", "Blair", true, NameSource: SpeakerNameSource.UserEdited) };
    private static SegmentAttributionOverride Override(string segmentId, SegmentAttributionOverrideTargetKind kind, string value) => new("override_1", "rev_1", segmentId, "speaker_00", kind, value, "UserOverride", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
