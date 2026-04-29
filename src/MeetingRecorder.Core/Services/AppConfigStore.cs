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
        var catalog = MeetingRecorderModelCatalog.CreateDefault();
        var defaultTranscriptionModelPath = Path.Combine(
            modelCache,
            catalog.Transcription.Standard.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var defaultDiarizationAssetPath = Path.Combine(
            modelCache,
            catalog.SpeakerLabeling.Standard.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return new AppConfig
        {
            AudioOutputDir = AppDataPaths.GetManagedRecordingsRoot(documentsDirectory),
            TranscriptOutputDir = AppDataPaths.GetManagedTranscriptsRoot(documentsDirectory),
            WorkDir = Path.Combine(rootDirectory, "work"),
            ModelCacheDir = modelCache,
            TranscriptionModelPath = defaultTranscriptionModelPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Standard,
            DiarizationAssetPath = defaultDiarizationAssetPath,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Standard,
            DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
            DiarizationAccelerationSecurityPromptMigrationApplied = true,
            MicCaptureEnabled = true,
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = true,
            AutoDetectSecurityPromptMigrationApplied = true,
            CalendarTitleFallbackEnabled = false,
            MeetingAttendeeEnrichmentEnabled = true,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = true,
            UpdateFeedUrl = AppBranding.DefaultUpdateFeedUrl,
            BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
            LastUpdateCheckUtc = null,
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = null,
            InstalledReleaseAssetSizeBytes = null,
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
            PendingUpdateInstallWhenIdleRequested = false,
            AutoDetectAudioPeakThreshold = 0.02d,
            MeetingStopTimeoutSeconds = 30,
            PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
            TeamsGraphTenantId = "organizations",
            TeamsGraphClientId = string.Empty,
            TeamsCapabilitySnapshot = new TeamsCapabilitySnapshot
            {
                Status = TeamsCapabilityStatus.FallbackOnly,
                Summary = "Fallback only.",
                Detail = "The local Teams detector remains active until you validate a stronger integration path.",
            },
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
        var normalizedModelCacheDir = string.IsNullOrWhiteSpace(config.ModelCacheDir)
            ? defaults.ModelCacheDir
            : config.ModelCacheDir;
        var normalizedTranscriptionModelPath = string.IsNullOrWhiteSpace(config.TranscriptionModelPath)
            ? defaults.TranscriptionModelPath
            : config.TranscriptionModelPath;
        var normalizedDiarizationAssetPath = string.IsNullOrWhiteSpace(config.DiarizationAssetPath)
            ? config.SpeakerLabelingModelProfilePreference == SpeakerLabelingModelProfilePreference.Disabled
                ? string.Empty
                : defaults.DiarizationAssetPath
            : config.DiarizationAssetPath;
        var normalizedPendingUpdateZipPath = string.IsNullOrWhiteSpace(config.PendingUpdateZipPath)
            ? string.Empty
            : config.PendingUpdateZipPath;
        var normalizedPendingUpdateVersion = string.IsNullOrWhiteSpace(config.PendingUpdateVersion)
            ? string.Empty
            : config.PendingUpdateVersion;
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
        var diarizationAccelerationSecurityPromptMigrationApplied =
            config.DiarizationAccelerationSecurityPromptMigrationApplied;
        var diarizationAccelerationPreference = NormalizeEnum(
            config.DiarizationAccelerationPreference,
            defaults.DiarizationAccelerationPreference);
        if (!diarizationAccelerationSecurityPromptMigrationApplied)
        {
            if (diarizationAccelerationPreference == InferenceAccelerationPreference.Auto)
            {
                diarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly;
            }

            diarizationAccelerationSecurityPromptMigrationApplied = true;
        }

        return config with
        {
            AudioOutputDir = NormalizePublishedOutputPath(config.AudioOutputDir, defaults.AudioOutputDir, GetLegacyAudioOutputDirectories(rootDirectory, documentsDirectory)),
            TranscriptOutputDir = NormalizePublishedOutputPath(config.TranscriptOutputDir, defaults.TranscriptOutputDir, GetLegacyTranscriptOutputDirectories(rootDirectory, documentsDirectory)),
            WorkDir = string.IsNullOrWhiteSpace(config.WorkDir) ? defaults.WorkDir : config.WorkDir,
            ModelCacheDir = normalizedModelCacheDir,
            TranscriptionModelPath = normalizedTranscriptionModelPath,
            TranscriptionModelProfilePreference = InferTranscriptionModelProfilePreference(
                config.TranscriptionModelProfilePreference,
                normalizedModelCacheDir,
                normalizedTranscriptionModelPath,
                defaults.TranscriptionModelPath),
            DiarizationAssetPath = normalizedDiarizationAssetPath,
            SpeakerLabelingModelProfilePreference = InferSpeakerLabelingModelProfilePreference(
                config.SpeakerLabelingModelProfilePreference,
                normalizedModelCacheDir,
                normalizedDiarizationAssetPath,
                defaults.DiarizationAssetPath),
            DiarizationAccelerationPreference = diarizationAccelerationPreference,
            DiarizationAccelerationSecurityPromptMigrationApplied = diarizationAccelerationSecurityPromptMigrationApplied,
            AutoDetectEnabled = autoDetectEnabled,
            AutoDetectSecurityPromptMigrationApplied = autoDetectSecurityPromptMigrationApplied,
            UpdateFeedUrl = string.IsNullOrWhiteSpace(config.UpdateFeedUrl) ? defaults.UpdateFeedUrl : config.UpdateFeedUrl,
            BackgroundProcessingMode = NormalizeEnum(config.BackgroundProcessingMode, defaults.BackgroundProcessingMode),
            BackgroundSpeakerLabelingMode = NormalizeEnum(config.BackgroundSpeakerLabelingMode, defaults.BackgroundSpeakerLabelingMode),
            InstalledReleaseVersion = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion) ? defaults.InstalledReleaseVersion : config.InstalledReleaseVersion,
            PendingUpdateZipPath = normalizedPendingUpdateZipPath,
            PendingUpdateVersion = normalizedPendingUpdateVersion,
            PendingUpdateInstallWhenIdleRequested =
                !string.IsNullOrWhiteSpace(normalizedPendingUpdateZipPath) &&
                config.PendingUpdateInstallWhenIdleRequested,
            AutoDetectAudioPeakThreshold = config.AutoDetectAudioPeakThreshold <= 0d ? defaults.AutoDetectAudioPeakThreshold : config.AutoDetectAudioPeakThreshold,
            MeetingStopTimeoutSeconds = config.MeetingStopTimeoutSeconds <= 0 ? defaults.MeetingStopTimeoutSeconds : config.MeetingStopTimeoutSeconds,
            PreferredTeamsIntegrationMode = NormalizeEnum(config.PreferredTeamsIntegrationMode, defaults.PreferredTeamsIntegrationMode),
            TeamsGraphTenantId = string.IsNullOrWhiteSpace(config.TeamsGraphTenantId) ? defaults.TeamsGraphTenantId : config.TeamsGraphTenantId.Trim(),
            TeamsGraphClientId = string.IsNullOrWhiteSpace(config.TeamsGraphClientId) ? string.Empty : config.TeamsGraphClientId.Trim(),
            TeamsCapabilitySnapshot = config.TeamsCapabilitySnapshot ?? defaults.TeamsCapabilitySnapshot,
            MeetingAttendeeEnrichmentEnabled = meetingAttendeeEnrichmentEnabled,
            MeetingsViewMode = meetingsViewMode,
            MeetingsGroupedViewMigrationApplied = groupedViewMigrationApplied,
            MeetingsSortKey = NormalizeEnum(config.MeetingsSortKey, defaults.MeetingsSortKey),
            MeetingsSortDescending = config.MeetingsSortDescending,
            MeetingsGroupKey = NormalizeEnum(config.MeetingsGroupKey, defaults.MeetingsGroupKey),
            RushProcessingRequest = NormalizeRushProcessingRequest(config.RushProcessingRequest),
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

    private static RushProcessingRequest? NormalizeRushProcessingRequest(RushProcessingRequest? rushProcessingRequest)
    {
        if (rushProcessingRequest is null || string.IsNullOrWhiteSpace(rushProcessingRequest.ManifestPath))
        {
            return null;
        }

        return new RushProcessingRequest(
            rushProcessingRequest.ManifestPath.Trim(),
            NormalizeEnum(rushProcessingRequest.Behavior, RushProcessingBehavior.RunNextOnly),
            rushProcessingRequest.RequestedAtUtc);
    }

    private static TranscriptionModelProfilePreference InferTranscriptionModelProfilePreference(
        TranscriptionModelProfilePreference configuredPreference,
        string modelCacheDir,
        string transcriptionModelPath,
        string defaultTranscriptionModelPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptionModelPath))
        {
            return TranscriptionModelProfilePreference.Standard;
        }

        var normalizedPath = NormalizeComparablePath(transcriptionModelPath);
        var normalizedDefaultPath = NormalizeComparablePath(defaultTranscriptionModelPath);
        var normalizedManagedAsrDirectory = NormalizeComparablePath(Path.Combine(modelCacheDir, "asr"));
        var fileName = Path.GetFileName(normalizedPath);

        if (configuredPreference == TranscriptionModelProfilePreference.HighAccuracyDownloaded)
        {
            return configuredPreference;
        }

        if (configuredPreference == TranscriptionModelProfilePreference.Custom &&
            !string.Equals(normalizedPath, normalizedDefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            return configuredPreference;
        }

        if (string.Equals(normalizedPath, normalizedDefaultPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "ggml-base.en-q8_0.bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "ggml-base.bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "ggml-base.en.bin", StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionModelProfilePreference.Standard;
        }

        if (normalizedPath.StartsWith(normalizedManagedAsrDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetDirectoryName(normalizedPath), normalizedManagedAsrDirectory, StringComparison.OrdinalIgnoreCase))
        {
            if (configuredPreference == TranscriptionModelProfilePreference.Custom)
            {
                return configuredPreference;
            }

            return fileName.Contains("small", StringComparison.OrdinalIgnoreCase)
                ? TranscriptionModelProfilePreference.HighAccuracyDownloaded
                : TranscriptionModelProfilePreference.Custom;
        }

        return TranscriptionModelProfilePreference.Custom;
    }

    private static SpeakerLabelingModelProfilePreference InferSpeakerLabelingModelProfilePreference(
        SpeakerLabelingModelProfilePreference configuredPreference,
        string modelCacheDir,
        string diarizationAssetPath,
        string defaultDiarizationAssetPath)
    {
        if (configuredPreference == SpeakerLabelingModelProfilePreference.Disabled)
        {
            return SpeakerLabelingModelProfilePreference.Disabled;
        }

        if (string.IsNullOrWhiteSpace(diarizationAssetPath))
        {
            return SpeakerLabelingModelProfilePreference.Standard;
        }

        var normalizedPath = NormalizeComparablePath(diarizationAssetPath);
        var normalizedDefaultPath = NormalizeComparablePath(defaultDiarizationAssetPath);
        var normalizedManagedDiarizationDirectory = NormalizeComparablePath(Path.Combine(modelCacheDir, "diarization"));
        var legacyManagedDiarizationPath = normalizedManagedDiarizationDirectory;

        if (configuredPreference == SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded)
        {
            return configuredPreference;
        }

        if (string.Equals(normalizedPath, normalizedDefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            return SpeakerLabelingModelProfilePreference.Standard;
        }

        if (string.Equals(normalizedPath, legacyManagedDiarizationPath, StringComparison.OrdinalIgnoreCase))
        {
            return configuredPreference == SpeakerLabelingModelProfilePreference.Standard
                ? SpeakerLabelingModelProfilePreference.Standard
                : configuredPreference;
        }

        if (normalizedPath.Contains(
                $"{Path.DirectorySeparatorChar}high-accuracy",
                StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains(
                $"{Path.DirectorySeparatorChar}accurate",
                StringComparison.OrdinalIgnoreCase))
        {
            return SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded;
        }

        if (normalizedPath.StartsWith(normalizedManagedDiarizationDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (configuredPreference == SpeakerLabelingModelProfilePreference.Custom)
            {
                return configuredPreference;
            }

            return SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded;
        }

        return SpeakerLabelingModelProfilePreference.Custom;
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
            NormalizeComparablePath(left),
            NormalizeComparablePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureDirectories(AppConfig config)
    {
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(config.WorkDir);
        Directory.CreateDirectory(config.ModelCacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(config.TranscriptionModelPath) ?? config.ModelCacheDir);
        if (!string.IsNullOrWhiteSpace(config.DiarizationAssetPath))
        {
            Directory.CreateDirectory(config.DiarizationAssetPath);
        }
    }
}
