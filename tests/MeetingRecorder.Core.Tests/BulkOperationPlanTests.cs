using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class BulkOperationPlanTests
{
    [Fact]
    public void Create_Shows_Immutable_Per_Target_Eligibility_And_Queue_Risk()
    {
        var plan = BulkOperationPlanner.Create(BulkOperationKind.AddSpeakerLabels,
        [
            new("one", "One", 2, "Needs Attention", true, null),
            new("two", "Two", 3, "Needs Attention", false, "Transcript is unavailable."),
        ]);
        Assert.Equal(1, plan.EligibleCount); Assert.Equal(1, plan.BlockedCount);
        Assert.True(plan.Previews.Single(item => item.MeetingId == "one").QueuesWork);
        Assert.Equal("Transcript is unavailable.", plan.Previews.Single(item => item.MeetingId == "two").Reason);
    }

    [Fact]
    public void Create_Describes_Archive_Recovery_Without_Claiming_Delete()
    {
        var plan = BulkOperationPlanner.Create(BulkOperationKind.Archive, [new("one", "One", 1, "Recent", true, null)]);
        var preview = Assert.Single(plan.Previews);
        Assert.True(preview.IsIrreversibleRisk);
        Assert.Contains("receipt", preview.Recoverability, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summarize_Keeps_Queued_Failed_Skipped_And_Cancelled_Distinct()
    {
        var result = BulkOperationPlanner.Summarize([new("one", BulkOperationOutcomeKind.Queued, "Queued."), new("two", BulkOperationOutcomeKind.Failed, "Failed."), new("three", BulkOperationOutcomeKind.Skipped, "Changed."), new("four", BulkOperationOutcomeKind.Cancelled, "Cancelled.")]);
        Assert.Equal(1, result.QueuedCount); Assert.Equal(1, result.FailedCount); Assert.Equal(1, result.SkippedCount); Assert.Equal(1, result.CancelledCount);
    }
}
