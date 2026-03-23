namespace AppPlatform.Configuration;

public sealed class LiveJsonConfig<TConfig>
{
    private readonly IConfigStore<TConfig> _store;
    private readonly object _syncRoot = new();
    private TConfig _current;
    private DateTimeOffset _lastObservedWriteUtc;

    public LiveJsonConfig(IConfigStore<TConfig> store, TConfig initialConfig)
    {
        _store = store;
        _current = initialConfig;
        _lastObservedWriteUtc = ReadLastWriteUtc();
    }

    public event EventHandler<LiveJsonConfigChangedEventArgs<TConfig>>? Changed;

    public string ConfigPath => _store.ConfigPath;

    public TConfig Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public async Task<TConfig> SaveAsync(TConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = await _store.SaveAsync(config, cancellationToken);
        UpdateCurrent(saved, LiveJsonConfigChangeSource.Save);
        return saved;
    }

    public async Task<TConfig?> ReloadIfChangedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentWriteUtc = ReadLastWriteUtc();
        lock (_syncRoot)
        {
            if (currentWriteUtc <= _lastObservedWriteUtc)
            {
                return default;
            }
        }

        var loaded = await _store.LoadOrCreateAsync(cancellationToken);
        UpdateCurrent(loaded, LiveJsonConfigChangeSource.Reload);
        return loaded;
    }

    private void UpdateCurrent(TConfig config, LiveJsonConfigChangeSource source)
    {
        LiveJsonConfigChangedEventArgs<TConfig> changedArgs;

        lock (_syncRoot)
        {
            var previous = _current;
            _current = config;
            _lastObservedWriteUtc = ReadLastWriteUtc();
            changedArgs = new LiveJsonConfigChangedEventArgs<TConfig>(previous, config, source);
        }

        Changed?.Invoke(this, changedArgs);
    }

    private DateTimeOffset ReadLastWriteUtc()
    {
        return File.Exists(_store.ConfigPath)
            ? File.GetLastWriteTimeUtc(_store.ConfigPath)
            : DateTimeOffset.MinValue;
    }
}

public enum LiveJsonConfigChangeSource
{
    Save = 0,
    Reload = 1,
}

public sealed record LiveJsonConfigChangedEventArgs<TConfig>(
    TConfig PreviousConfig,
    TConfig CurrentConfig,
    LiveJsonConfigChangeSource Source);
