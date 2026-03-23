using AppPlatform.Abstractions;
using System.Text.Json;

namespace AppPlatform.Deployment;

public static class AppProductManifestFileLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AppProductManifest Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("A product manifest path is required.", nameof(manifestPath));
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The product manifest could not be found.", manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<AppProductManifest>(
            File.ReadAllText(manifestPath),
            SerializerOptions);

        if (manifest is null)
        {
            throw new InvalidOperationException($"The product manifest at '{manifestPath}' could not be parsed.");
        }

        var expandedLayout = manifest.ManagedInstallLayout with
        {
            InstallRoot = Environment.ExpandEnvironmentVariables(manifest.ManagedInstallLayout.InstallRoot),
            DataRoot = Environment.ExpandEnvironmentVariables(manifest.ManagedInstallLayout.DataRoot),
            ConfigPath = Environment.ExpandEnvironmentVariables(manifest.ManagedInstallLayout.ConfigPath),
            LegacyInstallRoots = manifest.ManagedInstallLayout.LegacyInstallRoots
                .Select(Environment.ExpandEnvironmentVariables)
                .ToArray(),
        };

        return manifest with
        {
            ManagedInstallLayout = expandedLayout,
        };
    }
}
