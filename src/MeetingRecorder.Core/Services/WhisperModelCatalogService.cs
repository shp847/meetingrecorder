namespace MeetingRecorder.Core.Services;

public sealed class WhisperModelCatalogService
{
    private readonly WhisperModelService _modelService;

    public WhisperModelCatalogService(WhisperModelService modelService)
    {
        _modelService = modelService;
    }

    public IReadOnlyList<WhisperModelCatalogItem> ListAvailableModels(
        string modelCacheDir,
        string configuredModelPath)
    {
        var managedDirectory = GetManagedAsrDirectory(modelCacheDir);
        var discoveredPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(managedDirectory))
        {
            foreach (var modelPath in Directory.GetFiles(managedDirectory, "*.bin", SearchOption.TopDirectoryOnly))
            {
                discoveredPaths[Path.GetFullPath(modelPath)] = modelPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredModelPath))
        {
            var fullConfiguredPath = Path.GetFullPath(configuredModelPath);
            discoveredPaths[fullConfiguredPath] = configuredModelPath;
        }

        return discoveredPaths
            .Values
            .Select(path => BuildCatalogItem(path, configuredModelPath, managedDirectory))
            .OrderByDescending(item => item.IsConfigured)
            .ThenByDescending(item => item.Status.Kind == WhisperModelStatusKind.Valid)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WhisperModelCatalogResolution ResolveConfiguredOrFallbackModel(
        string modelCacheDir,
        string configuredModelPath)
    {
        var availableModels = ListAvailableModels(modelCacheDir, configuredModelPath);
        var configuredModel = availableModels.FirstOrDefault(item => item.IsConfigured);
        if (configuredModel is not null && configuredModel.Status.Kind == WhisperModelStatusKind.Valid)
        {
            return new WhisperModelCatalogResolution(configuredModelPath, configuredModel, UsedFallbackModel: false);
        }

        var configuredFileName = string.IsNullOrWhiteSpace(configuredModelPath)
            ? string.Empty
            : Path.GetFileName(configuredModelPath);

        var fallbackModel = availableModels
            .Where(item => item.IsManaged && item.Status.Kind == WhisperModelStatusKind.Valid)
            .OrderBy(item => GetManagedFallbackPriority(item.FileName, configuredFileName))
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new WhisperModelCatalogResolution(
            configuredModelPath,
            fallbackModel,
            UsedFallbackModel: fallbackModel is not null);
    }

    public async Task<WhisperModelCatalogItem> ImportModelIntoManagedDirectoryAsync(
        string sourcePath,
        string modelCacheDir,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source model path is required.", nameof(sourcePath));
        }

        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("The source model path must include a file name.");
        }

        var managedDirectory = GetManagedAsrDirectory(modelCacheDir);
        Directory.CreateDirectory(managedDirectory);

        var destinationPath = Path.Combine(managedDirectory, fileName);
        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildCatalogItem(destinationPath, destinationPath, managedDirectory);
        }

        var result = await _modelService.ImportModelAsync(sourcePath, destinationPath, cancellationToken);
        return BuildCatalogItem(result.ModelPath, result.ModelPath, managedDirectory);
    }

    public string GetManagedAsrDirectory(string modelCacheDir)
    {
        if (string.IsNullOrWhiteSpace(modelCacheDir))
        {
            throw new ArgumentException("A model cache directory is required.", nameof(modelCacheDir));
        }

        return Path.Combine(modelCacheDir, "asr");
    }

    private WhisperModelCatalogItem BuildCatalogItem(
        string modelPath,
        string configuredModelPath,
        string managedDirectory)
    {
        var normalizedModelPath = Path.GetFullPath(modelPath);
        var normalizedConfiguredPath = string.IsNullOrWhiteSpace(configuredModelPath)
            ? string.Empty
            : Path.GetFullPath(configuredModelPath);
        var normalizedManagedDirectory = Path.GetFullPath(managedDirectory);
        var isManaged = normalizedModelPath.StartsWith(normalizedManagedDirectory, StringComparison.OrdinalIgnoreCase);
        var status = _modelService.Inspect(normalizedModelPath);

        return new WhisperModelCatalogItem(
            normalizedModelPath,
            Path.GetFileName(normalizedModelPath),
            string.Equals(normalizedModelPath, normalizedConfiguredPath, StringComparison.OrdinalIgnoreCase),
            isManaged,
            status);
    }

    private static int GetManagedFallbackPriority(string fileName, string configuredFileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredFileName) &&
            string.Equals(fileName, configuredFileName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return fileName.ToLowerInvariant() switch
        {
            "ggml-base.bin" => 10,
            "ggml-base.en.bin" => 11,
            "ggml-small.bin" => 20,
            "ggml-small.en.bin" => 21,
            "ggml-medium.bin" => 30,
            "ggml-medium.en.bin" => 31,
            "ggml-tiny.bin" => 40,
            "ggml-tiny.en.bin" => 41,
            _ when fileName.Contains("base", StringComparison.OrdinalIgnoreCase) => 50,
            _ when fileName.Contains("small", StringComparison.OrdinalIgnoreCase) => 60,
            _ when fileName.Contains("medium", StringComparison.OrdinalIgnoreCase) => 70,
            _ when fileName.Contains("tiny", StringComparison.OrdinalIgnoreCase) => 80,
            _ => 100,
        };
    }
}

public sealed record WhisperModelCatalogItem(
    string ModelPath,
    string FileName,
    bool IsConfigured,
    bool IsManaged,
    WhisperModelStatus Status);

public sealed record WhisperModelCatalogResolution(
    string RequestedModelPath,
    WhisperModelCatalogItem? ActiveModel,
    bool UsedFallbackModel);
