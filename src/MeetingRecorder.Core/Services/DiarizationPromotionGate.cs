namespace MeetingRecorder.Core.Services;
public sealed record DiarizationCalibrationMetrics(int SampleCount, int ProtectedSampleCount, int FalseAutoApplyCount, int ProtectedFalseAutoApplyCount, int CorrectSpeakerCount, int ExpectedSpeakerCount, int UnknownCount);
public sealed record DiarizationPromotionDecision(bool CanPromote, string Explanation);
public static class DiarizationPromotionGate
{
    public static DiarizationPromotionDecision Evaluate(DiarizationCalibrationMetrics baseline, DiarizationCalibrationMetrics candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline); ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.SampleCount == 0 || candidate.ProtectedSampleCount == 0) return new(false, "Calibration evidence is incomplete; promotion needs a non-empty protected set.");
        if (candidate.ProtectedFalseAutoApplyCount > 0) return new(false, "A protected calibration case produced a false automatic name. Threshold promotion is blocked.");
        if (candidate.CorrectSpeakerCount < candidate.ExpectedSpeakerCount) return new(false, "Candidate speaker-count correctness is below the required corpus expectation.");
        if (candidate.FalseAutoApplyCount > baseline.FalseAutoApplyCount) return new(false, "Candidate increases false automatic naming and cannot be promoted.");
        return new(true, "Candidate passes the protected false-attribution and speaker-count gates; human review is still required.");
    }
}
