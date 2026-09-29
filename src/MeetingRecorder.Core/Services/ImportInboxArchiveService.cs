using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxArchiveStatus
{
    Archived = 0,
    Pending = 1,
    NoPendingItem = 2,
}

public sealed record ImportInboxArchiveResult(
    ImportInboxArchiveStatus Status,
    string Message);

/// <summary>
/// Moves only receipt-backed Inbox sources into the Inbox's managed Archive
/// child after their staged import committed. A failed move retains the source
/// and durable ArchivePending receipt for a later retry.
/// </summary>
public sealed class ImportInboxArchiveService
{
    private const string ArchiveDirectoryName = "Archive";

    public async Task<ImportInboxArchiveResult> ArchiveHeldEntryAsync(
        AppConfig config,
        ImportInboxJournalStore journal,
        ImportInboxJournalEntry entry,
        Guid leaseOwnerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(entry);
        if (leaseOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(leaseOwnerId));
        }

        var archiveRelativePath = BuildArchiveRelativePath(entry);
        try
        {
            var sourcePath = ResolveSafeInboxPath(config.ImportInboxDir, entry.RelativeSourcePath);
            var archivePath = ResolveSafeInboxPath(config.ImportInboxDir, archiveRelativePath);
            var archiveDirectory = Path.GetDirectoryName(archivePath)
                ?? throw new InvalidOperationException("The Inbox archive destination is invalid.");
            Directory.CreateDirectory(archiveDirectory);
            EnsurePathHasNoReparsePoints(archiveDirectory);

            if (File.Exists(archivePath))
            {
                if (!File.Exists(sourcePath) && new FileInfo(archivePath).Length == entry.SourceSizeBytes)
                {
                    await journal.CompleteLeaseAsArchivedAsync(
                        entry.ObservationKey,
                        leaseOwnerId,
                        archiveRelativePath,
                        nowUtc,
                        cancellationToken);
                    return new ImportInboxArchiveResult(
                        ImportInboxArchiveStatus.Archived,
                        "Inbox audio was already safely archived.");
                }

                throw new IOException("The managed Inbox archive destination is unavailable.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The Inbox source is no longer available.");
            }

            var sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length != entry.SourceSizeBytes ||
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc) != entry.SourceLastWriteUtc)
            {
                throw new IOException("The Inbox source changed before its archive move.");
            }

            EnsurePathHasNoReparsePoints(sourcePath);
            File.Move(sourcePath, archivePath, overwrite: false);
            await journal.CompleteLeaseAsArchivedAsync(
                entry.ObservationKey,
                leaseOwnerId,
                archiveRelativePath,
                nowUtc,
                cancellationToken);
            return new ImportInboxArchiveResult(
                ImportInboxArchiveStatus.Archived,
                "Inbox audio was archived after its work copy committed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await journal.ReleaseArchiveLeaseAsync(entry.ObservationKey, leaseOwnerId, nowUtc, cancellationToken);
            return new ImportInboxArchiveResult(
                ImportInboxArchiveStatus.Pending,
                "Inbox archive is pending; the original file remains in place.");
        }
    }

    public async Task<ImportInboxArchiveResult> TryArchiveNextPendingAsync(
        AppConfig config,
        ImportInboxJournalStore journal,
        Guid leaseOwnerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!config.ImportInboxArchiveAfterQueueEnabled)
        {
            return new ImportInboxArchiveResult(ImportInboxArchiveStatus.NoPendingItem, "Inbox archiving is off.");
        }

        var pendingEntry = (await journal.LoadAsync(cancellationToken)).Entries
            .Where(entry => entry.State == ImportInboxEntryState.ArchivePending)
            .OrderBy(entry => entry.CreatedAtUtc)
            .FirstOrDefault();
        if (pendingEntry is null)
        {
            return new ImportInboxArchiveResult(ImportInboxArchiveStatus.NoPendingItem, "No Inbox archive is pending.");
        }

        var lease = await journal.TryAcquireLeaseAsync(
            pendingEntry.ObservationKey,
            leaseOwnerId,
            TimeSpan.FromMinutes(5),
            nowUtc,
            cancellationToken);
        return !lease.Acquired || lease.Entry is null
            ? new ImportInboxArchiveResult(ImportInboxArchiveStatus.Pending, lease.Message)
            : await ArchiveHeldEntryAsync(config, journal, lease.Entry, leaseOwnerId, nowUtc, cancellationToken);
    }

    private static string BuildArchiveRelativePath(ImportInboxJournalEntry entry)
    {
        var sourceName = Path.GetFileName(entry.RelativeSourcePath);
        if (string.IsNullOrWhiteSpace(sourceName) ||
            !string.Equals(sourceName, entry.RelativeSourcePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Inbox source receipt is invalid.");
        }

        return Path.Combine(ArchiveDirectoryName, $"{entry.EntryId:N}-{sourceName}");
    }

    private static string ResolveSafeInboxPath(string inboxRoot, string relativePath)
    {
        var normalizedInboxRoot = Path.GetFullPath(inboxRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidatePath = Path.GetFullPath(Path.Combine(normalizedInboxRoot, relativePath));
        if (!candidatePath.StartsWith(normalizedInboxRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Inbox item is outside the configured Inbox.");
        }

        return candidatePath;
    }

    private static void EnsurePathHasNoReparsePoints(string path)
    {
        var currentPath = path;
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            if (Directory.Exists(currentPath) || File.Exists(currentPath))
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("The Inbox archive path is linked or redirected.");
                }
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentPath = parentPath;
        }
    }
}
