namespace MeetingRecorder.Core.Services;

public static class LegacyPortableDataMigrationService
{
    public static bool TryMigrateFromLegacyPortableInstall(
        string? applicationBaseDirectory = null,
        string? documentsDirectory = null,
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
        var managedConfigPath = Path.Combine(normalizedManagedRoot, "config", "appsettings.json");
        if (File.Exists(managedConfigPath))
        {
            return false;
        }

        var documentsRoot = string.IsNullOrWhiteSpace(documentsDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsDirectory;
        if (string.IsNullOrWhiteSpace(documentsRoot))
        {
            return false;
        }

        var legacyDataRoot = Path.Combine(documentsRoot, "MeetingRecorder", "data");
        if (!Directory.Exists(legacyDataRoot))
        {
            return false;
        }

        var normalizedLegacyRoot = Path.GetFullPath(legacyDataRoot);
        if (string.Equals(
            normalizedManagedRoot.TrimEnd(Path.DirectorySeparatorChar),
            normalizedLegacyRoot.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var copiedAnyFiles = false;
        foreach (var sourceFile in Directory.GetFiles(normalizedLegacyRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(normalizedLegacyRoot, sourceFile);
            var destinationFile = Path.Combine(normalizedManagedRoot, relativePath);
            if (File.Exists(destinationFile))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException("Destination file must have a parent directory."));
            File.Copy(sourceFile, destinationFile, overwrite: false);
            copiedAnyFiles = true;
        }

        return copiedAnyFiles;
    }
}
