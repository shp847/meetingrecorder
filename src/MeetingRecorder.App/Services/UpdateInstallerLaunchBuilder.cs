using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.Globalization;

namespace MeetingRecorder.App.Services;

internal static class UpdateInstallerLaunchBuilder
{
    internal const string DeploymentCliExecutableName = "AppPlatform.Deployment.Cli.exe";
    private const string InstallerLogDirectoryName = "MeetingRecorderInstaller";

    public static string ResolveInstalledAppRoot(string? processPath, string appContextBaseDirectory)
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

    public static ProcessStartInfo Build(
        string deploymentCliPath,
        string downloadedPath,
        string installRoot,
        int sourceProcessId,
        AppUpdateCheckResult updateResult)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = deploymentCliPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = installRoot,
        };

        startInfo.ArgumentList.Add("apply-update");
        startInfo.ArgumentList.Add("--zip-path");
        startInfo.ArgumentList.Add(downloadedPath);
        startInfo.ArgumentList.Add("--install-root");
        startInfo.ArgumentList.Add(installRoot);
        startInfo.ArgumentList.Add("--source-process-id");
        startInfo.ArgumentList.Add(sourceProcessId.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(updateResult.LatestVersion))
        {
            startInfo.ArgumentList.Add("--release-version");
            startInfo.ArgumentList.Add(updateResult.LatestVersion);
        }

        if (updateResult.LatestPublishedAtUtc.HasValue)
        {
            startInfo.ArgumentList.Add("--release-published-at-utc");
            startInfo.ArgumentList.Add(updateResult.LatestPublishedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (updateResult.LatestAssetSizeBytes is > 0)
        {
            startInfo.ArgumentList.Add("--release-asset-size-bytes");
            startInfo.ArgumentList.Add(updateResult.LatestAssetSizeBytes.Value.ToString(CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("--log-path");
        startInfo.ArgumentList.Add(BuildLogPath());
        startInfo.ArgumentList.Add("--pause-on-error");

        return startInfo;
    }

    private static string BuildLogPath()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), InstallerLogDirectoryName);
        Directory.CreateDirectory(logDirectory);
        return Path.Combine(
            logDirectory,
            $"apply-update-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");
    }
}
