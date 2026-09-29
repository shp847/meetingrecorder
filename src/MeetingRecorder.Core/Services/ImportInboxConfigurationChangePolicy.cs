using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Prevents a saved Inbox path/policy change from stranding discovery or an
/// archive lease that was created under the current Inbox ownership boundary.
/// </summary>
public static class ImportInboxConfigurationChangePolicy
{
    public static ImportInboxConfigurationChangeDecision Evaluate(
        AppConfig currentConfig,
        AppConfig nextConfig,
        ImportInboxJournal currentJournal)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(nextConfig);
        ArgumentNullException.ThrowIfNull(currentJournal);

        var inboxLocationChanged = !string.Equals(
            currentConfig.ImportInboxDir,
            nextConfig.ImportInboxDir,
            StringComparison.OrdinalIgnoreCase);
        var activeEntry = currentJournal.Entries.Any(entry => entry.State is
            ImportInboxEntryState.Discovered or
            ImportInboxEntryState.Ready or
            ImportInboxEntryState.Leased or
            ImportInboxEntryState.ArchivePending);
        if (inboxLocationChanged && activeEntry)
        {
            return new ImportInboxConfigurationChangeDecision(
                false,
                "Finish or recover active Inbox items before changing the Inbox location.");
        }

        var archiveChangeInterruptsPendingMove =
            currentConfig.ImportInboxArchiveAfterQueueEnabled != nextConfig.ImportInboxArchiveAfterQueueEnabled &&
            currentJournal.Entries.Any(entry => entry.State == ImportInboxEntryState.ArchivePending);
        if (archiveChangeInterruptsPendingMove)
        {
            return new ImportInboxConfigurationChangeDecision(
                false,
                "Finish or recover the pending Inbox archive before changing the archive policy.");
        }

        var errorChangeInterruptsPendingMove =
            currentConfig.ImportInboxMoveBlockedToErrorEnabled != nextConfig.ImportInboxMoveBlockedToErrorEnabled &&
            currentJournal.Entries.Any(entry => entry.State == ImportInboxEntryState.ErrorPending);
        return errorChangeInterruptsPendingMove
            ? new ImportInboxConfigurationChangeDecision(
                false,
                "Finish or recover the pending Inbox Error move before changing the Error policy.")
            : ImportInboxConfigurationChangeDecision.Allowed;
    }
}

public sealed record ImportInboxConfigurationChangeDecision(bool CanApply, string Message)
{
    public static ImportInboxConfigurationChangeDecision Allowed { get; } = new(true, "Import Inbox settings can be saved.");
}
