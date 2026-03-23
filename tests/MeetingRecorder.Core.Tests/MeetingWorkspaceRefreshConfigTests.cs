using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingWorkspaceRefreshConfigTests
{
    [Fact]
    public async Task LoadOrCreateAsync_Creates_Grouped_Default_With_Attendee_Enrichment_Enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);

        var config = await store.LoadOrCreateAsync();

        Assert.True(config.MeetingAttendeeEnrichmentEnabled);
        Assert.Equal(MeetingsViewMode.Grouped, config.MeetingsViewMode);
        Assert.True(config.MeetingsGroupedViewMigrationApplied);
    }

    [Fact]
    public async Task LoadOrCreateAsync_Migrates_Legacy_Meetings_View_To_Grouped_Once()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(new
            {
                audioOutputDir = Path.Combine(documentsRoot, "Audio"),
                transcriptOutputDir = Path.Combine(documentsRoot, "Transcripts"),
                workDir = Path.Combine(root, "work"),
                modelCacheDir = Path.Combine(root, "models"),
                transcriptionModelPath = Path.Combine(root, "models", "asr", "ggml-base.bin"),
                diarizationAssetPath = Path.Combine(root, "models", "diarization"),
                micCaptureEnabled = true,
                launchOnLoginEnabled = true,
                autoDetectEnabled = true,
                calendarTitleFallbackEnabled = false,
                updateCheckEnabled = true,
                autoInstallUpdatesEnabled = true,
                updateFeedUrl = "https://example.com/releases/latest.json",
                autoDetectAudioPeakThreshold = 0.02d,
                meetingStopTimeoutSeconds = 30,
                meetingsViewMode = (int)MeetingsViewMode.Table,
                meetingsSortKey = (int)MeetingsSortKey.Started,
                meetingsSortDescending = true,
                meetingsGroupKey = (int)MeetingsGroupKey.Week,
            }));

        var store = new AppConfigStore(configPath, documentsRoot);

        var config = await store.LoadOrCreateAsync();

        Assert.Equal(MeetingsViewMode.Grouped, config.MeetingsViewMode);
        Assert.True(config.MeetingsGroupedViewMigrationApplied);
    }

    [Fact]
    public async Task SaveAsync_Preserves_User_Selected_Table_View_After_Grouped_Migration()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var documentsRoot = Path.Combine(root, "documents");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, documentsRoot);

        var defaults = await store.LoadOrCreateAsync();
        await store.SaveAsync(defaults with
        {
            MeetingsViewMode = MeetingsViewMode.Table,
            MeetingsGroupedViewMigrationApplied = true,
        });

        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(MeetingsViewMode.Table, reloaded.MeetingsViewMode);
        Assert.True(reloaded.MeetingsGroupedViewMigrationApplied);
    }
}
