using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxIntakeStatus
{
    Disabled = 0,
    NoEligibleItem = 1,
    Queued = 2,
    Blocked = 3,
}

public sealed record ImportInboxIntakeResult(
    ImportInboxIntakeStatus Status,
    Guid? JournalEntryId,
    string Message)
{
    public string? ManifestPath { get; init; }
}

/// <summary>
/// Promotes one already-discovered Inbox source through the existing safe
/// external-audio queue while holding its journal lease. Optional archiving is
/// limited to a durable Inbox receipt after that queue transaction commits.
/// </summary>
public sealed class ImportInboxIntakeService
{
    private readonly ExternalAudioImportService _externalAudioImportService;
    private readonly ImportInboxArchiveService _archiveService;
    private readonly ImportInboxErrorService _errorService;
    private readonly SessionManifestStore _manifestStore;

    public ImportInboxIntakeService(ArtifactPathBuilder pathBuilder)
    {
        ArgumentNullException.ThrowIfNull(pathBuilder);
        _externalAudioImportService = new ExternalAudioImportService(pathBuilder);
        _archiveService = new ImportInboxArchiveService();
        _errorService = new ImportInboxErrorService();
        _manifestStore = new SessionManifestStore(pathBuilder);
    }

    public async Task<ImportInboxIntakeResult> TryQueueNextAsync(
        AppConfig config,
        Guid leaseOwnerId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (leaseOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A lease owner is required.", nameof(leaseOwnerId));
        }

        if (!config.ImportInboxEnabled)
        {
            return new ImportInboxIntakeResult(ImportInboxIntakeStatus.Disabled, null, "Import Inbox scanning is off.");
        }

        var journal = new ImportInboxJournalStore(Path.Combine(config.WorkDir, "import-inbox", "journal.json"));
        if (config.ImportInboxArchiveAfterQueueEnabled)
        {
            await _archiveService.TryArchiveNextPendingAsync(
                config,
                journal,
                leaseOwnerId,
                nowUtc,
                cancellationToken);
        }
        if (config.ImportInboxMoveBlockedToErrorEnabled)
        {
            await _errorService.TryMoveNextPendingAsync(
                config,
                journal,
                leaseOwnerId,
                nowUtc,
                cancellationToken);
        }

        var entries = (await journal.LoadAsync(cancellationToken)).Entries
            .Where(entry =>
                (entry.State is ImportInboxEntryState.Discovered or ImportInboxEntryState.Ready) ||
                (entry.State == ImportInboxEntryState.RetryPending &&
                 entry.NextProbeAtUtc <= nowUtc.ToUniversalTime()))
            .OrderBy(entry => entry.CreatedAtUtc)
            .ToArray();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await journal.TryAcquireLeaseAsync(
                entry.ObservationKey,
                leaseOwnerId,
                TimeSpan.FromMinutes(5),
                nowUtc,
                cancellationToken);
            if (!lease.Acquired || lease.Entry is null)
            {
                continue;
            }

            try
            {
                var sourcePath = ResolveSafeInboxSourcePath(config.ImportInboxDir, lease.Entry.RelativeSourcePath);
                if (!File.Exists(sourcePath))
                {
                    await journal.CompleteLeaseAsRetryPendingAsync(
                        entry.ObservationKey,
                        leaseOwnerId,
                        nameof(ExternalAudioImportPreflightStatus.MissingFile),
                        nowUtc.Add(GetProbeRetryDelay(lease.Entry.ProbeAttemptCount)),
                        nowUtc,
                        cancellationToken);
                    return new ImportInboxIntakeResult(
                        ImportInboxIntakeStatus.Blocked,
                        lease.Entry.EntryId,
                        "The Inbox file is no longer available.");
                }

                var candidates = await _externalAudioImportService.BuildImportCandidatesAsync(
                    config,
                    [sourcePath],
                    ExternalAudioImportMethod.ImportInbox,
                    nowUtc,
                    cancellationToken);
                var candidate = AssertSingleCandidate(candidates);
                if (!candidate.CanQueue)
                {
                    if (config.ImportInboxMoveBlockedToErrorEnabled &&
                        IsTerminalInboxFailure(candidate.Preflight.Status))
                    {
                        var errorReceipt = await journal.CompleteLeaseAsErrorPendingAsync(
                            entry.ObservationKey,
                            leaseOwnerId,
                            candidate.Preflight.Status.ToString(),
                            nowUtc,
                            cancellationToken);
                        var errorMove = await _errorService.MoveHeldEntryToErrorAsync(
                            config,
                            journal,
                            errorReceipt,
                            leaseOwnerId,
                            nowUtc,
                            cancellationToken);
                        return new ImportInboxIntakeResult(
                            ImportInboxIntakeStatus.Blocked,
                            lease.Entry.EntryId,
                            errorMove.Message);
                    }

                    if (IsTerminalInboxFailure(candidate.Preflight.Status))
                    {
                        await journal.CompleteLeaseAsTerminalFailureAsync(
                            entry.ObservationKey,
                            leaseOwnerId,
                            candidate.Preflight.Status.ToString(),
                            nowUtc,
                            cancellationToken);
                    }
                    else
                    {
                        await journal.CompleteLeaseAsRetryPendingAsync(
                            entry.ObservationKey,
                            leaseOwnerId,
                            candidate.Preflight.Status.ToString(),
                            nowUtc.Add(GetProbeRetryDelay(lease.Entry.ProbeAttemptCount)),
                            nowUtc,
                            cancellationToken);
                    }
                    return new ImportInboxIntakeResult(
                        ImportInboxIntakeStatus.Blocked,
                        lease.Entry.EntryId,
                        candidate.Preflight.Message);
                }

                var queued = await _externalAudioImportService.QueueImportAsync(
                    config,
                    new ExternalAudioImportRequest(
                        candidate.SourcePath,
                        candidate.SourceDisplayName,
                        candidate.SourceSizeBytes,
                        candidate.SourceLastWriteUtc,
                        ExternalAudioImportMethod.ImportInbox,
                        candidate.Title,
                        candidate.StartedAtUtc,
                        ProjectName: null,
                        candidate.Preflight.Duration,
                        SourceRetained: true),
                    nowUtc,
                    cancellationToken);
                var manifest = await _manifestStore.LoadAsync(queued.ManifestPath, cancellationToken);
                var queuedReceipt = await journal.CompleteLeaseAsQueuedAsync(
                    entry.ObservationKey,
                    leaseOwnerId,
                    manifest.SessionId,
                    nowUtc,
                    archiveRequested: config.ImportInboxArchiveAfterQueueEnabled,
                    cancellationToken: cancellationToken);
                var completionMessage = "Inbox audio queued without moving the source file.";
                if (config.ImportInboxArchiveAfterQueueEnabled)
                {
                    var archive = await _archiveService.ArchiveHeldEntryAsync(
                        config,
                        journal,
                        queuedReceipt,
                        leaseOwnerId,
                        nowUtc,
                        cancellationToken);
                    completionMessage = archive.Message;
                }

                return new ImportInboxIntakeResult(
                    ImportInboxIntakeStatus.Queued,
                    lease.Entry.EntryId,
                    completionMessage)
                {
                    ManifestPath = queued.ManifestPath,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await journal.ReleaseLeaseAsync(entry.ObservationKey, leaseOwnerId, nowUtc, cancellationToken);
                throw;
            }
        }

        return new ImportInboxIntakeResult(ImportInboxIntakeStatus.NoEligibleItem, null, "No ready Inbox audio is waiting.");
    }

