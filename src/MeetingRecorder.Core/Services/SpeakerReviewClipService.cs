using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace MeetingRecorder.Core.Services;

public enum SpeakerReviewClipState
{
    Available,
    Unavailable,
    Canceled,
}

public sealed record SpeakerReviewClipRequest(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    TimeSpan CueStart,
    TimeSpan CueEnd,
    string SourceAudioPath);

public sealed record SpeakerReviewClipResult(
    SpeakerReviewClipState State,
    string Message,
    string? ClipPath,
    TimeSpan Start,
    TimeSpan Duration)
{
    public bool IsAvailable => State == SpeakerReviewClipState.Available && !string.IsNullOrWhiteSpace(ClipPath);
}

/// <summary>
/// Creates short, local-only WAV clips for speaker review. Source audio is only read;
/// request identity and source fingerprint are opaque cache-key input and never written to artifacts.
/// </summary>
public sealed class SpeakerReviewClipService
{
    private const int MinimumClipSeconds = 6;
    private const int MaximumClipSeconds = 12;
    private const int MaximumCacheFiles = 48;
    private const long MaximumCacheBytes = 100L * 1024 * 1024;
    private static readonly TimeSpan MaximumCacheAge = TimeSpan.FromDays(3);
    private static readonly TimeSpan TemporaryClipMaximumAge = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, int> ActiveLeases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _generationGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheRoot;

