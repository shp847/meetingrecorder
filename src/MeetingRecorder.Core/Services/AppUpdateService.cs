using MeetingRecorder.Core.Branding;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class AppUpdateService
{
    private static readonly Regex PortableAppZipAssetNamePattern = new(
        "^MeetingRecorder(?:-v?[^\\\\/]+)?-win-(?:x64|arm64|x86)\\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IAppUpdateFeedClient _feedClient;

    public AppUpdateService()
        : this(new HttpAppUpdateFeedClient())
    {
    }

    public AppUpdateService(IAppUpdateFeedClient feedClient)
    {
        _feedClient = feedClient;
    }

    public Task<AppUpdateCheckResult> CheckForUpdateAsync(
        string currentVersion,
        string? feedUrl,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdateAsync(
            new AppUpdateLocalState(
                currentVersion,
                currentVersion,
                null,
                null),
            feedUrl,
            enabled,
            cancellationToken);
    }

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(
        AppUpdateLocalState localState,
        string? feedUrl,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            return new AppUpdateCheckResult(
                AppUpdateStatusKind.Disabled,
                localState.CurrentVersion,
                localState.CurrentVersion,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                "Update checks are disabled.");
        }

        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return new AppUpdateCheckResult(
                AppUpdateStatusKind.NotConfigured,
                localState.CurrentVersion,
                localState.CurrentVersion,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                "No update feed URL is configured.");
        }

        try
        {
            var payload = await _feedClient.GetStringAsync(feedUrl, cancellationToken);
            var release = ParseReleasePayload(payload);

            var hasCurrentComparableVersion = ReleaseVersionParsing.TryParseComparableVersion(localState.CurrentVersion, out var currentComparableVersion);
            var isNewerByVersion =
                hasCurrentComparableVersion &&
                release.ComparableVersion is not null &&
                release.ComparableVersion > currentComparableVersion;
            var isNewerByPublishedAt =
                localState.InstalledReleasePublishedAtUtc.HasValue &&
                release.PublishedAtUtc.HasValue &&
                release.PublishedAtUtc.Value > localState.InstalledReleasePublishedAtUtc.Value;
            var isNewerByAssetSize =
                localState.InstalledReleaseAssetSizeBytes.HasValue &&
                release.AssetSizeBytes.HasValue &&
                release.AssetSizeBytes.Value > 0 &&
                localState.InstalledReleaseAssetSizeBytes.Value > 0 &&
                release.AssetSizeBytes.Value != localState.InstalledReleaseAssetSizeBytes.Value;

            var status = isNewerByVersion || isNewerByPublishedAt || isNewerByAssetSize
                ? AppUpdateStatusKind.UpdateAvailable
                : AppUpdateStatusKind.UpToDate;
            var message = status == AppUpdateStatusKind.UpdateAvailable
                ? BuildUpdateAvailableMessage(release.Version, isNewerByVersion, isNewerByPublishedAt, isNewerByAssetSize)
                : $"You are already on version {localState.CurrentVersion}.";

            return new AppUpdateCheckResult(
                status,
                localState.CurrentVersion,
                release.Version,
                release.DownloadUrl,
                release.ReleasePageUrl,
                release.PublishedAtUtc,
                release.AssetSizeBytes,
                isNewerByVersion,
                isNewerByPublishedAt,
                isNewerByAssetSize,
                message);
        }
        catch (Exception exception)
        {
            return new AppUpdateCheckResult(
                AppUpdateStatusKind.Error,
                localState.CurrentVersion,
                localState.CurrentVersion,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                $"Update check failed: {exception.Message}");
        }
    }

    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("A download URL is required to fetch the update package.");
        }

        var updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "MeetingRecorder Updates");
        Directory.CreateDirectory(updatesDirectory);

        var fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"MeetingRecorder-v{version}-win-x64.zip";
        }

        var destinationPath = Path.Combine(updatesDirectory, fileName);
        await _feedClient.DownloadFileAsync(downloadUrl, destinationPath, cancellationToken);
        return destinationPath;
    }

    private static AppUpdateRelease ParseReleasePayload(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("tag_name", out var tagNameElement))
        {
            var asset = SelectGitHubAsset(root);
            var version = ReleaseVersionParsing.ResolveVersionInfo(
                tagNameElement.GetString(),
                tagNameElement.GetString(),
                TryGetString(root, "name"),
                asset?.Name);
            var releasePageUrl = TryGetString(root, "html_url");
            var publishedAtUtc = ResolveEffectivePublishedAtUtc(
                TryGetDateTimeOffset(root, "published_at"),
                asset?.UpdatedAtUtc);
            return new AppUpdateRelease(version.DisplayLabel, version.ComparableVersion, asset?.Url, releasePageUrl, publishedAtUtc, asset?.SizeBytes);
        }

        if (root.TryGetProperty("version", out var versionElement))
        {
            var version = ReleaseVersionParsing.ResolveVersionInfo(
                versionElement.GetString(),
                versionElement.GetString());
            var downloadUrl = TryGetString(root, "downloadUrl");
            var releasePageUrl = TryGetString(root, "releasePageUrl");
            var publishedAtUtc = TryGetDateTimeOffset(root, "publishedAtUtc");
            var assetSizeBytes = TryGetInt64(root, "assetSizeBytes");
            return new AppUpdateRelease(version.DisplayLabel, version.ComparableVersion, downloadUrl, releasePageUrl, publishedAtUtc, assetSizeBytes);
        }

        throw new InvalidOperationException("The update feed response does not match a supported release format.");
    }

    private static AppUpdateAsset? SelectGitHubAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var assets = assetsElement.EnumerateArray()
            .Select(asset => new AppUpdateAsset(
                TryGetString(asset, "name"),
                TryGetString(asset, "browser_download_url"),
                TryGetInt64(asset, "size"),
                TryGetDateTimeOffset(asset, "updated_at")))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
            .ToArray();

        return assets
            .FirstOrDefault(asset =>
                IsPortableAppZipAsset(asset.Name) &&
                asset.Name!.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(asset => IsPortableAppZipAsset(asset.Name))
            ?? assets.FirstOrDefault();
    }

    private static bool IsPortableAppZipAsset(string? assetName)
    {
        return !string.IsNullOrWhiteSpace(assetName) &&
            PortableAppZipAssetNamePattern.IsMatch(assetName);
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

    private static string BuildUpdateAvailableMessage(
        string version,
        bool isNewerByVersion,
        bool isNewerByPublishedAt,
        bool isNewerByAssetSize)
    {
        if (isNewerByVersion)
        {
            return $"Version {version} is available.";
        }

        if (isNewerByPublishedAt)
        {
            return $"A newer published build for version {version} is available.";
        }

        if (isNewerByAssetSize)
        {
            return $"A different packaged build for version {version} is available.";
        }

        return $"Version {version} is available.";
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
        if (string.IsNullOrWhiteSpace(rawValue)) {
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
}

public enum AppUpdateInstallBlockKind
{
    None = 0,
    Recording = 1,
    Processing = 2,
    InstallInProgress = 3,
}

public sealed class AppUpdateInstallPolicy
{
    public AppUpdateInstallBlockKind GetInstallBlockKind(
        bool hasActiveRecording,
        bool isProcessingInProgress,
        bool isUpdateAlreadyInProgress,
        bool allowCurrentInstallInProgress = false,
        bool allowProcessingOverride = false)
    {
        if (hasActiveRecording)
        {
            return AppUpdateInstallBlockKind.Recording;
        }

        if (isProcessingInProgress && !allowProcessingOverride)
        {
            return AppUpdateInstallBlockKind.Processing;
        }

        if (isUpdateAlreadyInProgress && !allowCurrentInstallInProgress)
        {
            return AppUpdateInstallBlockKind.InstallInProgress;
        }

        return AppUpdateInstallBlockKind.None;
    }

    public string? GetInstallBlockReason(
        bool hasActiveRecording,
        bool isProcessingInProgress,
        bool isUpdateAlreadyInProgress,
        bool allowCurrentInstallInProgress = false,
        bool allowProcessingOverride = false)
    {
        return GetInstallBlockKind(
            hasActiveRecording,
            isProcessingInProgress,
            isUpdateAlreadyInProgress,
            allowCurrentInstallInProgress,
            allowProcessingOverride) switch
        {
            AppUpdateInstallBlockKind.Recording => "Stop the active recording before installing an update.",
            AppUpdateInstallBlockKind.Processing => "Wait for background processing to finish before installing an update.",
            AppUpdateInstallBlockKind.InstallInProgress => "An update install is already in progress.",
            _ => null,
        };
    }

    public bool ShouldRetryPendingInstall(
        string? pendingUpdateZipPath,
        string? pendingUpdateVersion,
        string currentVersion,
        bool queuedInstallWhenIdleRequested,
        bool hasActiveRecording,
        bool isProcessingInProgress,
        bool isUpdateAlreadyInProgress)
    {
        return !string.IsNullOrWhiteSpace(pendingUpdateZipPath) &&
            (
                queuedInstallWhenIdleRequested ||
                !string.Equals(
                    pendingUpdateVersion?.Trim(),
                    currentVersion.Trim(),
                    StringComparison.OrdinalIgnoreCase)) &&
            GetInstallBlockReason(
                hasActiveRecording,
                isProcessingInProgress,
                isUpdateAlreadyInProgress) is null;
    }

    public bool ShouldAutoInstall(
        AppUpdateCheckResult? result,
        bool autoInstallEnabled,
        bool hasActiveRecording,
        bool isProcessingInProgress,
        bool isUpdateAlreadyInProgress)
    {
        return autoInstallEnabled &&
            result is { Status: AppUpdateStatusKind.UpdateAvailable, DownloadUrl: not null and not "", IsNewerByVersion: true } &&
            GetInstallBlockReason(
                hasActiveRecording,
                isProcessingInProgress,
                isUpdateAlreadyInProgress) is null;
    }
}

public sealed record AppUpdateLocalState(
    string CurrentVersion,
    string InstalledReleaseVersion,
    DateTimeOffset? InstalledReleasePublishedAtUtc,
    long? InstalledReleaseAssetSizeBytes);

public interface IAppUpdateFeedClient
{
    Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken);

    Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken);

    Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<FileDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        return DownloadFileAsync(downloadUrl, destinationPath, cancellationToken);
    }
}

