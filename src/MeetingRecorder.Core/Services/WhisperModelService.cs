using Whisper.net.Ggml;

namespace MeetingRecorder.Core.Services;

public interface IWhisperModelDownloader
{
    Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken = default);
}

public sealed class WhisperNetModelDownloader : IWhisperModelDownloader
{
    public Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken = default)
    {
        return WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
    }
}

public sealed class WhisperModelService
{
    public const long MinimumExpectedModelBytes = 1_000_000;

    private readonly IWhisperModelDownloader _downloader;

    public WhisperModelService(IWhisperModelDownloader downloader)
    {
        _downloader = downloader;
    }

    public WhisperModelStatus Inspect(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("A model path is required.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            return new WhisperModelStatus(
                modelPath,
                WhisperModelStatusKind.Missing,
                0L,
                "The configured Whisper model file does not exist.");
        }

        var fileSizeBytes = new FileInfo(modelPath).Length;
        if (fileSizeBytes < MinimumExpectedModelBytes)
        {
            return new WhisperModelStatus(
                modelPath,
                WhisperModelStatusKind.Invalid,
                fileSizeBytes,
                BuildInvalidModelMessage(modelPath, fileSizeBytes));
        }

        return new WhisperModelStatus(
            modelPath,
            WhisperModelStatusKind.Valid,
            fileSizeBytes,
            "The configured Whisper model file looks valid.");
    }

    public async Task<WhisperModelInstallResult> DownloadBaseModelAsync(
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("A model path is required.", nameof(modelPath));
        }

        var modelDirectory = Path.GetDirectoryName(modelPath)
            ?? throw new InvalidOperationException("Model path must include a directory.");
        Directory.CreateDirectory(modelDirectory);

        var tempPath = Path.Combine(modelDirectory, $"{Path.GetFileName(modelPath)}.download");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            await using var downloadedStream = await _downloader.DownloadBaseModelAsync(cancellationToken);
            await using (var fileStream = File.Create(tempPath))
            {
                await downloadedStream.CopyToAsync(fileStream, cancellationToken);
            }

            var status = Inspect(tempPath);
            EnsureValid(status);

            File.Move(tempPath, modelPath, overwrite: true);
            return new WhisperModelInstallResult(modelPath, status.FileSizeBytes, "Downloaded the Whisper base model.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task<WhisperModelInstallResult> ImportModelAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source model path is required.", nameof(sourcePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected model file does not exist.", sourcePath);
        }

        var sourceStatus = Inspect(sourcePath);
        EnsureValid(sourceStatus);

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Model path must include a directory.");
        Directory.CreateDirectory(targetDirectory);

        await using (var sourceStream = File.OpenRead(sourcePath))
        await using (var destinationStream = File.Create(targetPath))
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        return new WhisperModelInstallResult(targetPath, sourceStatus.FileSizeBytes, "Imported the selected Whisper model file.");
    }

    public static void EnsureValid(WhisperModelStatus status)
    {
        if (status.Kind == WhisperModelStatusKind.Valid)
        {
            return;
        }

        throw new InvalidOperationException(status.Message);
    }

    public static string BuildInvalidModelMessage(string modelPath, long modelLength)
    {
        return
            $"The Whisper model at '{modelPath}' is only {modelLength} bytes and is not a valid ggml model. " +
            "This usually means the download was blocked or replaced with an HTML/error response. " +
            "Replace it with a valid ggml-base.bin file or update transcriptionModelPath.";
    }
}

public enum WhisperModelStatusKind
{
    Missing = 0,
    Invalid = 1,
    Valid = 2,
}

public sealed record WhisperModelStatus(
    string ModelPath,
    WhisperModelStatusKind Kind,
    long FileSizeBytes,
    string Message);

public sealed record WhisperModelInstallResult(
    string ModelPath,
    long FileSizeBytes,
    string Message);
