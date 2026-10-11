using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerClusterMergeResolverTests
{
    [Fact]
    public void Resolve_Composes_Selected_Clusters_Into_The_Chosen_Canonical_Speaker()
    {
        var decision = SpeakerClusterMergeResolver.Resolve(Request(["speaker-a", "speaker-b", "speaker-c"], "speaker-b"));

        Assert.Equal(SpeakerClusterMergeDisposition.Accepted, decision.Disposition);
        Assert.Equal("speaker-b", decision.CanonicalBySpeakerId["speaker-a"]);
        Assert.Equal("speaker-b", decision.CanonicalBySpeakerId["speaker-b"]);
        Assert.Equal("speaker-b", decision.CanonicalBySpeakerId["speaker-c"]);
    }

    [Fact]
    public void Resolve_Rejects_Invalid_Or_Cyclic_Requests()
    {
        Assert.Equal(SpeakerClusterMergeDisposition.Invalid, SpeakerClusterMergeResolver.Resolve(Request(["speaker-a"], "speaker-a")).Disposition);
        Assert.Equal(
            SpeakerClusterMergeDisposition.Invalid,
            SpeakerClusterMergeResolver.Resolve(Request(["speaker-a", "speaker-b"], "speaker-a"), new Dictionary<string, string> { ["speaker-a"] = "speaker-b", ["speaker-b"] = "speaker-a" }).Disposition);
    }

    [Fact]
    public void Resolve_Rejects_A_Conflicting_Preexisting_Merge()
    {
        var decision = SpeakerClusterMergeResolver.Resolve(
            Request(["speaker-a", "speaker-b"], "speaker-b"),
            new Dictionary<string, string> { ["speaker-a"] = "speaker-c" });

        Assert.Equal(SpeakerClusterMergeDisposition.ConflictingExistingMerge, decision.Disposition);
    }

    [Fact]
    public void Resolve_Returns_Redundant_When_All_Selected_Speakers_Already_Point_To_Canonical()
    {
        var decision = SpeakerClusterMergeResolver.Resolve(
            Request(["speaker-a", "speaker-b"], "speaker-b"),
            new Dictionary<string, string> { ["speaker-a"] = "speaker-b" });

        Assert.Equal(SpeakerClusterMergeDisposition.Redundant, decision.Disposition);
    }

    [Fact]
    public void BuildCanonicalMap_Composes_Persisted_Merge_Chains_Without_Changing_Source_Ids()
    {
        var map = SpeakerClusterMergeResolver.BuildCanonicalMap(
        [
            new SpeakerClusterMergeMetadata("merge-1", "revision-1", ["speaker-a", "speaker-b"], "speaker-a", "evidence-1", DateTimeOffset.UtcNow),
            new SpeakerClusterMergeMetadata("merge-2", "revision-2", ["speaker-a", "speaker-c"], "speaker-c", "evidence-2", DateTimeOffset.UtcNow),
        ]);

        Assert.Equal("speaker-c", map["speaker-a"]);
        Assert.Equal("speaker-c", map["speaker-b"]);
    }

    private static SpeakerClusterMergeRecord Request(IReadOnlyList<string> sourceIds, string canonicalId) =>
        new("operation-1", "meeting-1", "revision-1", sourceIds, canonicalId, "evidence-1");
}