    private static ExternalAudioImportCandidate AssertSingleCandidate(
        IReadOnlyList<ExternalAudioImportCandidate> candidates)
    {
        if (candidates.Count != 1)
        {
            throw new InvalidOperationException("The Inbox intake candidate set is invalid.");
        }

        return candidates[0];
    }

    private static bool IsTerminalInboxFailure(ExternalAudioImportPreflightStatus status) => status is
        ExternalAudioImportPreflightStatus.DecodeFailed or
        ExternalAudioImportPreflightStatus.EmptyAudio or
        ExternalAudioImportPreflightStatus.UnsupportedCodec or
        ExternalAudioImportPreflightStatus.TooShort or
        ExternalAudioImportPreflightStatus.UnsupportedExtension or
        ExternalAudioImportPreflightStatus.UnsupportedLocation or
        ExternalAudioImportPreflightStatus.Duplicate or
        ExternalAudioImportPreflightStatus.ResourceLimit;

    private static TimeSpan GetProbeRetryDelay(int completedProbeAttempts)
    {
        var exponent = Math.Clamp(completedProbeAttempts, 0, 5);
        return TimeSpan.FromSeconds(Math.Min(300, 15 * (1 << exponent)));
    }

    private static string ResolveSafeInboxSourcePath(string inboxRoot, string relativeSourcePath)
    {
        var normalizedInboxRoot = Path.GetFullPath(inboxRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sourcePath = Path.GetFullPath(Path.Combine(normalizedInboxRoot, relativeSourcePath));
        if (!sourcePath.StartsWith(normalizedInboxRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Inbox source path is outside the configured Inbox.");
        }

        return sourcePath;
    }
}
