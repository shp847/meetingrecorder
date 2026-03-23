using AppPlatform.Abstractions;

namespace AppPlatform.Deployment;

public static class ManualInstallStepsBuilder
{
    public static string Build(AppProductManifest manifest, string installRoot, string? zipPath = null)
    {
        var lines = new List<string>
        {
            $"1. Download the latest {manifest.ProductName} ZIP bundle from:",
            $"   {manifest.ReleasePageUrl}",
            "2. Extract the ZIP to a folder you control.",
            $"3. Run '{manifest.PortableLauncherFileName}' from the extracted folder, or copy the files into:",
            $"   {installRoot}",
            "4. If shortcuts are blocked, launch the app from the install folder and pin it manually.",
        };

        if (!string.IsNullOrWhiteSpace(zipPath))
        {
            lines.Add($"Downloaded update package: {zipPath}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
