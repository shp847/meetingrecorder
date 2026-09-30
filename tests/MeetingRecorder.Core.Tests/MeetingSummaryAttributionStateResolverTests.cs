using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class MeetingSummaryAttributionStateResolverTests
{
    [Fact] public void Fingerprint_ChangesForLabelsButNotIrrelevantMetadata() { var a = MeetingSummaryAttributionStateResolver.ComputeAttributionFingerprint("r1", [new("s1", "sp1", "Speaker 1", "Generic")], "m1"); var changed = MeetingSummaryAttributionStateResolver.ComputeAttributionFingerprint("r1", [new("s1", "sp1", "Alex", "UserEntered")], "m1"); var same = MeetingSummaryAttributionStateResolver.ComputeAttributionFingerprint("r1", [new("s1", "sp1", "Speaker 1", "Generic")], "m1"); Assert.NotEqual(a, changed); Assert.Equal(a, same); }
    [Fact] public void Resolve_KeepsOldSummaryReadableAndMarksExactStaleness() { var attribution = MeetingSummaryAttributionStateResolver.Resolve(new(true, false, false, "t1", "a2", new("t1", "a1", 1))); var transcript = MeetingSummaryAttributionStateResolver.Resolve(new(true, false, false, "t2", "a1", new("t1", "a1", 1))); Assert.Equal(MeetingSummaryAttributionState.AttributionChanged, attribution.State); Assert.True(attribution.CanRegenerate); Assert.Equal(MeetingSummaryAttributionState.TranscriptChanged, transcript.State); }
}