public sealed record FileDownloadProgress(long BytesDownloaded, long? TotalBytes);

public sealed class HttpAppUpdateFeedClient : IAppUpdateFeedClient
{
    private readonly HttpClient _httpClient;

    public HttpAppUpdateFeedClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeetingRecorder", AppBranding.Version));
    }

    public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
    {
        return _httpClient.GetStringAsync(feedUrl, cancellationToken);
    }

    public async Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        await DownloadFileAsync(downloadUrl, destinationPath, progress: null, cancellationToken);
    }

    public async Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<FileDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        long bytesDownloaded = 0;
        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;
            progress?.Report(new FileDownloadProgress(bytesDownloaded, totalBytes));
        }
    }
}

public enum AppUpdateStatusKind
{
    Disabled = 0,
    NotConfigured = 1,
    UpToDate = 2,
    UpdateAvailable = 3,
    Error = 4,
}

public sealed record AppUpdateCheckResult(
    AppUpdateStatusKind Status,
    string CurrentVersion,
    string LatestVersion,
    string? DownloadUrl,
    string? ReleasePageUrl,
    DateTimeOffset? LatestPublishedAtUtc,
    long? LatestAssetSizeBytes,
    bool IsNewerByVersion,
    bool IsNewerByPublishedAt,
    bool IsNewerByAssetSize,
    string Message);

