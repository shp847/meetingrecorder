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

        Assert.NotNull(manifest.ImportedSourceAudio);
        Assert.Equal(ExternalAudioImportMethod.DragDrop, manifest.ImportedSourceAudio!.ImportMethod);
        Assert.Equal("Client Call.wav", manifest.ImportedSourceAudio.SourceDisplayName);
        Assert.Equal(TimeSpan.FromSeconds(5), manifest.ImportedSourceAudio.ProbedDuration);
        Assert.True(manifest.ImportedSourceAudio.SourceRetained);
        Assert.Equal("Project Delta", manifest.ProjectName);
        Assert.Equal(startedAtUtc, manifest.StartedAtUtc);
        Assert.True(File.Exists(sourcePath));
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
}
