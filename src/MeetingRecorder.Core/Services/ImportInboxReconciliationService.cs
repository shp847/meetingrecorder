using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxReconciliationStatus
{
    Disabled = 0,
    Ready = 1,
    BlockedStorage = 2,
}

public sealed record ImportInboxReconciliationResult(
    ImportInboxReconciliationStatus Status,
    int DiscoveredCount,
    int IgnoredCount,
    string Message);

/// <summary>
/// Bounded reconciliation owns discovery receipts only. It never queues a job,
/// moves a source, follows subdirectories, or decides archive behavior.
/// </summary>
public sealed class ImportInboxReconciliationService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".mp4",
    };

    public async Task<ImportInboxReconciliationResult> ReconcileAsync(
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();

        if (!config.ImportInboxEnabled)
        {
            return new ImportInboxReconciliationResult(
                ImportInboxReconciliationStatus.Disabled,
                DiscoveredCount: 0,
                IgnoredCount: 0,
                "Import Inbox scanning is off.");
        }

        var pathValidation = ImportInboxPathPolicy.Validate(
            config.ImportInboxDir,
            [config.AudioOutputDir, config.TranscriptOutputDir, config.WorkDir]);
        if (!pathValidation.IsValid)
        {
            return new ImportInboxReconciliationResult(
                ImportInboxReconciliationStatus.BlockedStorage,
                DiscoveredCount: 0,
                IgnoredCount: 0,
                pathValidation.Message);
        }

        var health = ImportInboxPathPolicy.CheckStorageHealth(config.ImportInboxDir, requiredBytes: 0);
        if (!health.IsReady)
        {
            return new ImportInboxReconciliationResult(
                ImportInboxReconciliationStatus.BlockedStorage,
                DiscoveredCount: 0,
                IgnoredCount: 0,
                health.Message);
        }

        var journalPath = Path.Combine(config.WorkDir, "import-inbox", "journal.json");
        var journal = new ImportInboxJournalStore(journalPath);
        var candidates = Directory.EnumerateFiles(config.ImportInboxDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(config.ImportInboxMaxBatchSize)
            .ToArray();
        var discoveredCount = 0;
        var ignoredCount = 0;
        foreach (var candidatePath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportedExtensions.Contains(Path.GetExtension(candidatePath)))
            {
                ignoredCount++;
                continue;
            }

            var fileInfo = new FileInfo(candidatePath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                ignoredCount++;
                continue;
            }

            var relativePath = Path.GetRelativePath(config.ImportInboxDir, candidatePath);
            var observationKey = ExternalAudioImportIdentity.BuildObservationKey(
                candidatePath,
                fileInfo.Length,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc));
            await journal.UpsertDiscoveredAsync(
                relativePath,
                observationKey,
                fileInfo.Length,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc),
                nowUtc,
                cancellationToken);
            discoveredCount++;
        }

        return new ImportInboxReconciliationResult(
            ImportInboxReconciliationStatus.Ready,
            discoveredCount,
            ignoredCount,
            "Import Inbox discovery is up to date.");
    }
}
