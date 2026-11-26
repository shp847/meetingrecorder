using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class CleanupReviewProjectionTests
{
    [Fact]
    public void Separates_Automatic_Backlog_And_Optional_Suggestions_From_Attention()
    {
        var automatic = Recommendation(MeetingCleanupAction.GenerateSpeakerLabels, true);
        Assert.False(CleanupReviewProjection.Resolve(automatic, null).NeedsAttention);
        Assert.Equal("Automatic backlog", CleanupReviewProjection.Resolve(automatic, null).Label);
        Assert.False(CleanupReviewProjection.Resolve(Recommendation(MeetingCleanupAction.Rename, false), null).NeedsAttention);
        Assert.True(CleanupReviewProjection.Resolve(Recommendation(MeetingCleanupAction.GenerateSpeakerLabels, false), null).NeedsAttention);
    }

    [Theory]
    [InlineData((int)CleanupWorkState.Failed, true)]
    [InlineData((int)CleanupWorkState.ManualReview, true)]
    [InlineData((int)CleanupWorkState.Queued, false)]
    [InlineData((int)CleanupWorkState.Processing, false)]
    [InlineData((int)CleanupWorkState.Completed, false)]
    public void Uses_Ledger_State_To_Distinguish_Review_From_Active_Work(int state, bool expectedAttention)
    {
        var entry = new CleanupWorkLedgerEntry("fingerprint", (CleanupWorkState)state, DateTimeOffset.UtcNow);
        Assert.Equal(expectedAttention, CleanupReviewProjection.Resolve(Recommendation(MeetingCleanupAction.GenerateSpeakerLabels, true), entry).NeedsAttention);
    }

    private static MeetingCleanupRecommendation Recommendation(MeetingCleanupAction action, bool automatic) =>
        new("fingerprint", action, MeetingCleanupConfidence.High, "title", "description", "meeting", ["meeting"], automatic, null, null);
}
