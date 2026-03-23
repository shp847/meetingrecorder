using AppPlatform.Configuration;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class AppConfigStore : IConfigStore<AppConfig>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string? _documentsDirectoryOverride;

    public AppConfigStore(string configPath, string? documentsDirectoryOverride = null)
    {
        ConfigPath = configPath;
        _documentsDirectoryOverride = documentsDirectoryOverride;
    }

    public string ConfigPath { get; }

    public string? LastLoadDiagnosticMessage { get; private set; }

    public Task<AppConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastLoadDiagnosticMessage = null;

        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var rootDirectory = Directory.GetParent(configDirectory)?.FullName
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");
        var documentsDirectory = ResolveDocumentsDirectory();

        Directory.CreateDirectory(configDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaults = BuildDefaultConfig(rootDirectory, documentsDirectory);
            EnsureDirectories(defaults);
            WriteConfigAtomically(ConfigPath, defaults);
            return Task.FromResult(defaults);
        }

        var loaded = TryLoadExistingConfig(rootDirectory, documentsDirectory);
        var normalized = Normalize(loaded, rootDirectory, documentsDirectory);
        MigrateManagedPublishedOutputsIfNeeded(rootDirectory, documentsDirectory, normalized);
        EnsureDirectories(normalized);
        WriteConfigAtomically(ConfigPath, normalized);
        return Task.FromResult(normalized);
    }

    public Task<AppConfig> SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configDirectory = Path.GetDirectoryName(ConfigPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        var rootDirectory = Directory.GetParent(configDirectory)?.FullName
            ?? throw new InvalidOperationException("Config directory must have a parent directory.");
        var documentsDirectory = ResolveDocumentsDirectory();

        Directory.CreateDirectory(configDirectory);
        var normalized = Normalize(config, rootDirectory, documentsDirectory);
        MigrateManagedPublishedOutputsIfNeeded(rootDirectory, documentsDirectory, normalized);
        EnsureDirectories(normalized);
        WriteConfigAtomically(ConfigPath, normalized);
        return Task.FromResult(normalized);
    }

    private AppConfig TryLoadExistingConfig(string rootDirectory, string documentsDirectory)
    {
        var fileContents = File.ReadAllText(ConfigPath);
        var sanitizedContents = fileContents.Trim('\0', '\uFEFF', ' ', '\t', '\r', '\n');
        if (string.IsNullOrWhiteSpace(sanitizedContents))
        {
            LastLoadDiagnosticMessage = $"Recovered config at '{ConfigPath}' because it was empty or contained only null/whitespace bytes. Default settings were restored.";
            return BuildDefaultConfig(rootDirectory, documentsDirectory);
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(sanitizedContents, SerializerOptions)
                ?? BuildDefaultConfig(rootDirectory, documentsDirectory);
        }
        catch (JsonException)
        {
            TryBackupCorruptedConfig();
            LastLoadDiagnosticMessage = $"Recovered config at '{ConfigPath}' because it was corrupted and could not be parsed. Default settings were restored.";
            return BuildDefaultConfig(rootDirectory, documentsDirectory);
        }
    }

    private void TryBackupCorruptedConfig()
    {
        try
        {
            var corruptPath = ConfigPath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(ConfigPath, corruptPath, overwrite: false);
        }
        catch
        {
            // Best effort only. The main goal is to recover the app with defaults.
        }
    }

    private static void WriteConfigAtomically(string configPath, AppConfig config)
    {
        var configDirectory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("Config path must have a parent directory.");
        Directory.CreateDirectory(configDirectory);

        var tempPath = configPath + ".tmp";
        var backupPath = configPath + ".bak";
        var json = JsonSerializer.Serialize(config, SerializerOptions);

        File.WriteAllText(tempPath, json);

        if (File.Exists(configPath))
        {
            File.Replace(tempPath, configPath, backupPath, ignoreMetadataErrors: true);
            File.Delete(backupPath);
            return;
        }

        File.Move(tempPath, configPath);
    }

    private string ResolveDocumentsDirectory()
    {
        return string.IsNullOrWhiteSpace(_documentsDirectoryOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : _documentsDirectoryOverride;
    }

    private static AppConfig BuildDefaultConfig(string rootDirectory, string documentsDirectory)
    {
        var modelCache = Path.Combine(rootDirectory, "models");

        return new AppConfig
        {
            AudioOutputDir = AppDataPaths.GetManagedRecordingsRoot(documentsDirectory),
            TranscriptOutputDir = AppDataPaths.GetManagedTranscriptsRoot(documentsDirectory),
            WorkDir = Path.Combine(rootDirectory, "work"),
            ModelCacheDir = modelCache,
            TranscriptionModelPath = Path.Combine(modelCache, "asr", "ggml-base.bin"),
            DiarizationAssetPath = Path.Combine(modelCache, "diarization"),
            DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
            MicCaptureEnabled = true,
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = false,
            AutoDetectSecurityPromptMigrationApplied = true,
            CalendarTitleFallbackEnabled = false,
            MeetingAttendeeEnrichmentEnabled = true,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = true,
            UpdateFeedUrl = AppBranding.DefaultUpdateFeedUrl,
            LastUpdateCheckUtc = null,
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = null,
            InstalledReleaseAssetSizeBytes = null,
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
            AutoDetectAudioPeakThreshold = 0.02d,
            MeetingStopTimeoutSeconds = 30,
            MeetingsViewMode = MeetingsViewMode.Grouped,
            MeetingsGroupedViewMigrationApplied = true,
            MeetingsSortKey = MeetingsSortKey.Started,
            MeetingsSortDescending = true,
            MeetingsGroupKey = MeetingsGroupKey.Week,
            DismissedMeetingRecommendations = Array.Empty<DismissedMeetingRecommendation>(),
        };
    }

    private static AppConfig Normalize(AppConfig config, string rootDirectory, string documentsDirectory)
    {
        var defaults = BuildDefaultConfig(rootDirectory, documentsDirectory);
        var isLegacyMeetingsWorkspaceConfig = !config.MeetingsGroupedViewMigrationApplied;
        var groupedViewMigrationApplied = config.MeetingsGroupedViewMigrationApplied;
        var meetingsViewMode = NormalizeEnum(config.MeetingsViewMode, defaults.MeetingsViewMode);
        if (!groupedViewMigrationApplied)
        {
            meetingsViewMode = MeetingsViewMode.Grouped;
            groupedViewMigrationApplied = true;
        }
        var meetingAttendeeEnrichmentEnabled = isLegacyMeetingsWorkspaceConfig
            ? defaults.MeetingAttendeeEnrichmentEnabled
            : config.MeetingAttendeeEnrichmentEnabled;
        var autoDetectSecurityPromptMigrationApplied = config.AutoDetectSecurityPromptMigrationApplied;
        var autoDetectEnabled = config.AutoDetectEnabled;
        if (!autoDetectSecurityPromptMigrationApplied)
        {
            autoDetectEnabled = false;
            autoDetectSecurityPromptMigrationApplied = true;
        }

        return config with
        {
            AudioOutputDir = NormalizePublishedOutputPath(config.AudioOutputDir, defaults.AudioOutputDir, GetLegacyAudioOutputDirectories(rootDirectory, documentsDirectory)),
            TranscriptOutputDir = NormalizePublishedOutputPath(config.TranscriptOutputDir, defaults.TranscriptOutputDir, GetLegacyTranscriptOutputDirectories(rootDirectory, documentsDirectory)),
            WorkDir = string.IsNullOrWhiteSpace(config.WorkDir) ? defaults.WorkDir : config.WorkDir,
            ModelCacheDir = string.IsNullOrWhiteSpace(config.ModelCacheDir) ? defaults.ModelCacheDir : config.ModelCacheDir,
            TranscriptionModelPath = string.IsNullOrWhiteSpace(config.TranscriptionModelPath) ? defaults.TranscriptionModelPath : config.TranscriptionModelPath,
            DiarizationAssetPath = string.IsNullOrWhiteSpace(config.DiarizationAssetPath) ? defaults.DiarizationAssetPath : config.DiarizationAssetPath,
            DiarizationAccelerationPreference = NormalizeEnum(config.DiarizationAccelerationPreference, defaults.DiarizationAccelerationPreference),
            AutoDetectEnabled = autoDetectEnabled,
            AutoDetectSecurityPromptMigrationApplied = autoDetectSecurityPromptMigrationApplied,
            UpdateFeedUrl = string.IsNullOrWhiteSpace(config.UpdateFeedUrl) ? defaults.UpdateFeedUrl : config.UpdateFeedUrl,
            InstalledReleaseVersion = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion) ? defaults.InstalledReleaseVersion : config.InstalledReleaseVersion,
            PendingUpdateZipPath = string.IsNullOrWhiteSpace(config.PendingUpdateZipPath) ? string.Empty : config.PendingUpdateZipPath,
            PendingUpdateVersion = string.IsNullOrWhiteSpace(config.PendingUpdateVersion) ? string.Empty : config.PendingUpdateVersion,
            AutoDetectAudioPeakThreshold = config.AutoDetectAudioPeakThreshold <= 0d ? defaults.AutoDetectAudioPeakThreshold : config.AutoDetectAudioPeakThreshold,
            MeetingStopTimeoutSeconds = config.MeetingStopTimeoutSeconds <= 0 ? defaults.MeetingStopTimeoutSeconds : config.MeetingStopTimeoutSeconds,
            MeetingAttendeeEnrichmentEnabled = meetingAttendeeEnrichmentEnabled,
            MeetingsViewMode = meetingsViewMode,
            MeetingsGroupedViewMigrationApplied = groupedViewMigrationApplied,
            MeetingsSortKey = NormalizeEnum(config.MeetingsSortKey, defaults.MeetingsSortKey),
            MeetingsSortDescending = config.MeetingsSortDescending,
            MeetingsGroupKey = NormalizeEnum(config.MeetingsGroupKey, defaults.MeetingsGroupKey),
            DismissedMeetingRecommendations = NormalizeDismissedMeetingRecommendations(config.DismissedMeetingRecommendations),
        };
    }

    private static TEnum NormalizeEnum<TEnum>(TEnum configuredValue, TEnum fallbackValue)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(configuredValue)
            ? configuredValue
            : fallbackValue;
    }

    private static IReadOnlyList<DismissedMeetingRecommendation> NormalizeDismissedMeetingRecommendations(
        IReadOnlyList<DismissedMeetingRecommendation>? dismissedRecommendations)
    {
        if (dismissedRecommendations is null || dismissedRecommendations.Count == 0)
        {
            return Array.Empty<DismissedMeetingRecommendation>();
        }

        return dismissedRecommendations
            .Where(item => !string.IsNullOrWhiteSpace(item.Fingerprint))
            .GroupBy(item => item.Fingerprint.Trim(), StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => item.DismissedAtUtc)
                .First() with
                {
                    Fingerprint = group.Key,
                })
            .OrderBy(item => item.DismissedAtUtc)
            .TakeLast(256)
            .ToArray();
    }

    private static string NormalizePublishedOutputPath(
        string configuredPath,
        string defaultPath,
        IReadOnlyList<string> legacyPaths)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return defaultPath;
        }

        var normalizedConfiguredPath = Path.GetFullPath(configuredPath);
        return legacyPaths.Any(path => PathsEqual(path, normalizedConfiguredPath))
            ? defaultPath
            : configuredPath;
    }

    private static void MigrateManagedPublishedOutputsIfNeeded(
        string rootDirectory,
        string documentsDirectory,
        AppConfig normalizedConfig)
    {
        var defaultAudioOutputDir = AppDataPaths.GetManagedRecordingsRoot(documentsDirectory);
        if (PathsEqual(normalizedConfig.AudioOutputDir, defaultAudioOutputDir))
        {
            foreach (var legacyAudioDirectory in GetLegacyAudioOutputDirectories(rootDirectory, documentsDirectory))
            {
                CopyMissingFiles(legacyAudioDirectory, defaultAudioOutputDir);
            }
        }

        var defaultTranscriptOutputDir = AppDataPaths.GetManagedTranscriptsRoot(documentsDirectory);
        if (PathsEqual(normalizedConfig.TranscriptOutputDir, defaultTranscriptOutputDir))
        {
            foreach (var legacyTranscriptDirectory in GetLegacyTranscriptOutputDirectories(rootDirectory, documentsDirectory))
            {
                CopyMissingFiles(legacyTranscriptDirectory, defaultTranscriptOutputDir);
            }
        }
    }

    private static IReadOnlyList<string> GetLegacyAudioOutputDirectories(string rootDirectory, string documentsDirectory)
    {
        return BuildDistinctPathList(
            Path.Combine(rootDirectory, "audio"),
            Path.Combine(documentsDirectory, "MeetingRecorder", "data", "audio"));
    }

    private static IReadOnlyList<string> GetLegacyTranscriptOutputDirectories(string rootDirectory, string documentsDirectory)
    {
        return BuildDistinctPathList(
            Path.Combine(rootDirectory, "transcripts"),
            Path.Combine(documentsDirectory, "MeetingRecorder", "data", "transcripts"));
    }

    private static IReadOnlyList<string> BuildDistinctPathList(params string[] paths)
    {
        return paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CopyMissingFiles(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory) || PathsEqual(sourceDirectory, destinationDirectory))
        {
            return;
        }

        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            if (File.Exists(destinationFile))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException("Destination file must have a parent directory."));
            File.Copy(sourceFile, destinationFile, overwrite: false);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
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
