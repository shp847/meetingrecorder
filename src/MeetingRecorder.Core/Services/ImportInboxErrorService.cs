using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxErrorMoveStatus
{
    Moved = 0,
    Pending = 1,
    NoPendingItem = 2,
}

public sealed record ImportInboxErrorMoveResult(
    ImportInboxErrorMoveStatus Status,
    string Message);

/// <summary>
/// Moves only terminally blocked, receipt-backed Inbox files to the managed
/// Error child when the user opted in. Transient failures never reach this
/// service; a failed move retains the source and the ErrorPending receipt.
/// </summary>
public sealed class ImportInboxErrorService
{
    private const string ErrorDirectoryName = "Error";

    public async Task<ImportInboxErrorMoveResult> MoveHeldEntryToErrorAsync(
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

        var errorRelativePath = BuildErrorRelativePath(entry);
        try
        {
            var sourcePath = ResolveSafeInboxPath(config.ImportInboxDir, entry.RelativeSourcePath);
            var errorPath = ResolveSafeInboxPath(config.ImportInboxDir, errorRelativePath);
            var errorDirectory = Path.GetDirectoryName(errorPath)
                ?? throw new InvalidOperationException("The Inbox error destination is invalid.");
            Directory.CreateDirectory(errorDirectory);
            EnsurePathHasNoReparsePoints(errorDirectory);

            if (File.Exists(errorPath))
            {
                if (!File.Exists(sourcePath) && new FileInfo(errorPath).Length == entry.SourceSizeBytes)
                {
                    await journal.CompleteLeaseAsErroredAsync(
                        entry.ObservationKey,
                        leaseOwnerId,
                        errorRelativePath,
                        nowUtc,
                        cancellationToken);
                    return new ImportInboxErrorMoveResult(
                        ImportInboxErrorMoveStatus.Moved,
                        "Inbox audio was already safely moved to Error.");
                }

                throw new IOException("The managed Inbox Error destination is unavailable.");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The Inbox source is no longer available.");
            }

            var sourceInfo = new FileInfo(sourcePath);
            if (sourceInfo.Length != entry.SourceSizeBytes ||
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc) != entry.SourceLastWriteUtc)
            {
                throw new IOException("The Inbox source changed before its Error move.");
            }

            EnsurePathHasNoReparsePoints(sourcePath);
            File.Move(sourcePath, errorPath, overwrite: false);
            await journal.CompleteLeaseAsErroredAsync(
                entry.ObservationKey,
                leaseOwnerId,
                errorRelativePath,
                nowUtc,
                cancellationToken);
            return new ImportInboxErrorMoveResult(
                ImportInboxErrorMoveStatus.Moved,
                "Terminally unreadable Inbox audio was moved to Error.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await journal.ReleaseErrorLeaseAsync(entry.ObservationKey, leaseOwnerId, nowUtc, cancellationToken);
            return new ImportInboxErrorMoveResult(
                ImportInboxErrorMoveStatus.Pending,
                "Inbox Error move is pending; the original file remains in place.");
        }
    }

    public async Task<ImportInboxErrorMoveResult> TryMoveNextPendingAsync(
        AppConfig config,
        ImportInboxJournalStore journal,
        Guid leaseOwnerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!config.ImportInboxMoveBlockedToErrorEnabled)
        {
            return new ImportInboxErrorMoveResult(ImportInboxErrorMoveStatus.NoPendingItem, "Inbox Error moves are off.");
        }

        var pendingEntry = (await journal.LoadAsync(cancellationToken)).Entries
            .Where(entry => entry.State == ImportInboxEntryState.ErrorPending)
            .OrderBy(entry => entry.CreatedAtUtc)
            .FirstOrDefault();
        if (pendingEntry is null)
        {
            return new ImportInboxErrorMoveResult(ImportInboxErrorMoveStatus.NoPendingItem, "No Inbox Error move is pending.");
        }

        var lease = await journal.TryAcquireLeaseAsync(
            pendingEntry.ObservationKey,
            leaseOwnerId,
            TimeSpan.FromMinutes(5),
            nowUtc,
            cancellationToken);
        return !lease.Acquired || lease.Entry is null
            ? new ImportInboxErrorMoveResult(ImportInboxErrorMoveStatus.Pending, lease.Message)
            : await MoveHeldEntryToErrorAsync(config, journal, lease.Entry, leaseOwnerId, nowUtc, cancellationToken);
    }

    private static string BuildErrorRelativePath(ImportInboxJournalEntry entry)
    {
        var sourceName = Path.GetFileName(entry.RelativeSourcePath);
        if (string.IsNullOrWhiteSpace(sourceName) ||
            !string.Equals(sourceName, entry.RelativeSourcePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Inbox source receipt is invalid.");
        }

        return Path.Combine(ErrorDirectoryName, $"{entry.EntryId:N}-{sourceName}");
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
                    throw new InvalidOperationException("The Inbox Error path is linked or redirected.");
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
