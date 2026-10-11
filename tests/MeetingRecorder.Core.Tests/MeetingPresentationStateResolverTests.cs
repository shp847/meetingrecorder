using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingPresentationStateResolverTests
{
    private readonly MeetingPresentationStateResolver _resolver = new();

    [Theory]
    [InlineData(false, true, MeetingPresentationState.Unavailable)]
    [InlineData(true, false, MeetingPresentationState.RefreshRequired)]
    public void Missing_Or_Stale_Data_Never_Claims_Complete(bool catalogEntry, bool fresh, MeetingPresentationState expected) =>
        Assert.Equal(expected, _resolver.Resolve(Input(HasCatalogEntry: catalogEntry, IsCatalogFresh: fresh)).State);

    [Fact]
    public void Precedence_Protects_Archive_Failure_Block_And_Queue_Truth()
    {
        Assert.Equal(MeetingPresentationState.Archived, _resolver.Resolve(Input(IsArchived: true, HasArtifactFailure: true)).State);
        Assert.Equal(MeetingPresentationState.FailedOrNeedsAttention, _resolver.Resolve(Input(HasArtifactFailure: true, IsSetupBlocked: true)).State);
        Assert.Equal(MeetingPresentationState.Blocked, _resolver.Resolve(Input(IsSetupBlocked: true, QueueState: MeetingPresentationQueueState.Running)).State);
        Assert.Equal(MeetingPresentationState.Processing, _resolver.Resolve(Input(QueueState: MeetingPresentationQueueState.Running, HasActionableRecommendation: true)).State);
    }

    [Fact]
    public void Recommendation_Is_One_Safe_Presentation_Action_And_Complete_Requires_Both_Artifacts()
    {
        var recommendation = _resolver.Resolve(Input(HasActionableRecommendation: true));
        var complete = _resolver.Resolve(Input(HasReadableAudio: true, HasReadableTranscript: true));
        var missing = _resolver.Resolve(Input(HasReadableAudio: true));

        Assert.Equal(MeetingPresentationState.NeedsAction, recommendation.State);
        Assert.Equal(MeetingActionId.ReviewRecommendation, recommendation.PrimaryAction);
        Assert.Equal(MeetingPresentationState.Complete, complete.State);
        Assert.Equal(MeetingPresentationState.Unavailable, missing.State);
        Assert.DoesNotContain("path", complete.AccessibleDescription, StringComparison.OrdinalIgnoreCase);
    }

    private static MeetingPresentationStateInput Input(
        bool HasCatalogEntry = true,
        bool IsCatalogFresh = true,
        bool IsArchived = false,
        bool HasArtifactFailure = false,
        bool IsQueueFresh = true,
        MeetingPresentationQueueState QueueState = MeetingPresentationQueueState.None,
        bool IsSetupBlocked = false,
        bool HasActionableRecommendation = false,
        bool HasReadableAudio = false,
        bool HasReadableTranscript = false) =>
        new(HasCatalogEntry, IsCatalogFresh, IsArchived, HasArtifactFailure, IsQueueFresh, QueueState, IsSetupBlocked, HasActionableRecommendation, HasReadableAudio, HasReadableTranscript);
}
