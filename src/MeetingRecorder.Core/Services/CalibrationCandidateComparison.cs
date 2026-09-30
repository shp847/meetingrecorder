namespace MeetingRecorder.Core.Services;
public enum CalibrationCandidateStatus { Inconclusive, Rejected, EligibleForManualPromotion }
public sealed record CalibrationCandidateComparisonInput(int RequiredProtectedCases, int EnabledProtectedCases, int BaselineCorrectCases, int CandidateCorrectCases, int BaselineFalseAutoApplyCount, int CandidateFalseAutoApplyCount, int ProtectedRegressionCount, int CandidateFailureCount);
public sealed record CalibrationCandidateComparisonResult(CalibrationCandidateStatus Status, string Reason);
public static class CalibrationCandidateComparison
{
    public static CalibrationCandidateComparisonResult Evaluate(CalibrationCandidateComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.EnabledProtectedCases < input.RequiredProtectedCases) return new(CalibrationCandidateStatus.Inconclusive, "Required protected fixture coverage is incomplete.");
        if (input.CandidateFailureCount > 0 || input.ProtectedRegressionCount > 0 || input.CandidateFalseAutoApplyCount > input.BaselineFalseAutoApplyCount) return new(CalibrationCandidateStatus.Rejected, "Candidate regresses protected evidence or increases false automatic naming.");
        if (input.CandidateCorrectCases <= input.BaselineCorrectCases) return new(CalibrationCandidateStatus.Inconclusive, "Candidate has no measured practical improvement over baseline.");
        return new(CalibrationCandidateStatus.EligibleForManualPromotion, "Candidate improves protected evidence without new false automatic naming; human review and holdout replay are required.");
    }
}