    public SpeakerReviewClipService(string? cacheRoot = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(AppDataPaths.GetAppRoot(), "speaker-review"));
    }

    public async Task<SpeakerReviewClipResult> GetOrCreateAsync(
        SpeakerReviewClipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Canceled();
        }

        if (string.IsNullOrWhiteSpace(request.MeetingStableIdentity) ||
            string.IsNullOrWhiteSpace(request.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(request.SpeakerId) ||
            string.IsNullOrWhiteSpace(request.SourceAudioPath) ||
            !File.Exists(request.SourceAudioPath))
        {
            return Unavailable("Audio clip is unavailable for this review.");
        }

        var source = new FileInfo(request.SourceAudioPath);
        var sourceLength = source.Length;
        var sourceLastWriteUtc = source.LastWriteTimeUtc;
        var fingerprint = $"{sourceLength}:{sourceLastWriteUtc.Ticks}";
        try
        {
            using var durationReader = new AudioFileReader(source.FullName);
            var duration = durationReader.TotalTime;
            if (duration < TimeSpan.FromSeconds(MinimumClipSeconds))
            {
                return Unavailable("Audio is too short for a review clip.");
            }

            var (start, clipDuration) = SelectWindow(request.CueStart, request.CueEnd, duration);
            Directory.CreateDirectory(_cacheRoot);
            await CleanupAsync(cancellationToken);
            var cachePath = Path.Combine(_cacheRoot, CreateOpaqueKey(request, fingerprint) + ".wav");
            if (TryReadValidClip(cachePath, clipDuration))
            {
                File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
                return new(SpeakerReviewClipState.Available, "Local review clip ready.", cachePath, start, clipDuration);
            }

            var gate = _generationGates.GetOrAdd(cachePath, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (TryReadValidClip(cachePath, clipDuration))
                {
                    File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
                    return new(SpeakerReviewClipState.Available, "Local review clip ready.", cachePath, start, clipDuration);
                }

                var tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await Task.Run(() => Extract(source.FullName, tempPath, start, clipDuration, cancellationToken), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var after = new FileInfo(source.FullName);
                    if (after.Length != sourceLength || after.LastWriteTimeUtc != sourceLastWriteUtc)
                    {
                        return Unavailable("Audio changed while the review clip was prepared.");
                    }

                    if (!TryReadValidClip(tempPath, clipDuration))
                    {
                        return Unavailable("Audio clip could not be prepared safely.");
                    }

                    File.Move(tempPath, cachePath, overwrite: true);
                    await CleanupAsync(cancellationToken);
                    return new(SpeakerReviewClipState.Available, "Local review clip ready.", cachePath, start, clipDuration);
                }
                finally
                {
                    TryDelete(tempPath);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return new(SpeakerReviewClipState.Canceled, "Audio clip preparation was canceled.", null, TimeSpan.Zero, TimeSpan.Zero);
        }
        catch (Exception)
        {
            return Unavailable("Audio clip is unavailable for this review.");
        }
    }

    public Task CleanupAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (!Directory.Exists(_cacheRoot)) return;
        var files = new DirectoryInfo(_cacheRoot).EnumerateFiles("*.wav")
            .OrderByDescending(file => file.LastAccessTimeUtc).ToList();
        long retainedBytes = 0;
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            if (!IsLeased(file.FullName) &&
                (DateTime.UtcNow - file.LastWriteTimeUtc > MaximumCacheAge || index >= MaximumCacheFiles || retainedBytes + file.Length > MaximumCacheBytes))
            {
                TryDelete(file.FullName);
            }
            else
            {
                retainedBytes += file.Length;
            }
        }
        foreach (var temporary in new DirectoryInfo(_cacheRoot).EnumerateFiles("*.tmp-*")
                     .Where(file => DateTime.UtcNow - file.LastWriteTimeUtc > TemporaryClipMaximumAge))
        {
            TryDelete(temporary.FullName);
        }
    }, cancellationToken);

    public IDisposable AcquireLease(string clipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clipPath);
        var fullPath = Path.GetFullPath(clipPath);
        ActiveLeases.AddOrUpdate(fullPath, 1, (_, count) => count + 1);
        return new ClipLease(fullPath);
    }

    private static (TimeSpan Start, TimeSpan Duration) SelectWindow(TimeSpan cueStart, TimeSpan cueEnd, TimeSpan sourceDuration)
    {
        var safeStart = cueStart < TimeSpan.Zero ? TimeSpan.Zero : cueStart;
        var safeEnd = cueEnd <= safeStart ? safeStart + TimeSpan.FromSeconds(1) : cueEnd;
        var requested = Math.Clamp((safeEnd - safeStart).TotalSeconds + 4, MinimumClipSeconds, MaximumClipSeconds);
        requested = Math.Min(requested, sourceDuration.TotalSeconds);
        var start = TimeSpan.FromSeconds(Math.Clamp((safeStart + ((safeEnd - safeStart) / 2)).TotalSeconds - requested / 2, 0, sourceDuration.TotalSeconds - requested));
        return (start, TimeSpan.FromSeconds(requested));
    }

    private static void Extract(string sourcePath, string targetPath, TimeSpan start, TimeSpan duration, CancellationToken token)
    {
        using var reader = new AudioFileReader(sourcePath);
        var source = new OffsetSampleProvider(reader) { SkipOver = start, Take = duration };
        using var writer = new WaveFileWriter(targetPath, new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels));
        var wave = new SampleToWaveProvider16(source);
        var buffer = new byte[16_384];
        int read;
        while ((read = wave.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            writer.Write(buffer, 0, read);
        }
    }

    private static bool TryReadValidClip(string path, TimeSpan expectedDuration)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 256) return false;
            using var reader = new WaveFileReader(path);
            return reader.TotalTime > TimeSpan.Zero && reader.TotalTime <= expectedDuration + TimeSpan.FromSeconds(1);
        }
        catch { return false; }
    }

    private static string CreateOpaqueKey(SpeakerReviewClipRequest request, string fingerprint)
    {
        var input = string.Join("\n", request.MeetingStableIdentity, request.ArtifactRevision, request.SpeakerId, request.CueStart.Ticks, request.CueEnd.Ticks, fingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static SpeakerReviewClipResult Unavailable(string message) => new(SpeakerReviewClipState.Unavailable, message, null, TimeSpan.Zero, TimeSpan.Zero);
    private static SpeakerReviewClipResult Canceled() => new(SpeakerReviewClipState.Canceled, "Audio clip preparation was canceled.", null, TimeSpan.Zero, TimeSpan.Zero);
    private static bool IsLeased(string path) => ActiveLeases.ContainsKey(path);
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private sealed class ClipLease(string path) : IDisposable
    {
        private string? _path = path;

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _path, null);
            if (value is null) return;
            ActiveLeases.AddOrUpdate(value, 0, (_, count) => count <= 1 ? 0 : count - 1);
            if (ActiveLeases.TryGetValue(value, out var count) && count == 0)
            {
                ActiveLeases.TryRemove(value, out _);
            }
        }
    }
}
