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
        Assert.Contains("Text=\"Install footprint\"", xaml);
        Assert.Contains("Text=\"Installed package published (UTC)\"", xaml);
        Assert.Contains("Text=\"Installed package size\"", xaml);
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
        Assert.Contains("installedDiagnostics.InstallFootprintBytes", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleaseAssetSizeBytes", methodBlock);
        Assert.DoesNotContain("config.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.DoesNotContain("config.InstalledReleaseAssetSizeBytes", methodBlock);
        Assert.Contains("result?.LatestPublishedAtUtc", methodBlock);
        Assert.Contains("result?.LatestAssetSizeBytes", methodBlock);
        Assert.Contains("result is { Status: AppUpdateStatusKind.UpToDate }", methodBlock);
        Assert.DoesNotContain("string.Equals(result.LatestVersion, AppBranding.Version", methodBlock);
    }

    [Fact]
    public void BuildLocalUpdateState_Uses_Trusted_Installed_Diagnostics_Metadata_Without_Config_Fallback()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private AppUpdateLocalState BuildLocalUpdateState", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private bool TryGetUpdateInstallBlockReason", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("InstalledApplicationDiagnosticsService.Inspect(", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.Contains("installedDiagnostics.InstalledReleaseAssetSizeBytes", methodBlock);
        Assert.DoesNotContain("config.InstalledReleasePublishedAtUtc", methodBlock);
        Assert.DoesNotContain("config.InstalledReleaseAssetSizeBytes", methodBlock);
    }

    [Fact]
    public void CheckForUpdatesCoreAsync_Backfills_Installed_Package_Metadata_When_GitHub_Confirms_The_Current_Release()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task<AppUpdateCheckResult> CheckForUpdatesCoreAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task PersistLastUpdateCheckUtcAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("InstalledProvenanceRepairService.TryBackfillInstalledReleaseMetadata(", methodBlock);
        Assert.Contains("result.Status == AppUpdateStatusKind.UpToDate", methodBlock);
        Assert.Contains("result.LatestPublishedAtUtc", methodBlock);
        Assert.Contains("result.LatestAssetSizeBytes", methodBlock);
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
