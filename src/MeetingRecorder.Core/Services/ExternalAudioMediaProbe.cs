using NAudio.Wave;
using System.Collections.Concurrent;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Stable, metadata-only outcomes for read-only external-audio admission.
/// These values are safe to persist or project; they contain no local path,
/// decoder exception, or audio content.
/// </summary>
public enum ExternalAudioMediaProbeStatus
{
    Ready = 0,
    WaitingForSettle = 1,
    BlockedStorage = 2,
    PotentialDuplicate = 3,
    UnsupportedExtension = 4,
    UnsupportedCodec = 5,
    NoAudio = 6,
    Empty = 7,
    TooShort = 8,
    DecodeFailed = 9,
    CopyFailed = 10,
    UnsafePath = 11,
    Missing = 12,
    Offline = 13,
    Changing = 14,
    ResourceLimit = 15,
}

public sealed record ExternalAudioMediaProbeRequest(
    string SourcePath,
    string ObservationKey,
    long ExpectedSizeBytes,
    DateTimeOffset ExpectedLastWriteUtc);

public sealed record ExternalAudioMediaProbeResult(
    ExternalAudioMediaProbeStatus Status,
    string Message,
    TimeSpan? Duration,
    int? SampleRate,
    int? Channels,
    long SourceSizeBytes,
    string DecoderVersion,
    bool NormalizationFeasible,
    ImportStorageRequirements? StorageRequirements)
{
    public bool IsReady => Status == ExternalAudioMediaProbeStatus.Ready;
}

public sealed record ExternalAudioDecodedProbe(
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    string DecoderVersion);

public interface IExternalAudioPreparationProbe
{
    Task<ExternalAudioDecodedProbe> ProbeAsync(
        string sourcePath,
        string temporaryPreparedAudioPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the same local preparation implementation as transcription. It writes
/// only a randomized app-owned temporary WAV and removes it before returning.
/// </summary>
public sealed class TranscriptionAudioPreparationProbe : IExternalAudioPreparationProbe
{
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public TranscriptionAudioPreparationProbe(TranscriptionAudioPreparer? audioPreparer = null)
    {
        _audioPreparer = audioPreparer ?? new TranscriptionAudioPreparer();
    }

    public async Task<ExternalAudioDecodedProbe> ProbeAsync(
        string sourcePath,
        string temporaryPreparedAudioPath,
        CancellationToken cancellationToken = default)
    {
        await _audioPreparer.PrepareAsync(sourcePath, temporaryPreparedAudioPath, cancellationToken);
        using var preparedReader = new AudioFileReader(temporaryPreparedAudioPath);
        return new ExternalAudioDecodedProbe(
            preparedReader.TotalTime,
            preparedReader.WaveFormat.SampleRate,
            preparedReader.WaveFormat.Channels,
            "NAudio+TranscriptionAudioPreparer/v1");
    }
}

/// <summary>
/// Bounds and snapshots a local decode probe before any staged import starts.
/// Callers still own source-path, duplicate, offline, and storage policy; this
/// class owns preparation parity, lock/change detection, cache, and safe
/// taxonomy only.
/// </summary>
public sealed class ExternalAudioMediaProbe
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".mp4",
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> VolumeGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ExternalAudioMediaProbeResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IExternalAudioPreparationProbe _preparationProbe;

    public ExternalAudioMediaProbe(IExternalAudioPreparationProbe? preparationProbe = null)
    {
        _preparationProbe = preparationProbe ?? new TranscriptionAudioPreparationProbe();
    }

    public static TimeSpan MinimumSupportedDuration { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan MaximumProbeDuration { get; } = TimeSpan.FromMinutes(2);

    public static long MaximumSourceBytes { get; } = 2L * 1024 * 1024 * 1024;

    public async Task<ExternalAudioMediaProbeResult> ProbeAsync(
        ExternalAudioMediaProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (_cache.TryGetValue(request.ObservationKey, out var cached))
        {
            return cached;
        }

        var initialSnapshot = CaptureSnapshot(request.SourcePath);
        if (initialSnapshot is null)
        {
            return Cache(request.ObservationKey, CreateResult(
                ExternalAudioMediaProbeStatus.Missing,
                "The source file is no longer available.",
                null,
                null,
                null,
                0));
        }

        if (!MatchesExpectedObservation(initialSnapshot.Value, request))
        {
            return Cache(request.ObservationKey, CreateResult(
                ExternalAudioMediaProbeStatus.Changing,
                "The source changed while it was being reviewed. Try again after copying finishes.",
                null,
                null,
                null,
                initialSnapshot.Value.Length));
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(request.SourcePath)))
        {
            return Cache(request.ObservationKey, CreateResult(
                ExternalAudioMediaProbeStatus.UnsupportedExtension,
                "Unsupported file type. Import .wav, .mp3, .m4a, .aac, or .mp4 audio.",
                null,
                null,
                null,
                initialSnapshot.Value.Length));
        }

        if (initialSnapshot.Value.Length == 0)
        {
            return Cache(request.ObservationKey, CreateResult(
                ExternalAudioMediaProbeStatus.Empty,
                "This source file is empty.",
                TimeSpan.Zero,
                null,
                null,
                0));
        }

        if (initialSnapshot.Value.Length > MaximumSourceBytes)
        {
            return Cache(request.ObservationKey, CreateResult(
                ExternalAudioMediaProbeStatus.ResourceLimit,
                "This source is too large for a safe local preflight.",
                null,
                null,
                null,
                initialSnapshot.Value.Length));
        }

        var volumeGate = VolumeGates.GetOrAdd(GetVolumeKey(request.SourcePath), _ => new SemaphoreSlim(2, 2));
        await volumeGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanOpenForRead(request.SourcePath))
            {
                return CreateResult(
                    ExternalAudioMediaProbeStatus.WaitingForSettle,
                    "Waiting for the source file to become available before import begins.",
                    null,
                    null,
                    null,
                    initialSnapshot.Value.Length);
            }

            var temporaryPreparedAudioPath = BuildTemporaryPreparedAudioPath();
            try
            {
                using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCancellation.CancelAfter(MaximumProbeDuration);
                ExternalAudioDecodedProbe decoded;
                try
                {
                    decoded = await _preparationProbe.ProbeAsync(
                        request.SourcePath,
                        temporaryPreparedAudioPath,
                        timeoutCancellation.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return CreateResult(
                        ExternalAudioMediaProbeStatus.ResourceLimit,
                        "The local preflight timed out. Try a shorter or simpler audio file.",
                        null,
                        null,
                        null,
                        initialSnapshot.Value.Length);
                }
                catch (WavInputManualReviewException)
                {
                    return CreateResult(
                        ExternalAudioMediaProbeStatus.UnsupportedCodec,
                        "This WAV structure is not safe for local import. Choose another file or repair the source.",
                        null,
                        null,
                        null,
                        initialSnapshot.Value.Length);
                }
                catch (IOException)
                {
                    return CreateResult(
                        ExternalAudioMediaProbeStatus.WaitingForSettle,
                        "Waiting for the source file to become available before import begins.",
                        null,
                        null,
                        null,
                        initialSnapshot.Value.Length);
                }
                catch (Exception)
                {
                    return CreateResult(
                        ExternalAudioMediaProbeStatus.DecodeFailed,
                        "Meeting Recorder could not read this file with the local transcription audio stack.",
                        null,
                        null,
                        null,
                        initialSnapshot.Value.Length);
                }

                var finalSnapshot = CaptureSnapshot(request.SourcePath);
                if (finalSnapshot is null || finalSnapshot.Value != initialSnapshot.Value)
                {
                    return CreateResult(
                        ExternalAudioMediaProbeStatus.Changing,
                        "The source changed while it was being reviewed. Try again after copying finishes.",
                        null,
                        null,
                        null,
                        initialSnapshot.Value.Length);
                }

                if (decoded.Duration <= TimeSpan.Zero)
                {
                    return Cache(request.ObservationKey, CreateResult(
                        ExternalAudioMediaProbeStatus.NoAudio,
                        "This source file does not contain readable audio.",
                        decoded.Duration,
                        decoded.SampleRate,
                        decoded.Channels,
                        initialSnapshot.Value.Length));
                }

                if (decoded.Duration < MinimumSupportedDuration)
                {
                    return Cache(request.ObservationKey, CreateResult(
                        ExternalAudioMediaProbeStatus.TooShort,
                        "This source is too short to create a reliable transcript.",
                        decoded.Duration,
                        decoded.SampleRate,
                        decoded.Channels,
                        initialSnapshot.Value.Length));
                }

                return Cache(request.ObservationKey, CreateResult(
                    ExternalAudioMediaProbeStatus.Ready,
                    "Ready to queue.",
                    decoded.Duration,
                    decoded.SampleRate,
                    decoded.Channels,
                    initialSnapshot.Value.Length,
                    decoded.DecoderVersion));
            }
            finally
            {
                TryDelete(temporaryPreparedAudioPath);
                TryDelete(temporaryPreparedAudioPath + ".normalized-source.wav");
            }
        }
        finally
        {
            volumeGate.Release();
        }
    }

