using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class SpeakerQualityDiagnosisResolverTests
{
    [Fact] public void Resolve_UnknownMetadata_NeverClaimsBadDiarization() => Assert.Equal(SpeakerQualitySeverity.Unknown, SpeakerQualityDiagnosisResolver.Resolve(Input() with { HasCurrentMetadata = false }).Severity);
    [Fact] public void Resolve_FragmentedLabels_RoutesToRepairOnlyWhenReady() { var repair = SpeakerQualityDiagnosisResolver.Resolve(Input() with { ClusterCount = 10, TinyTurnRatio = .6d }); var wait = SpeakerQualityDiagnosisResolver.Resolve(Input() with { ClusterCount = 10, TinyTurnRatio = .6d, IsRepairReady = false }); Assert.Equal(SpeakerQualityRoute.Repair, repair.Route); Assert.Equal(SpeakerQualityRoute.SetupOrWait, wait.Route); }
    [Fact] public void Resolve_PreservesNamingAndMergeAsSeparateRoutes() { Assert.Equal(SpeakerQualityRoute.Merge, SpeakerQualityDiagnosisResolver.Resolve(Input() with { HasNormalSplitCandidate = true }).Route); Assert.Equal(SpeakerQualityRoute.Name, SpeakerQualityDiagnosisResolver.Resolve(Input()).Route); }
    private static SpeakerQualityDiagnosisInput Input() => new(true, false, true, 2, 0, 0, 0, 0, true, false, false, true);
}
