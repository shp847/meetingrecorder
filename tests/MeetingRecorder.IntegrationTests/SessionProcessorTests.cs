using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.IntegrationTests;

public sealed class SessionProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Publishes_All_Artifacts_And_Creates_Ready_Last()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "models", "diarization"),
        };

        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Manual,
            "Manual Session",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(config.WorkDir, manifest.SessionId);
        var rawDir = Path.Combine(sessionRoot, "raw");
        Directory.CreateDirectory(rawDir);
        var chunkPath = Path.Combine(rawDir, "chunk-0001.wav");
        CreateWave(chunkPath, TimeSpan.FromMilliseconds(250));

        var updatedManifest = manifest with
        {
            RawChunkPaths = new[] { chunkPath },
        };

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(updatedManifest, manifestPath);

        var processor = new SessionProcessor(
            manifestStore,
            new ArtifactPathBuilder(),
            new WaveChunkMerger(),
            new FakeTranscriptionProvider(),
            new FakeDiarizationProvider(),
            new TranscriptRenderer(),
            new FilePublishService());

        var published = await processor.ProcessAsync(manifestPath, config);

        Assert.True(File.Exists(published.AudioPath));
        Assert.True(File.Exists(published.MarkdownPath));
        Assert.True(File.Exists(published.JsonPath));
        Assert.True(File.Exists(published.ReadyMarkerPath));
        Assert.True(File.GetCreationTimeUtc(published.ReadyMarkerPath) >= File.GetCreationTimeUtc(published.JsonPath));
    }

    [Fact]
    public async Task ProcessAsync_Publishes_Transcript_When_Diarization_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "models", "diarization"),
        };

        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Teams,
            "Diarization Failure",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(config.WorkDir, manifest.SessionId);
        var rawDir = Path.Combine(sessionRoot, "raw");
        Directory.CreateDirectory(rawDir);
        var chunkPath = Path.Combine(rawDir, "chunk-0001.wav");
        CreateWave(chunkPath, TimeSpan.FromMilliseconds(250));

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest with { RawChunkPaths = new[] { chunkPath } }, manifestPath);

        var processor = new SessionProcessor(
            manifestStore,
            new ArtifactPathBuilder(),
            new WaveChunkMerger(),
            new FakeTranscriptionProvider(),
            new ThrowingDiarizationProvider(),
            new TranscriptRenderer(),
            new FilePublishService());

        var published = await processor.ProcessAsync(manifestPath, config);

        Assert.True(File.Exists(published.MarkdownPath));
        Assert.True(File.Exists(published.ReadyMarkerPath));
    }

    [Fact]
    public async Task ProcessAsync_Publishes_Audio_When_Transcription_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "models", "diarization"),
        };

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Teams,
            "Transcription Failure",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(config.WorkDir, manifest.SessionId);
        var rawDir = Path.Combine(sessionRoot, "raw");
        Directory.CreateDirectory(rawDir);
        var chunkPath = Path.Combine(rawDir, "chunk-0001.wav");
        CreateWave(chunkPath, TimeSpan.FromMilliseconds(250));

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest with { RawChunkPaths = new[] { chunkPath } }, manifestPath);

        var processor = new SessionProcessor(
            manifestStore,
            pathBuilder,
            new WaveChunkMerger(),
            new ThrowingTranscriptionProvider(),
            new FakeDiarizationProvider(),
            new TranscriptRenderer(),
            new FilePublishService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(manifestPath, config));

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, "Transcription Failure");
        var expectedAudioPath = Path.Combine(config.AudioOutputDir, $"{stem}.wav");
        Assert.True(File.Exists(expectedAudioPath));
    }

    [Fact]
    public async Task ProcessAsync_Mixes_Microphone_Chunks_Into_Published_Audio()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "models", "diarization"),
        };

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Teams,
            "Microphone Mix",
            Array.Empty<DetectionSignal>());

        var sessionRoot = Path.Combine(config.WorkDir, manifest.SessionId);
        var rawDir = Path.Combine(sessionRoot, "raw");
        Directory.CreateDirectory(rawDir);
        var loopbackChunkPath = Path.Combine(rawDir, "loopback-chunk-0001.wav");
        var microphoneChunkPath = Path.Combine(rawDir, "microphone-chunk-0001.wav");
        CreateFloatWave(loopbackChunkPath, TimeSpan.FromMilliseconds(250), amplitude: 0f, sampleRate: 48000, channels: 2);
        CreatePcmWave(microphoneChunkPath, TimeSpan.FromMilliseconds(250), amplitude: 12000, sampleRate: 16000, channels: 1);

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(
            manifest with
            {
                RawChunkPaths = new[] { loopbackChunkPath },
                MicrophoneChunkPaths = new[] { microphoneChunkPath },
            },
            manifestPath);

        var processor = new SessionProcessor(
            manifestStore,
            pathBuilder,
            new WaveChunkMerger(),
            new ThrowingTranscriptionProvider(),
            new FakeDiarizationProvider(),
            new TranscriptRenderer(),
            new FilePublishService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(manifestPath, config));

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, manifest.StartedAtUtc, "Microphone Mix");
        var expectedAudioPath = Path.Combine(config.AudioOutputDir, $"{stem}.wav");
        Assert.True(File.Exists(expectedAudioPath));
        Assert.True(ReadPeakAmplitude(expectedAudioPath) > 0.01f);
    }

    [Fact]
    public async Task ProcessAsync_Uses_Existing_MergedAudioPath_When_Raw_Chunks_Are_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "models", "diarization"),
        };

        Directory.CreateDirectory(config.AudioOutputDir);
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Teams,
            "Legacy Audio",
            Array.Empty<DetectionSignal>());

        var existingAudioPath = Path.Combine(config.AudioOutputDir, "2026-03-16_004645_teams_legacy-audio.wav");
        CreateWave(existingAudioPath, TimeSpan.FromMilliseconds(250));

        var manifestPath = Path.Combine(config.WorkDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(
            manifest with
            {
                StartedAtUtc = new DateTimeOffset(2026, 3, 16, 0, 46, 45, TimeSpan.Zero),
                RawChunkPaths = Array.Empty<string>(),
                MicrophoneChunkPaths = Array.Empty<string>(),
                MergedAudioPath = existingAudioPath,
            },
            manifestPath);

        var processor = new SessionProcessor(
            manifestStore,
            pathBuilder,
            new WaveChunkMerger(),
            new FakeTranscriptionProvider(),
            new FakeDiarizationProvider(),
            new TranscriptRenderer(),
            new FilePublishService());

        var published = await processor.ProcessAsync(manifestPath, config);

        Assert.Equal(existingAudioPath, published.AudioPath);
        Assert.True(File.Exists(published.MarkdownPath));
        Assert.True(File.Exists(published.JsonPath));
        Assert.True(File.Exists(published.ReadyMarkerPath));
    }

    private static void CreateWave(string path, TimeSpan duration)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1));
        var totalSamples = (int)(16000 * duration.TotalSeconds);
        var buffer = new byte[totalSamples * 2];
        writer.Write(buffer, 0, buffer.Length);
    }

    private static void CreateFloatWave(string path, TimeSpan duration, float amplitude, int sampleRate, int channels)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
        var totalSamples = (int)(sampleRate * duration.TotalSeconds * channels);
        var samples = Enumerable.Repeat(amplitude, totalSamples).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static void CreatePcmWave(string path, TimeSpan duration, short amplitude, int sampleRate, int channels)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        var totalFrames = (int)(sampleRate * duration.TotalSeconds);
        var samples = new byte[totalFrames * channels * sizeof(short)];
        for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            for (var channelIndex = 0; channelIndex < channels; channelIndex++)
            {
                var offset = (frameIndex * channels + channelIndex) * sizeof(short);
                BitConverter.TryWriteBytes(samples.AsSpan(offset, sizeof(short)), amplitude);
            }
        }

        writer.Write(samples, 0, samples.Length);
    }

    private static float ReadPeakAmplitude(string path)
    {
        using var reader = new AudioFileReader(path);
        var buffer = new float[4096];
        var peak = 0f;
        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < samplesRead; index++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[index]));
            }
        }

        return peak;
    }

    private sealed class FakeTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            IReadOnlyList<TranscriptSegment> segments =
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, "Hello world"),
                new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), null, "Action items follow"),
            ];

            return Task.FromResult(new TranscriptionResult(segments, "en", null));
        }
    }

    private sealed class FakeDiarizationProvider : IDiarizationProvider
    {
        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            var segments = transcriptSegments
                .Select((segment, index) => segment with { SpeakerLabel = $"Speaker {(index % 2) + 1}" })
                .ToArray();

            return Task.FromResult(new DiarizationResult(segments, true, null));
        }
    }

    private sealed class ThrowingDiarizationProvider : IDiarizationProvider
    {
        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sidecar missing");
        }
    }

    private sealed class ThrowingTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("model load failed");
        }
    }
}
