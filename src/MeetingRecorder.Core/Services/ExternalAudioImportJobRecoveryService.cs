using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Repairs the safe half of an interrupted imported-audio admission: a committed
/// imported manifest and its app-owned staged input exist, but the local job
/// companion was not written before interruption. It deliberately ignores
/// non-imported, malformed, future-schema, and unsafe-path sessions.
/// </summary>
public sealed class ExternalAudioImportJobRecoveryService
{
    private readonly SessionManifestStore _manifestStore;

    public ExternalAudioImportJobRecoveryService(SessionManifestStore manifestStore)
    {
        _manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
    }

    public async Task<int> RecoverMissingCompanionJobsAsync(
        string workDir,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(workDir))
        {
            return 0;
        }

        var recoveredCount = 0;
        var manifestPaths = Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories).ToArray();
        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
                if (manifest.ImportedSourceAudio is null)
                {
                    continue;
                }

                var sessionRoot = Path.GetDirectoryName(manifestPath)
                    ?? throw new InvalidOperationException("An import manifest must have a session directory.");
                var jobStore = new ExternalAudioImportJobStore(Path.Combine(sessionRoot, "import-job.json"));
                if (File.Exists(jobStore.JobPath))
                {
                    continue;
                }

                var migration = await jobStore.MigrateLegacyManifestAsync(manifest, nowUtc, cancellationToken);
                if (migration.Migrated)
                {
                    recoveredCount++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Startup recovery is best-effort. An unsafe or incomplete session remains
                // untouched for explicit repair instead of being guessed into the queue.
            }
        }

        return recoveredCount;
    }
}
