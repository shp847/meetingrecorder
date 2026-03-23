using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class WhisperModelReleaseCatalogService
{
    private readonly IAppUpdateFeedClient _feedClient;
    private readonly WhisperModelService _modelService;

    public WhisperModelReleaseCatalogService(
        IAppUpdateFeedClient feedClient,
        WhisperModelService modelService)
    {
        _feedClient = feedClient;
        _modelService = modelService;
    }

    public async Task<IReadOnlyList<WhisperRemoteModelAsset>> ListAvailableRemoteModelsAsync(
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new ArgumentException("A model feed URL is required.", nameof(feedUrl));
        }

        var payload = await _feedClient.GetStringAsync(feedUrl, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<WhisperRemoteModelAsset>();
        }

        return assetsElement
            .EnumerateArray()
            .Select(BuildRemoteModelAsset)
            .Where(asset => asset is not null)
            .Select(asset => asset!)
            .OrderByDescending(asset => asset.IsRecommended)
            .ThenByDescending(asset => asset.FileSizeBytes ?? -1L)
            .ThenBy(asset => GetRecommendationPriority(asset.FileName))
            .ThenBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<WhisperModelCatalogItem> DownloadRemoteModelIntoManagedDirectoryAsync(
        WhisperRemoteModelAsset model,
        string modelCacheDir,
        IProgress<FileDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrWhiteSpace(modelCacheDir))
        {
            throw new ArgumentException("A model cache directory is required.", nameof(modelCacheDir));
        }

        var managedDirectory = Path.Combine(modelCacheDir, "asr");
        Directory.CreateDirectory(managedDirectory);

        var destinationPath = Path.Combine(managedDirectory, model.FileName);
        var tempPath = destinationPath + ".download";

        try
        {
            await _feedClient.DownloadFileAsync(model.DownloadUrl, tempPath, progress, cancellationToken);
            return await BuildInstalledCatalogItemAsync(tempPath, destinationPath, managedDirectory, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task<WhisperModelCatalogItem> BuildInstalledCatalogItemAsync(
        string sourcePath,
        string destinationPath,
        string managedDirectory,
        CancellationToken cancellationToken)
    {
        var result = await _modelService.ImportModelAsync(sourcePath, destinationPath, cancellationToken);
        var status = _modelService.Inspect(result.ModelPath);
        return new WhisperModelCatalogItem(
            result.ModelPath,
            Path.GetFileName(result.ModelPath),
            IsConfigured: true,
            IsManaged: result.ModelPath.StartsWith(Path.GetFullPath(managedDirectory), StringComparison.OrdinalIgnoreCase),
            status);
    }

    private static WhisperRemoteModelAsset? BuildRemoteModelAsset(JsonElement asset)
    {
        var fileName = TryGetString(asset, "name");
        var downloadUrl = TryGetString(asset, "browser_download_url");
        if (string.IsNullOrWhiteSpace(fileName) ||
            string.IsNullOrWhiteSpace(downloadUrl) ||
            !fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
            !fileName.Contains("ggml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        long? fileSizeBytes = null;
        if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var sizeValue))
        {
            fileSizeBytes = sizeValue;
        }

        return new WhisperRemoteModelAsset(
            fileName,
            downloadUrl,
            fileSizeBytes,
            IsRecommended: GetRecommendationPriority(fileName) <= 13,
            BuildDescription(fileName));
    }

    private static int GetRecommendationPriority(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        return normalized switch
        {
            "ggml-base.en-q8_0.bin" => 10,
            "ggml-base.bin" => 11,
            "ggml-base.en.bin" => 12,
            "ggml-base-q8_0.bin" => 13,
            "ggml-small.en-q8_0.bin" => 20,
            "ggml-small.bin" => 21,
            "ggml-small.en.bin" => 22,
            "ggml-small-q8_0.bin" => 23,
            "ggml-tiny.en-q8_0.bin" => 30,
            "ggml-tiny.bin" => 31,
            "ggml-medium.en-q8_0.bin" => 40,
            "ggml-medium.bin" => 41,
            _ when normalized.Contains("base", StringComparison.Ordinal) => 50,
            _ when normalized.Contains("small", StringComparison.Ordinal) => 60,
            _ when normalized.Contains("tiny", StringComparison.Ordinal) => 70,
            _ when normalized.Contains("medium", StringComparison.Ordinal) => 80,
            _ when normalized.Contains("large", StringComparison.Ordinal) => 90,
            _ => 100,
        };
    }

    private static string BuildDescription(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();

        if (normalized.Contains("base", StringComparison.Ordinal) &&
            normalized.Contains("q8_0", StringComparison.Ordinal))
        {
            return "Recommended default for most laptops: balanced accuracy, download size, and CPU speed.";
        }

        if (normalized.Contains("small", StringComparison.Ordinal) &&
            normalized.Contains("q8_0", StringComparison.Ordinal))
        {
            return "Better accuracy than base, but slower and larger; good when transcript quality matters more than speed.";
        }

        if (normalized.Contains("tiny", StringComparison.Ordinal) &&
            normalized.Contains("q8_0", StringComparison.Ordinal))
        {
            return "Smallest and fastest option; best for quickest setup or lighter machines, with the lowest accuracy.";
        }

        if (normalized.Contains("medium", StringComparison.Ordinal) &&
            normalized.Contains("q8_0", StringComparison.Ordinal))
        {
            return "Most accurate of the four q8_0 options, but much larger and slower; best for stronger machines.";
        }

        if (normalized.Contains("base", StringComparison.Ordinal))
        {
            return "Recommended default for most laptops.";
        }

        if (normalized.Contains("small", StringComparison.Ordinal))
        {
            return "Higher accuracy than base, with a larger download and slower CPU processing.";
        }

        if (normalized.Contains("tiny", StringComparison.Ordinal))
        {
            return "Fastest and smallest option, with lower accuracy.";
        }

        if (normalized.Contains("medium", StringComparison.Ordinal) || normalized.Contains("large", StringComparison.Ordinal))
        {
            return "Best suited for stronger machines because it is much larger.";
        }

        return "Available model asset from the GitHub release.";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

public sealed record WhisperRemoteModelAsset(
    string FileName,
    string DownloadUrl,
    long? FileSizeBytes,
    bool IsRecommended,
    string Description);
