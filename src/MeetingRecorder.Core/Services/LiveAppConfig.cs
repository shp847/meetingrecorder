using AppPlatform.Configuration;
using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public sealed class LiveAppConfig
{
    private readonly LiveJsonConfig<AppConfig> _liveConfig;

    public LiveAppConfig(AppConfigStore configStore, AppConfig initialConfig)
    {
        _liveConfig = new LiveJsonConfig<AppConfig>(configStore, initialConfig);
        _liveConfig.Changed += OnLiveConfigChanged;
    }

    public event EventHandler<LiveAppConfigChangedEventArgs>? Changed;

    public string ConfigPath => _liveConfig.ConfigPath;

    public AppConfig Current => _liveConfig.Current;

    public async Task<AppConfig> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        return await _liveConfig.SaveAsync(config, cancellationToken);
    }

    public async Task<AppConfig?> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        return await _liveConfig.ReloadIfChangedAsync(cancellationToken);
    }

    private void OnLiveConfigChanged(object? sender, LiveJsonConfigChangedEventArgs<AppConfig> changedArgs)
    {
        var source = changedArgs.Source == LiveJsonConfigChangeSource.Reload
            ? LiveAppConfigChangeSource.Reload
            : LiveAppConfigChangeSource.Save;
        Changed?.Invoke(this, new LiveAppConfigChangedEventArgs(changedArgs.PreviousConfig, changedArgs.CurrentConfig, source));
    }
}

public enum LiveAppConfigChangeSource
{
    Save = 0,
    Reload = 1,
}

public sealed record LiveAppConfigChangedEventArgs(
    AppConfig PreviousConfig,
    AppConfig CurrentConfig,
    LiveAppConfigChangeSource Source);
