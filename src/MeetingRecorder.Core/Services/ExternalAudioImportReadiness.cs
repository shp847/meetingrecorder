using System.Security.Cryptography;
using System.Text;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Metadata-only readiness result used to decide whether a verified, app-owned
/// import copy may enter processing. It deliberately contains no model path,
/// provider detail, or source locator.
/// </summary>
public enum ExternalAudioImportReadinessState
{
    Ready = 0,
    BlockedBySetup = 1,
    Unknown = 2,
}

public enum ExternalAudioImportReadinessReason
{
    None = 0,
    TranscriptionModelMissing = 1,
    TranscriptionModelInvalid = 2,
    ReadinessCheckFailed = 3,
}

public sealed record ExternalAudioImportReadinessSnapshot(
    int SchemaVersion,
    ExternalAudioImportReadinessState State,
    ExternalAudioImportReadinessReason Reason,
    string ConfigurationRevision,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? RetryAtUtc,
    string RecoveryText)
{
    public const int CurrentSchemaVersion = 1;

    public bool CanQueue => State == ExternalAudioImportReadinessState.Ready;

    public static ExternalAudioImportReadinessSnapshot Ready(
        string configurationRevision,
        DateTimeOffset checkedAtUtc) => new(
        CurrentSchemaVersion,
        ExternalAudioImportReadinessState.Ready,
        ExternalAudioImportReadinessReason.None,
        NormalizeConfigurationRevision(configurationRevision),
        checkedAtUtc.ToUniversalTime(),
        RetryAtUtc: null,
        "Transcription is ready. This staged import can enter the queue.");

    public static ExternalAudioImportReadinessSnapshot Blocked(
        ExternalAudioImportReadinessReason reason,
        string configurationRevision,
        DateTimeOffset checkedAtUtc) => new(
        CurrentSchemaVersion,
        ExternalAudioImportReadinessState.BlockedBySetup,
        reason,
        NormalizeConfigurationRevision(configurationRevision),
        checkedAtUtc.ToUniversalTime(),
        RetryAtUtc: null,
        reason == ExternalAudioImportReadinessReason.TranscriptionModelInvalid
            ? "Open Setup to select or import a valid transcription model, then resume this import."
            : "Open Setup to install or select a transcription model, then resume this import.");

    public static ExternalAudioImportReadinessSnapshot Unknown(
        string configurationRevision,
        DateTimeOffset checkedAtUtc) => new(
        CurrentSchemaVersion,
        ExternalAudioImportReadinessState.Unknown,
        ExternalAudioImportReadinessReason.ReadinessCheckFailed,
        NormalizeConfigurationRevision(configurationRevision),
        checkedAtUtc.ToUniversalTime(),
        checkedAtUtc.ToUniversalTime().AddSeconds(30),
        "Meeting Recorder could not verify transcription setup. Retry after Setup is available.");

    private static string NormalizeConfigurationRevision(string configurationRevision)
    {
        if (string.IsNullOrWhiteSpace(configurationRevision))
        {
            throw new ArgumentException("A configuration revision is required.", nameof(configurationRevision));
        }

        return configurationRevision.Trim();
    }
}

public static class ExternalAudioImportReadinessResolver
{
    public static ExternalAudioImportReadinessSnapshot Resolve(
        WhisperModelStatusKind modelStatus,
        string transcriptionModelPath,
        DateTimeOffset checkedAtUtc)
    {
        var configurationRevision = BuildConfigurationRevision(transcriptionModelPath);
        return modelStatus switch
        {
            WhisperModelStatusKind.Valid => ExternalAudioImportReadinessSnapshot.Ready(configurationRevision, checkedAtUtc),
            WhisperModelStatusKind.Invalid => ExternalAudioImportReadinessSnapshot.Blocked(
                ExternalAudioImportReadinessReason.TranscriptionModelInvalid,
                configurationRevision,
                checkedAtUtc),
            _ => ExternalAudioImportReadinessSnapshot.Blocked(
                ExternalAudioImportReadinessReason.TranscriptionModelMissing,
                configurationRevision,
                checkedAtUtc),
        };
    }

    public static ExternalAudioImportReadinessSnapshot ResolveUnknown(
        string transcriptionModelPath,
        DateTimeOffset checkedAtUtc) => ExternalAudioImportReadinessSnapshot.Unknown(
        BuildConfigurationRevision(transcriptionModelPath),
        checkedAtUtc);

