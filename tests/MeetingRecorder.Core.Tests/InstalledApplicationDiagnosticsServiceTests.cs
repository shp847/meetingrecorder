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
    public void Inspect_Uses_Install_Provenance_Write_Time_And_Installed_Root_Footprint()
    {
        var installRoot = CreateDirectory("install-root");
        var appRoot = CreateDirectory("app-root");
        var executablePath = CreateFile(installRoot, "MeetingRecorder.App.exe", 12);
        CreateFile(installRoot, Path.Combine("runtimes", "runtime.dll"), 7);
        var provenancePath = CreateFile(appRoot, "install-provenance.json", 5);
        var provenanceTimestampUtc = new DateTime(2026, 4, 10, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(provenancePath, provenanceTimestampUtc);

        var result = InstalledApplicationDiagnosticsService.InspectFromPaths(
            installRoot,
            executablePath,
            appRoot);

        Assert.Equal(new DateTimeOffset(provenanceTimestampUtc), result.InstalledAtUtc);
        Assert.Equal(19, result.InstallFootprintBytes);
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
