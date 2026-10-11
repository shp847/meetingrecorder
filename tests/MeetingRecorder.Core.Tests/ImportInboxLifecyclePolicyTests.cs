using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxLifecyclePolicyTests
{
    [Fact]
    public void ShouldReconcile_UsesBoundedCadenceAndNeverRunsWhenDisabled()
    {
        var now = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var enabled = new AppConfig { ImportInboxEnabled = true, ImportInboxScanIntervalSeconds = 60 };

        Assert.True(ImportInboxLifecyclePolicy.ShouldReconcile(enabled, null, now));
        Assert.False(ImportInboxLifecyclePolicy.ShouldReconcile(enabled, now.AddSeconds(-59), now));
        Assert.True(ImportInboxLifecyclePolicy.ShouldReconcile(enabled, now.AddSeconds(-60), now));
        Assert.False(ImportInboxLifecyclePolicy.ShouldReconcile(enabled with { ImportInboxEnabled = false }, null, now));
    }

    [Fact]
    public void MayQueueDiscoveredWork_RequiresBothOptInAndReadyTranscription()
    {
        var enabled = new AppConfig { ImportInboxEnabled = true };

        Assert.True(ImportInboxLifecyclePolicy.MayQueueDiscoveredWork(enabled, hasReadyTranscriptionModel: true));
        Assert.False(ImportInboxLifecyclePolicy.MayQueueDiscoveredWork(enabled, hasReadyTranscriptionModel: false));
        Assert.False(ImportInboxLifecyclePolicy.MayQueueDiscoveredWork(enabled with { ImportInboxEnabled = false }, true));
    }
}
