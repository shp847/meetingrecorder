using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class UpdateDiagnosticsSourceTests
{
    [Fact]
    public void Updates_Tab_Labels_Current_Installation_As_Local_Install_Facts()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"Installed on (UTC)\"", xaml);
        Assert.Contains("Text=\"Installed package published (UTC)\"", xaml);
        Assert.Contains("Text=\"Installed package size\"", xaml);
        Assert.DoesNotContain("Text=\"Install footprint\"", xaml);
    }

    [Fact]
    public void RefreshUpdateMetadataDisplay_Uses_Installed_Application_Diagnostics_Rather_Than_Release_Metadata_Config()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private void RefreshUpdateMetadataDisplay", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private string BuildUpdateAutomationSummary", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("InstalledApplicationDiagnosticsService.Inspect(", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledAtUtc", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleaseAssetSizeBytes", methodBlock);
    }

    [Fact]
    public void BuildLocalUpdateState_Uses_Resolved_Installed_Diagnostics_Metadata_Before_Config_Fallback()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private AppUpdateLocalState BuildLocalUpdateState", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private bool TryGetUpdateInstallBlockReason", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("InstalledApplicationDiagnosticsService.Inspect(", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleasePublishedAtUtc ?? config.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleaseAssetSizeBytes ?? config.InstalledReleaseAssetSizeBytes", methodBlock);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }
}
