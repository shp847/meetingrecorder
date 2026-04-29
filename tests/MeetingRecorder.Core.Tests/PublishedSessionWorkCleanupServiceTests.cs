using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class PublishedSessionWorkCleanupServiceTests
{
    [Fact]
    public async Task PrunePublishedSessionsAsync_Reclaims_Raw_Work_Only_For_Published_Sessions_With_Retained_Merged_Audio()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var workDir = Path.Combine(root, "work");
            var audioOutputDir = Path.Combine(root, "audio");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(audioOutputDir);
            var manifestStore = new SessionManifestStore(pathBuilder);

            var publishedManifestPath = await CreateSessionAsync(
                manifestStore,
                workDir,
                audioOutputDir,
                "Published Meeting",
                SessionState.Published,
                includeMergedAudio: true);
            var queuedManifestPath = await CreateSessionAsync(
                manifestStore,
                workDir,
                audioOutputDir,
                "Queued Meeting",
                SessionState.Queued,
                includeMergedAudio: true);
            var missingMergedManifestPath = await CreateSessionAsync(
                manifestStore,
                workDir,
                audioOutputDir,
                "Missing Merged Audio",
                SessionState.Published,
                includeMergedAudio: false);

            var publishedRawFile = Path.Combine(Path.GetDirectoryName(publishedManifestPath)!, "raw", "loopback-chunk-0001.wav");
            var queuedRawFile = Path.Combine(Path.GetDirectoryName(queuedManifestPath)!, "raw", "loopback-chunk-0001.wav");
            var missingMergedRawFile = Path.Combine(Path.GetDirectoryName(missingMergedManifestPath)!, "raw", "loopback-chunk-0001.wav");

            var result = await PublishedSessionWorkCleanupService.PrunePublishedSessionsAsync(
                manifestStore,
                workDir,
                audioOutputDir,
                CancellationToken.None);

            var publishedManifest = await manifestStore.LoadAsync(publishedManifestPath);
            var queuedManifest = await manifestStore.LoadAsync(queuedManifestPath);
            var missingMergedManifest = await manifestStore.LoadAsync(missingMergedManifestPath);

            Assert.Equal(2, result.SessionsPruned);
            Assert.False(File.Exists(publishedRawFile));
            Assert.Empty(publishedManifest.RawChunkPaths);
            Assert.Empty(publishedManifest.LoopbackCaptureSegments);
            Assert.True(result.BytesReclaimed >= 0);

            Assert.True(File.Exists(queuedRawFile));
            Assert.Single(queuedManifest.RawChunkPaths);
            Assert.Single(queuedManifest.LoopbackCaptureSegments);

            Assert.False(File.Exists(missingMergedRawFile));
            Assert.Empty(missingMergedManifest.RawChunkPaths);
            Assert.Empty(missingMergedManifest.LoopbackCaptureSegments);
            Assert.True(File.Exists(missingMergedManifest.MergedAudioPath));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task<string> CreateSessionAsync(
        SessionManifestStore manifestStore,
        string workDir,
        string audioOutputDir,
        string title,
        SessionState state,
        bool includeMergedAudio)
    {
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            title,
            Array.Empty<DetectionSignal>());
        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var rawDir = Path.Combine(sessionRoot, "raw");
        var processingDir = Path.Combine(sessionRoot, "processing");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(processingDir);

        var rawChunkPath = Path.Combine(rawDir, "loopback-chunk-0001.wav");
        await WriteSilentWaveFileAsync(rawChunkPath, TimeSpan.FromSeconds(1));

        var manifestMergedAudioPath = includeMergedAudio
            ? Path.Combine(processingDir, "merged.wav")
            : Path.Combine(processingDir, "missing.wav");
        if (includeMergedAudio)
        {
            await WriteSilentWaveFileAsync(manifestMergedAudioPath, TimeSpan.FromSeconds(1));
        }

        var publishedAudioPath = Path.Combine(audioOutputDir, Path.GetFileName(manifestMergedAudioPath));
        await WriteSilentWaveFileAsync(publishedAudioPath, TimeSpan.FromSeconds(1));

        await manifestStore.SaveAsync(
            manifest with
            {
                State = state,
                EndedAtUtc = manifest.StartedAtUtc.AddMinutes(1),
                RawChunkPaths = [rawChunkPath],
                LoopbackCaptureSegments =
                [
                    new LoopbackCaptureSegment(
                        manifest.StartedAtUtc,
                        manifest.StartedAtUtc.AddMinutes(1),
                        [rawChunkPath],
                        "loopback-device",
                        "Loopback Device",
                        "Multimedia"),
                ],
                MergedAudioPath = manifestMergedAudioPath,
            },
            manifestPath);

        return manifestPath;
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1));
        var totalBytes = (int)Math.Round(duration.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond);
        var buffer = new byte[Math.Min(totalBytes, writer.WaveFormat.AverageBytesPerSecond)];
        var remainingBytes = totalBytes;
        while (remainingBytes > 0)
        {
            var bytesToWrite = Math.Min(buffer.Length, remainingBytes);
            writer.Write(buffer, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }

        return Task.CompletedTask;
    }
}
