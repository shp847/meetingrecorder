using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public sealed class LiveAppConfig
{
    private readonly AppConfigStore _configStore;
    private readonly object _syncRoot = new();
    private AppConfig _current;
    private DateTimeOffset _lastObservedWriteUtc;

    public LiveAppConfig(AppConfigStore configStore, AppConfig initialConfig)
    {
        _configStore = configStore;
        _current = initialConfig;
        _lastObservedWriteUtc = ReadLastWriteUtc();
    }

    public event EventHandler<LiveAppConfigChangedEventArgs>? Changed;

    public string ConfigPath => _configStore.ConfigPath;

    public AppConfig Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public async Task<AppConfig> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var saved = await _configStore.SaveAsync(config, cancellationToken);
        UpdateCurrent(saved, LiveAppConfigChangeSource.Save);
        return saved;
    }

    public async Task<AppConfig?> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentWriteUtc = ReadLastWriteUtc();
        lock (_syncRoot)
        {
            if (currentWriteUtc <= _lastObservedWriteUtc)
            {
                return null;
            }
        }

        var loaded = await _configStore.LoadOrCreateAsync(cancellationToken);
        UpdateCurrent(loaded, LiveAppConfigChangeSource.Reload);
        return loaded;
    }

    private void UpdateCurrent(AppConfig config, LiveAppConfigChangeSource source)
    {
        var changedArgs = default(LiveAppConfigChangedEventArgs);

        lock (_syncRoot)
        {
            var previous = _current;
            _current = config;
            _lastObservedWriteUtc = ReadLastWriteUtc();
            changedArgs = new LiveAppConfigChangedEventArgs(previous, config, source);
        }

        Changed?.Invoke(this, changedArgs);
    }

    private DateTimeOffset ReadLastWriteUtc()
    {
        return File.Exists(_configStore.ConfigPath)
            ? File.GetLastWriteTimeUtc(_configStore.ConfigPath)
            : DateTimeOffset.MinValue;
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
