using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.IO;
using System.IO.Compression;

namespace MeetingRecorder.Installer;

internal sealed class InstallerBootstrapper
{
    private const double BytesPerMegabyte = 1024d * 1024d;
    private readonly GitHubReleaseBootstrapService _bootstrapService;
    private readonly HttpFileDownloader _fileDownloader;
    private readonly PortableInstallService _portableInstallService;

    public InstallerBootstrapper(
        GitHubReleaseBootstrapService bootstrapService,
        HttpFileDownloader fileDownloader,
        PortableInstallService portableInstallService)
    {
        _bootstrapService = bootstrapService;
        _fileDownloader = fileDownloader;
        _portableInstallService = portableInstallService;
    }

    public async Task<InstallerSessionResult> InstallLatestAsync(
        IProgress<InstallerProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new InstallerProgressInfo(
            "Checking GitHub for the latest release",
            "Reading the current release metadata.",
            5));

        var release = await _bootstrapService.GetLatestReleaseAsync(
            AppBranding.DefaultUpdateFeedUrl,
            cancellationToken);
        var installRoot = _portableInstallService.GetDefaultInstallRoot();
        var manualSteps = ManualInstallGuideBuilder.Build(release, installRoot);

        progress?.Report(new InstallerProgressInfo(
            "Latest release found",
            $"Preparing to install {release.Version} into {installRoot}.",
            10,
            BuildReleaseSummary(release)));

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderInstaller-" + Guid.NewGuid().ToString("N"));
        var downloadPath = Path.Combine(tempRoot, release.AppZipAsset.Name);
        var extractPath = Path.Combine(tempRoot, "bundle");

        try
        {
            Directory.CreateDirectory(tempRoot);

            progress?.Report(new InstallerProgressInfo(
                "Downloading the installer package",
                $"Downloading {release.AppZipAsset.Name}.",
                12));

            await _fileDownloader.DownloadFileAsync(
                release.AppZipAsset.DownloadUrl,
                downloadPath,
                (bytesReceived, totalBytes, estimatedRemaining) =>
                {
                    var percent = totalBytes.HasValue && totalBytes.Value > 0
                        ? 12 + ((double)bytesReceived / totalBytes.Value * 58)
                        : (double?)null;
                    var detail = BuildDownloadDetail(bytesReceived, totalBytes, estimatedRemaining);
                    progress?.Report(new InstallerProgressInfo(
                        "Downloading the installer package",
                        $"Downloading {release.AppZipAsset.Name}.",
                        percent,
                        detail));
                },
                cancellationToken);

            progress?.Report(new InstallerProgressInfo(
                "Extracting the installer package",
                "Unpacking the downloaded files.",
                72));

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(downloadPath, extractPath, overwriteFiles: true);

            var sourceBundleRoot = ResolveSourceBundleRoot(extractPath);
            var executablePath = await _portableInstallService.InstallAsync(
                sourceBundleRoot,
                installRoot,
                release,
                progress,
                cancellationToken);

            LaunchInstalledApp(executablePath, installRoot);

            return new InstallerSessionResult(
                installRoot,
                executablePath,
                release,
                manualSteps,
                release.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public async Task<string> DownloadBackupInstallerAsync(
        GitHubReleaseBootstrapInfo? releaseInfo,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderInstallerBackup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var commandUrl = releaseInfo?.BackupCommandAsset?.DownloadUrl ?? BuildStableReleaseAssetUrl("Install-LatestFromGitHub.cmd");
        var powerShellUrl = releaseInfo?.BackupPowerShellAsset?.DownloadUrl ?? BuildStableReleaseAssetUrl("Install-LatestFromGitHub.ps1");
        var commandPath = Path.Combine(tempRoot, "Install-LatestFromGitHub.cmd");
        var powerShellPath = Path.Combine(tempRoot, "Install-LatestFromGitHub.ps1");

        await _fileDownloader.DownloadFileAsync(
            commandUrl,
            commandPath,
            (_, _, _) => { },
            cancellationToken);
        await _fileDownloader.DownloadFileAsync(
            powerShellUrl,
            powerShellPath,
            (_, _, _) => { },
            cancellationToken);

        return commandPath;
    }

    public string BuildManualSteps(GitHubReleaseBootstrapInfo? releaseInfo)
    {
        return ManualInstallGuideBuilder.Build(
            releaseInfo,
            _portableInstallService.GetDefaultInstallRoot());
    }

    public string GetReleasePageUrl(GitHubReleaseBootstrapInfo? releaseInfo)
    {
        return releaseInfo?.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl;
    }

    private static string BuildStableReleaseAssetUrl(string assetName)
    {
        return $"https://github.com/{AppBranding.GitHubRepositoryOwner}/{AppBranding.GitHubRepositoryName}/releases/latest/download/{assetName}";
    }

    private static string ResolveSourceBundleRoot(string extractedRoot)
    {
        var nestedBundleRoot = Path.Combine(extractedRoot, "MeetingRecorder");
        if (File.Exists(Path.Combine(nestedBundleRoot, "MeetingRecorder.App.exe")))
        {
            return nestedBundleRoot;
        }

        if (File.Exists(Path.Combine(extractedRoot, "MeetingRecorder.App.exe")))
        {
            return extractedRoot;
        }

        throw new InvalidOperationException("The downloaded release did not contain a portable Meeting Recorder bundle.");
    }

    private static string BuildReleaseSummary(GitHubReleaseBootstrapInfo release)
    {
        var sizeText = release.AppZipAsset.SizeBytes.HasValue
            ? $"{Math.Round(release.AppZipAsset.SizeBytes.Value / BytesPerMegabyte, 1):0.0} MB"
            : "size unknown";
        var publishedText = release.PublishedAtUtc.HasValue
            ? release.PublishedAtUtc.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "publish time unavailable";
        return $"Version {release.Version} | {sizeText} | published {publishedText}";
    }

    private static string BuildDownloadDetail(long bytesReceived, long? totalBytes, TimeSpan? estimatedRemaining)
    {
        var downloadedText = FormatMegabytes(bytesReceived);
        var totalText = totalBytes.HasValue ? FormatMegabytes(totalBytes.Value) : "unknown total";
        var etaText = estimatedRemaining.HasValue && estimatedRemaining.Value > TimeSpan.Zero
            ? $" | about {Math.Ceiling(estimatedRemaining.Value.TotalSeconds)}s remaining"
            : string.Empty;
        return $"{downloadedText} of {totalText}{etaText}";
    }

    private static string FormatMegabytes(long bytes)
    {
        return $"{Math.Round(bytes / BytesPerMegabyte, 1):0.0} MB";
    }

    private static void LaunchInstalledApp(string executablePath, string workingDirectory)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        System.Diagnostics.Process.Start(startInfo);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
