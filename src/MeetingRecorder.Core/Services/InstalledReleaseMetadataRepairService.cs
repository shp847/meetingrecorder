using MeetingRecorder.Core.Branding;

namespace MeetingRecorder.Core.Services;

public static class InstalledReleaseMetadataRepairService
{
    public static bool TryRepairFromLegacyPortableInstall(
        string managedConfigPath,
        string? applicationBaseDirectory = null,
        string? documentsDirectory = null,
        string? desktopDirectory = null,
        string? localApplicationDataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(managedConfigPath) || !File.Exists(managedConfigPath))
        {
            return false;
        }

        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;
        if (AppDataPaths.IsPortableMode(baseDirectory))
        {
            return false;
        }

        var documentsRoot = string.IsNullOrWhiteSpace(documentsDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsDirectory;
        var desktopRoot = string.IsNullOrWhiteSpace(desktopDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : desktopDirectory;
        var managedAppRoot = AppDataPaths.GetManagedAppRoot(localApplicationDataDirectory);
        var legacyDataRoots = EnumerateLegacyDataRoots(baseDirectory, documentsRoot, desktopRoot, managedAppRoot);
        if (legacyDataRoots.Count == 0)
        {
            return false;
        }

        var managedStore = new AppConfigStore(managedConfigPath, documentsRoot);
        var managedConfig = managedStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var latestLegacyMetadata = GetLatestLegacyMetadata(legacyDataRoots);
        if (latestLegacyMetadata is null || !ShouldApply(latestLegacyMetadata, managedConfig))
        {
            return false;
        }

        var repaired = managedConfig with
        {
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = latestLegacyMetadata.PublishedAtUtc,
            InstalledReleaseAssetSizeBytes = latestLegacyMetadata.AssetSizeBytes > 0
                ? latestLegacyMetadata.AssetSizeBytes
                : managedConfig.InstalledReleaseAssetSizeBytes,
        };

        managedStore.SaveAsync(repaired).GetAwaiter().GetResult();
        return true;
    }

    private static bool ShouldApply(LegacyInstalledReleaseMetadata legacyMetadata, Configuration.AppConfig managedConfig)
    {
        if (!legacyMetadata.PublishedAtUtc.HasValue)
        {
            return false;
        }

        if (!managedConfig.InstalledReleasePublishedAtUtc.HasValue)
        {
            return true;
        }

        return legacyMetadata.PublishedAtUtc.Value > managedConfig.InstalledReleasePublishedAtUtc.Value;
    }

    private static LegacyInstalledReleaseMetadata? GetLatestLegacyMetadata(IReadOnlyList<string> legacyDataRoots)
    {
        LegacyInstalledReleaseMetadata? best = null;

        foreach (var legacyDataRoot in legacyDataRoots)
        {
            var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
            if (!File.Exists(legacyConfigPath))
            {
                continue;
            }

            var legacyConfig = new AppConfigStore(legacyConfigPath).LoadOrCreateAsync().GetAwaiter().GetResult();
            if (!legacyConfig.InstalledReleasePublishedAtUtc.HasValue)
            {
                continue;
            }

            var candidate = new LegacyInstalledReleaseMetadata(
                legacyConfig.InstalledReleasePublishedAtUtc,
                legacyConfig.InstalledReleaseAssetSizeBytes);

            if (best is null || candidate.PublishedAtUtc > best.PublishedAtUtc)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IReadOnlyList<string> EnumerateLegacyDataRoots(
        string applicationBaseDirectory,
        string documentsDirectory,
        string desktopDirectory,
        string managedAppRoot)
    {
        var candidateRoots = new[]
        {
            Path.Combine(applicationBaseDirectory, "data"),
            Path.Combine(documentsDirectory, "MeetingRecorder", "data"),
            Path.Combine(desktopDirectory, "MeetingRecorder", "data"),
        };

        return candidateRoots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Where(path => !string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar),
                managedAppRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record LegacyInstalledReleaseMetadata(
        DateTimeOffset? PublishedAtUtc,
        long? AssetSizeBytes);
}
