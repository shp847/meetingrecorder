using AppPlatform.Abstractions;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class InstalledProvenanceRepairServiceTests : IDisposable
{
    private readonly string _root;

    public InstalledProvenanceRepairServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), nameof(InstalledProvenanceRepairServiceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task TryRepairMissingInstallProvenance_Creates_Basic_Provenance_When_File_Is_Missing()
    {
        var documentsRoot = CreateDirectory("documents");
        var localAppDataRoot = CreateDirectory("localappdata");
        var installRoot = CreateDirectory("install");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        File.SetLastWriteTimeUtc(executablePath, new DateTime(2026, 4, 24, 13, 22, 06, DateTimeKind.Utc));

        var configPath = AppDataPaths.GetManagedConfigPath(localAppDataRoot);
        var store = new AppConfigStore(configPath, documentsRoot);
        var config = await store.LoadOrCreateAsync();
        await store.SaveAsync(config with
        {
            InstalledReleaseVersion = AppBranding.Version,
        });

        var repaired = InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(
            configPath,
            AppBranding.Version,
            executablePath,
            installRoot,
            localAppDataRoot);

        Assert.True(repaired);

        var diagnostics = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            AppDataPaths.GetManagedAppRoot(localAppDataRoot));

        Assert.Equal(DateTimeOffset.Parse("2026-04-24T13:22:06Z"), diagnostics.InstalledAtUtc);
        Assert.Null(diagnostics.InstalledReleasePublishedAtUtc);
        Assert.Null(diagnostics.InstalledReleaseAssetSizeBytes);
    }

    [Fact]
    public void TryRepairMissingInstallProvenance_Creates_Basic_Provenance_When_Config_File_Does_Not_Exist_Yet()
    {
        var localAppDataRoot = CreateDirectory("localappdata");
        var installRoot = CreateDirectory("install");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        File.SetLastWriteTimeUtc(executablePath, new DateTime(2026, 4, 24, 13, 22, 06, DateTimeKind.Utc));

        var configPath = AppDataPaths.GetManagedConfigPath(localAppDataRoot);

        var repaired = InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(
            configPath,
            AppBranding.Version,
            executablePath,
            installRoot,
            localAppDataRoot);

        Assert.True(repaired);
        Assert.True(File.Exists(configPath));

        var diagnostics = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            AppDataPaths.GetManagedAppRoot(localAppDataRoot));

        Assert.Equal(DateTimeOffset.Parse("2026-04-24T13:22:06Z"), diagnostics.InstalledAtUtc);
        Assert.Null(diagnostics.InstalledReleasePublishedAtUtc);
        Assert.Null(diagnostics.InstalledReleaseAssetSizeBytes);
    }

    [Fact]
    public async Task TryRepairMissingInstallProvenance_Does_Not_Rehydrate_Package_Metadata_From_Config()
    {
        var documentsRoot = CreateDirectory("documents");
        var localAppDataRoot = CreateDirectory("localappdata");
        var installRoot = CreateDirectory("install");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        File.SetLastWriteTimeUtc(executablePath, new DateTime(2026, 4, 24, 13, 22, 06, DateTimeKind.Utc));

        var configPath = AppDataPaths.GetManagedConfigPath(localAppDataRoot);
        var store = new AppConfigStore(configPath, documentsRoot);
        var config = await store.LoadOrCreateAsync();
        await store.SaveAsync(config with
        {
            InstalledReleaseVersion = AppBranding.Version,
            InstalledReleasePublishedAtUtc = DateTimeOffset.Parse("2026-04-24T13:24:08Z"),
            InstalledReleaseAssetSizeBytes = 158_556_023,
        });

        var repaired = InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(
            configPath,
            AppBranding.Version,
            executablePath,
            installRoot,
            localAppDataRoot);

        Assert.True(repaired);

        var diagnostics = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            AppDataPaths.GetManagedAppRoot(localAppDataRoot));

        Assert.Null(diagnostics.InstalledReleasePublishedAtUtc);
        Assert.Null(diagnostics.InstalledReleaseAssetSizeBytes);
    }

    [Fact]
    public async Task TryRepairMissingInstallProvenance_Does_Nothing_When_Provenance_Already_Exists()
    {
        var documentsRoot = CreateDirectory("documents");
        var localAppDataRoot = CreateDirectory("localappdata");
        var installRoot = CreateDirectory("install");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);

        var configPath = AppDataPaths.GetManagedConfigPath(localAppDataRoot);
        var store = new AppConfigStore(configPath, documentsRoot);
        await store.LoadOrCreateAsync();

        var provenancePath = Path.Combine(AppDataPaths.GetManagedAppRoot(localAppDataRoot), "install-provenance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(provenancePath)!);
        File.WriteAllText(
            provenancePath,
            """
            {
              "initialChannel": "Unknown",
              "lastUpdateChannel": "Unknown",
              "initialVersion": "0.3",
              "lastInstalledVersion": "0.3",
              "lastInstalledAtUtc": "2026-04-24T13:22:06Z"
            }
            """);

        var repaired = InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(
            configPath,
            AppBranding.Version,
            executablePath,
            installRoot,
            localAppDataRoot);

        Assert.False(repaired);
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

    private static string CreateFile(string root, string relativePath, int sizeBytes)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'a', sizeBytes).ToArray());
        return path;
    }
}
