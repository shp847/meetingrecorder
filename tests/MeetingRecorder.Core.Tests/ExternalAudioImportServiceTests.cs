using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportServiceTests
{
    [Fact]
    public async Task ImportPendingAudioFilesAsync_Creates_Queued_WorkManifest_And_Preserves_Source_File()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceInfo = new FileInfo(sourcePath);
        var sourceLastWriteUtc = DateTime.SpecifyKind(sourceInfo.LastWriteTimeUtc.AddMinutes(-5), DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceLastWriteUtc);
        sourceInfo.Refresh();

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

        var result = Assert.Single(imported);
        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);
        Assert.Equal(SessionState.Queued, manifest.State);
        Assert.Equal("Voice Memo 17", manifest.DetectedTitle);
        Assert.NotNull(manifest.ImportedSourceAudio);
        Assert.Equal(sourcePath, manifest.ImportedSourceAudio!.OriginalPath);
        Assert.Equal(sourceInfo.Length, manifest.ImportedSourceAudio.SourceSizeBytes);
        Assert.Equal(sourceInfo.LastWriteTimeUtc, manifest.ImportedSourceAudio.SourceLastWriteUtc.UtcDateTime);
        Assert.Equal("Voice Memo 17.wav", manifest.ImportedSourceAudio.SourceDisplayName);
        Assert.Equal(ExternalAudioImportMethod.WatchedFolder, manifest.ImportedSourceAudio.ImportMethod);
        Assert.True(manifest.ImportedSourceAudio.SourceRetained);
        Assert.NotNull(manifest.ImportedSourceAudio.ProbedDuration);
        Assert.NotNull(manifest.MergedAudioPath);
        Assert.True(File.Exists(manifest.MergedAudioPath));
        Assert.True(File.Exists(sourcePath));

        var job = await new ExternalAudioImportJobStore(
                Path.Combine(Path.GetDirectoryName(result.ManifestPath)!, "import-job.json"))
            .LoadAsync();
        Assert.NotNull(job.Job);
        Assert.Equal(manifest.SessionId, job.Job!.SessionId);
        Assert.Equal(ExternalAudioImportJobState.Queued, job.Job.State);
        Assert.Equal(Path.Combine("processing", "imported-source.wav"), job.Job.StagedWorkIdentity);
    }

    [Fact]
    public async Task BuildImportCandidatesAsync_Creates_Ready_Explicit_Review_Candidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var candidates = await service.BuildImportCandidatesAsync(
            config,
            [sourcePath],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.CanQueue);
        Assert.Equal(ExternalAudioImportPreflightStatus.Ready, candidate.Preflight.Status);
        Assert.Equal("Voice Memo 17", candidate.Title);
        Assert.Equal("Voice Memo 17.wav", candidate.SourceDisplayName);
        Assert.Equal(ExternalAudioImportMethod.FilePicker, candidate.ImportMethod);
        Assert.NotNull(candidate.Preflight.Duration);
    }

    [Fact]
    public async Task BuildImportCandidatesAsync_Flags_Unsupported_Extensions_Before_Queue()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "not audio");

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var candidates = await service.BuildImportCandidatesAsync(
            config,
            [sourcePath],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow);

        var candidate = Assert.Single(candidates);
        Assert.False(candidate.CanQueue);
        Assert.Equal(ExternalAudioImportPreflightStatus.UnsupportedExtension, candidate.Preflight.Status);
    }

    [Fact]
    public async Task BuildImportCandidatesAsync_Reports_Unsupported_Wav_Without_Reading_Or_Mutating_The_Source()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "not-a-wave.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "not audio");
        var sourceSnapshot = CaptureSourceSnapshot(sourcePath);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var candidate = Assert.Single(await service.BuildImportCandidatesAsync(
            config,
            [sourcePath],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow));

        Assert.Equal(ExternalAudioImportPreflightStatus.UnsupportedCodec, candidate.Preflight.Status);
        Assert.Equal(
            "This WAV structure is not safe for local import. Choose another file or repair the source.",
            candidate.Preflight.Message);
        Assert.DoesNotContain(sourcePath, candidate.Preflight.Message, StringComparison.OrdinalIgnoreCase);
        AssertSourceIsUnchanged(sourcePath, sourceSnapshot);
    }

    [Fact]
    public async Task BuildImportCandidatesAsync_Blocks_A_Ready_Source_When_Managed_Storage_Is_Unavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var unavailableStoragePath = Path.Combine(root, "not-a-directory");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(unavailableStoragePath, "file");
        var config = CreateConfig(root) with { AudioOutputDir = unavailableStoragePath };
        var sourcePath = Path.Combine(root, "imports", "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));

        var candidate = Assert.Single(await new ExternalAudioImportService(new ArtifactPathBuilder())
            .BuildImportCandidatesAsync(
                config,
                [sourcePath],
                ExternalAudioImportMethod.FilePicker,
                DateTimeOffset.UtcNow));

        Assert.Equal(ExternalAudioImportPreflightStatus.BlockedStorage, candidate.Preflight.Status);
        Assert.Equal("Meeting Recorder recordings storage is not ready for this import.", candidate.Preflight.Message);
        Assert.False(candidate.CanQueue);
    }

    [Fact]
    public async Task BuildImportCandidatesAsync_Rejects_Unc_Source_Before_Probing_Or_Queueing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var candidate = Assert.Single(await service.BuildImportCandidatesAsync(
            config,
            ["\\\\fileserver\\imports\\memo.wav"],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow));

        Assert.False(candidate.CanQueue);
        Assert.Equal(ExternalAudioImportPreflightStatus.UnsupportedLocation, candidate.Preflight.Status);
        Assert.Equal("Choose a source stored on this PC before importing.", candidate.Preflight.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(config.WorkDir));
    }

    [Fact]
    public async Task QueueImportAsync_Persists_Explicit_Import_Metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Client Call.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(5));
        var sourceInfo = new FileInfo(sourcePath);
        var startedAtUtc = DateTimeOffset.Parse("2026-07-14T15:30:00Z");

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());
        var result = await service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "Client Call.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.DragDrop,
                "Client Call",
                startedAtUtc,
                "Project Delta",
                TimeSpan.FromSeconds(5),
                SourceRetained: true),
            DateTimeOffset.UtcNow);
        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);
        var importJobPath = Path.Combine(Path.GetDirectoryName(result.ManifestPath)!, "import-job.json");
        var importJob = (await new ExternalAudioImportJobStore(importJobPath).LoadAsync()).Job;
        var serializedJob = await File.ReadAllTextAsync(importJobPath);

        Assert.NotNull(manifest.ImportedSourceAudio);
        Assert.Equal(ExternalAudioImportMethod.DragDrop, manifest.ImportedSourceAudio!.ImportMethod);
        Assert.Equal("Client Call.wav", manifest.ImportedSourceAudio.SourceDisplayName);
        Assert.Equal(TimeSpan.FromSeconds(5), manifest.ImportedSourceAudio.ProbedDuration);
        Assert.True(manifest.ImportedSourceAudio.SourceRetained);
        Assert.Equal("Project Delta", manifest.ProjectName);
        Assert.Equal(startedAtUtc, manifest.StartedAtUtc);
        Assert.True(File.Exists(sourcePath));
        Assert.NotNull(importJob!.ProbeReceipt);
        Assert.Equal("Ready", importJob.ProbeReceipt!.ResultCode);
        Assert.Equal(importJob.Source.ObservationKey, importJob.ProbeReceipt.SourceObservationKey);
        Assert.Equal(TimeSpan.FromSeconds(5), importJob.ProbeReceipt.Duration);
        Assert.DoesNotContain(sourcePath, serializedJob, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(manifest.MergedAudioPath);
        Assert.DoesNotContain(manifest.MergedAudioPath!, serializedJob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueueImportAsync_Stages_And_Persists_A_Setup_Block_Without_Queueing_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);
        var sourcePath = Path.Combine(root, "imports", "Setup blocked.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(3));
        var sourceInfo = new FileInfo(sourcePath);
        var now = DateTimeOffset.UtcNow;
        var readiness = ExternalAudioImportReadinessResolver.Resolve(
            WhisperModelStatusKind.Missing,
            Path.Combine(root, "missing-model.bin"),
            now);

        var result = await new ExternalAudioImportService(new ArtifactPathBuilder()).QueueImportAsync(
            config,
            new ExternalAudioImportRequest(
                sourcePath,
                "Setup blocked.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.FilePicker,
                "Setup blocked",
                now,
                ProjectName: null,
                ProbedDuration: TimeSpan.FromSeconds(3),
                SourceRetained: true),
            now,
            readiness);

        var job = (await new ExternalAudioImportJobStore(
                Path.Combine(Path.GetDirectoryName(result.ManifestPath)!, "import-job.json"))
            .LoadAsync()).Job;
        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);

        Assert.Equal(ExternalAudioImportJobState.BlockedBySetup, result.ImportJobState);
        Assert.Equal(ExternalAudioImportJobState.BlockedBySetup, job!.State);
        Assert.Equal(ExternalAudioImportJobReason.SetupRequired, job.Reason);
        Assert.False(job.ReadinessSnapshot!.CanQueue);
        Assert.True(File.Exists(manifest.MergedAudioPath));
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task QueueImportAsync_Records_Source_Retention_From_The_Copy_Operation_Not_The_Request_Flag()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Source ownership.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceSnapshot = CaptureSourceSnapshot(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());
        var result = await service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "Source ownership.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.FilePicker,
                "Source ownership",
                DateTimeOffset.UtcNow,
                ProjectName: null,
                ProbedDuration: TimeSpan.FromSeconds(2),
                SourceRetained: false),
            DateTimeOffset.UtcNow);

        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);

        Assert.True(manifest.ImportedSourceAudio!.SourceRetained);
        AssertSourceIsUnchanged(sourcePath, sourceSnapshot);
    }

    [Fact]
    public async Task QueueImportAsync_Rejects_A_Source_Changed_After_Preflight_Without_Creating_A_Work_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Changing memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());
        var candidate = Assert.Single(await service.BuildImportCandidatesAsync(
            config,
            [sourcePath],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow));

        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(3));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        var changedSourceSnapshot = CaptureSourceSnapshot(sourcePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                candidate.SourcePath,
                candidate.SourceDisplayName,
                candidate.SourceSizeBytes,
                candidate.SourceLastWriteUtc,
                candidate.ImportMethod,
                candidate.Title,
                candidate.StartedAtUtc,
                ProjectName: null,
                ProbedDuration: candidate.Preflight.Duration,
                SourceRetained: true),
            DateTimeOffset.UtcNow));

        Assert.Equal("The selected source file changed after review. Review it again before queueing.", exception.Message);
        Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
        AssertSourceIsUnchanged(sourcePath, changedSourceSnapshot);
    }

    [Fact]
    public async Task QueueImportAsync_When_Source_Is_Locked_Leaves_Source_And_App_Work_Storage_Unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Locked memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceSnapshot = CaptureSourceSnapshot(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        using (new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => service.QueueImportAsync(
                config.WorkDir,
                new ExternalAudioImportRequest(
                    sourcePath,
                    "Locked memo.wav",
                    sourceInfo.Length,
                    new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                    ExternalAudioImportMethod.DragDrop,
                    "Locked memo",
                    DateTimeOffset.UtcNow,
                    ProjectName: null,
                    ProbedDuration: TimeSpan.FromSeconds(2),
                    SourceRetained: true),
                DateTimeOffset.UtcNow));
        }

        Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
        AssertSourceIsUnchanged(sourcePath, sourceSnapshot);
    }

    [Fact]
    public async Task QueueImportAsync_When_Canceled_Leaves_Source_And_App_Work_Storage_Unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Canceled memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceSnapshot = CaptureSourceSnapshot(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "Canceled memo.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.FilePicker,
                "Canceled memo",
                DateTimeOffset.UtcNow,
                ProjectName: null,
                ProbedDuration: TimeSpan.FromSeconds(2),
                SourceRetained: true),
            DateTimeOffset.UtcNow,
            cancellation.Token));

        Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
        AssertSourceIsUnchanged(sourcePath, sourceSnapshot);
    }

    [Fact]
    public async Task QueueImportAsync_Uses_The_Staged_Copy_After_Original_Source_Is_Removed()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(root, "imports", "Source removed later.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceInfo = new FileInfo(sourcePath);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());
        var result = await service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "Source removed later.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.FilePicker,
                "Source removed later",
                DateTimeOffset.UtcNow,
                ProjectName: null,
                ProbedDuration: TimeSpan.FromSeconds(2),
                SourceRetained: true),
            DateTimeOffset.UtcNow);

        File.Delete(sourcePath);
        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);
        var job = await new ExternalAudioImportJobStore(
                Path.Combine(Path.GetDirectoryName(result.ManifestPath)!, "import-job.json"))
            .LoadAsync();

        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(manifest.MergedAudioPath));
        Assert.Equal(ExternalAudioImportJobState.Queued, job.Job!.State);
    }

    [Fact]
    public async Task QueueImportAsync_Rejects_And_Cleans_An_AppOwned_Copy_When_PostStage_Preparation_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);
        var sourcePath = Path.Combine(root, "imports", "Post-stage failure.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "synthetic probe input");
        var adapter = new SecondProbeFailsPreparationProbe();
        var service = new ExternalAudioImportService(
            new ArtifactPathBuilder(),
            new ExternalAudioMediaProbe(adapter));
        var candidate = Assert.Single(await service.BuildImportCandidatesAsync(
            config,
            [sourcePath],
            ExternalAudioImportMethod.FilePicker,
            DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                candidate.SourcePath,
                candidate.SourceDisplayName,
                candidate.SourceSizeBytes,
                candidate.SourceLastWriteUtc,
                candidate.ImportMethod,
                candidate.Title,
                candidate.StartedAtUtc,
                ProjectName: null,
                candidate.Preflight.Duration,
                SourceRetained: true),
            DateTimeOffset.UtcNow));

        Assert.Equal(
            "The app-owned import copy could not be prepared with the local transcription audio stack. Review the source and try again.",
            exception.Message);
        Assert.Equal(2, adapter.CallCount);
        Assert.True(File.Exists(sourcePath));
        Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
    }

    [Fact]
    public async Task QueueImportAsync_Rejects_A_Source_Inside_App_Work_Storage()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.WorkDir, "not-an-external-source.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceSnapshot = CaptureSourceSnapshot(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "not-an-external-source.wav",
                sourceInfo.Length,
                new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
                ExternalAudioImportMethod.FilePicker,
                "Not external",
                DateTimeOffset.UtcNow,
                ProjectName: null,
                ProbedDuration: TimeSpan.FromSeconds(2),
                SourceRetained: true),
            DateTimeOffset.UtcNow));

        Assert.Equal("Choose a source outside Meeting Recorder work storage before queueing.", exception.Message);
        AssertSourceIsUnchanged(sourcePath, sourceSnapshot);
        Assert.False(Directory.Exists(Path.Combine(config.WorkDir, ".import-staging")));
    }

    [Fact]
    public async Task QueueImportAsync_Rejects_An_Unchanged_Source_That_Already_Has_An_Imported_Work_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);
        var sourcePath = Path.Combine(root, "imports", "Repeat memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceInfo = new FileInfo(sourcePath);
        var request = new ExternalAudioImportRequest(
            sourcePath,
            "Repeat memo.wav",
            sourceInfo.Length,
            new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
            ExternalAudioImportMethod.FilePicker,
            "Repeat memo",
            DateTimeOffset.UtcNow,
            ProjectName: null,
            ProbedDuration: TimeSpan.FromSeconds(2),
            SourceRetained: true);
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        await service.QueueImportAsync(config.WorkDir, request, DateTimeOffset.UtcNow);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.QueueImportAsync(config.WorkDir, request, DateTimeOffset.UtcNow));

        Assert.Equal(
            "This unchanged source file already has an imported work session. Review the existing import instead.",
            exception.Message);
        Assert.Single(Directory.EnumerateFiles(config.WorkDir, "manifest.json", SearchOption.AllDirectories));
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task QueueImportAsync_Rejects_An_Overlong_Source_Path_Before_Reading_Or_Staging_It()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.WorkDir);
        var sourcePath = Path.Combine(root, new string('a', 260) + ".wav");
        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueImportAsync(
            config.WorkDir,
            new ExternalAudioImportRequest(
                sourcePath,
                "long-path.wav",
                0,
                DateTimeOffset.UtcNow,
                ExternalAudioImportMethod.FilePicker,
                "Long path",
                DateTimeOffset.UtcNow,
                ProjectName: null,
                ProbedDuration: null,
                SourceRetained: true),
            DateTimeOffset.UtcNow));

        Assert.Equal("Choose a shorter source path before queueing.", exception.Message);
        Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
    }

    [Fact]
    public async Task ImportPendingAudioFilesAsync_Skips_Audio_That_Already_Has_A_Transcript()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(Path.Combine(config.TranscriptOutputDir, "json"));
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));
        await File.WriteAllTextAsync(Path.Combine(config.TranscriptOutputDir, "json", "Voice Memo 17.ready"), "ready");

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

        Assert.Empty(imported);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task ImportPendingAudioFilesAsync_Skips_Offline_Audio()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));

        try
        {
            File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.Offline);

            var service = new ExternalAudioImportService(new ArtifactPathBuilder());

            var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

            Assert.Empty(imported);
            Assert.True(File.Exists(sourcePath));
            Assert.Empty(Directory.EnumerateDirectories(config.WorkDir));
        }
        finally
        {
            if (File.Exists(sourcePath))
            {
                File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) & ~FileAttributes.Offline);
            }
        }
    }

    [Fact]
    public async Task ImportPendingAudioFilesAsync_DoesNotRetry_Unchanged_File_After_A_Failed_Import()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "Voice Memo 17.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var sourceInfo = new FileInfo(sourcePath);
        var sourceLastWriteUtc = DateTime.SpecifyKind(sourceInfo.LastWriteTimeUtc.AddMinutes(-5), DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceLastWriteUtc);
        sourceInfo.Refresh();

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var existingManifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Manual,
            "Voice Memo 17",
            Array.Empty<DetectionSignal>());

        var sessionRoot = pathBuilder.BuildSessionRoot(config.WorkDir, existingManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var importedCopyPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        File.Copy(sourcePath, importedCopyPath, overwrite: true);

        await manifestStore.SaveAsync(
            existingManifest with
            {
                State = SessionState.Failed,
                MergedAudioPath = importedCopyPath,
                ImportedSourceAudio = new ImportedSourceAudioInfo(
                    sourcePath,
                    sourceInfo.Length,
                    new DateTimeOffset(sourceInfo.LastWriteTimeUtc)),
                ErrorSummary = "model load failed",
            },
            manifestPath);

        var service = new ExternalAudioImportService(pathBuilder);

        var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

        Assert.Empty(imported);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task ImportPendingAudioFilesAsync_Preserves_AppStyle_Stem_Metadata_When_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "2026-03-16_004645_teams_test-call-3.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));

        var service = new ExternalAudioImportService(new ArtifactPathBuilder());

        var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

        var result = Assert.Single(imported);
        var manifest = await new SessionManifestStore(new ArtifactPathBuilder()).LoadAsync(result.ManifestPath);
        Assert.Equal(MeetingPlatform.Teams, manifest.Platform);
        Assert.Equal("Test Call 3", manifest.DetectedTitle);
        Assert.Equal(new DateTimeOffset(2026, 3, 16, 0, 46, 45, TimeSpan.Zero), manifest.StartedAtUtc);
    }

    [Fact]
    public async Task ImportPendingAudioFilesAsync_Skips_AppPublished_Audio_When_A_Normal_Session_Already_Exists_For_The_Same_Meeting()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var config = CreateConfig(root);
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);

        var sourcePath = Path.Combine(config.AudioOutputDir, "2026-03-19_143250_teams_chao-adam.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-5));

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var existingManifest = await manifestStore.CreateAsync(
            config.WorkDir,
            MeetingPlatform.Teams,
            "Chao Adam",
            Array.Empty<DetectionSignal>());

        var sessionRoot = pathBuilder.BuildSessionRoot(config.WorkDir, existingManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(
            existingManifest with
            {
                StartedAtUtc = new DateTimeOffset(2026, 3, 19, 14, 32, 50, TimeSpan.Zero),
                State = SessionState.Processing,
                MergedAudioPath = Path.Combine(sessionRoot, "processing", "merged.wav"),
                ImportedSourceAudio = null,
            },
            manifestPath);

        var service = new ExternalAudioImportService(pathBuilder);

        var imported = await service.ImportPendingAudioFilesAsync(config, DateTimeOffset.UtcNow);

        Assert.Empty(imported);
        Assert.True(File.Exists(sourcePath));
    }

    private static AppConfig CreateConfig(string root)
    {
        return new AppConfig
        {
            AudioOutputDir = Path.Combine(root, "audio"),
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
            WorkDir = Path.Combine(root, "work"),
            ModelCacheDir = Path.Combine(root, "models"),
            TranscriptionModelPath = Path.Combine(root, "models", "fake.bin"),
            DiarizationAssetPath = Path.Combine(root, "diarization"),
        };
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16000, 16, 1);
        var totalBytes = (int)(format.AverageBytesPerSecond * duration.TotalSeconds);
        var buffer = new byte[totalBytes];

        using var writer = new WaveFileWriter(path, format);
        writer.Write(buffer, 0, buffer.Length);
        writer.Flush();
        return Task.CompletedTask;
    }

    private static SourceSnapshot CaptureSourceSnapshot(string sourcePath)
    {
        var sourceInfo = new FileInfo(sourcePath);
        return new SourceSnapshot(
            sourceInfo.FullName,
            sourceInfo.Length,
            sourceInfo.LastWriteTimeUtc,
            File.ReadAllBytes(sourcePath));
    }

    private static void AssertSourceIsUnchanged(string sourcePath, SourceSnapshot expected)
    {
        var sourceInfo = new FileInfo(sourcePath);
        Assert.True(sourceInfo.Exists);
        Assert.Equal(expected.Path, sourceInfo.FullName);
        Assert.Equal(expected.Length, sourceInfo.Length);
        Assert.Equal(expected.LastWriteTimeUtc, sourceInfo.LastWriteTimeUtc);
        Assert.Equal(expected.Contents, File.ReadAllBytes(sourcePath));
    }

    private sealed record SourceSnapshot(
        string Path,
        long Length,
        DateTime LastWriteTimeUtc,
        byte[] Contents);

    private sealed class SecondProbeFailsPreparationProbe : IExternalAudioPreparationProbe
    {
        public int CallCount { get; private set; }

        public Task<ExternalAudioDecodedProbe> ProbeAsync(
            string sourcePath,
            string temporaryPreparedAudioPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 2)
            {
                throw new InvalidDataException("Synthetic staged decode failure.");
            }

            return Task.FromResult(new ExternalAudioDecodedProbe(
                TimeSpan.FromSeconds(2),
                16_000,
                1,
                "test-decoder"));
        }
    }
}
