namespace MeetingRecorder.Core.Services;
public sealed record DiarizationFixtureEvidenceInput(int ExpectedSpeakerCount, int DetectedSpeakerCount, int SuggestionCount, int AutoApplyCount, int AcceptedAutoApplyCount, int RejectedAutoApplyCount, int UnknownAutoApplyCount, int ElapsedMilliseconds);
public sealed record DiarizationFixtureEvidenceResult(string SpeakerCountStatus, int FalseAutoApplyCount, int UnknownAutoApplyCount, string RecognitionStatus);
public static class DiarizationFixtureEvidenceMetrics
{
    public static DiarizationFixtureEvidenceResult Calculate(DiarizationFixtureEvidenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var countStatus = input.DetectedSpeakerCount == input.ExpectedSpeakerCount ? "pass" : input.DetectedSpeakerCount < input.ExpectedSpeakerCount ? "too_few" : "too_many";
        var falseAuto = Math.Max(0, input.AutoApplyCount - input.AcceptedAutoApplyCount - input.UnknownAutoApplyCount);
        var recognition = falseAuto > 0 ? "false_auto_apply" : input.UnknownAutoApplyCount > 0 ? "unknown_mapping" : input.AutoApplyCount == 0 && input.SuggestionCount == 0 ? "no_match" : "pass";
        return new(countStatus, falseAuto, Math.Max(0, input.UnknownAutoApplyCount), recognition);
    }
}
