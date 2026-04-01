using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class DiarizationAssetReleaseCatalogService
{
    private readonly IAppUpdateFeedClient _feedClient;
    private readonly DiarizationAssetCatalogService _catalogService;

    public DiarizationAssetReleaseCatalogService(
        IAppUpdateFeedClient feedClient,
        DiarizationAssetCatalogService catalogService)
    {
        _feedClient = feedClient;
        _catalogService = catalogService;
    }

    public async Task<IReadOnlyList<DiarizationRemoteAsset>> ListAvailableRemoteAssetsAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new ArgumentException("A diarization asset feed URL is required.", nameof(feedUrl));
        }

        var payload = await _feedClient.GetStringAsync(feedUrl, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DiarizationRemoteAsset>();
        }

        return assetsElement
            .EnumerateArray()
            .Select(BuildRemoteAsset)
            .Where(asset => asset is not null)
            .Select(asset => asset!)
            .OrderByDescending(asset => asset.IsRecommended)
            .ThenBy(asset => GetAssetPriority(asset))
            .ThenBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<DiarizationAssetInstallStatus> DownloadRemoteAssetIntoManagedDirectoryAsync(
        DiarizationRemoteAsset asset,
        string modelCacheDir,
        CancellationToken cancellationToken = default)
    {
        if (asset is null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        var destinationDirectory = _catalogService.GetManagedAssetDirectory(modelCacheDir);
        return await DownloadRemoteAssetIntoDirectoryAsync(asset, destinationDirectory, cancellationToken);
    }

    public async Task<DiarizationAssetInstallStatus> DownloadRemoteAssetIntoDirectoryAsync(
        DiarizationRemoteAsset asset,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (asset is null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        Directory.CreateDirectory(destinationDirectory);

        var tempDownloadPath = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderDiarizationDownloads",
            Guid.NewGuid().ToString("N") + Path.GetExtension(asset.FileName));
        Directory.CreateDirectory(Path.GetDirectoryName(tempDownloadPath)!);

        try
        {
            await _feedClient.DownloadFileAsync(asset.DownloadUrl, tempDownloadPath, cancellationToken);
            return await _catalogService.ImportAssetIntoDirectoryAsync(tempDownloadPath, destinationDirectory, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempDownloadPath))
            {
                File.Delete(tempDownloadPath);
            }
        }
    }

    private static DiarizationRemoteAsset? BuildRemoteAsset(JsonElement asset)
    {
        var fileName = TryGetString(asset, "name");
        var downloadUrl = TryGetString(asset, "browser_download_url");
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var kind = TryGetAssetKind(fileName);
        if (kind is null)
        {
            return null;
        }

        long? fileSizeBytes = null;
        if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var sizeValue))
        {
            fileSizeBytes = sizeValue;
        }

        return new DiarizationRemoteAsset(
            fileName,
            downloadUrl,
            fileSizeBytes,
            kind.Value,
            IsRecommended: kind.Value == DiarizationRemoteAssetKind.Bundle,
            BuildDescription(fileName, kind.Value));
    }

    private static DiarizationRemoteAssetKind? TryGetAssetKind(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (normalized.Contains("diarization", StringComparison.Ordinal) && extension == ".zip")
        {
            return DiarizationRemoteAssetKind.Bundle;
        }

        if (!normalized.Contains("diarization", StringComparison.Ordinal))
        {
            return null;
        }

        return extension switch
        {
            ".zip" => DiarizationRemoteAssetKind.Bundle,
            ".exe" => DiarizationRemoteAssetKind.Executable,
            ".onnx" => DiarizationRemoteAssetKind.Model,
            ".bin" => DiarizationRemoteAssetKind.Model,
            ".json" => DiarizationRemoteAssetKind.Config,
            ".yaml" => DiarizationRemoteAssetKind.Config,
            ".yml" => DiarizationRemoteAssetKind.Config,
            _ => null,
        };
    }

    private static int GetAssetPriority(DiarizationRemoteAsset asset)
    {
        return asset.Kind switch
        {
            DiarizationRemoteAssetKind.Bundle => 10,
            DiarizationRemoteAssetKind.Executable => 20,
            DiarizationRemoteAssetKind.Model => 30,
            DiarizationRemoteAssetKind.Config => 40,
            _ => 100,
        };
    }

    private static string BuildDescription(string fileName, DiarizationRemoteAssetKind kind)
    {
        return kind switch
        {
            DiarizationRemoteAssetKind.Bundle => "Recommended: installs the diarization model bundle, including the segmentation model, embedding model, and manifest.",
            DiarizationRemoteAssetKind.Executable => "Legacy executable-only asset. Use this only for older diarization setups.",
            DiarizationRemoteAssetKind.Model => $"Supporting diarization model asset: {fileName}.",
            DiarizationRemoteAssetKind.Config => $"Supporting diarization config asset: {fileName}.",
            _ => "Downloadable diarization asset from the current GitHub release.",
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

public enum DiarizationRemoteAssetKind
{
    Bundle,
    Executable,
    Model,
    Config,
}

public sealed record DiarizationRemoteAsset(
    string FileName,
    string DownloadUrl,
    long? FileSizeBytes,
    DiarizationRemoteAssetKind Kind,
    bool IsRecommended,
    string Description);
