using AppPlatform.Abstractions;
using MeetingRecorder.Core.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

public static class InstalledProvenanceRepairService
{
    private const string InstallProvenanceFileName = "install-provenance.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public static bool TryRepairMissingInstallProvenance(
        string configPath,
        string currentVersion,
        string? executablePath = null,
        string? applicationBaseDirectory = null,
        string? localApplicationDataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
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

        var appRoot = AppDataPaths.GetManagedAppRoot(localApplicationDataDirectory);
        var provenancePath = Path.Combine(appRoot, InstallProvenanceFileName);
        if (File.Exists(provenancePath))
        {
            return false;
        }

        var configStore = new AppConfigStore(configPath);
        var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var provenance = CreateBaselineProvenance(
            config,
            currentVersion,
            executablePath,
            baseDirectory,
            appRoot);

        Directory.CreateDirectory(appRoot);
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance, SerializerOptions));
        return true;
    }

    public static bool TryBackfillInstalledReleaseMetadata(
        string configPath,
        string currentVersion,
        DateTimeOffset? releasePublishedAtUtc,
        long? releaseAssetSizeBytes,
        string? executablePath = null,
        string? applicationBaseDirectory = null,
        string? localApplicationDataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return false;
        }

        var normalizedPublishedAtUtc = NormalizeTimestamp(releasePublishedAtUtc);
        var normalizedAssetSizeBytes = NormalizeSize(releaseAssetSizeBytes);
        if (!normalizedPublishedAtUtc.HasValue && !normalizedAssetSizeBytes.HasValue)
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

        var appRoot = AppDataPaths.GetManagedAppRoot(localApplicationDataDirectory);
        var provenancePath = Path.Combine(appRoot, InstallProvenanceFileName);
        var configStore = new AppConfigStore(configPath);
        var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var currentProvenance = TryLoadInstallProvenance(provenancePath) ?? CreateBaselineProvenance(
            config,
            currentVersion,
            executablePath,
            baseDirectory,
            appRoot);

        var updatedProvenance = currentProvenance with
        {
            LastReleasePublishedAtUtc = normalizedPublishedAtUtc ?? currentProvenance.LastReleasePublishedAtUtc,
            LastReleaseAssetSizeBytes = normalizedAssetSizeBytes ?? currentProvenance.LastReleaseAssetSizeBytes,
        };

        if (updatedProvenance == currentProvenance)
        {
            return false;
        }

        Directory.CreateDirectory(appRoot);
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(updatedProvenance, SerializerOptions));
        return true;
    }

    private static string ResolveInstallRoot(string? processPath, string appContextBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processDirectory = Path.GetDirectoryName(processPath.Trim());
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                return processDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return appContextBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static InstallProvenance CreateBaselineProvenance(
        AppConfig config,
        string currentVersion,
        string? executablePath,
        string baseDirectory,
        string appRoot)
    {
        var diagnostics = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot: ResolveInstallRoot(executablePath, baseDirectory),
            executablePath,
            appRoot);

        var installedVersion = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
            ? currentVersion
            : config.InstalledReleaseVersion;

        return new InstallProvenance(
            InitialChannel: InstallChannel.Unknown,
            LastUpdateChannel: InstallChannel.Unknown,
            InitialVersion: installedVersion,
            LastInstalledVersion: installedVersion,
            LastInstalledAtUtc: diagnostics.InstalledAtUtc,
            LastReleasePublishedAtUtc: null,
            LastReleaseAssetSizeBytes: null);
    }

    private static InstallProvenance? TryLoadInstallProvenance(string provenancePath)
    {
        if (!File.Exists(provenancePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallProvenance>(
                File.ReadAllText(provenancePath),
                SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? NormalizeTimestamp(DateTimeOffset? value)
    {
        return value.HasValue && value.Value != DateTimeOffset.MinValue
            ? value.Value
            : null;
    }

    private static long? NormalizeSize(long? value)
    {
        return value is > 0
            ? value
            : null;
    }
}
