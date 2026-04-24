using AppPlatform.Abstractions;
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
        var diagnostics = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot: ResolveInstallRoot(executablePath, baseDirectory),
            executablePath,
            appRoot);

        var installedVersion = string.IsNullOrWhiteSpace(config.InstalledReleaseVersion)
            ? currentVersion
            : config.InstalledReleaseVersion;

        var provenance = new InstallProvenance(
            InitialChannel: InstallChannel.Unknown,
            LastUpdateChannel: InstallChannel.Unknown,
            InitialVersion: installedVersion,
            LastInstalledVersion: installedVersion,
            LastInstalledAtUtc: diagnostics.InstalledAtUtc,
            LastReleasePublishedAtUtc: null,
            LastReleaseAssetSizeBytes: null);

        Directory.CreateDirectory(appRoot);
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance, SerializerOptions));
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
}
