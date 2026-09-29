using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxEntryState
{
    Discovered = 0,
    Ready = 1,
    Leased = 2,
    Queued = 3,
    BlockedStorage = 4,
    Changing = 5,
    Missing = 6,
    ArchivePending = 7,
    Removed = 8,
    ErrorPending = 9,
    Errored = 10,
    RetryPending = 11,
    TerminalFailure = 12,
}

public sealed record ImportInboxLease(
    Guid OwnerId,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record ImportInboxJournalEntry
{
    public Guid EntryId { get; init; }

    public int Revision { get; init; }

    public string RelativeSourcePath { get; init; } = string.Empty;

    public string ObservationKey { get; init; } = string.Empty;

    public long SourceSizeBytes { get; init; }

    public DateTimeOffset SourceLastWriteUtc { get; init; }

    public ImportInboxEntryState State { get; init; }

    public string? SessionId { get; init; }

    public string? ArchivedRelativePath { get; init; }

    public string? ErrorRelativePath { get; init; }

    public string? ErrorReasonCode { get; init; }

    // Stable preflight outcome for this exact opaque observation. This is
    // metadata only: it deliberately excludes raw paths, decoder output, and
    // exception text.
    public string? PreflightStatusCode { get; init; }

    public int ProbeAttemptCount { get; init; }

    public DateTimeOffset? NextProbeAtUtc { get; init; }

    public ImportInboxLease? Lease { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record ImportInboxJournal(
    int SchemaVersion,
    IReadOnlyList<ImportInboxJournalEntry> Entries)
{
    public const int CurrentSchemaVersion = 1;

    public static ImportInboxJournal Empty { get; } = new(CurrentSchemaVersion, Array.Empty<ImportInboxJournalEntry>());
}

public sealed record ImportInboxLeaseAcquireResult(
    bool Acquired,
    ImportInboxJournalEntry? Entry,
    string Message);

/// <summary>
/// Local-only journal for files whose ownership has already been proved by a
/// configured Inbox. It never accepts an absolute source path or performs file
/// moves; later intake/archival services must supply those authority checks.
/// </summary>
public sealed class ImportInboxJournalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ImportInboxJournalStore(string journalPath)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
        {
            throw new ArgumentException("An Inbox journal path is required.", nameof(journalPath));
        }

        JournalPath = journalPath;
    }

    public string JournalPath { get; }

    public async Task<ImportInboxJournal> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(JournalPath))
        {
            return ImportInboxJournal.Empty;
        }

        await using var stream = File.OpenRead(JournalPath);
        var journal = await JsonSerializer.DeserializeAsync<ImportInboxJournal>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Import Inbox journal is empty.");
        return ValidateJournal(journal);
    }

    public async Task<ImportInboxJournalEntry> UpsertDiscoveredAsync(
        string relativeSourcePath,
        string observationKey,
        long sourceSizeBytes,
        DateTimeOffset sourceLastWriteUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDiscoveredInput(relativeSourcePath, observationKey, sourceSizeBytes);
        return await MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            var timestamp = nowUtc.ToUniversalTime();
            var entry = existing is null
                ? new ImportInboxJournalEntry
                {
                    EntryId = Guid.NewGuid(),
                    Revision = 0,
                    RelativeSourcePath = relativeSourcePath,
                    ObservationKey = observationKey,
                    SourceSizeBytes = sourceSizeBytes,
                    SourceLastWriteUtc = sourceLastWriteUtc.ToUniversalTime(),
                    State = ImportInboxEntryState.Discovered,
                    CreatedAtUtc = timestamp,
                    UpdatedAtUtc = timestamp,
                }
                : existing with
                {
                    RelativeSourcePath = relativeSourcePath,
                    SourceSizeBytes = sourceSizeBytes,
                    SourceLastWriteUtc = sourceLastWriteUtc.ToUniversalTime(),
                    Revision = existing.Revision + 1,
                    UpdatedAtUtc = timestamp,
                };
            return (ReplaceEntry(journal, entry), entry);
        }, cancellationToken);
    }

    public Task<ImportInboxLeaseAcquireResult> TryAcquireLeaseAsync(
        string observationKey,
        Guid ownerId,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        return MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return (journal, new ImportInboxLeaseAcquireResult(false, null, "The Inbox item is no longer available."));
            }

            var timestamp = nowUtc.ToUniversalTime();
            if (existing.State == ImportInboxEntryState.RetryPending &&
                existing.NextProbeAtUtc is { } nextProbeAtUtc &&
                nextProbeAtUtc > timestamp)
            {
                return (journal, new ImportInboxLeaseAcquireResult(
                    false,
                    existing,
                    "This Inbox item is waiting for its next safe preflight check."));
            }

            if (existing.State is not (
                    ImportInboxEntryState.Discovered or
                    ImportInboxEntryState.Ready or
                    ImportInboxEntryState.RetryPending or
                    ImportInboxEntryState.Leased or
                    ImportInboxEntryState.ArchivePending or
                    ImportInboxEntryState.ErrorPending))
            {
                return (journal, new ImportInboxLeaseAcquireResult(
                    false,
                    existing,
                    "This Inbox item is already queued or removed."));
            }

            if (existing.Lease is { } lease &&
                lease.ExpiresAtUtc > timestamp &&
                lease.OwnerId != ownerId)
            {
                return (journal, new ImportInboxLeaseAcquireResult(false, existing, "Another Meeting Recorder instance is reviewing this Inbox item."));
            }

            var updated = existing with
            {
                State = ImportInboxEntryState.Leased,
                Revision = existing.Revision + 1,
                UpdatedAtUtc = timestamp,
                Lease = new ImportInboxLease(ownerId, timestamp, timestamp.Add(leaseDuration)),
            };
            return (
                ReplaceEntry(journal, updated),
                new ImportInboxLeaseAcquireResult(true, updated, "Inbox item lease acquired."));
        }, cancellationToken);
    }

    public async Task<bool> ReleaseLeaseAsync(
        string observationKey,
        Guid ownerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        return await MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId)
            {
                return (journal, false);
            }

            var updated = existing with
            {
                State = ImportInboxEntryState.Ready,
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), true);
        }, cancellationToken);
    }

    /// <summary>
    /// Records a retryable, metadata-only preflight result. The entry remains
    /// tied to its observation key and cannot be claimed again before the
    /// bounded next-check time.
    /// </summary>
    public Task<ImportInboxJournalEntry> CompleteLeaseAsRetryPendingAsync(
        string observationKey,
        Guid ownerId,
        string preflightStatusCode,
        DateTimeOffset nextProbeAtUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidatePreflightCompletionInput(observationKey, ownerId, preflightStatusCode, nextProbeAtUtc, nowUtc);
        return MutateAsync(journal =>
        {
            var existing = RequireHeldLease(journal, observationKey, ownerId, nowUtc);
            var updated = existing with
            {
                State = ImportInboxEntryState.RetryPending,
                PreflightStatusCode = preflightStatusCode.Trim(),
                ProbeAttemptCount = existing.ProbeAttemptCount + 1,
                NextProbeAtUtc = nextProbeAtUtc.ToUniversalTime(),
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    /// <summary>
    /// Suppresses a terminal preflight failure for this exact observation.
    /// Discovery of a changed source creates a different observation key and
    /// remains eligible; no source is moved or changed here.
    /// </summary>
    public Task<ImportInboxJournalEntry> CompleteLeaseAsTerminalFailureAsync(
        string observationKey,
        Guid ownerId,
        string preflightStatusCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidatePreflightCompletionInput(observationKey, ownerId, preflightStatusCode, nowUtc, nowUtc);
        return MutateAsync(journal =>
        {
            var existing = RequireHeldLease(journal, observationKey, ownerId, nowUtc);
            var updated = existing with
            {
                State = ImportInboxEntryState.TerminalFailure,
                PreflightStatusCode = preflightStatusCode.Trim(),
                ProbeAttemptCount = existing.ProbeAttemptCount + 1,
                NextProbeAtUtc = null,
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    public Task<ImportInboxJournalEntry> CompleteLeaseAsQueuedAsync(
        string observationKey,
        Guid ownerId,
        string sessionId,
        DateTimeOffset nowUtc,
        bool archiveRequested = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("A session id is required.", nameof(sessionId));
        }

        return MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.Lease.ExpiresAtUtc <= nowUtc.ToUniversalTime())
            {
                throw new InvalidOperationException("The Import Inbox lease is no longer held by this intake operation.");
            }

            var updated = existing with
            {
                State = archiveRequested ? ImportInboxEntryState.ArchivePending : ImportInboxEntryState.Queued,
                SessionId = sessionId.Trim(),
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = archiveRequested ? existing.Lease : null,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    public Task<ImportInboxJournalEntry> CompleteLeaseAsArchivedAsync(
        string observationKey,
        Guid ownerId,
        string archivedRelativePath,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDiscoveredInput(archivedRelativePath, observationKey, sourceSizeBytes: 0);
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        return MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.Lease.ExpiresAtUtc <= nowUtc.ToUniversalTime() ||
                existing.State != ImportInboxEntryState.ArchivePending)
            {
                throw new InvalidOperationException("The Import Inbox archive lease is no longer held by this operation.");
            }

            var updated = existing with
            {
                State = ImportInboxEntryState.Queued,
                ArchivedRelativePath = archivedRelativePath,
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    public async Task<bool> ReleaseArchiveLeaseAsync(
        string observationKey,
        Guid ownerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        return await MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.State != ImportInboxEntryState.ArchivePending)
            {
                return (journal, false);
            }

            var updated = existing with
            {
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), true);
        }, cancellationToken);
    }

    public Task<ImportInboxJournalEntry> CompleteLeaseAsErrorPendingAsync(
        string observationKey,
        Guid ownerId,
        string errorReasonCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(errorReasonCode))
        {
            throw new ArgumentException("An Inbox error reason is required.", nameof(errorReasonCode));
        }

        return MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.Lease.ExpiresAtUtc <= nowUtc.ToUniversalTime())
            {
                throw new InvalidOperationException("The Import Inbox error lease is no longer held by this operation.");
            }

            var updated = existing with
            {
                State = ImportInboxEntryState.ErrorPending,
                ErrorReasonCode = errorReasonCode.Trim(),
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = existing.Lease,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    public Task<ImportInboxJournalEntry> CompleteLeaseAsErroredAsync(
        string observationKey,
        Guid ownerId,
        string errorRelativePath,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateDiscoveredInput(errorRelativePath, observationKey, sourceSizeBytes: 0);
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        return MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.Lease.ExpiresAtUtc <= nowUtc.ToUniversalTime() ||
                existing.State != ImportInboxEntryState.ErrorPending)
            {
                throw new InvalidOperationException("The Import Inbox error lease is no longer held by this operation.");
            }

            var updated = existing with
            {
                State = ImportInboxEntryState.Errored,
                ErrorRelativePath = errorRelativePath,
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), updated);
        }, cancellationToken);
    }

    public async Task<bool> ReleaseErrorLeaseAsync(
        string observationKey,
        Guid ownerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        return await MutateAsync(journal =>
        {
            var existing = journal.Entries.SingleOrDefault(entry =>
                string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
            if (existing?.Lease?.OwnerId != ownerId ||
                existing.State != ImportInboxEntryState.ErrorPending)
            {
                return (journal, false);
            }

            var updated = existing with
            {
                Revision = existing.Revision + 1,
                UpdatedAtUtc = nowUtc.ToUniversalTime(),
                Lease = null,
            };
            return (ReplaceEntry(journal, updated), true);
        }, cancellationToken);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<ImportInboxJournal, (ImportInboxJournal Journal, TResult Result)> mutation,
        CancellationToken cancellationToken)
    {
        await using var leaseLock = await OpenExclusiveLockAsync(cancellationToken);
        var journal = await LoadAsync(cancellationToken);
        var (updatedJournal, result) = mutation(journal);
        if (!ReferenceEquals(updatedJournal, journal))
        {
            await SaveAsync(updatedJournal, cancellationToken);
        }

        return result;
    }

    private async Task<FileStream> OpenExclusiveLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = JournalPath + ".lock";
        var directory = Path.GetDirectoryName(lockPath)
            ?? throw new InvalidOperationException("The Inbox journal path must include a directory.");
        Directory.CreateDirectory(directory);
        const int attempts = 20;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }

        throw new IOException("The Import Inbox journal is busy. Try again shortly.");
    }

    private async Task SaveAsync(ImportInboxJournal journal, CancellationToken cancellationToken)
    {
        var validated = ValidateJournal(journal);
        var directory = Path.GetDirectoryName(JournalPath)
            ?? throw new InvalidOperationException("The Inbox journal path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = JournalPath + ".bak";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, validated, SerializerOptions, cancellationToken);
            }

            if (File.Exists(JournalPath))
            {
                File.Replace(temporaryPath, JournalPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, JournalPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static ImportInboxJournal ValidateJournal(ImportInboxJournal journal)
    {
        if (journal.SchemaVersion != ImportInboxJournal.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Update Meeting Recorder before changing this Import Inbox journal.");
        }

        var duplicateKey = journal.Entries
            .GroupBy(entry => entry.ObservationKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1);
        if (duplicateKey is not null)
        {
            throw new InvalidOperationException("The Import Inbox journal has an invalid item identity.");
        }

        return journal with { Entries = journal.Entries.ToArray() };
    }

    private static ImportInboxJournalEntry RequireHeldLease(
        ImportInboxJournal journal,
        string observationKey,
        Guid ownerId,
        DateTimeOffset nowUtc)
    {
        var existing = journal.Entries.SingleOrDefault(entry =>
            string.Equals(entry.ObservationKey, observationKey, StringComparison.OrdinalIgnoreCase));
        if (existing?.Lease?.OwnerId != ownerId ||
            existing.Lease.ExpiresAtUtc <= nowUtc.ToUniversalTime())
        {
            throw new InvalidOperationException("The Import Inbox lease is no longer held by this intake operation.");
        }

        return existing;
    }

    private static void ValidatePreflightCompletionInput(
        string observationKey,
        Guid ownerId,
        string preflightStatusCode,
        DateTimeOffset nextProbeAtUtc,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(preflightStatusCode))
        {
            throw new ArgumentException("A preflight status code is required.", nameof(preflightStatusCode));
        }

        if (nextProbeAtUtc < nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nextProbeAtUtc));
        }
    }

    private static void ValidateDiscoveredInput(string relativeSourcePath, string observationKey, long sourceSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(relativeSourcePath) ||
            Path.IsPathFullyQualified(relativeSourcePath) ||
            relativeSourcePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(relativeSourcePath, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The Inbox source path must be a safe relative path.", nameof(relativeSourcePath));
        }

        if (string.IsNullOrWhiteSpace(observationKey))
        {
            throw new ArgumentException("An observation key is required.", nameof(observationKey));
        }

        if (sourceSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSizeBytes));
        }
    }

    private static ImportInboxJournal ReplaceEntry(ImportInboxJournal journal, ImportInboxJournalEntry replacement)
    {
        var entries = journal.Entries
            .Where(entry => !string.Equals(entry.ObservationKey, replacement.ObservationKey, StringComparison.OrdinalIgnoreCase))
            .Append(replacement)
            .OrderBy(entry => entry.CreatedAtUtc)
            .ToArray();
        return journal with { Entries = entries };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale app-owned recovery file is safer than a failed journal write.
        }
    }
}
