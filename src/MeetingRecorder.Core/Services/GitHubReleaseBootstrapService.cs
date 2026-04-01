using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class GitHubReleaseBootstrapService
{
    private readonly IAppUpdateFeedClient _feedClient;

    public GitHubReleaseBootstrapService(IAppUpdateFeedClient feedClient)
    {
        _feedClient = feedClient;
    }

    public async Task<GitHubReleaseBootstrapInfo> GetLatestReleaseAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new ArgumentException("A release feed URL is required.", nameof(feedUrl));
        }

        var payload = await _feedClient.GetStringAsync(feedUrl, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var tagName = TryGetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("The GitHub release payload did not contain tag_name.");
        }

        var releasePageUrl = TryGetString(root, "html_url");
        var releasePublishedAtUtc = TryGetDateTimeOffset(root, "published_at");

        var assets = root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array
            ? assetsElement.EnumerateArray()
                .Select(BuildAsset)
                .Where(asset => asset is not null)
                .Select(asset => asset!)
                .ToArray()
            : Array.Empty<GitHubReleaseAssetInfo>();

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
        var version = ReleaseVersionParsing.ResolveVersionInfo(
            tagName,
            tagName,
            TryGetString(root, "name"),
            appZipAsset.Name);
        var publishedAtUtc = ResolveEffectivePublishedAtUtc(releasePublishedAtUtc, appZipAsset.UpdatedAtUtc);

        return new GitHubReleaseBootstrapInfo(
            version.DisplayLabel,
            releasePageUrl,
            publishedAtUtc,
            InstallerExecutableAsset: null,
            appZipAsset,
            backupCommandAsset,
            backupPowerShellAsset);
    }

    private static GitHubReleaseAssetInfo? BuildAsset(JsonElement asset)
    {
        var name = TryGetString(asset, "name");
        var downloadUrl = TryGetString(asset, "browser_download_url");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        return new GitHubReleaseAssetInfo(
            name,
            downloadUrl,
            TryGetInt64(asset, "size"),
            TryGetDateTimeOffset(asset, "updated_at"));
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

        return DateTimeOffset.TryParse(rawValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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
}

public sealed record GitHubReleaseBootstrapInfo(
    string Version,
    string? ReleasePageUrl,
    DateTimeOffset? PublishedAtUtc,
    GitHubReleaseAssetInfo? InstallerExecutableAsset,
    GitHubReleaseAssetInfo AppZipAsset,
    GitHubReleaseAssetInfo? BackupCommandAsset,
    GitHubReleaseAssetInfo? BackupPowerShellAsset);

public sealed record GitHubReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long? SizeBytes,
    DateTimeOffset? UpdatedAtUtc);
