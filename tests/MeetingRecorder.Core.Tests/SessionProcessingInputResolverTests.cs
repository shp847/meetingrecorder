using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class SessionProcessingInputResolverTests
{
    [Fact]
    public async Task ResolveAsync_Uses_Only_The_Verified_AppOwned_Import_Copy()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            var manifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            var input = await new SessionProcessingInputResolver().ResolveAsync(
                manifest,
                context.ManifestPath,
                Path.Combine(context.SessionRoot, "processing"),
                "ignored-for-imports",
                new WaveChunkMerger());

            Assert.False(File.Exists(context.OriginalSourcePath));
            Assert.Equal(SessionProcessingInputKind.ImportedAudio, input.Kind);
            Assert.Equal(context.StagedAudioPath, input.AudioPath);
            Assert.Equal(TimeSpan.FromSeconds(2), input.Duration);
            Assert.NotNull(input.ImportJob);
            Assert.Equal(ExternalAudioImportJobState.Queued, input.ImportJob!.State);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ResolveAsync_Rejects_A_Staged_Copy_Changed_After_Its_Probe_Receipt()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            await File.AppendAllTextAsync(context.StagedAudioPath, "changed");
            var manifest = await context.ManifestStore.LoadAsync(context.ManifestPath);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new SessionProcessingInputResolver().ResolveAsync(
                    manifest,
                    context.ManifestPath,
                    Path.Combine(context.SessionRoot, "processing"),
                    "ignored-for-imports",
                    new WaveChunkMerger()));

            Assert.Contains("staged copy changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(context.OriginalSourcePath, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ResolveAsync_Allows_A_Safely_Migrated_Legacy_Staged_Copy_Without_Reading_Its_Original()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            var manifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            File.Delete(context.ImportJobPath);
            var migration = await new ExternalAudioImportJobStore(context.ImportJobPath)
                .MigrateLegacyManifestAsync(manifest, DateTimeOffset.UtcNow);

            var input = await new SessionProcessingInputResolver().ResolveAsync(
                manifest,
                context.ManifestPath,
                Path.Combine(context.SessionRoot, "processing"),
                "ignored-for-imports",
                new WaveChunkMerger());

            Assert.True(migration.Migrated);
            Assert.False(File.Exists(context.OriginalSourcePath));
            Assert.Equal(SessionProcessingInputKind.ImportedAudio, input.Kind);
            Assert.Equal(context.StagedAudioPath, input.AudioPath);
            Assert.Equal(TimeSpan.FromSeconds(2), input.Duration);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Uses_The_Staged_Import_And_Completes_Its_Job_With_A_Stable_Import_Stem()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            var transcription = new TrackingTranscriptionProvider();
            var processor = new SessionProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new WaveChunkMerger(),
                transcription,
                new SkippingDiarizationProvider(),
                new TranscriptRenderer(),
                new FilePublishService());

            var published = await processor.ProcessAsync(
                context.ManifestPath,
                new AppConfig
                {
                    WorkDir = context.WorkDir,
                    AudioOutputDir = context.AudioDir,
                    TranscriptOutputDir = context.TranscriptDir,
                    TranscriptionModelPath = Path.Combine(context.Root, "models", "dummy.bin"),
                });

            var job = (await new ExternalAudioImportJobStore(context.ImportJobPath).LoadAsync()).Job;
            var expectedStem = context.PathBuilder.BuildImportedFileStem(
                MeetingPlatform.Manual,
                context.StartedAtUtc,
                "Imported planning call",
                context.SessionId);

            Assert.False(File.Exists(context.OriginalSourcePath));
            Assert.Equal(context.StagedAudioPath, transcription.LastAudioPath);
            Assert.Equal(ExternalAudioImportJobState.Published, job!.State);
            Assert.Equal(Path.Combine(context.AudioDir, $"{expectedStem}.wav"), published.AudioPath);
            Assert.True(File.Exists(published.AudioPath));
            Assert.True(File.Exists(published.MarkdownPath));
            Assert.True(File.Exists(published.JsonPath));
            Assert.True(File.Exists(published.ReadyMarkerPath));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Records_Imported_Transcription_Failure_Without_Creating_A_Ready_Transcript()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            var processor = new SessionProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new WaveChunkMerger(),
                new ThrowingTranscriptionProvider(),
                new SkippingDiarizationProvider(),
                new TranscriptRenderer(),
                new FilePublishService());

            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(
                context.ManifestPath,
                new AppConfig
                {
                    WorkDir = context.WorkDir,
                    AudioOutputDir = context.AudioDir,
                    TranscriptOutputDir = context.TranscriptDir,
                    TranscriptionModelPath = Path.Combine(context.Root, "models", "dummy.bin"),
                }));

            var expectedStem = context.PathBuilder.BuildImportedFileStem(
                MeetingPlatform.Manual,
                context.StartedAtUtc,
                "Imported planning call",
                context.SessionId);
            var job = (await new ExternalAudioImportJobStore(context.ImportJobPath).LoadAsync()).Job;

            Assert.Equal(ExternalAudioImportJobState.Failed, job!.State);
            Assert.True(File.Exists(Path.Combine(context.AudioDir, $"{expectedStem}.wav")));
            Assert.False(File.Exists(Path.Combine(context.TranscriptDir, $"{expectedStem}.md")));
            Assert.False(File.Exists(Path.Combine(context.TranscriptDir, "json", $"{expectedStem}.json")));
            Assert.False(File.Exists(Path.Combine(context.TranscriptDir, "json", $"{expectedStem}.ready")));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ProcessAsync_Uses_The_Same_Speaker_Labeling_Stage_For_Imported_Staged_Audio()
    {
        var context = await CreateImportedContextAsync();
        try
        {
            var manifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
            await context.ManifestStore.SaveAsync(
                manifest with { ProcessingOverrides = null },
                context.ManifestPath);
            var diarization = new LabelingDiarizationProvider();
            var processor = new SessionProcessor(
                context.ManifestStore,
                context.PathBuilder,
                new WaveChunkMerger(),
                new TrackingTranscriptionProvider(),
                diarization,
                new TranscriptRenderer(),
                new FilePublishService());

            await processor.ProcessAsync(
                context.ManifestPath,
                new AppConfig
                {
                    WorkDir = context.WorkDir,
                    AudioOutputDir = context.AudioDir,
                    TranscriptOutputDir = context.TranscriptDir,
                    TranscriptionModelPath = Path.Combine(context.Root, "models", "dummy.bin"),
                });
            var processed = await context.ManifestStore.LoadAsync(context.ManifestPath);

            Assert.False(File.Exists(context.OriginalSourcePath));
            Assert.Equal(context.StagedAudioPath, diarization.LastAudioPath);
            Assert.Equal(StageExecutionState.Succeeded, processed.DiarizationStatus.State);
            Assert.True(processed.ProcessingMetadata!.HasSpeakerLabels);
            Assert.Equal("speaker-1", Assert.Single(processed.ProcessingMetadata.Speakers!).Id);
            Assert.Equal("speaker-1", Assert.Single(processed.ProcessingMetadata.SpeakerVoiceSamples!).SpeakerId);
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    private static async Task<ImportedContext> CreateImportedContextAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var workDir = Path.Combine(root, "work");
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var startedAtUtc = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var created = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Manual,
            "Imported planning call",
            Array.Empty<DetectionSignal>());
        var sessionRoot = Path.Combine(workDir, created.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var stagedAudioPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        await WriteSilentWaveFileAsync(stagedAudioPath, TimeSpan.FromSeconds(2));
        var stagedFile = new FileInfo(stagedAudioPath);
        var originalSourcePath = Path.Combine(root, "unavailable-original-source.wav");
        var source = ExternalAudioImportSourceObservation.Create(
            originalSourcePath,
            "Planning call.wav",
            ExternalAudioImportMethod.FilePicker,
            42,
            startedAtUtc.AddMinutes(-1),
            startedAtUtc);
        var receipt = ExternalAudioImportProbeReceipt.CreateReady(
            source,
            ExternalAudioImportIdentity.BuildObservationKey(
                stagedAudioPath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc)),
            "test-decoder/v1",
            TimeSpan.FromSeconds(2),
            16_000,
            1,
            startedAtUtc);
        var job = ExternalAudioImportJobFactory.CreateQueued(
            source,
            created.SessionId,
            Path.Combine("processing", "imported-source.wav"),
            startedAtUtc,
            receipt,
            ExternalAudioImportReadinessSnapshot.Ready("test-config", startedAtUtc));
        var importJobPath = Path.Combine(sessionRoot, "import-job.json");
        await new ExternalAudioImportJobStore(importJobPath).SaveAsync(job);
        await manifestStore.SaveAsync(
            created with
            {
                State = SessionState.Queued,
                DetectedTitle = "Imported planning call",
                StartedAtUtc = startedAtUtc,
                MergedAudioPath = stagedAudioPath,
                ImportedSourceAudio = new ImportedSourceAudioInfo(
                    originalSourcePath,
                    source.SourceSizeBytes,
                    source.SourceLastWriteUtc,
                    source.DisplayName,
                    source.ImportMethod,
                    TimeSpan.FromSeconds(2),
                    sourceRetained: true),
                ProcessingOverrides = new MeetingProcessingOverrides(null, null, SkipSpeakerLabeling: true),
            },
            manifestPath);

        return new ImportedContext(
            root,
            workDir,
            audioDir,
            transcriptDir,
            sessionRoot,
            manifestPath,
            importJobPath,
            stagedAudioPath,
            originalSourcePath,
            created.SessionId,
            startedAtUtc,
            pathBuilder,
            manifestStore);
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

    private sealed class TrackingTranscriptionProvider : ITranscriptionProvider
    {
        public string? LastAudioPath { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            LastAudioPath = audioPath;
            IReadOnlyList<TranscriptSegment> segments =
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, null, "verified staged import"),
            ];
            return Task.FromResult(new TranscriptionResult(segments, "en", "ok"));
        }
    }

    private sealed class SkippingDiarizationProvider : IDiarizationProvider
    {
        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken) => Task.FromResult(
                new DiarizationResult(transcriptSegments, false, "Speaker labeling is not configured for this test."));
    }

    private sealed class LabelingDiarizationProvider : IDiarizationProvider
    {
        public string? LastAudioPath { get; private set; }

        public Task<DiarizationResult> ApplySpeakerLabelsAsync(
            string audioPath,
            IReadOnlyList<TranscriptSegment> transcriptSegments,
            CancellationToken cancellationToken)
        {
            LastAudioPath = audioPath;
            var speaker = new SpeakerIdentity("speaker-1", "Speaker 1", false);
            var sample = new SpeakerVoiceSample(
                "speaker-1",
                "local-embedding.onnx",
                2,
                [0.2f, 0.8f],
                TimeSpan.FromSeconds(1),
                DateTimeOffset.UtcNow);
            var labeledSegments = transcriptSegments
                .Select(segment => segment with { SpeakerId = "speaker-1", SpeakerLabel = "Speaker 1" })
                .ToArray();
            return Task.FromResult(new DiarizationResult(
                labeledSegments,
                AppliedSpeakerLabels: true,
                "Applied from staged audio.",
                [speaker],
                [new SpeakerTurn("speaker-1", TimeSpan.Zero, TimeSpan.FromSeconds(1))],
                Metadata: null,
                [sample]));
        }
    }

    private sealed class ThrowingTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Transcription test failure.");
    }

    private sealed record ImportedContext(
        string Root,
        string WorkDir,
        string AudioDir,
        string TranscriptDir,
        string SessionRoot,
        string ManifestPath,
        string ImportJobPath,
        string StagedAudioPath,
        string OriginalSourcePath,
        string SessionId,
        DateTimeOffset StartedAtUtc,
        ArtifactPathBuilder PathBuilder,
        SessionManifestStore ManifestStore);
}
