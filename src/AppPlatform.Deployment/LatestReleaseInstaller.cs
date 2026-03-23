using AppPlatform.Abstractions;
using System.IO.Compression;

namespace AppPlatform.Deployment;

public sealed class LatestReleaseInstaller
{
    private readonly GitHubReleaseCatalogService _releaseCatalogService;
    private readonly PortableBundleInstaller _bundleInstaller;
    private readonly IDeploymentLogger _logger;

    public LatestReleaseInstaller(
        GitHubReleaseCatalogService releaseCatalogService,
        PortableBundleInstaller bundleInstaller,
        IDeploymentLogger? logger = null)
    {
        _releaseCatalogService = releaseCatalogService;
        _bundleInstaller = bundleInstaller;
        _logger = logger ?? NullDeploymentLogger.Instance;
    }

    public async Task<(ReleaseAssetSet Release, InstallResult Install)> InstallLatestAsync(
        AppProductManifest manifest,
        string? installRoot,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool launchAfterInstall,
        InstallChannel installChannel,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Starting latest-release install using feed '{manifest.UpdateFeedUrl}'.");
        var release = await _releaseCatalogService.GetLatestReleaseAsync(
            manifest.UpdateFeedUrl,
            cancellationToken);

        var tempRoot = Path.Combine(Path.GetTempPath(), manifest.ProductId + "-install-" + Guid.NewGuid().ToString("N"));
        var downloadPath = Path.Combine(tempRoot, release.AppZipAsset.Name);
        var extractPath = Path.Combine(tempRoot, "bundle");
        _logger.Info($"Using installer workspace '{tempRoot}'.");

        try
        {
            Directory.CreateDirectory(tempRoot);
            await _releaseCatalogService.DownloadAssetAsync(release.AppZipAsset, downloadPath, cancellationToken);

            Directory.CreateDirectory(extractPath);
            _logger.Info($"Extracting '{downloadPath}' to '{extractPath}'.");
            ZipFile.ExtractToDirectory(downloadPath, extractPath, overwriteFiles: true);

            var installResult = await _bundleInstaller.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: extractPath,
                    InstallRoot: installRoot,
                    CreateDesktopShortcut: createDesktopShortcut,
                    CreateStartMenuShortcut: createStartMenuShortcut,
                    LaunchAfterInstall: launchAfterInstall,
                    ReleaseVersion: release.Version,
                    ReleasePublishedAtUtc: release.PublishedAtUtc,
                    ReleaseAssetSizeBytes: release.AppZipAsset.SizeBytes,
                    Channel: installChannel),
                cancellationToken);

            return (release, installResult);
        }
        finally
        {
            _logger.Info($"Cleaning up installer workspace '{tempRoot}'.");
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
        }
    }
}
