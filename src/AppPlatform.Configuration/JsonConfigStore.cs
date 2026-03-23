using System.Text.Json;

namespace AppPlatform.Configuration;

public sealed class JsonConfigStore<TConfig> : IConfigStore<TConfig>
{
    private readonly Func<TConfig> _createDefault;
    private readonly Func<TConfig, TConfig> _normalize;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonConfigStore(
        string configPath,
        Func<TConfig> createDefault,
        Func<TConfig, TConfig> normalize,
        JsonSerializerOptions? serializerOptions = null)
    {
        ConfigPath = configPath;
        _createDefault = createDefault;
        _normalize = normalize;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }

    public string ConfigPath { get; }

    public TConfig LoadOrCreate()
    {
        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        Directory.CreateDirectory(configDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaults = _normalize(_createDefault());
            WriteAtomically(defaults);
            return defaults;
        }

        var rawContents = File.ReadAllText(ConfigPath).Trim('\0', '\uFEFF', ' ', '\t', '\r', '\n');
        if (string.IsNullOrWhiteSpace(rawContents))
        {
            var defaults = _normalize(_createDefault());
            WriteAtomically(defaults);
            return defaults;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<TConfig>(rawContents, _serializerOptions);
            var normalized = _normalize(loaded ?? _createDefault());
            WriteAtomically(normalized);
            return normalized;
        }
        catch (JsonException)
        {
            var defaults = _normalize(_createDefault());
            WriteAtomically(defaults);
            return defaults;
        }
    }

    public TConfig Save(TConfig config)
    {
        var normalized = _normalize(config);
        WriteAtomically(normalized);
        return normalized;
    }

    public Task<TConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoadOrCreate());
    }

    public Task<TConfig> SaveAsync(TConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Save(config));
    }

    private void WriteAtomically(TConfig config)
    {
        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        Directory.CreateDirectory(configDirectory);

        var tempPath = ConfigPath + ".tmp";
        var backupPath = ConfigPath + ".bak";
        var json = JsonSerializer.Serialize(config, _serializerOptions);

        File.WriteAllText(tempPath, json);

        if (File.Exists(ConfigPath))
        {
            File.Replace(tempPath, ConfigPath, backupPath, ignoreMetadataErrors: true);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            return;
        }

        File.Move(tempPath, ConfigPath);
    }
}
