using MeetingRecorder.Core.Configuration;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public AppConfigStore(string configPath)
    {
        ConfigPath = configPath;
    }

    public string ConfigPath { get; }

    public Task<AppConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var rootDirectory = Directory.GetParent(configDirectory)?.FullName
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");

        Directory.CreateDirectory(configDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaults = BuildDefaultConfig(rootDirectory);
            EnsureDirectories(defaults);
            var json = JsonSerializer.Serialize(defaults, SerializerOptions);
            File.WriteAllText(ConfigPath, json);
            return Task.FromResult(defaults);
        }

        var fileContents = File.ReadAllText(ConfigPath);
        var loaded = JsonSerializer.Deserialize<AppConfig>(fileContents, SerializerOptions) ?? BuildDefaultConfig(rootDirectory);
        var normalized = Normalize(loaded, rootDirectory);
        EnsureDirectories(normalized);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, SerializerOptions));
        return Task.FromResult(normalized);
    }

    public Task<AppConfig> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var rootDirectory = Directory.GetParent(configDirectory)?.FullName
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");

        Directory.CreateDirectory(configDirectory);
        var normalized = Normalize(config, rootDirectory);
        EnsureDirectories(normalized);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, SerializerOptions));
        return Task.FromResult(normalized);
    }

    private static AppConfig BuildDefaultConfig(string rootDirectory)
    {
        var modelCache = Path.Combine(rootDirectory, "models");

        return new AppConfig
        {
            AudioOutputDir = Path.Combine(rootDirectory, "audio"),
            TranscriptOutputDir = Path.Combine(rootDirectory, "transcripts"),
            WorkDir = Path.Combine(rootDirectory, "work"),
            ModelCacheDir = modelCache,
            TranscriptionModelPath = Path.Combine(modelCache, "asr", "ggml-base.bin"),
            DiarizationAssetPath = Path.Combine(modelCache, "diarization"),
            MicCaptureEnabled = false,
            AutoDetectEnabled = true,
            AutoDetectAudioPeakThreshold = 0.02d,
            MeetingStopTimeoutSeconds = 30,
        };
    }

    private static AppConfig Normalize(AppConfig config, string rootDirectory)
    {
        var defaults = BuildDefaultConfig(rootDirectory);

        return config with
        {
            AudioOutputDir = string.IsNullOrWhiteSpace(config.AudioOutputDir) ? defaults.AudioOutputDir : config.AudioOutputDir,
            TranscriptOutputDir = string.IsNullOrWhiteSpace(config.TranscriptOutputDir) ? defaults.TranscriptOutputDir : config.TranscriptOutputDir,
            WorkDir = string.IsNullOrWhiteSpace(config.WorkDir) ? defaults.WorkDir : config.WorkDir,
            ModelCacheDir = string.IsNullOrWhiteSpace(config.ModelCacheDir) ? defaults.ModelCacheDir : config.ModelCacheDir,
            TranscriptionModelPath = string.IsNullOrWhiteSpace(config.TranscriptionModelPath) ? defaults.TranscriptionModelPath : config.TranscriptionModelPath,
            DiarizationAssetPath = string.IsNullOrWhiteSpace(config.DiarizationAssetPath) ? defaults.DiarizationAssetPath : config.DiarizationAssetPath,
            AutoDetectAudioPeakThreshold = config.AutoDetectAudioPeakThreshold <= 0d ? defaults.AutoDetectAudioPeakThreshold : config.AutoDetectAudioPeakThreshold,
            MeetingStopTimeoutSeconds = config.MeetingStopTimeoutSeconds <= 0 ? defaults.MeetingStopTimeoutSeconds : config.MeetingStopTimeoutSeconds,
        };
    }

    private static void EnsureDirectories(AppConfig config)
    {
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);
        Directory.CreateDirectory(config.ModelCacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(config.TranscriptionModelPath) ?? config.ModelCacheDir);
        Directory.CreateDirectory(config.DiarizationAssetPath);
    }
}
