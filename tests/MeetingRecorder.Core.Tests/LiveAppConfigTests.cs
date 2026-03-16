using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class LiveAppConfigTests
{
    [Fact]
    public async Task SaveAsync_Updates_Current_Config_Immediately()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath);
        var initial = await store.LoadOrCreateAsync();
        var runtime = new LiveAppConfig(store, initial);

        var saved = await runtime.SaveAsync(initial with
        {
            AutoDetectEnabled = false,
            MeetingStopTimeoutSeconds = 12,
        });

        Assert.False(runtime.Current.AutoDetectEnabled);
        Assert.Equal(12, runtime.Current.MeetingStopTimeoutSeconds);
        Assert.Equal(saved, runtime.Current);
    }

    [Fact]
    public async Task ReloadIfChangedAsync_Refreshes_Current_Config_When_File_Was_Updated_Externally()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath);
        var initial = await store.LoadOrCreateAsync();
        var runtime = new LiveAppConfig(store, initial);

        await Task.Delay(1100);
        await store.SaveAsync(initial with
        {
            AutoDetectAudioPeakThreshold = 0.09d,
            AudioOutputDir = Path.Combine(root, "replacement-audio"),
        });

        var reloaded = await runtime.ReloadIfChangedAsync();

        Assert.NotNull(reloaded);
        Assert.Equal(0.09d, runtime.Current.AutoDetectAudioPeakThreshold);
        Assert.Equal(Path.Combine(root, "replacement-audio"), runtime.Current.AudioOutputDir);
    }
}
