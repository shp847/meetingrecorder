using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerClusterMergeInverseResolverTests
{
    [Fact]
    public void Resolve_Allows_Only_The_Newest_Operation_And_Preserves_Earlier_History()
    {
        var first = Merge("merge-1");
        var second = Merge("merge-2");
        var decision = SpeakerClusterMergeInverseResolver.Resolve(new("merge-2", "current-revision", [first, second]));

        Assert.True(decision.CanReverse);
        Assert.Equal(second, decision.Merge);
        Assert.Equal([first], decision.RemainingMerges);
    }

    [Fact]
    public void Resolve_Rejects_Superseded_Missing_Or_Invalid_History()
    {
        var first = Merge("merge-1");
        var second = Merge("merge-2");

        Assert.Equal(SpeakerClusterMergeInverseDisposition.Superseded, SpeakerClusterMergeInverseResolver.Resolve(new("merge-1", "current-revision", [first, second])).Disposition);
        Assert.Equal(SpeakerClusterMergeInverseDisposition.NotFound, SpeakerClusterMergeInverseResolver.Resolve(new("missing", "current-revision", [first])).Disposition);
        Assert.Equal(SpeakerClusterMergeInverseDisposition.Invalid, SpeakerClusterMergeInverseResolver.Resolve(new("merge-1", "", [first])).Disposition);
    }

    private static SpeakerClusterMergeMetadata Merge(string operationId) =>
        new(operationId, "source-revision", ["speaker-a", "speaker-b"], "speaker-a", "evidence", DateTimeOffset.UtcNow);
}
