using System.IO.Compression;

namespace MeetingRecorder.Core.Services;

public sealed class DiarizationAssetCatalogService
{
    private static readonly string[] SupportingFileExtensions =
    [
        ".onnx",
        ".bin",
        ".json",
        ".yaml",
        ".yml",
        ".txt",
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
                Array.Empty<string>(),
                false,
                "No diarization asset folder is configured.",
                "Choose a diarization sidecar bundle or set the diarization asset path in Config.");
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                null,
                Array.Empty<string>(),
                false,
                "Diarization sidecar not installed.",
                $"The configured folder '{normalizedRoot}' does not exist yet.");
        }

        var allFiles = Directory.GetFiles(normalizedRoot, "*", SearchOption.AllDirectories);
        var sidecarExecutablePath = allFiles.FirstOrDefault(file =>
            string.Equals(
                Path.GetFileName(file),
                "MeetingRecorder.Diarization.Sidecar.exe",
                StringComparison.OrdinalIgnoreCase));

        var supportingFiles = allFiles
            .Where(file =>
                !string.Equals(file, sidecarExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                SupportingFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(sidecarExecutablePath))
        {
            return new DiarizationAssetInstallStatus(
                normalizedRoot,
                null,
                supportingFiles,
                false,
                "Diarization sidecar not installed.",
                $"No 'MeetingRecorder.Diarization.Sidecar.exe' was found under '{normalizedRoot}'.");
        }

        var supportingFileSummary = supportingFiles.Length == 0
            ? "No extra model/config files were detected in the folder."
            : $"Supporting files detected: {supportingFiles.Length}.";
        return new DiarizationAssetInstallStatus(
            normalizedRoot,
            sidecarExecutablePath,
            supportingFiles,
            true,
            "Diarization sidecar looks ready.",
            $"{supportingFileSummary} Speaker labels will be applied when the sidecar runs successfully.");
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
        Directory.CreateDirectory(managedDirectory);

        var sourceExtension = Path.GetExtension(sourcePath);
        if (sourceExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractArchiveIntoDirectoryAsync(sourcePath, managedDirectory, cancellationToken);
        }
        else
        {
            var destinationPath = Path.Combine(managedDirectory, Path.GetFileName(sourcePath));
            await using var sourceStream = File.OpenRead(sourcePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        return InspectInstalledAssets(managedDirectory);
    }

    public string GetManagedAssetDirectory(string modelCacheDir)
    {
        if (string.IsNullOrWhiteSpace(modelCacheDir))
        {
            throw new ArgumentException("A model cache directory is required.", nameof(modelCacheDir));
        }

        return Path.Combine(modelCacheDir, "diarization");
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
}

public sealed record DiarizationAssetInstallStatus(
    string AssetRootPath,
    string? SidecarExecutablePath,
    IReadOnlyList<string> SupportingFilePaths,
    bool IsReady,
    string StatusText,
    string DetailsText);
