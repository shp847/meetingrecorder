using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class DiarizationPromotionGateTests
{
    [Fact] public void ProtectedFalseAutoApply_AlwaysBlocksPromotion() { var r = DiarizationPromotionGate.Evaluate(Metrics(), Metrics() with { ProtectedFalseAutoApplyCount = 1 }); Assert.False(r.CanPromote); Assert.Contains("false automatic", r.Explanation); }
    [Fact] public void CompleteNonRegressingCandidate_StillRequiresHumanReview() { var r = DiarizationPromotionGate.Evaluate(Metrics(), Metrics()); Assert.True(r.CanPromote); Assert.Contains("human review", r.Explanation); }
    private static DiarizationCalibrationMetrics Metrics() => new(10, 3, 0, 0, 10, 10, 0);
}