internal sealed record AppUpdateRelease(
    string Version,
    Version? ComparableVersion,
    string? DownloadUrl,
    string? ReleasePageUrl,
    DateTimeOffset? PublishedAtUtc,
    long? AssetSizeBytes);

internal sealed record AppUpdateAsset(
    string? Name,
    string? Url,
    long? SizeBytes,
    DateTimeOffset? UpdatedAtUtc);

internal readonly record struct ReleaseVersionInfo(
    string DisplayLabel,
    Version? ComparableVersion);

internal static class ReleaseVersionParsing
{
    private static readonly Regex EmbeddedVersionPattern = new(
        @"(?<!\d)(?:v)?(?<version>\d+(?:\.\d+){1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static ReleaseVersionInfo ResolveVersionInfo(string? rawFallbackLabel, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryNormalizeVersionLabel(candidate, out var normalizedLabel, out var comparableVersion))
            {
                return new ReleaseVersionInfo(normalizedLabel, comparableVersion);
            }
        }

        var rawLabel = candidates
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
            ?? rawFallbackLabel;
        return new ReleaseVersionInfo(
            string.IsNullOrWhiteSpace(rawLabel) ? "unknown" : rawLabel.Trim(),
            null);
    }

    public static bool TryNormalizeVersionLabel(string? candidate, out string normalizedLabel, out Version comparableVersion)
    {
        normalizedLabel = string.Empty;
        comparableVersion = default!;

        if (!TryExtractComparableToken(candidate, out var comparableToken) ||
            !TryParseComparableVersion(comparableToken, out comparableVersion))
        {
            return false;
        }

        normalizedLabel = FormatComparableLabel(comparableToken);
        return true;
    }

    public static bool TryParseComparableVersion(string? version, out Version comparableVersion)
    {
        comparableVersion = default!;

        var normalizedToken = NormalizeComparableToken(version);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return false;
        }

        var segments = normalizedToken
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (segments.Count == 0 || segments.Any(segment => !int.TryParse(segment, out _)))
        {
            return false;
        }

        while (segments.Count < 3)
        {
            segments.Add("0");
        }

        comparableVersion = Version.Parse(string.Join('.', segments.Take(3)));
        return true;
    }

    private static bool TryExtractComparableToken(string? candidate, out string comparableToken)
    {
        comparableToken = string.Empty;

        var normalizedToken = NormalizeComparableToken(candidate);
        if (!string.IsNullOrWhiteSpace(normalizedToken) && TryParseComparableVersion(normalizedToken, out _))
        {
            comparableToken = normalizedToken;
            return true;
        }

        var match = EmbeddedVersionPattern.Match(candidate ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        normalizedToken = NormalizeComparableToken(match.Groups["version"].Value);
        if (string.IsNullOrWhiteSpace(normalizedToken) || !TryParseComparableVersion(normalizedToken, out _))
        {
            return false;
        }

        comparableToken = normalizedToken;
        return true;
    }

    private static string? NormalizeComparableToken(string? candidate)
    {
        var raw = (candidate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..];
        }

        var suffixIndex = raw.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            raw = raw[..suffixIndex];
        }

        return raw;
    }

    private static string FormatComparableLabel(string comparableToken)
    {
        var segments = comparableToken
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        while (segments.Count < 2)
        {
            segments.Add("0");
        }

        var keepCount = segments.Count >= 3 && segments[2] != "0" ? 3 : 2;
        return string.Join('.', segments.Take(keepCount));
    }
}
