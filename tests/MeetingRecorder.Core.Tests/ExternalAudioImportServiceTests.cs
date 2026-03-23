using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportServiceTests
{
    [Fact]
    public async Task ImportPendingAudioFilesAsync_Creates_Queued_WorkManifest_And_Removes_Source_File()
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
        Assert.NotNull(manifest.MergedAudioPath);
        Assert.True(File.Exists(manifest.MergedAudioPath));
        Assert.False(File.Exists(sourcePath));
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
