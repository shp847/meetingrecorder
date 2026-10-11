using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Reconciles imported work before startup hands manifests to a worker. It is
/// read-only: a later recovery action owns job writes, leases, retries, and
/// cleanup. This keeps a damaged or stale import visible for repair without
/// guessing it into the backlog.
/// </summary>
public sealed class ExternalAudioImportStartupReconciliationService
{
    private readonly SessionManifestStore _manifestStore;

    public ExternalAudioImportStartupReconciliationService(SessionManifestStore manifestStore)
    {
        _manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
    }

    public async Task<ExternalAudioImportStartupReconciliationResult> ReconcileAsync(
        string workDir,
        string audioOutputDir,
        string transcriptOutputDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(workDir))
        {
            return ExternalAudioImportStartupReconciliationResult.Empty;
        }

        var pending = new List<(string Path, MeetingSessionManifest Manifest)>();
        var counts = new Dictionary<ExternalAudioImportRecoveryDisposition, int>();
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeetingSessionManifest manifest;
            try
            {
                manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (manifest.ImportedSourceAudio is null)
            {
                AddIfPending(pending, manifestPath, manifest);
                continue;
            }

            var decision = await ClassifyImportedManifestAsync(
                manifestPath,
                manifest,
                audioOutputDir,
                transcriptOutputDir,
                cancellationToken);
            counts[decision.Disposition] = counts.GetValueOrDefault(decision.Disposition) + 1;
            if (decision.MayEnqueue && IsPendingState(manifest.State))
            {
                pending.Add((manifestPath, manifest));
            }
        }

        return new ExternalAudioImportStartupReconciliationResult(
            pending
                .OrderBy(candidate => SessionManifestStore.GetPendingResumePriority(candidate.Manifest))
                .ThenBy(candidate => candidate.Manifest.StartedAtUtc)
                .Select(candidate => candidate.Path)
                .ToArray(),
            counts);
    }

    private static void AddIfPending(
        ICollection<(string Path, MeetingSessionManifest Manifest)> pending,
        string manifestPath,
        MeetingSessionManifest manifest)
    {
        if (IsPendingState(manifest.State))
        {
            pending.Add((manifestPath, manifest));
        }
    }

    private static bool IsPendingState(SessionState state) => state is
        SessionState.Queued or SessionState.Processing or SessionState.Finalizing;

    private static async Task<ExternalAudioImportRecoveryDecision> ClassifyImportedManifestAsync(
        string manifestPath,
        MeetingSessionManifest manifest,
        string audioOutputDir,
        string transcriptOutputDir,
        CancellationToken cancellationToken)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot))
        {
            return ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
                Job: null,
                Schema: null,
                manifest.State,
                HasVerifiedStagedInput: false,
                HasCurrentPublishedArtifacts: false));
        }

        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        if (!File.Exists(jobPath))
        {
            // A migration-safe legacy manifest may not have a companion yet.
            // Keep it under its existing resume rule; the worker still applies
            // its stricter input boundary before reading an imported path.
            return new ExternalAudioImportRecoveryDecision(
                IsPendingState(manifest.State)
                    ? ExternalAudioImportRecoveryDisposition.ResumeQueued
                    : ExternalAudioImportRecoveryDisposition.AwaitUser,
                "Legacy imported work remains available for explicit recovery.");
        }

        ExternalAudioImportJobLoadResult loaded;
        try
        {
            loaded = await new ExternalAudioImportJobStore(jobPath).LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
                Job: null,
                Schema: null,
                manifest.State,
                HasVerifiedStagedInput: false,
                HasCurrentPublishedArtifacts: false));
        }

        return ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
            loaded.Job,
            loaded.Schema,
            manifest.State,
            HasVerifiedStagedInput: TryGetAdmissibleStagedInput(sessionRoot, loaded.Job),
            HasCurrentPublishedArtifacts: HasCurrentPublishedArtifacts(manifest, audioOutputDir, transcriptOutputDir)));
    }

    private static bool TryGetAdmissibleStagedInput(string sessionRoot, ExternalAudioImportJob? job)
    {
        if (job is null || string.IsNullOrWhiteSpace(job.StagedWorkIdentity))
        {
            return false;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(sessionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var stagedPath = Path.GetFullPath(Path.Combine(normalizedRoot, job.StagedWorkIdentity));
            var processingPrefix = normalizedRoot + Path.DirectorySeparatorChar + "processing" + Path.DirectorySeparatorChar;
            if (!stagedPath.StartsWith(processingPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(stagedPath))
            {
                return false;
            }

            // Companion jobs migrated from an older manifest have no probe
            // receipt, but their staging identity was already validated by
            // ExternalAudioImportJobStore.MigrateLegacyManifestAsync. Preserve
            // that app-owned-only compatibility route without ever consulting
            // the original source. New readiness-aware jobs must have a
            // receipt, so they cannot use this branch.
            if (job.ProbeReceipt is null)
            {
                return job.ReadinessSnapshot is null;
            }

            var stagedFile = new FileInfo(stagedPath);
            var observation = ExternalAudioImportIdentity.BuildObservationKey(
                stagedPath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc));
            return string.Equals(observation, job.ProbeReceipt.StagedObservationKey, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCurrentPublishedArtifacts(
        MeetingSessionManifest manifest,
        string audioOutputDir,
        string transcriptOutputDir)
    {
        var stem = manifest.ImportedSourceAudio?.OutputStem;
        if (string.IsNullOrWhiteSpace(stem) ||
            string.IsNullOrWhiteSpace(audioOutputDir) ||
            string.IsNullOrWhiteSpace(transcriptOutputDir))
        {
            return false;
        }

        var sidecarRoot = ArtifactPathBuilder.BuildTranscriptSidecarRoot(transcriptOutputDir);
        return File.Exists(Path.Combine(audioOutputDir, $"{stem}.wav")) &&
               File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.md")) &&
               File.Exists(Path.Combine(sidecarRoot, $"{stem}.json")) &&
               File.Exists(Path.Combine(sidecarRoot, $"{stem}.ready"));
    }
}

public sealed record ExternalAudioImportStartupReconciliationResult(
    IReadOnlyList<string> PendingManifestPaths,
    IReadOnlyDictionary<ExternalAudioImportRecoveryDisposition, int> DecisionCounts)
{
    public static ExternalAudioImportStartupReconciliationResult Empty { get; } = new(
        Array.Empty<string>(),
        new Dictionary<ExternalAudioImportRecoveryDisposition, int>());

    public int Count(ExternalAudioImportRecoveryDisposition disposition) =>
        DecisionCounts.TryGetValue(disposition, out var count) ? count : 0;
}