    public void ClearCachedObservation(string observationKey)
    {
        if (!string.IsNullOrWhiteSpace(observationKey))
        {
            _cache.TryRemove(observationKey, out _);
        }
    }

    private static ExternalAudioMediaProbeResult CreateResult(
        ExternalAudioMediaProbeStatus status,
        string message,
        TimeSpan? duration,
        int? sampleRate,
        int? channels,
        long sourceSizeBytes,
        string decoderVersion = "NAudio+TranscriptionAudioPreparer/v1") =>
        new(
            status,
            message,
            duration,
            sampleRate,
            channels,
            sourceSizeBytes,
            decoderVersion,
            status == ExternalAudioMediaProbeStatus.Ready,
            duration is null ? null : ImportStorageRequirementEstimator.Estimate(sourceSizeBytes, duration));

    private ExternalAudioMediaProbeResult Cache(string observationKey, ExternalAudioMediaProbeResult result)
    {
        if (!string.IsNullOrWhiteSpace(observationKey))
        {
            _cache.TryAdd(observationKey, result);
        }

        return result;
    }

    private static bool CanOpenForRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.Length >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static FileSnapshot? CaptureSnapshot(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new FileSnapshot(info.Length, new DateTimeOffset(info.LastWriteTimeUtc))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesExpectedObservation(FileSnapshot snapshot, ExternalAudioMediaProbeRequest request) =>
        snapshot.Length == request.ExpectedSizeBytes &&
        snapshot.LastWriteUtc == request.ExpectedLastWriteUtc.ToUniversalTime();

    private static string GetVolumeKey(string sourcePath) =>
        Path.GetPathRoot(Path.GetFullPath(sourcePath)) ?? string.Empty;

    private static string BuildTemporaryPreparedAudioPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MeetingRecorderImportProbe");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.wav");
    }

    private static void ValidateRequest(ExternalAudioMediaProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            throw new ArgumentException("A source path is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ObservationKey))
        {
            throw new ArgumentException("An opaque source observation is required.", nameof(request));
        }

        if (request.ExpectedSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
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
            // Temporary probe cleanup is best effort only.
        }
    }

    private readonly record struct FileSnapshot(long Length, DateTimeOffset LastWriteUtc);
}
