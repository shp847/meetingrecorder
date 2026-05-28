using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task ProcessAsync_Ignores_Persisted_Transcription_Snapshot_When_ForceTranscription_Is_Set()
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
                ProcessingOverrides = new MeetingProcessingOverrides(
                    null,
                    null,
                    SkipSpeakerLabeling: true,
                    ForceTranscription: true),
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
                      "text": "stale persisted text"
                    }
                  ],
                  "language": "en",
                  "message": "persisted"
                }
                """);

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

            var finalManifest = await manifestStore.LoadAsync(manifestPath);
            var markdown = await File.ReadAllTextAsync(published.MarkdownPath);

            Assert.Equal(1, transcriptionProvider.CallCount);
            Assert.Equal(0, diarizationProvider.CallCount);
            Assert.False(finalManifest.ProcessingOverrides?.ForceTranscription == true);
            Assert.Contains("should not be used", markdown);
            Assert.DoesNotContain("stale persisted text", markdown);
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
            Assert.Equal(Path.GetFullPath(published.AudioPath), Path.GetFullPath(finalManifest.MergedAudioPath!));
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
    public async Task ProcessAsync_Generates_Summary_After_Diarization_And_Publishes_Artifacts()
    {
        var context = await CreateQueuedSessionWithExistingAudioAsync();

        try
        {
            var summaryProvider = new TrackingSummaryProvider();
            var processor = CreateProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new FakeTranscriptionProvider(),
                new LabelingDiarizationProvider(),
                summaryProvider);

            var published = await processor.ProcessAsync(
                context.ManifestPath,
                context.CreateConfig() with
                {
                    SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                });

            var finalManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            var snapshotPath = SessionProcessor.GetPersistedSummarySnapshotPath(Path.Combine(context.SessionRoot, "processing"));
            var markdown = await File.ReadAllTextAsync(published.MarkdownPath);
            var json = await File.ReadAllTextAsync(published.JsonPath);
            var document = JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidOperationException("Transcript JSON was not an object.");

            Assert.Equal(1, summaryProvider.CallCount);
            Assert.Equal(StageExecutionState.Succeeded, finalManifest.DiarizationStatus.State);
            Assert.Equal(StageExecutionState.Succeeded, finalManifest.SummarizationStatus.State);
            Assert.Equal("speaker_00", Assert.Single(summaryProvider.LastSegments).SpeakerId);
            Assert.True(File.Exists(snapshotPath));
            Assert.Contains("## Summary", markdown, StringComparison.Ordinal);
            Assert.Contains("The processed meeting has a generated summary.", markdown, StringComparison.Ordinal);
            Assert.Equal("The processed meeting has a generated summary.", document["summary"]?["overview"]?.GetValue<string>());
            Assert.True(File.Exists(published.ReadyMarkerPath));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Skips_Summary_When_Disabled_Without_Calling_Provider()
    {
        var context = await CreateQueuedSessionWithExistingAudioAsync(skipSpeakerLabeling: true);

        try
        {
            var summaryProvider = new TrackingSummaryProvider();
            var processor = CreateProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new FakeTranscriptionProvider(),
                new TrackingDiarizationProvider(),
                summaryProvider);

            var published = await processor.ProcessAsync(context.ManifestPath, context.CreateConfig());
            var finalManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);

            Assert.Equal(0, summaryProvider.CallCount);
            Assert.Equal(SessionState.Published, finalManifest.State);
            Assert.Equal(StageExecutionState.Skipped, finalManifest.SummarizationStatus.State);
            Assert.Contains("disabled", finalManifest.SummarizationStatus.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(published.ReadyMarkerPath));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Marks_Summary_Failed_But_Still_Publishes_Ready_Marker()
    {
        var context = await CreateQueuedSessionWithExistingAudioAsync(skipSpeakerLabeling: true);

        try
        {
            var summaryProvider = new TrackingSummaryProvider
            {
                ResultFactory = request => new MeetingSummaryResult(
                    new ProcessingStageStatus(
                        "summarization",
                        StageExecutionState.Failed,
                        DateTimeOffset.UtcNow,
                        "ModelProxy returned HTTP 503 Failure."),
                    null),
            };
            var processor = CreateProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new FakeTranscriptionProvider(),
                new TrackingDiarizationProvider(),
                summaryProvider);

            var published = await processor.ProcessAsync(
                context.ManifestPath,
                context.CreateConfig() with
                {
                    SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                });
            var finalManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            var markdown = await File.ReadAllTextAsync(published.MarkdownPath);

            Assert.Equal(SessionState.Published, finalManifest.State);
            Assert.Equal(StageExecutionState.Failed, finalManifest.SummarizationStatus.State);
            Assert.True(File.Exists(published.ReadyMarkerPath));
            Assert.DoesNotContain("## Summary", markdown, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Reuses_Persisted_Summary_Snapshot_When_Fingerprint_Matches()
    {
        var context = await CreateQueuedSessionWithExistingAudioAsync(skipSpeakerLabeling: true);

        try
        {
            var segments = new[]
            {
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, null, "hello world"),
            };
            var fingerprint = MeetingSummaryTranscriptFingerprint.Compute(segments);
            var snapshotPath = SessionProcessor.GetPersistedSummarySnapshotPath(Path.Combine(context.SessionRoot, "processing"));
            await File.WriteAllTextAsync(
                snapshotPath,
                JsonSerializer.Serialize(
                    CreateSummary(fingerprint, "Loaded persisted summary."),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            var summaryProvider = new TrackingSummaryProvider();
            var processor = CreateProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new FakeTranscriptionProvider(),
                new TrackingDiarizationProvider(),
                summaryProvider);

            var published = await processor.ProcessAsync(
                context.ManifestPath,
                context.CreateConfig() with
                {
                    SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                });
            var finalManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            var markdown = await File.ReadAllTextAsync(published.MarkdownPath);

            Assert.Equal(0, summaryProvider.CallCount);
            Assert.Equal(StageExecutionState.Succeeded, finalManifest.SummarizationStatus.State);
            Assert.Equal("Loaded persisted summary.", finalManifest.Summary?.Overview);
            Assert.Contains("Loaded persisted summary.", markdown, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Summarizes_When_Diarization_Fails()
    {
        var context = await CreateQueuedSessionWithExistingAudioAsync();

        try
        {
            var summaryProvider = new TrackingSummaryProvider();
            var processor = CreateProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new FakeTranscriptionProvider(),
                new ThrowingDiarizationProvider(),
                summaryProvider);

            var published = await processor.ProcessAsync(
                context.ManifestPath,
                context.CreateConfig() with
                {
                    SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                });
            var finalManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);

            Assert.Equal(StageExecutionState.Failed, finalManifest.DiarizationStatus.State);
            Assert.Equal(StageExecutionState.Succeeded, finalManifest.SummarizationStatus.State);
            Assert.Equal(1, summaryProvider.CallCount);
            Assert.True(File.Exists(published.ReadyMarkerPath));
        }
        finally
        {
            DeleteDirectory(context.Root);
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

    private static SessionProcessor CreateProcessor(
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        ITranscriptionProvider transcriptionProvider,
        IDiarizationProvider diarizationProvider,
        IMeetingSummarizationProvider summarizationProvider)
    {
        return new SessionProcessor(
            manifestStore,
            pathBuilder,
            new WaveChunkMerger(),
            transcriptionProvider,
            diarizationProvider,
            new TranscriptRenderer(),
            new FilePublishService(),
            summarizationProvider);
    }

    private static async Task<ProcessorTestContext> CreateQueuedSessionWithExistingAudioAsync(
        bool skipSpeakerLabeling = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
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
        await manifestStore.SaveAsync(
            manifest with
            {
                State = SessionState.Queued,
                EndedAtUtc = manifest.StartedAtUtc.AddMinutes(1),
                MergedAudioPath = sourceAudioPath,
                ProcessingOverrides = new MeetingProcessingOverrides(null, null, skipSpeakerLabeling),
            },
            manifestPath);

        return new ProcessorTestContext(
            root,
            workDir,
            audioDir,
            transcriptDir,
            sessionRoot,
            manifestPath,
            manifestStore,
            pathBuilder);
    }

    private static MeetingSummary CreateSummary(string fingerprint, string overview)
    {
        return new MeetingSummary(
            overview,
            ["Launch remains on track."],
            ["Proceed with the pilot."],
            [new MeetingSummaryActionItem("Send pilot checklist.", "Pranav", "Friday")],
            ["Confirm legal review timing."],
            new MeetingSummaryProviderInfo(SummaryChatProviderKind.OpenAi, "OpenAI", "gpt-5-mini", false),
            DateTimeOffset.Parse("2026-05-22T14:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            fingerprint);
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
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

    private sealed class LabelingDiarizationProvider : IDiarizationProvider
    {
        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TranscriptSegment> labeledSegments = transcriptSegments
                .Select(segment => segment with
                {
                    SpeakerId = "speaker_00",
                })
                .ToArray();
            return Task.FromResult(new DiarizationResult(
                labeledSegments,
                true,
                "speaker labels applied",
                [new SpeakerIdentity("speaker_00", "Pranav", false)],
                [new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(1))],
                null));
        }
    }

    private sealed class ThrowingDiarizationProvider : IDiarizationProvider
    {
        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("diarization failed");
        }
    }

    private sealed class TrackingSummaryProvider : IMeetingSummarizationProvider
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<TranscriptSegment> LastSegments { get; private set; } = Array.Empty<TranscriptSegment>();

        public Func<MeetingSummaryRequest, MeetingSummaryResult>? ResultFactory { get; init; }

        public Task<MeetingSummaryResult> SummarizeAsync(
            MeetingSummaryRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSegments = request.Segments;
            var result = ResultFactory?.Invoke(request) ?? new MeetingSummaryResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Succeeded,
                    DateTimeOffset.UtcNow,
                    "Summary generated."),
                CreateSummary(
                    MeetingSummaryTranscriptFingerprint.Compute(request.Segments),
                    "The processed meeting has a generated summary."));
            return Task.FromResult(result);
        }
    }

    private sealed record ProcessorTestContext(
        string Root,
        string WorkDir,
        string AudioDir,
        string TranscriptDir,
        string SessionRoot,
        string ManifestPath,
        SessionManifestStore ManifestStore,
        ArtifactPathBuilder PathBuilder)
    {
        public AppConfig CreateConfig()
        {
            return new AppConfig
            {
                WorkDir = WorkDir,
                AudioOutputDir = AudioDir,
                TranscriptOutputDir = TranscriptDir,
                TranscriptionModelPath = Path.Combine(Root, "models", "dummy.bin"),
            };
        }
    }
}
