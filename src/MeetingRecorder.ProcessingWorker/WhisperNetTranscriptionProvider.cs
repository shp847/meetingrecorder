using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using Whisper.net;
using Whisper.net.Ggml;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class WhisperNetTranscriptionProvider : ITranscriptionProvider
{
    private const long MinimumExpectedModelBytes = 1_000_000;
    private readonly string _modelPath;
    private readonly FileLogWriter _logger;

    public WhisperNetTranscriptionProvider(string modelPath, FileLogWriter logger)
    {
        _modelPath = modelPath;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
    {
        await EnsureModelAsync(cancellationToken);

        var audioLength = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0L;
        var modelLength = File.Exists(_modelPath) ? new FileInfo(_modelPath).Length : 0L;
        _logger.Log($"Starting transcription for '{audioPath}'. AudioBytes={audioLength}. ModelPath='{_modelPath}'. ModelBytes={modelLength}.");

        using var whisperFactory = WhisperFactory.FromPath(_modelPath);
        using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .Build();

        await using var audioStream = File.OpenRead(audioPath);
        var segments = new List<TranscriptSegment>();

        await foreach (var result in processor.ProcessAsync(audioStream).WithCancellation(cancellationToken))
        {
            var text = result.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            segments.Add(new TranscriptSegment(result.Start, result.End, null, text));
        }

        _logger.Log($"Transcription completed with {segments.Count} segments.");
        return new TranscriptionResult(segments, "auto", "Whisper transcription completed.");
    }

    private async Task EnsureModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_modelPath))
        {
            var modelLength = new FileInfo(_modelPath).Length;
            EnsureModelLooksValid(modelLength);
            _logger.Log($"Using existing Whisper model at '{_modelPath}' ({modelLength} bytes).");
            return;
        }

        var modelDirectory = Path.GetDirectoryName(_modelPath)
            ?? throw new InvalidOperationException("Model path must include a directory.");
        Directory.CreateDirectory(modelDirectory);

        _logger.Log($"Transcription model '{_modelPath}' was not found. Attempting first-run download.");

        try
        {
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
            await using var fileStream = File.Create(_modelPath);
            await modelStream.CopyToAsync(fileStream, cancellationToken);
            var downloadedLength = new FileInfo(_modelPath).Length;
            EnsureModelLooksValid(downloadedLength);
            _logger.Log($"Downloaded Whisper model to '{_modelPath}' ({downloadedLength} bytes).");
        }
        catch (Exception exception)
        {
            _logger.Log($"Model download failed: {exception.Message}");
            if (exception is InvalidOperationException)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"The transcription model was not found and automatic download failed. Place a Whisper model at '{_modelPath}' or update the configured transcriptionModelPath.",
                exception);
        }
    }

    private void EnsureModelLooksValid(long modelLength)
    {
        if (modelLength >= MinimumExpectedModelBytes)
        {
            return;
        }

        var message =
            $"The Whisper model at '{_modelPath}' is only {modelLength} bytes and is not a valid ggml model. " +
            "This usually means the download was blocked or replaced with an HTML/error response. " +
            "Replace it with a valid ggml-base.bin file or update transcriptionModelPath.";
        _logger.Log(message);
        throw new InvalidOperationException(message);
    }
}
