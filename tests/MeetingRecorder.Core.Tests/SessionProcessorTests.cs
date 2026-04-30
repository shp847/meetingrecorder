using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
namespace MeetingRecorder.Core.Tests;

public sealed class SessionProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Marks_Source_Audio_Preparation_Failure_As_Failed()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var workDir = Path.Combine(root, "work");
            var audioDir = Path.Combine(root, "audio");
            var transcriptDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(audioDir);
            Directory.CreateDirectory(transcriptDir);

            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = await manifestStore.CreateAsync(
                workDir,
                MeetingPlatform.Teams,
                "Queued Session",
                Array.Empty<DetectionSignal>());
            var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
            await manifestStore.SaveAsync(manifest with { State = SessionState.Queued }, manifestPath);

            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                new FakeTranscriptionProvider(),
                new TrackingDiarizationProvider(),
                new TranscriptRenderer(),
                new FilePublishService());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync(
                    manifestPath,
                    new AppConfig
                    {
                        WorkDir = workDir,
                        AudioOutputDir = audioDir,
                        TranscriptOutputDir = transcriptDir,
                        TranscriptionModelPath = Path.Combine(root, "models", "dummy.bin"),
                    }));
            var failedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Contains("No raw audio chunks", exception.Message, StringComparison.Ordinal);
            Assert.Equal(SessionState.Failed, failedManifest.State);
            Assert.Equal(StageExecutionState.Failed, failedManifest.TranscriptionStatus.State);
            Assert.Equal(StageExecutionState.Skipped, failedManifest.DiarizationStatus.State);
            Assert.Equal(StageExecutionState.Skipped, failedManifest.PublishStatus.State);
            Assert.Contains("No raw audio chunks", failedManifest.ErrorSummary, StringComparison.Ordinal);
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

    [Fact]
    public async Task ProcessAsync_Skips_Diarization_When_Manifest_Overrides_Disable_Speaker_Labeling()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var workDir = Path.Combine(root, "work");
            var audioDir = Path.Combine(root, "audio");
            var transcriptDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(audioDir);
            Directory.CreateDirectory(transcriptDir);

            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = await manifestStore.CreateAsync(
                workDir,
                MeetingPlatform.Teams,
                "Queued Session",
                Array.Empty<DetectionSignal>());
            var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
            var sourceAudioPath = Path.Combine(workDir, manifest.SessionId, "processing", "existing-audio.wav");
            await WriteSilentWaveFileAsync(sourceAudioPath, TimeSpan.FromSeconds(1));

            var updatedManifest = manifest with
            {
                State = SessionState.Queued,
                EndedAtUtc = manifest.StartedAtUtc.AddMinutes(1),
                MergedAudioPath = sourceAudioPath,
                ProcessingOverrides = new MeetingProcessingOverrides(null, null, true),
            };
            await manifestStore.SaveAsync(updatedManifest, manifestPath);

            var transcriptionProvider = new FakeTranscriptionProvider();
            var diarizationProvider = new TrackingDiarizationProvider();
            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                transcriptionProvider,
                diarizationProvider,
                new TranscriptRenderer(),
                new FilePublishService());

            var published = await processor.ProcessAsync(
                manifestPath,
                new AppConfig
                {
                    WorkDir = workDir,
                    AudioOutputDir = audioDir,
                    TranscriptOutputDir = transcriptDir,
                    TranscriptionModelPath = Path.Combine(root, "models", "dummy.bin"),
                });

            var finalManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(0, diarizationProvider.CallCount);
            Assert.Equal(SessionState.Published, finalManifest.State);
            Assert.Equal(StageExecutionState.Skipped, finalManifest.DiarizationStatus.State);
            Assert.True(File.Exists(published.AudioPath));
            Assert.True(File.Exists(published.MarkdownPath));
            Assert.True(File.Exists(published.JsonPath));
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

    [Fact]
    public async Task ProcessAsync_Reuses_A_Persisted_Transcription_Snapshot_For_A_Repaired_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var workDir = Path.Combine(root, "work");
            var audioDir = Path.Combine(root, "audio");
            var transcriptDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(audioDir);
            Directory.CreateDirectory(transcriptDir);

            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = await manifestStore.CreateAsync(
                workDir,
                MeetingPlatform.Teams,
                "Queued Session",
                Array.Empty<DetectionSignal>());
            var sessionRoot = Path.Combine(workDir, manifest.SessionId);
            var manifestPath = Path.Combine(sessionRoot, "manifest.json");
            var sourceAudioPath = Path.Combine(sessionRoot, "processing", "existing-audio.wav");
            await WriteSilentWaveFileAsync(sourceAudioPath, TimeSpan.FromSeconds(1));

            var updatedManifest = manifest with
            {
                State = SessionState.Queued,
                EndedAtUtc = manifest.StartedAtUtc.AddMinutes(1),
                MergedAudioPath = sourceAudioPath,
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "done"),
                ProcessingOverrides = new MeetingProcessingOverrides(null, null, true),
            };
            await manifestStore.SaveAsync(updatedManifest, manifestPath);

            var persistedTranscriptionPath = SessionProcessor.GetPersistedTranscriptionSnapshotPath(Path.Combine(sessionRoot, "processing"));
            await File.WriteAllTextAsync(
                persistedTranscriptionPath,
                """
                {
                  "segments": [
                    {
                      "start": "00:00:00",
                      "end": "00:00:01",
                      "speakerId": null,
                      "speakerLabel": null,
                      "text": "persisted text"
                    }
                  ],
                  "language": "en",
                  "message": "persisted"
                }
                """);
            var loadedSnapshot = await SessionProcessor.LoadPersistedTranscriptionSnapshotAsync(persistedTranscriptionPath);
            Assert.NotNull(loadedSnapshot);
            Assert.Single(loadedSnapshot.Segments);
            Assert.Equal("persisted text", loadedSnapshot.Segments[0].Text);

            var transcriptionProvider = new TrackingTranscriptionProvider();
            var diarizationProvider = new TrackingDiarizationProvider();
            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                transcriptionProvider,
                diarizationProvider,
                new TranscriptRenderer(),
                new FilePublishService());

            var published = await processor.ProcessAsync(
                manifestPath,
                new AppConfig
                {
                    WorkDir = workDir,
                    AudioOutputDir = audioDir,
                    TranscriptOutputDir = transcriptDir,
                    TranscriptionModelPath = Path.Combine(root, "models", "dummy.bin"),
                });

            Assert.Equal(0, transcriptionProvider.CallCount);
            Assert.Equal(0, diarizationProvider.CallCount);
            Assert.True(File.Exists(published.MarkdownPath));
            Assert.Contains("persisted text", await File.ReadAllTextAsync(published.MarkdownPath));
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

    [Fact]
    public async Task ProcessAsync_Prunes_Raw_Capture_Files_After_Successful_Publish()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var workDir = Path.Combine(root, "work");
            var audioDir = Path.Combine(root, "audio");
            var transcriptDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(audioDir);
            Directory.CreateDirectory(transcriptDir);

            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = await manifestStore.CreateAsync(
                workDir,
                MeetingPlatform.Teams,
                "Queued Session",
                Array.Empty<DetectionSignal>());
            var sessionRoot = Path.Combine(workDir, manifest.SessionId);
            var manifestPath = Path.Combine(sessionRoot, "manifest.json");
            var rawDir = Path.Combine(sessionRoot, "raw");
            Directory.CreateDirectory(rawDir);
            var loopbackChunkPath = Path.Combine(rawDir, "loopback-chunk-0001.wav");
            var microphoneChunkPath = Path.Combine(rawDir, "microphone-chunk-0001.wav");
            await WriteSilentWaveFileAsync(loopbackChunkPath, TimeSpan.FromSeconds(1));
            await WriteSilentWaveFileAsync(microphoneChunkPath, TimeSpan.FromSeconds(1));

            var updatedManifest = manifest with
            {
                State = SessionState.Queued,
                EndedAtUtc = manifest.StartedAtUtc.AddMinutes(1),
                RawChunkPaths = [loopbackChunkPath],
                LoopbackCaptureSegments =
                [
                    new LoopbackCaptureSegment(
                        manifest.StartedAtUtc,
                        manifest.StartedAtUtc.AddMinutes(1),
                        [loopbackChunkPath],
                        "loopback-device",
                        "Loopback Device",
                        "Multimedia"),
                ],
                MicrophoneChunkPaths = [microphoneChunkPath],
                MicrophoneCaptureSegments =
                [
                    new MicrophoneCaptureSegment(
                        manifest.StartedAtUtc,
                        manifest.StartedAtUtc.AddMinutes(1),
                        [microphoneChunkPath]),
                ],
                ProcessingOverrides = new MeetingProcessingOverrides(null, null, true),
            };
            await manifestStore.SaveAsync(updatedManifest, manifestPath);

            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                new FakeTranscriptionProvider(),
                new TrackingDiarizationProvider(),
                new TranscriptRenderer(),
                new FilePublishService());

            var published = await processor.ProcessAsync(
                manifestPath,
                new AppConfig
                {
                    WorkDir = workDir,
                    AudioOutputDir = audioDir,
                    TranscriptOutputDir = transcriptDir,
                    TranscriptionModelPath = Path.Combine(root, "models", "dummy.bin"),
                });

            var finalManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(SessionState.Published, finalManifest.State);
            Assert.Empty(finalManifest.RawChunkPaths);
            Assert.Empty(finalManifest.LoopbackCaptureSegments);
            Assert.Empty(finalManifest.MicrophoneChunkPaths);
            Assert.Empty(finalManifest.MicrophoneCaptureSegments);
            Assert.False(File.Exists(loopbackChunkPath));
            Assert.False(File.Exists(microphoneChunkPath));
            Assert.True(File.Exists(finalManifest.MergedAudioPath));
            Assert.True(File.Exists(published.AudioPath));
            Assert.True(File.Exists(published.MarkdownPath));
            Assert.True(File.Exists(published.JsonPath));
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

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[format.AverageBytesPerSecond];
        var remainingBytes = (int)Math.Round(duration.TotalSeconds * format.AverageBytesPerSecond);
        while (remainingBytes > 0)
        {
            var bytesToWrite = Math.Min(buffer.Length, remainingBytes);
            writer.Write(buffer, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }

        return Task.CompletedTask;
    }

    private sealed class FakeTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            IReadOnlyList<TranscriptSegment> segments =
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, null, "hello world"),
            ];

            return Task.FromResult(new TranscriptionResult(segments, "en", "ok"));
        }
    }

    private sealed class TrackingTranscriptionProvider : ITranscriptionProvider
    {
        public int CallCount { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            CallCount++;
            IReadOnlyList<TranscriptSegment> segments =
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, null, "should not be used"),
            ];

            return Task.FromResult(new TranscriptionResult(segments, "en", "ok"));
        }
    }

    private sealed class TrackingDiarizationProvider : IDiarizationProvider
    {
        public int CallCount { get; private set; }

        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new DiarizationResult(transcriptSegments, false, "not expected"));
        }
    }
}
