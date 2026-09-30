using MeetingRecorder.Core.Services;
namespace MeetingRecorder.Core.Tests;
public sealed class VoiceProfileLifecycleResolverTests
{
    [Fact] public void Delete_RequiresConfirmationAndNeverChangesHistoricNames() { var r = VoiceProfileLifecycleResolver.Resolve(new(VoiceProfileLifecycleAction.Delete, 1, 1, true, false, true)); Assert.True(r.RequiresConfirmation); Assert.False(r.ChangesHistoricAttribution); Assert.Contains("sensitive", r.Explanation); }
    [Fact] public void StoreFailureAndActiveMutation_BlockProfileChange() { Assert.Equal(VoiceProfileLifecycleDisposition.Blocked, VoiceProfileLifecycleResolver.Resolve(new(VoiceProfileLifecycleAction.Disable, 1, 1, false, false, true)).Disposition); Assert.Equal(VoiceProfileLifecycleDisposition.Blocked, VoiceProfileLifecycleResolver.Resolve(new(VoiceProfileLifecycleAction.Disable, 1, 1, true, true, true)).Disposition); }
    [Fact] public void DisableAndEnable_AffectFutureMatchingOnly() { var r = VoiceProfileLifecycleResolver.Resolve(new(VoiceProfileLifecycleAction.Disable, 1, 1, true, false, true)); Assert.Equal(VoiceProfileLifecycleDisposition.Ready, r.Disposition); Assert.Contains("future", r.Explanation); }
}
