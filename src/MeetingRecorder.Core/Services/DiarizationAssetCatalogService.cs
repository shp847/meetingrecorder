using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class DiarizationAssetCatalogService
{
    public const string BundleManifestFileName = "meeting-recorder-diarization-bundle.json";
    public const string RuntimeStatusFileName = "meeting-recorder-diarization-runtime-status.json";

    private static readonly string[] SupportingFileExtensions =
    [
        ".onnx",
        ".json",
        ".txt",
        ".md",
        ".yaml",
        ".yml",
    ];

    public DiarizationAssetInstallStatus InspectInstalledAssets(string diarizationAssetPath)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(diarizationAssetPath)
            ? string.Empty
            : Path.GetFullPath(diarizationAssetPath);

        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return new DiarizationAssetInstallStatus(
                string.Empty,
                null,
                null,
                null,
                null,
                Array.Empty<string>(),
                false,
                "No diarization model bundle is configured.",
                "Choose a diarization model bundle or set the diarization asset path in Settings.",
                null,
                null,
                null);
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                null,
                null,
                null,
                null,
                Array.Empty<string>(),
                false,
                "Diarization model bundle not installed.",
                $"The configured folder '{normalizedRoot}' does not exist yet.",
                null,
                null,
                null);
        }

        var allFiles = Directory.GetFiles(normalizedRoot, "*", SearchOption.AllDirectories);
        var runtimeStatus = TryReadRuntimeStatus(normalizedRoot);
        var manifestPath = allFiles.FirstOrDefault(file =>
            string.Equals(
                Path.GetFileName(file),
                BundleManifestFileName,
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                null,
                null,
                null,
                null,
                ListSupportingFiles(allFiles, excludedPaths: Array.Empty<string>()),
                false,
                "Diarization model bundle not installed.",
                $"No '{BundleManifestFileName}' bundle manifest was found under '{normalizedRoot}'.",
                runtimeStatus?.GpuAccelerationAvailable,
                runtimeStatus?.EffectiveExecutionProvider,
                runtimeStatus?.DiagnosticMessage);
        }

        DiarizationModelBundleManifest manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<DiarizationModelBundleManifest>(json, SerializerOptions)
                ?? throw new InvalidOperationException("The bundle manifest was empty.");
        }
        catch (Exception exception)
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                manifestPath,
                null,
                null,
                null,
                ListSupportingFiles(allFiles, excludedPaths: [manifestPath]),
                false,
                "Diarization model bundle is invalid.",
                $"Unable to read '{BundleManifestFileName}': {exception.Message}",
                runtimeStatus?.GpuAccelerationAvailable,
                runtimeStatus?.EffectiveExecutionProvider,
                runtimeStatus?.DiagnosticMessage);
        }

        if (string.IsNullOrWhiteSpace(manifest.BundleVersion) ||
            string.IsNullOrWhiteSpace(manifest.SegmentationModelFileName) ||
            string.IsNullOrWhiteSpace(manifest.EmbeddingModelFileName))
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                manifestPath,
                null,
                null,
                null,
                ListSupportingFiles(allFiles, excludedPaths: [manifestPath]),
                false,
                "Diarization model bundle is invalid.",
                $"'{BundleManifestFileName}' must include bundleVersion, segmentationModelFileName, and embeddingModelFileName.",
                runtimeStatus?.GpuAccelerationAvailable,
                runtimeStatus?.EffectiveExecutionProvider,
                runtimeStatus?.DiagnosticMessage);
        }

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? normalizedRoot;
        var segmentationModelPath = ResolveBundleFilePath(manifestDirectory, manifest.SegmentationModelFileName);
        var embeddingModelPath = ResolveBundleFilePath(manifestDirectory, manifest.EmbeddingModelFileName);
        var missingFiles = new List<string>();

        if (!File.Exists(segmentationModelPath))
        {
            missingFiles.Add(manifest.SegmentationModelFileName);
        }

        if (!File.Exists(embeddingModelPath))
        {
            missingFiles.Add(manifest.EmbeddingModelFileName);
        }

        var supportingFiles = ListSupportingFiles(
            allFiles,
            excludedPaths:
            [
                manifestPath,
                segmentationModelPath,
                embeddingModelPath,
            ]);

        if (missingFiles.Count > 0)
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                manifestPath,
                File.Exists(segmentationModelPath) ? segmentationModelPath : null,
                File.Exists(embeddingModelPath) ? embeddingModelPath : null,
                manifest.BundleVersion,
                supportingFiles,
                false,
                "Diarization model bundle is incomplete.",
                $"The bundle manifest was found, but these files are missing: {string.Join(", ", missingFiles)}.",
                runtimeStatus?.GpuAccelerationAvailable,
                runtimeStatus?.EffectiveExecutionProvider,
                runtimeStatus?.DiagnosticMessage);
        }

        return new DiarizationAssetInstallStatus(
            normalizedRoot,
            manifestPath,
            segmentationModelPath,
            embeddingModelPath,
            manifest.BundleVersion,
            supportingFiles,
            true,
            "Diarization model bundle looks ready.",
            $"Bundle version '{manifest.BundleVersion}' is installed. Speaker labeling will run with '{Path.GetFileName(segmentationModelPath)}' and '{Path.GetFileName(embeddingModelPath)}'.",
            runtimeStatus?.GpuAccelerationAvailable,
            runtimeStatus?.EffectiveExecutionProvider,
            runtimeStatus?.DiagnosticMessage);
    }

    public async Task<DiarizationAssetInstallStatus> ImportAssetIntoManagedDirectoryAsync(
        string sourcePath,
        string modelCacheDir,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source asset path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(modelCacheDir))
        {
            throw new ArgumentException("A model cache directory is required.", nameof(modelCacheDir));
        }

        var managedDirectory = GetManagedAssetDirectory(modelCacheDir);
        return await ImportAssetIntoDirectoryAsync(sourcePath, managedDirectory, cancellationToken);
    }

    public string GetManagedAssetDirectory(string modelCacheDir)
    {
        if (string.IsNullOrWhiteSpace(modelCacheDir))
        {
            throw new ArgumentException("A model cache directory is required.", nameof(modelCacheDir));
        }

        return Path.Combine(modelCacheDir, "diarization");
    }

    public async Task<DiarizationAssetInstallStatus> ImportAssetIntoDirectoryAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source asset path is required.", nameof(sourcePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected diarization asset does not exist.", sourcePath);
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        }

        PrepareDestinationDirectory(destinationDirectory);

        var sourceExtension = Path.GetExtension(sourcePath);
        if (sourceExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractArchiveIntoDirectoryAsync(sourcePath, destinationDirectory, cancellationToken);
        }
        else
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            await using var sourceStream = File.OpenRead(sourcePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        return InspectInstalledAssets(destinationDirectory);
    }

    internal Task ExtractArchiveIntoDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderDiarizationExtract",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, extractRoot);
            foreach (var file in Directory.GetFiles(extractRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(extractRoot, file);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(file, destinationPath, overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public async Task WriteRuntimeStatusAsync(
        string diarizationAssetPath,
        DiarizationRuntimeStatus runtimeStatus,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diarizationAssetPath))
        {
            return;
        }

        Directory.CreateDirectory(diarizationAssetPath);
        var statusPath = Path.Combine(diarizationAssetPath, RuntimeStatusFileName);
        var serializerOptions = new JsonSerializerOptions(SerializerOptions)
        {
            WriteIndented = true,
        };
        await File.WriteAllTextAsync(
            statusPath,
            JsonSerializer.Serialize(runtimeStatus, serializerOptions),
            cancellationToken);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ResolveBundleFilePath(string manifestDirectory, string fileName)
    {
        return Path.GetFullPath(Path.Combine(manifestDirectory, fileName.Trim()));
    }

    private static IReadOnlyList<string> ListSupportingFiles(
        IReadOnlyList<string> allFiles,
        IReadOnlyList<string> excludedPaths)
    {
        return allFiles
            .Where(file =>
                SupportingFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) &&
                !excludedPaths.Any(excluded => string.Equals(file, excluded, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DiarizationRuntimeStatus? TryReadRuntimeStatus(string assetRootPath)
    {
        var statusPath = Path.Combine(assetRootPath, RuntimeStatusFileName);
        if (!File.Exists(statusPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiarizationRuntimeStatus>(File.ReadAllText(statusPath), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void PrepareDestinationDirectory(string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
    }
}

internal sealed record DiarizationModelBundleManifest(
    string BundleVersion,
    string SegmentationModelFileName,
    string EmbeddingModelFileName);

public sealed record DiarizationAssetInstallStatus(
    string AssetRootPath,
    string? BundleManifestPath,
    string? SegmentationModelPath,
    string? EmbeddingModelPath,
    string? BundleVersion,
    IReadOnlyList<string> SupportingFilePaths,
    bool IsReady,
    string StatusText,
    string DetailsText,
    bool? GpuAccelerationAvailable,
    DiarizationExecutionProvider? EffectiveExecutionProvider,
    string? DiagnosticMessage)
{
    public string? SidecarExecutablePath => string.IsNullOrWhiteSpace(AssetRootPath) || !Directory.Exists(AssetRootPath)
        ? null
        : Directory.EnumerateFiles(AssetRootPath, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
}

public sealed record DiarizationRuntimeStatus(
    [property: JsonPropertyName("gpuAccelerationAvailable")] bool GpuAccelerationAvailable,
    [property: JsonPropertyName("effectiveExecutionProvider")] DiarizationExecutionProvider? EffectiveExecutionProvider,
    [property: JsonPropertyName("diagnosticMessage")] string? DiagnosticMessage,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);
