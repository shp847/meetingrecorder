using AppPlatform.Abstractions;
using System.Text.Json;

namespace AppPlatform.Deployment;

public sealed class GitHubReleaseCatalogService
{
    private readonly DeploymentDownloadClient _downloadClient;
    private readonly IDeploymentLogger _logger;

    public GitHubReleaseCatalogService(
        DeploymentDownloadClient downloadClient,
        IDeploymentLogger? logger = null)
    {
        _downloadClient = downloadClient;
        _logger = logger ?? NullDeploymentLogger.Instance;
    }

    public async Task<ReleaseAssetSet> GetLatestReleaseAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new ArgumentException("A release feed URL is required.", nameof(feedUrl));
        }

        _logger.Info($"Fetching latest release metadata from '{feedUrl}'.");
        var payload = await _downloadClient.GetStringAsync(feedUrl, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var tagName = TryGetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("The GitHub release payload did not contain tag_name.");
        }

        var assets = root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array
            ? assetsElement.EnumerateArray()
                .Select(BuildAsset)
                .Where(static asset => asset is not null)
                .Select(static asset => asset!)
                .ToArray()
            : Array.Empty<ReleaseAssetDescriptor>();

        var appZipAsset =
            assets.FirstOrDefault(asset =>
                asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ??
            assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (appZipAsset is null)
        {
            throw new InvalidOperationException("The GitHub release did not contain a ZIP installer asset.");
        }

        var backupCommandAsset = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "Install-LatestFromGitHub.cmd", StringComparison.OrdinalIgnoreCase));
        var backupPowerShellAsset = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "Install-LatestFromGitHub.ps1", StringComparison.OrdinalIgnoreCase));
        var version = NormalizeVersion(tagName);
        var releasePublishedAtUtc = TryGetDateTimeOffset(root, "published_at");
        var publishedAtUtc = ResolveEffectivePublishedAtUtc(releasePublishedAtUtc, appZipAsset.UpdatedAtUtc);
        _logger.Info(
            $"Selected release version '{version}' with app ZIP asset '{appZipAsset.Name}' and no executable bootstrap asset.");

        return new ReleaseAssetSet(
            Version: version,
            ReleasePageUrl: TryGetString(root, "html_url"),
            PublishedAtUtc: publishedAtUtc,
            InstallerExecutableAsset: null,
            AppZipAsset: appZipAsset,
            BackupCommandAsset: backupCommandAsset,
            BackupPowerShellAsset: backupPowerShellAsset);
    }

    public Task DownloadAssetAsync(
        ReleaseAssetDescriptor asset,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return _downloadClient.DownloadFileAsync(asset.DownloadUrl, destinationPath, cancellationToken);
    }

    private static ReleaseAssetDescriptor? BuildAsset(JsonElement asset)
    {
        var name = TryGetString(asset, "name");
        var downloadUrl = TryGetString(asset, "browser_download_url");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        return new ReleaseAssetDescriptor(
            name,
            downloadUrl,
            TryGetInt64(asset, "size"),
            TryGetDateTimeOffset(asset, "updated_at"));
    }

    private static string NormalizeVersion(string rawVersion)
    {
        if (rawVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return rawVersion[1..];
        }

        return rawVersion;
    }

    private static DateTimeOffset? ResolveEffectivePublishedAtUtc(
        DateTimeOffset? releasePublishedAtUtc,
        DateTimeOffset? assetUpdatedAtUtc)
    {
        if (!releasePublishedAtUtc.HasValue)
        {
            return assetUpdatedAtUtc;
        }

        if (!assetUpdatedAtUtc.HasValue)
        {
            return releasePublishedAtUtc;
        }

        return assetUpdatedAtUtc.Value > releasePublishedAtUtc.Value
            ? assetUpdatedAtUtc
            : releasePublishedAtUtc;
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var rawValue = TryGetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            rawValue,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