    private static string BuildConfigurationRevision(string transcriptionModelPath)
    {
        var material = string.IsNullOrWhiteSpace(transcriptionModelPath)
            ? "missing"
            : Path.GetFullPath(transcriptionModelPath).Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

public sealed record ExternalAudioImportResumeResult(
    IReadOnlyList<string> ManifestPaths,
    int PreservedBlockedCount,
    int StagedWorkUnavailableCount);

/// <summary>
/// Explicitly resumes only user-requested setup blocks. Startup never invokes
/// this coordinator, so a repaired model cannot silently begin old work.
/// </summary>
public sealed class ExternalAudioImportReadinessCoordinator
{
    public async Task<ExternalAudioImportResumeResult> ResumeBlockedJobsAsync(
        string workDir,
        ExternalAudioImportReadinessSnapshot readiness,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDir);
        ArgumentNullException.ThrowIfNull(readiness);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(workDir) || !readiness.CanQueue)
        {
            return new ExternalAudioImportResumeResult(Array.Empty<string>(), 0, 0);
        }

        var resumed = new List<string>();
        var preservedBlocked = 0;
        var stagedUnavailable = 0;
        foreach (var jobPath in Directory.EnumerateFiles(workDir, "import-job.json", SearchOption.AllDirectories).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var jobStore = new ExternalAudioImportJobStore(jobPath);
                var loaded = await jobStore.LoadAsync(cancellationToken);
                var job = loaded.Job;
                if (job is null ||
                    job.State != ExternalAudioImportJobState.BlockedBySetup ||
                    job.QueueIntent != ExternalAudioImportQueueIntent.UserRequested ||
                    !loaded.Schema.CanWrite)
                {
                    continue;
                }

                if (!TryGetVerifiedStagedPath(jobPath, job, out _))
                {
                    stagedUnavailable++;
                    var retained = job with
                    {
                        Reason = ExternalAudioImportJobReason.StagedWorkUnavailable,
                        ReadinessSnapshot = readiness,
                        UpdatedAtUtc = nowUtc.ToUniversalTime(),
                    };
                    await jobStore.SaveAsync(retained, cancellationToken);
                    continue;
                }

                var ready = ExternalAudioImportJobTransitions.TryTransition(
                    job with { ReadinessSnapshot = readiness },
                    job.Revision,
                    ExternalAudioImportJobState.ReadyToQueue,
                    ExternalAudioImportJobReason.None,
                    nowUtc);
                if (!ready.Applied || ready.Job is null)
                {
                    preservedBlocked++;
                    continue;
                }

                var queued = ExternalAudioImportJobTransitions.TryTransition(
                    ready.Job,
                    ready.Job.Revision,
                    ExternalAudioImportJobState.Queued,
                    ExternalAudioImportJobReason.None,
                    nowUtc);
                if (!queued.Applied || queued.Job is null)
                {
                    preservedBlocked++;
                    continue;
                }

                await jobStore.SaveAsync(queued.Job, cancellationToken);
                var sessionRoot = Path.GetDirectoryName(jobPath)!;
                var manifestPath = Path.Combine(sessionRoot, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    resumed.Add(manifestPath);
                }
                else
                {
                    preservedBlocked++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                preservedBlocked++;
            }
        }

        return new ExternalAudioImportResumeResult(resumed, preservedBlocked, stagedUnavailable);
    }

    private static bool TryGetVerifiedStagedPath(string jobPath, ExternalAudioImportJob job, out string stagedPath)
    {
        stagedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(job.StagedWorkIdentity) || job.ProbeReceipt is null)
        {
            return false;
        }

        var sessionRoot = Path.GetDirectoryName(jobPath)!;
        var normalizedRoot = Path.GetFullPath(sessionRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, job.StagedWorkIdentity));
        var processingPrefix = normalizedRoot + Path.DirectorySeparatorChar + "processing" + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(processingPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return false;
        }

        var file = new FileInfo(candidate);
        var observation = ExternalAudioImportIdentity.BuildObservationKey(
            candidate,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc));
        if (!string.Equals(observation, job.ProbeReceipt.StagedObservationKey, StringComparison.Ordinal))
        {
            return false;
        }

        stagedPath = candidate;
        return true;
    }
}
