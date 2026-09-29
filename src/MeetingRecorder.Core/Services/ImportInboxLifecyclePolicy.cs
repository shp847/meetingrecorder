using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Separates safe Inbox discovery from worker admission. Discovery may record
/// an Inbox-owned file while transcription setup is unavailable; queueing still
/// requires setup and remains an explicit normal-import transition.
/// </summary>
public static class ImportInboxLifecyclePolicy
{
    public static bool ShouldReconcile(
        AppConfig config,
        DateTimeOffset? lastReconciliationUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.ImportInboxEnabled)
        {
            return false;
        }

        if (!lastReconciliationUtc.HasValue)
        {
            return true;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, config.ImportInboxScanIntervalSeconds));
        return nowUtc.ToUniversalTime() - lastReconciliationUtc.Value.ToUniversalTime() >= interval;
    }

    public static bool MayQueueDiscoveredWork(AppConfig config, bool hasReadyTranscriptionModel)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.ImportInboxEnabled && hasReadyTranscriptionModel;
    }
}
