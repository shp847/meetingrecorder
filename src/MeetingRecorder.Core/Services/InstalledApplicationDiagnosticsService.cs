namespace MeetingRecorder.Core.Services;

public sealed record InstalledApplicationDiagnostics(
    DateTimeOffset? InstalledAtUtc,
    long? InstallFootprintBytes);

public static class InstalledApplicationDiagnosticsService
{
    private const string InstallProvenanceFileName = "install-provenance.json";

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
        return new InstalledApplicationDiagnostics(
            ResolveInstalledAtUtc(installRoot, executablePath, appRoot),
            ResolveInstallFootprintBytes(installRoot));
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

    private static DateTimeOffset? ResolveInstalledAtUtc(string? installRoot, string? executablePath, string? appRoot)
    {
        var provenanceTimestampUtc = TryGetInstallProvenanceTimestampUtc(appRoot);
        if (provenanceTimestampUtc.HasValue)
        {
            return provenanceTimestampUtc;
        }

        var executableTimestampUtc = TryGetFileTimestampUtc(executablePath);
        if (executableTimestampUtc.HasValue)
        {
            return executableTimestampUtc;
        }

        return TryGetDirectoryTimestampUtc(installRoot);
    }

    private static DateTimeOffset? TryGetInstallProvenanceTimestampUtc(string? appRoot)
    {
        if (string.IsNullOrWhiteSpace(appRoot))
        {
            return null;
        }

        var provenancePath = Path.Combine(appRoot, InstallProvenanceFileName);
        return TryGetFileTimestampUtc(provenancePath);
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

    private static long? ResolveInstallFootprintBytes(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
        {
            return null;
        }

        try
        {
            long totalBytes = 0;
            foreach (var filePath in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
            {
                totalBytes += new FileInfo(filePath).Length;
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
}
