using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AutomaticMutationContainmentPolicyTests
{
    [Theory]
    [InlineData(MeetingCleanupAction.Archive)]
    [InlineData(MeetingCleanupAction.Merge)]
    [InlineData(MeetingCleanupAction.RegenerateTranscript)]
    public void CanDispatch_Holds_Automatic_Destructive_Or_Reprocessing_Actions(MeetingCleanupAction action)
    {
        Assert.False(AutomaticMutationContainmentPolicy.CanDispatch(action));
        Assert.Contains("manually", AutomaticMutationContainmentPolicy.GetReason(action), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MeetingCleanupAction.GenerateSpeakerLabels)]
    [InlineData(MeetingCleanupAction.RepairSpeakerLabels)]
    [InlineData(MeetingCleanupAction.GenerateSummary)]
    public void CanDispatch_Preserves_NonDestructive_Incremental_Actions(MeetingCleanupAction action)
    {
        Assert.True(AutomaticMutationContainmentPolicy.CanDispatch(action));
    }

    [Fact]
    public void CanDispatch_Allows_Only_V2_Continuity_Merge_Recommendations()
    {
        var continuity = new MeetingCleanupRecommendation("a", MeetingCleanupAction.Merge, MeetingCleanupConfidence.High, "t", "d", "s", ["s", "t"], true, null, null, "continuity-repair-v2-historical-title");
        var ordinary = continuity with { ReasonCode = "merge-split-pair" };

        Assert.True(AutomaticMutationContainmentPolicy.CanDispatch(continuity));
        Assert.False(AutomaticMutationContainmentPolicy.CanDispatch(ordinary));
    }
}
