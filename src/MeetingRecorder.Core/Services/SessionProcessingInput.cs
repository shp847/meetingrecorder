using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// The one safe audio input accepted by a processing run. Imported sessions are
/// deliberately represented by their verified app-owned staged copy only; the
/// original user-selected locator is never part of this contract.
/// </summary>
public sealed record SessionProcessingInput(
    SessionProcessingInputKind Kind,
    string AudioPath,
    TimeSpan? Duration,
    string? ImportJobPath = null,
    ExternalAudioImportJob? ImportJob = null);

public enum SessionProcessingInputKind
{
    CapturedAudio = 0,
    ImportedAudio = 1,
}

/// <summary>
/// Resolves a session manifest into one processor input. This is intentionally
/// the boundary that validates imported work: processing may read its staged
/// copy, but must never fall back to the original source path in import
/// provenance.
/// </summary>
public sealed class SessionProcessingInputResolver
{
    public async Task<SessionProcessingInput> ResolveAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        string processingRoot,
        string stem,
        WaveChunkMerger waveChunkMerger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(waveChunkMerger);
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.ImportedSourceAudio is not null)
        {
            return await ResolveImportedAsync(manifest, manifestPath, cancellationToken);
        }

        if (manifest.RawChunkPaths.Count > 0 || manifest.MicrophoneCaptureSegments.Count > 0)
        {
            var mergedAudioPath = Path.Combine(processingRoot, $"{stem}.wav");
            if (manifest.MicrophoneCaptureSegments.Count > 0)
            {
                await waveChunkMerger.MergeAsync(
                    manifest.RawChunkPaths,
                    manifest.MicrophoneCaptureSegments,
                    manifest.StartedAtUtc,
                    manifest.EndedAtUtc,
                    mergedAudioPath,
                    cancellationToken);
            }
            else
            {
                await waveChunkMerger.MergeAsync(
                    manifest.RawChunkPaths,
                    manifest.MicrophoneChunkPaths,
                    mergedAudioPath,
                    cancellationToken);
            }

            return new SessionProcessingInput(SessionProcessingInputKind.CapturedAudio, mergedAudioPath, null);
        }

        if (!string.IsNullOrWhiteSpace(manifest.MergedAudioPath) && File.Exists(manifest.MergedAudioPath))
        {
            return new SessionProcessingInput(SessionProcessingInputKind.CapturedAudio, manifest.MergedAudioPath, null);
        }

        throw new InvalidOperationException(
            "No raw audio chunks were available, and no existing merged audio file could be found for this session.");
    }

    private static async Task<SessionProcessingInput> ResolveImportedAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("The imported session work directory is unavailable.");
        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        if (!File.Exists(jobPath))
        {
            throw new InvalidOperationException(
                "This imported recording needs recovery before it can be processed. Its staged import receipt is unavailable.");
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
            throw new InvalidOperationException(
                "This imported recording needs recovery before it can be processed. Its import record could not be verified.");
        }

        var job = loaded.Job;
        var isLegacyUnreceipted = job?.ProbeReceipt is null && job?.ReadinessSnapshot is null;
        if (job is null ||
            !loaded.Schema.CanRead ||
            job.IsReadOnly ||
            !string.Equals(job.SessionId, manifest.SessionId, StringComparison.Ordinal) ||
            (job.QueueIntent != ExternalAudioImportQueueIntent.UserRequested && !isLegacyUnreceipted) ||
            job.State is not (ExternalAudioImportJobState.Queued or ExternalAudioImportJobState.Processing) ||
            job.ReadinessSnapshot is { CanQueue: false })
        {
            throw new InvalidOperationException(
                "This imported recording is not ready for processing. Review its import status and resume it when setup is ready.");
        }

        var receipt = job.ProbeReceipt;
        if (string.IsNullOrWhiteSpace(job.StagedWorkIdentity) ||
            (receipt is null && job.ReadinessSnapshot is not null) ||
            (receipt is not null &&
             (receipt.SchemaVersion != ExternalAudioImportProbeReceipt.CurrentSchemaVersion ||
              receipt.ResultCode is not "Ready" ||
              receipt.Duration <= TimeSpan.Zero ||
              receipt.SampleRate <= 0 ||
              receipt.Channels <= 0 ||
              receipt.SourceObservationRevision != job.Source.ObservationRevision ||
              !string.Equals(receipt.SourceObservationKey, job.Source.ObservationKey, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                "This imported recording needs recovery before it can be processed. Its staged import receipt is invalid.");
        }

        var normalizedRoot = Path.GetFullPath(sessionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string stagedPath;
        try
        {
            stagedPath = Path.GetFullPath(Path.Combine(normalizedRoot, job.StagedWorkIdentity));
        }
        catch
        {
            throw new InvalidOperationException(
                "This imported recording needs recovery before it can be processed. Its staged work identity is invalid.");
        }

        var processingPrefix = normalizedRoot + Path.DirectorySeparatorChar + "processing" + Path.DirectorySeparatorChar;
        if (!stagedPath.StartsWith(processingPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(stagedPath))
        {
            throw new InvalidOperationException(
                "This imported recording needs recovery before it can be processed. Its verified staged copy is unavailable.");
        }

        var stagedFile = new FileInfo(stagedPath);
        if (receipt is not null)
        {
            var stagedObservationKey = ExternalAudioImportIdentity.BuildObservationKey(
                stagedPath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc));
            if (!string.Equals(stagedObservationKey, receipt.StagedObservationKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "This imported recording needs recovery before it can be processed. Its staged copy changed after review.");
            }
        }

        return new SessionProcessingInput(
            SessionProcessingInputKind.ImportedAudio,
            stagedPath,
            receipt?.Duration ?? manifest.ImportedSourceAudio?.ProbedDuration,
            jobPath,
            job);
    }
}
