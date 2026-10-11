using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class DiarizationFixtureEvidenceMetricsTests
{
    [Fact] public void Calculate_SeparatesFalseAndUnknownAutomaticNames() { var r=DiarizationFixtureEvidenceMetrics.Calculate(new(2,3,1,3,1,0,1,42)); Assert.Equal("too_many",r.SpeakerCountStatus); Assert.Equal(1,r.FalseAutoApplyCount); Assert.Equal(1,r.UnknownAutoApplyCount); Assert.Equal("false_auto_apply",r.RecognitionStatus); }
    [Fact] public void Calculate_DoesNotTreatUnknownAsTrueMatch() { var r=DiarizationFixtureEvidenceMetrics.Calculate(new(1,1,0,1,0,0,1,1)); Assert.Equal("unknown_mapping",r.RecognitionStatus); Assert.Equal(0,r.FalseAutoApplyCount); }
}
