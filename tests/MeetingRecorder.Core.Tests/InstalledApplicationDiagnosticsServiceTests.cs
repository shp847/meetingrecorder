using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class InstalledApplicationDiagnosticsServiceTests : IDisposable
{
    private readonly string _root;

    public InstalledApplicationDiagnosticsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), nameof(InstalledApplicationDiagnosticsServiceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Inspect_Uses_Explicit_Install_Provenance_Metadata_When_Available()
    {
        var installRoot = CreateDirectory("install-root");
        var appRoot = CreateDirectory("app-root");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        File.SetLastWriteTimeUtc(executablePath, new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc));
        CreateFile(installRoot, Path.Combine("runtimes", "runtime.dll"), 7);
        File.WriteAllText(
            Path.Combine(appRoot, "install-provenance.json"),
            """
            {
              "initialChannel": "CommandBootstrap",
              "lastUpdateChannel": "AutoUpdate",
              "initialVersion": "0.3",
              "lastInstalledVersion": "0.3",
              "lastInstalledAtUtc": "2026-04-20T13:54:17Z",
              "lastReleasePublishedAtUtc": "2026-04-20T13:26:09Z",
              "lastReleaseAssetSizeBytes": 158519569
            }
            """);

        var result = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            appRoot);

        Assert.Equal(DateTimeOffset.Parse("2026-04-20T13:54:17Z"), result.InstalledAtUtc);
        Assert.Equal(19, result.InstallFootprintBytes);
        Assert.Equal(DateTimeOffset.Parse("2026-04-20T13:26:09Z"), result.InstalledReleasePublishedAtUtc);
        Assert.Equal(158519569, result.InstalledReleaseAssetSizeBytes);
    }

    [Fact]
    public void Inspect_Falls_Back_To_Executable_Write_Time_When_Install_Provenance_Is_Missing()
    {
        var installRoot = CreateDirectory("install-root");
        var appRoot = CreateDirectory("app-root");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        var executableTimestampUtc = new DateTime(2026, 4, 9, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(executablePath, executableTimestampUtc);

        var result = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            appRoot);

        Assert.Equal(new DateTimeOffset(executableTimestampUtc), result.InstalledAtUtc);
        Assert.Equal(12, result.InstallFootprintBytes);
        Assert.Null(result.InstalledReleasePublishedAtUtc);
        Assert.Null(result.InstalledReleaseAssetSizeBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
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
