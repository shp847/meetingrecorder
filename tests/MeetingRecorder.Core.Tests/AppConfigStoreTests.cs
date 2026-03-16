using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppConfigStoreTests
{
    [Fact]
    public async Task LoadOrCreateAsync_Creates_Default_Config_When_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath);

        var config = await store.LoadOrCreateAsync();

        Assert.True(File.Exists(configPath));
        Assert.Equal(Path.Combine(root, "audio"), config.AudioOutputDir);
        Assert.Equal(Path.Combine(root, "transcripts"), config.TranscriptOutputDir);
        Assert.Equal(Path.Combine(root, "work"), config.WorkDir);
        Assert.Equal(Path.Combine(root, "models"), config.ModelCacheDir);
        Assert.Equal(0.02d, config.AutoDetectAudioPeakThreshold);
    }

    [Fact]
    public async Task SaveAsync_Persists_Updated_Config()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath);
        _ = await store.LoadOrCreateAsync();

        var saved = await store.SaveAsync(new MeetingRecorder.Core.Configuration.AppConfig
        {
            AudioOutputDir = Path.Combine(root, "custom-audio"),
            TranscriptOutputDir = Path.Combine(root, "custom-transcripts"),
            WorkDir = Path.Combine(root, "custom-work"),
            ModelCacheDir = Path.Combine(root, "custom-models"),
            TranscriptionModelPath = Path.Combine(root, "custom-models", "asr", "ggml-base.bin"),
            DiarizationAssetPath = Path.Combine(root, "custom-models", "diarization"),
            MicCaptureEnabled = true,
            AutoDetectEnabled = false,
            AutoDetectAudioPeakThreshold = 0.09d,
            MeetingStopTimeoutSeconds = 12,
        });

        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(saved, reloaded);
        Assert.Equal(0.09d, reloaded.AutoDetectAudioPeakThreshold);
        Assert.Equal(12, reloaded.MeetingStopTimeoutSeconds);
        Assert.True(reloaded.MicCaptureEnabled);
        Assert.False(reloaded.AutoDetectEnabled);
    }

    [Fact]
    public void GetAppRoot_Returns_Portable_Data_Path_When_Portable_Marker_Is_Present()
    {
        var bundleRoot = Path.Combine(Path.GetTempPath(), "MeetingRecorderPortableTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundleRoot);
        File.WriteAllText(Path.Combine(bundleRoot, "portable.mode"), "portable");

        var appRoot = AppDataPaths.GetAppRoot(bundleRoot);

        Assert.Equal(Path.Combine(bundleRoot, "data"), appRoot);
    }
}
