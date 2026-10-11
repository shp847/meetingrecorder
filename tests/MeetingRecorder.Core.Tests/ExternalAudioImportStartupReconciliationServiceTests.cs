using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportStartupReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAsync_Admits_Only_A_Queued_Import_With_A_Current_Staged_Receipt()
    {
        var context = await CreateImportedContextAsync(SessionState.Queued, ExternalAudioImportJobState.Queued);
        try
        {
            var result = await context.Service.ReconcileAsync(context.WorkDir, context.AudioDir, context.TranscriptDir);

            Assert.Equal([context.ManifestPath], result.PendingManifestPaths);
            Assert.Equal(1, result.Count(ExternalAudioImportRecoveryDisposition.ResumeQueued));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ReconcileAsync_Leaves_A_Changed_Staged_Copy_Out_Of_Backlog()
    {
        var context = await CreateImportedContextAsync(SessionState.Queued, ExternalAudioImportJobState.Queued);
        try
        {
            await File.AppendAllTextAsync(context.StagedAudioPath, "changed");

            var result = await context.Service.ReconcileAsync(context.WorkDir, context.AudioDir, context.TranscriptDir);

            Assert.Empty(result.PendingManifestPaths);
            Assert.Equal(1, result.Count(ExternalAudioImportRecoveryDisposition.ManualRecovery));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ReconcileAsync_Treats_Complete_Artifacts_As_Published_After_A_Crash_Before_Job_Checkpoint()
    {
        var context = await CreateImportedContextAsync(SessionState.Published, ExternalAudioImportJobState.Processing);
        try
        {
            var sidecarRoot = ArtifactPathBuilder.BuildTranscriptSidecarRoot(context.TranscriptDir);
            Directory.CreateDirectory(sidecarRoot);
            await File.WriteAllTextAsync(Path.Combine(context.AudioDir, $"{context.OutputStem}.wav"), "audio");
            await File.WriteAllTextAsync(Path.Combine(context.TranscriptDir, $"{context.OutputStem}.md"), "transcript");
            await File.WriteAllTextAsync(Path.Combine(sidecarRoot, $"{context.OutputStem}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(sidecarRoot, $"{context.OutputStem}.ready"), "ready");

            var result = await context.Service.ReconcileAsync(context.WorkDir, context.AudioDir, context.TranscriptDir);

            Assert.Empty(result.PendingManifestPaths);
            Assert.Equal(1, result.Count(ExternalAudioImportRecoveryDisposition.Published));
        }
        finally
        {
            DeleteDirectory(context.Root);
        }
    }

    [Fact]
    public async Task ReconcileAsync_Preserves_Legacy_Queued_Imports_For_Existing_Recovery_Flow()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workDir = Path.Combine(root, "work");
            var audioDir = Path.Combine(root, "audio");
            var transcriptDir = Path.Combine(root, "transcripts");
            Directory.CreateDirectory(workDir);
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = await manifestStore.CreateAsync(workDir, MeetingPlatform.Manual, "Legacy import", Array.Empty<DetectionSignal>());
            var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
            await manifestStore.SaveAsync(
                manifest with
                {
                    State = SessionState.Queued,
                    ImportedSourceAudio = new ImportedSourceAudioInfo("C:\\legacy\\source.wav", 1, DateTimeOffset.UtcNow),
                },
                manifestPath);

            var result = await new ExternalAudioImportStartupReconciliationService(manifestStore)
                .ReconcileAsync(workDir, audioDir, transcriptDir);

            Assert.Equal([manifestPath], result.PendingManifestPaths);
            Assert.Equal(1, result.Count(ExternalAudioImportRecoveryDisposition.ResumeQueued));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<ImportContext> CreateImportedContextAsync(
        SessionState manifestState,
        ExternalAudioImportJobState jobState)
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
        var startedAtUtc = DateTimeOffset.Parse("2026-09-27T15:00:00Z");
        var manifest = await manifestStore.CreateAsync(workDir, MeetingPlatform.Manual, "Recovered import", Array.Empty<DetectionSignal>());
        var sessionRoot = Path.Combine(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var stagedAudioPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        await WriteSilentWaveFileAsync(stagedAudioPath, TimeSpan.FromSeconds(2));
        var staged = new FileInfo(stagedAudioPath);
        var source = ExternalAudioImportSourceObservation.Create(
            Path.Combine(root, "original-never-read.wav"),
            "Recovered import.wav",
            ExternalAudioImportMethod.FilePicker,
            12,
            startedAtUtc.AddMinutes(-1),
            startedAtUtc);
        var receipt = ExternalAudioImportProbeReceipt.CreateReady(
            source,
            ExternalAudioImportIdentity.BuildObservationKey(stagedAudioPath, staged.Length, new DateTimeOffset(staged.LastWriteTimeUtc)),
            "test-decoder/v1",
            TimeSpan.FromSeconds(2),
            16_000,
            1,
            startedAtUtc);
        var queued = ExternalAudioImportJobFactory.CreateQueued(
            source,
            manifest.SessionId,
            Path.Combine("processing", "imported-source.wav"),
            startedAtUtc,
            receipt,
            ExternalAudioImportReadinessSnapshot.Ready("test-config", startedAtUtc));
        var job = jobState == ExternalAudioImportJobState.Queued
            ? queued
            : ExternalAudioImportJobTransitions.TryTransition(
                queued,
                queued.Revision,
                jobState,
                ExternalAudioImportJobReason.None,
                startedAtUtc.AddSeconds(1)).Job!;
        await new ExternalAudioImportJobStore(Path.Combine(sessionRoot, "import-job.json")).SaveAsync(job);

        var outputStem = pathBuilder.BuildImportedFileStem(
            MeetingPlatform.Manual,
            startedAtUtc,
            "Recovered import",
            manifest.SessionId);
        await manifestStore.SaveAsync(
            manifest with
            {
                State = manifestState,
                StartedAtUtc = startedAtUtc,
                MergedAudioPath = stagedAudioPath,
                ImportedSourceAudio = new ImportedSourceAudioInfo(
                    source.OriginalLocator,
                    source.SourceSizeBytes,
                    source.SourceLastWriteUtc,
                    source.DisplayName,
                    source.ImportMethod,
                    TimeSpan.FromSeconds(2),
                    sourceRetained: true)
                {
                    OutputStem = outputStem,
                },
            },
            manifestPath);

        return new ImportContext(
            root,
            workDir,
            audioDir,
            transcriptDir,
            manifestPath,
            stagedAudioPath,
            outputStem,
            new ExternalAudioImportStartupReconciliationService(manifestStore));
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        var bytes = new byte[(int)(duration.TotalSeconds * format.AverageBytesPerSecond)];
        writer.Write(bytes, 0, bytes.Length);
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

    private sealed record ImportContext(
        string Root,
        string WorkDir,
        string AudioDir,
        string TranscriptDir,
        string ManifestPath,
        string StagedAudioPath,
        string OutputStem,
        ExternalAudioImportStartupReconciliationService Service);
}
