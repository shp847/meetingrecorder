using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class InstalledReleaseMetadataRepairServiceTests : IDisposable
{
    private readonly string _root;

    public InstalledReleaseMetadataRepairServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task TryRepairFromLegacyPortableInstall_Refreshes_Managed_Install_Metadata_When_Legacy_Metadata_Is_Newer()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var localAppDataDirectory = CreateDirectory("localappdata");
        var managedConfigPath = AppDataPaths.GetManagedConfigPath(localAppDataDirectory);
        var managedStore = new AppConfigStore(managedConfigPath, documentsDirectory);
        var currentConfig = await managedStore.LoadOrCreateAsync();

        await managedStore.SaveAsync(currentConfig with
        {
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-03-17T19:28:22Z"),
            InstalledReleaseAssetSizeBytes = 74220015,
        });

        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));
        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(
            legacyConfigPath,
            """
            {
              "installedReleaseVersion": "0.2",
              "installedReleasePublishedAtUtc": "2026-03-20T01:34:24Z",
              "installedReleaseAssetSizeBytes": 74231265
            }
            """);

        var repaired = InstalledReleaseMetadataRepairService.TryRepairFromLegacyPortableInstall(
            managedConfigPath,
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            localApplicationDataDirectory: localAppDataDirectory);

        Assert.True(repaired);

        var reloaded = await managedStore.LoadOrCreateAsync();
        Assert.Equal(AppBranding.Version, reloaded.InstalledReleaseVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-03-20T01:34:24Z"), reloaded.InstalledReleasePublishedAtUtc);
        Assert.Equal(74231265, reloaded.InstalledReleaseAssetSizeBytes);
    }

    [Fact]
    public async Task TryRepairFromLegacyPortableInstall_Does_Not_Downgrade_Managed_Install_Metadata()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var localAppDataDirectory = CreateDirectory("localappdata");
        var managedConfigPath = AppDataPaths.GetManagedConfigPath(localAppDataDirectory);
        var managedStore = new AppConfigStore(managedConfigPath, documentsDirectory);
        var currentConfig = await managedStore.LoadOrCreateAsync();

        await managedStore.SaveAsync(currentConfig with
        {
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-03-20T01:34:24Z"),
            InstalledReleaseAssetSizeBytes = 74231265,
        });

        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));
        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(
            legacyConfigPath,
            """
            {
              "installedReleaseVersion": "0.2",
              "installedReleasePublishedAtUtc": "2026-03-17T19:28:22Z",
              "installedReleaseAssetSizeBytes": 74220015
            }
            """);

        var repaired = InstalledReleaseMetadataRepairService.TryRepairFromLegacyPortableInstall(
            managedConfigPath,
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            localApplicationDataDirectory: localAppDataDirectory);

        Assert.False(repaired);

        var reloaded = await managedStore.LoadOrCreateAsync();
        Assert.Equal(DateTimeOffset.Parse("2026-03-20T01:34:24Z"), reloaded.InstalledReleasePublishedAtUtc);
        Assert.Equal(74231265, reloaded.InstalledReleaseAssetSizeBytes);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
