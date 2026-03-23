namespace AppPlatform.Configuration;

public interface IConfigStore<TConfig>
{
    string ConfigPath { get; }

    Task<TConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default);

    Task<TConfig> SaveAsync(TConfig config, CancellationToken cancellationToken = default);
}
