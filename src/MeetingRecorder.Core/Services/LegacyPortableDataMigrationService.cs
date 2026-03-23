using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public static class LegacyPortableDataMigrationService
{
    public static bool TryMigrateFromLegacyPortableInstall(
        string? applicationBaseDirectory = null,
        string? documentsDirectory = null,
        string? desktopDirectory = null,
        string? managedAppRootOverride = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;

        if (AppDataPaths.IsPortableMode(baseDirectory))
        {
            return false;
        }

        var managedAppRoot = string.IsNullOrWhiteSpace(managedAppRootOverride)
            ? AppDataPaths.GetAppRoot(baseDirectory)
            : managedAppRootOverride;
        var normalizedManagedRoot = Path.GetFullPath(managedAppRoot);

        var documentsRoot = string.IsNullOrWhiteSpace(documentsDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsDirectory;
        var desktopRoot = string.IsNullOrWhiteSpace(desktopDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : desktopDirectory;

        var legacyDataRoots = EnumerateLegacyDataRoots(baseDirectory, documentsRoot, desktopRoot, normalizedManagedRoot);
        if (legacyDataRoots.Count == 0)
        {
            return false;
        }

        var copiedAnyFiles = false;
        copiedAnyFiles |= EnsureManagedConfigExists(normalizedManagedRoot, legacyDataRoots, documentsRoot);

        foreach (var legacyDataRoot in legacyDataRoots)
        {
            foreach (var sourceFile in Directory.GetFiles(legacyDataRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(legacyDataRoot, sourceFile);
                if (IsPrimaryConfigFile(relativePath))
                {
                    continue;
                }

                var destinationFile = ResolveMigrationDestination(
                    relativePath,
                    normalizedManagedRoot,
                    documentsRoot);
                if (File.Exists(destinationFile))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                    ?? throw new InvalidOperationException("Destination file must have a parent directory."));
                File.Copy(sourceFile, destinationFile, overwrite: false);
                copiedAnyFiles = true;
            }
        }

        return copiedAnyFiles;
    }

    private static bool EnsureManagedConfigExists(string managedAppRoot, IReadOnlyList<string> legacyDataRoots, string documentsRoot)
    {
        var managedConfigPath = Path.Combine(managedAppRoot, "config", "appsettings.json");
        if (File.Exists(managedConfigPath))
        {
            return false;
        }

        var managedStore = new AppConfigStore(managedConfigPath, documentsRoot);
        var managedDefaults = managedStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var legacyConfigPath = legacyDataRoots
            .Select(root => Path.Combine(root, "config", "appsettings.json"))
            .FirstOrDefault(File.Exists);
        if (legacyConfigPath is null)
        {
            return true;
        }

        var legacyConfig = new AppConfigStore(legacyConfigPath).LoadOrCreateAsync().GetAwaiter().GetResult();
        var migratedConfig = PreservePortableSettings(managedDefaults, legacyConfig, legacyDataRoots, managedAppRoot);
        managedStore.SaveAsync(migratedConfig).GetAwaiter().GetResult();
        return true;
    }

    private static string ResolveMigrationDestination(
        string relativePath,
        string managedAppRoot,
        string documentsRoot)
    {
        var normalizedRelativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var audioPrefix = "audio" + Path.DirectorySeparatorChar;
        if (normalizedRelativePath.StartsWith(audioPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var audioRelativePath = normalizedRelativePath[audioPrefix.Length..];
            return Path.Combine(AppDataPaths.GetManagedRecordingsRoot(documentsRoot), audioRelativePath);
        }

        var transcriptsPrefix = "transcripts" + Path.DirectorySeparatorChar;
        if (normalizedRelativePath.StartsWith(transcriptsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var transcriptRelativePath = normalizedRelativePath[transcriptsPrefix.Length..];
            return Path.Combine(AppDataPaths.GetManagedTranscriptsRoot(documentsRoot), transcriptRelativePath);
        }

        return Path.Combine(managedAppRoot, normalizedRelativePath);
    }

    private static IReadOnlyList<string> EnumerateLegacyDataRoots(
        string applicationBaseDirectory,
        string? documentsDirectory,
        string? desktopDirectory,
        string managedAppRoot)
    {
        var candidateRoots = new[]
        {
            Path.Combine(applicationBaseDirectory, "data"),
            string.IsNullOrWhiteSpace(documentsDirectory)
                ? null
                : Path.Combine(documentsDirectory, "MeetingRecorder", "data"),
            string.IsNullOrWhiteSpace(desktopDirectory)
                ? null
                : Path.Combine(desktopDirectory, "MeetingRecorder", "data"),
        };

        return candidateRoots
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Where(path => !string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar),
                managedAppRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPrimaryConfigFile(string relativePath)
    {
        return string.Equals(
            relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
            Path.Combine("config", "appsettings.json"),
            StringComparison.OrdinalIgnoreCase);
    }

    private static AppConfig PreservePortableSettings(
        AppConfig managedDefaults,
        AppConfig legacyConfig,
        IReadOnlyList<string> legacyDataRoots,
        string managedAppRoot)
    {
        var remappedModelCacheDir = RemapLegacyManagedPath(legacyConfig.ModelCacheDir, legacyDataRoots, managedAppRoot);
        var remappedTranscriptionModelPath = RemapLegacyManagedPath(legacyConfig.TranscriptionModelPath, legacyDataRoots, managedAppRoot);
        var remappedDiarizationAssetPath = RemapLegacyManagedPath(legacyConfig.DiarizationAssetPath, legacyDataRoots, managedAppRoot);

        return managedDefaults with
        {
            ModelCacheDir = string.IsNullOrWhiteSpace(remappedModelCacheDir)
                ? managedDefaults.ModelCacheDir
                : remappedModelCacheDir,
            TranscriptionModelPath = string.IsNullOrWhiteSpace(remappedTranscriptionModelPath)
                ? managedDefaults.TranscriptionModelPath
                : remappedTranscriptionModelPath,
            DiarizationAssetPath = string.IsNullOrWhiteSpace(remappedDiarizationAssetPath)
                ? managedDefaults.DiarizationAssetPath
                : remappedDiarizationAssetPath,
            MicCaptureEnabled = managedDefaults.MicCaptureEnabled,
            LaunchOnLoginEnabled = legacyConfig.LaunchOnLoginEnabled,
            AutoDetectEnabled = legacyConfig.AutoDetectEnabled,
            CalendarTitleFallbackEnabled = legacyConfig.CalendarTitleFallbackEnabled,
            UpdateCheckEnabled = legacyConfig.UpdateCheckEnabled,
            AutoInstallUpdatesEnabled = legacyConfig.AutoInstallUpdatesEnabled,
            UpdateFeedUrl = legacyConfig.UpdateFeedUrl,
            LastUpdateCheckUtc = legacyConfig.LastUpdateCheckUtc,
            InstalledReleaseVersion = managedDefaults.InstalledReleaseVersion,
            InstalledReleasePublishedAtUtc = managedDefaults.InstalledReleasePublishedAtUtc,
            InstalledReleaseAssetSizeBytes = managedDefaults.InstalledReleaseAssetSizeBytes,
            PendingUpdateZipPath = managedDefaults.PendingUpdateZipPath,
            PendingUpdateVersion = managedDefaults.PendingUpdateVersion,
            PendingUpdatePublishedAtUtc = managedDefaults.PendingUpdatePublishedAtUtc,
            PendingUpdateAssetSizeBytes = managedDefaults.PendingUpdateAssetSizeBytes,
            AutoDetectAudioPeakThreshold = legacyConfig.AutoDetectAudioPeakThreshold,
            MeetingStopTimeoutSeconds = legacyConfig.MeetingStopTimeoutSeconds,
        };
    }

    private static string RemapLegacyManagedPath(
        string configuredPath,
        IReadOnlyList<string> legacyDataRoots,
        string managedAppRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var fullConfiguredPath = Path.GetFullPath(configuredPath);
        foreach (var legacyDataRoot in legacyDataRoots)
        {
            var normalizedLegacyRoot = Path.GetFullPath(legacyDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathWithSeparator = normalizedLegacyRoot + Path.DirectorySeparatorChar;

            if (string.Equals(fullConfiguredPath, normalizedLegacyRoot, StringComparison.OrdinalIgnoreCase))
            {
                return managedAppRoot;
            }

            if (fullConfiguredPath.StartsWith(pathWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(normalizedLegacyRoot, fullConfiguredPath);
                return Path.Combine(managedAppRoot, relativePath);
            }
        }

        return configuredPath;
    }
}
