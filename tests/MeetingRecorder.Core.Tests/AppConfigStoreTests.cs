using AppPlatform.Configuration;
using MeetingRecorder.Core.Services;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Tests;

public sealed class AppConfigStoreTests
{
    [Fact]
    public void AppConfigStore_Implements_Platform_Config_Store_Contract()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);

        Assert.IsAssignableFrom<IConfigStore<AppConfig>>(store);
    }

    [Fact]
    public async Task LoadOrCreateAsync_Creates_Default_Config_When_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);

        var config = await store.LoadOrCreateAsync();

        Assert.True(File.Exists(configPath));
        Assert.Equal(Path.Combine(documentsRoot, "Meetings", "Recordings"), config.AudioOutputDir);
        Assert.Equal(Path.Combine(documentsRoot, "Meetings", "Transcripts"), config.TranscriptOutputDir);
        Assert.Equal(Path.Combine(root, "work"), config.WorkDir);
        Assert.Equal(Path.Combine(root, "models"), config.ModelCacheDir);
        Assert.Equal(0.02d, config.AutoDetectAudioPeakThreshold);
        Assert.Equal(InferenceAccelerationPreference.Auto, config.DiarizationAccelerationPreference);
        Assert.True(config.MicCaptureEnabled);
        Assert.True(config.LaunchOnLoginEnabled);
        Assert.False(config.AutoDetectEnabled);
        Assert.True(config.AutoDetectSecurityPromptMigrationApplied);
        Assert.False(config.CalendarTitleFallbackEnabled);
        Assert.True(config.MeetingAttendeeEnrichmentEnabled);
        Assert.True(config.UpdateCheckEnabled);
        Assert.True(config.AutoInstallUpdatesEnabled);
        Assert.Equal(AppBranding.DefaultUpdateFeedUrl, config.UpdateFeedUrl);
        Assert.Equal(AppBranding.Version, config.InstalledReleaseVersion);
        Assert.Equal(string.Empty, config.PendingUpdateZipPath);
        Assert.Equal(string.Empty, config.PendingUpdateVersion);
        Assert.Equal(MeetingsViewMode.Grouped, config.MeetingsViewMode);
        Assert.True(config.MeetingsGroupedViewMigrationApplied);
        Assert.Equal(MeetingsSortKey.Started, config.MeetingsSortKey);
        Assert.True(config.MeetingsSortDescending);
        Assert.Equal(MeetingsGroupKey.Week, config.MeetingsGroupKey);
    }

    [Fact]
    public async Task SaveAsync_Persists_Updated_Config()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);
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
            DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
            AutoDetectAudioPeakThreshold = 0.09d,
            MeetingStopTimeoutSeconds = 12,
            MeetingsViewMode = MeetingsViewMode.Grouped,
            MeetingsSortKey = MeetingsSortKey.Title,
            MeetingsSortDescending = false,
            MeetingsGroupKey = MeetingsGroupKey.Month,
            DismissedMeetingRecommendations =
            [
                new DismissedMeetingRecommendation("archive:meeting-1", DateTimeOffset.Parse("2026-03-20T01:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)),
            ],
        });

        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(0.09d, reloaded.AutoDetectAudioPeakThreshold);
        Assert.Equal(12, reloaded.MeetingStopTimeoutSeconds);
        Assert.True(reloaded.MicCaptureEnabled);
        Assert.True(reloaded.LaunchOnLoginEnabled);
        Assert.False(reloaded.AutoDetectEnabled);
        Assert.True(reloaded.CalendarTitleFallbackEnabled);
        Assert.True(reloaded.MeetingAttendeeEnrichmentEnabled);
        Assert.False(reloaded.UpdateCheckEnabled);
        Assert.False(reloaded.AutoInstallUpdatesEnabled);
        Assert.Equal("https://example.com/releases/latest.json", reloaded.UpdateFeedUrl);
        Assert.Equal("0.2", reloaded.InstalledReleaseVersion);
        Assert.Equal(987654321, reloaded.InstalledReleaseAssetSizeBytes);
        Assert.Equal(Path.Combine(root, "downloads", "MeetingRecorder-v0.3-win-x64.zip"), reloaded.PendingUpdateZipPath);
        Assert.Equal("0.3", reloaded.PendingUpdateVersion);
        Assert.Equal(123456789, reloaded.PendingUpdateAssetSizeBytes);
        Assert.Equal(InferenceAccelerationPreference.CpuOnly, reloaded.DiarizationAccelerationPreference);
        Assert.Equal(MeetingsViewMode.Grouped, reloaded.MeetingsViewMode);
        Assert.Equal(MeetingsSortKey.Title, reloaded.MeetingsSortKey);
        Assert.False(reloaded.MeetingsSortDescending);
        Assert.Equal(MeetingsGroupKey.Month, reloaded.MeetingsGroupKey);
        Assert.Equal(saved.AudioOutputDir, reloaded.AudioOutputDir);
        Assert.Equal(saved.TranscriptOutputDir, reloaded.TranscriptOutputDir);
        Assert.Equal(saved.WorkDir, reloaded.WorkDir);
        Assert.Equal(saved.ModelCacheDir, reloaded.ModelCacheDir);
        Assert.Single(reloaded.DismissedMeetingRecommendations);
        Assert.Equal("archive:meeting-1", reloaded.DismissedMeetingRecommendations[0].Fingerprint);
    }

    [Fact]
    public async Task LoadOrCreateAsync_Rebuilds_Default_Config_When_File_Is_Corrupted()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllBytesAsync(configPath, new byte[] { 0x00, 0x00, 0x00, 0x00 });

        var store = new AppConfigStore(configPath, documentsRoot);

        var config = await store.LoadOrCreateAsync();
        var rewritten = await File.ReadAllTextAsync(configPath);

        Assert.Equal(Path.Combine(documentsRoot, "Meetings", "Recordings"), config.AudioOutputDir);
        Assert.Contains("\"audioOutputDir\"", rewritten);
        Assert.DoesNotContain('\0', rewritten);
        Assert.Contains("Recovered config", store.LastLoadDiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadOrCreateAsync_Disables_Legacy_AutoDetect_Once_To_Avoid_Endpoint_Prompts()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "audioOutputDir": "",
              "transcriptOutputDir": "",
              "workDir": "",
              "modelCacheDir": "",
              "transcriptionModelPath": "",
              "diarizationAssetPath": "",
              "micCaptureEnabled": true,
              "launchOnLoginEnabled": true,
              "autoDetectEnabled": true,
              "calendarTitleFallbackEnabled": false,
              "meetingAttendeeEnrichmentEnabled": true,
              "updateCheckEnabled": true,
              "autoInstallUpdatesEnabled": true,
              "updateFeedUrl": ""
            }
            """);

        var store = new AppConfigStore(configPath, documentsRoot);

        var migrated = await store.LoadOrCreateAsync();
        var persisted = await store.LoadOrCreateAsync();

        Assert.False(migrated.AutoDetectEnabled);
        Assert.True(migrated.AutoDetectSecurityPromptMigrationApplied);
        Assert.False(persisted.AutoDetectEnabled);
        Assert.True(persisted.AutoDetectSecurityPromptMigrationApplied);

        var reenabled = await store.SaveAsync(migrated with
        {
            AutoDetectEnabled = true,
        });
        var reloaded = await store.LoadOrCreateAsync();

        Assert.True(reenabled.AutoDetectEnabled);
        Assert.True(reenabled.AutoDetectSecurityPromptMigrationApplied);
        Assert.True(reloaded.AutoDetectEnabled);
        Assert.True(reloaded.AutoDetectSecurityPromptMigrationApplied);
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

    [Fact]
    public async Task SaveAsync_Prunes_Invalid_And_Excess_Dismissed_Meeting_Recommendations()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);
        var defaults = await store.LoadOrCreateAsync();

        var dismissed = Enumerable.Range(0, 300)
            .Select(index => new DismissedMeetingRecommendation($"fingerprint-{index:000}", DateTimeOffset.Parse("2026-03-20T01:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)))
            .Concat([new DismissedMeetingRecommendation(" ", DateTimeOffset.Parse("2026-03-20T01:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind))])
            .ToArray();

        var saved = await store.SaveAsync(defaults with
        {
            DismissedMeetingRecommendations = dismissed,
        });

        Assert.Equal(256, saved.DismissedMeetingRecommendations.Count);
        Assert.DoesNotContain(saved.DismissedMeetingRecommendations, item => string.IsNullOrWhiteSpace(item.Fingerprint));
        Assert.Equal("fingerprint-299", saved.DismissedMeetingRecommendations[^1].Fingerprint);
    }
}
