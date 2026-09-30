using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class CalibrationCandidateComparisonTests
{
    [Fact] public void MissingCoverage_IsInconclusive() => Assert.Equal(CalibrationCandidateStatus.Inconclusive, CalibrationCandidateComparison.Evaluate(Input() with { EnabledProtectedCases = 2 }).Status);
    [Fact] public void RegressionOrFalseAuto_IsRejected() { Assert.Equal(CalibrationCandidateStatus.Rejected,CalibrationCandidateComparison.Evaluate(Input() with { ProtectedRegressionCount=1 }).Status); Assert.Equal(CalibrationCandidateStatus.Rejected,CalibrationCandidateComparison.Evaluate(Input() with { CandidateFalseAutoApplyCount=1 }).Status); }
    [Fact] public void ImprovementIsManualPromotionOnly() { var r=CalibrationCandidateComparison.Evaluate(Input() with { CandidateCorrectCases=9 }); Assert.Equal(CalibrationCandidateStatus.EligibleForManualPromotion,r.Status); Assert.Contains("human review",r.Reason); }
    private static CalibrationCandidateComparisonInput Input() => new(3,3,7,7,0,0,0,0);
}
