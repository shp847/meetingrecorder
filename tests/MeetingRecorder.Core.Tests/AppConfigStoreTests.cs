using MeetingRecorder.Core.Services;
using MeetingRecorder.Core.Branding;

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
        Assert.True(config.MicCaptureEnabled);
        Assert.True(config.LaunchOnLoginEnabled);
        Assert.False(config.CalendarTitleFallbackEnabled);
        Assert.True(config.UpdateCheckEnabled);
        Assert.True(config.AutoInstallUpdatesEnabled);
        Assert.Equal(AppBranding.DefaultUpdateFeedUrl, config.UpdateFeedUrl);
        Assert.Equal(AppBranding.Version, config.InstalledReleaseVersion);
        Assert.Equal(string.Empty, config.PendingUpdateZipPath);
        Assert.Equal(string.Empty, config.PendingUpdateVersion);
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
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = false,
            CalendarTitleFallbackEnabled = true,
            UpdateCheckEnabled = false,
            AutoInstallUpdatesEnabled = false,
            UpdateFeedUrl = "https://example.com/releases/latest.json",
            LastUpdateCheckUtc = DateTimeOffset.Parse("2026-03-16T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            InstalledReleaseVersion = "0.2",
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-03-16T11:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            InstalledReleaseAssetSizeBytes = 987654321,
            PendingUpdateZipPath = Path.Combine(root, "downloads", "MeetingRecorder-v0.3-win-x64.zip"),
            PendingUpdateVersion = "0.3",
            PendingUpdatePublishedAtUtc = DateTimeOffset.Parse("2026-03-16T12:30:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            PendingUpdateAssetSizeBytes = 123456789,
            AutoDetectAudioPeakThreshold = 0.09d,
            MeetingStopTimeoutSeconds = 12,
        });

        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(saved, reloaded);
        Assert.Equal(0.09d, reloaded.AutoDetectAudioPeakThreshold);
        Assert.Equal(12, reloaded.MeetingStopTimeoutSeconds);
        Assert.True(reloaded.MicCaptureEnabled);
        Assert.True(reloaded.LaunchOnLoginEnabled);
        Assert.False(reloaded.AutoDetectEnabled);
        Assert.True(reloaded.CalendarTitleFallbackEnabled);
        Assert.False(reloaded.UpdateCheckEnabled);
        Assert.False(reloaded.AutoInstallUpdatesEnabled);
        Assert.Equal("https://example.com/releases/latest.json", reloaded.UpdateFeedUrl);
        Assert.Equal("0.2", reloaded.InstalledReleaseVersion);
        Assert.Equal(987654321, reloaded.InstalledReleaseAssetSizeBytes);
        Assert.Equal(Path.Combine(root, "downloads", "MeetingRecorder-v0.3-win-x64.zip"), reloaded.PendingUpdateZipPath);
        Assert.Equal("0.3", reloaded.PendingUpdateVersion);
        Assert.Equal(123456789, reloaded.PendingUpdateAssetSizeBytes);
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
