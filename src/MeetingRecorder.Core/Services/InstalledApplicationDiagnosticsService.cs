using AppPlatform.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

public sealed record InstalledApplicationDiagnostics(
    DateTimeOffset? InstalledAtUtc,
    long? InstallFootprintBytes,
    DateTimeOffset? InstalledReleasePublishedAtUtc,
    long? InstalledReleaseAssetSizeBytes);

public static class InstalledApplicationDiagnosticsService
{
    private const string InstallProvenanceFileName = "install-provenance.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public static InstalledApplicationDiagnostics Inspect(
        string? processPath,
        string appContextBaseDirectory,
        string? localApplicationDataRoot = null)
    {
        var installRoot = ResolveInstallRoot(processPath, appContextBaseDirectory);
        var appRoot = AppDataPaths.GetManagedAppRoot(localApplicationDataRoot);
        return InspectFromPaths(installRoot, processPath, appRoot);
    }

    public static InstalledApplicationDiagnostics InspectFromPaths(
        string? installRoot,
        string? executablePath,
        string? appRoot)
    {
        var provenance = TryLoadInstallProvenance(appRoot);

        return new InstalledApplicationDiagnostics(
            ResolveInstalledAtUtc(installRoot, executablePath, provenance),
            TryGetDirectoryFootprintBytes(installRoot),
            NormalizeTimestamp(provenance?.LastReleasePublishedAtUtc),
            NormalizeSize(provenance?.LastReleaseAssetSizeBytes));
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

    private static DateTimeOffset? ResolveInstalledAtUtc(
        string? installRoot,
        string? executablePath,
        InstallProvenance? provenance)
    {
        var provenanceInstalledAtUtc = NormalizeTimestamp(provenance?.LastInstalledAtUtc);
        var executableTimestampUtc = TryGetFileTimestampUtc(executablePath);
        if (provenanceInstalledAtUtc.HasValue)
        {
            if (executableTimestampUtc.HasValue && executableTimestampUtc.Value > provenanceInstalledAtUtc.Value)
            {
                return executableTimestampUtc;
            }

            return provenanceInstalledAtUtc;
        }

        if (executableTimestampUtc.HasValue)
        {
            return executableTimestampUtc;
        }

        return TryGetDirectoryTimestampUtc(installRoot);
    }

    private static InstallProvenance? TryLoadInstallProvenance(string? appRoot)
    {
        if (string.IsNullOrWhiteSpace(appRoot))
        {
            return null;
        }

        var provenancePath = Path.Combine(appRoot, InstallProvenanceFileName);
        if (!File.Exists(provenancePath))
        {
            return null;
        }

        try
        {
            var contents = File.ReadAllText(provenancePath);
            return JsonSerializer.Deserialize<InstallProvenance>(contents, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetFileTimestampUtc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            return lastWriteTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(lastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetDirectoryTimestampUtc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(path);
            return lastWriteTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(lastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static long? TryGetDirectoryFootprintBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            long totalBytes = 0;
            foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
            }

            return totalBytes > 0
                ? totalBytes
                : null;
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
