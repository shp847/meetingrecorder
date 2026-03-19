using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using Whisper.net;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class WhisperNetTranscriptionProvider : ITranscriptionProvider
{
    private readonly string _modelPath;
    private readonly FileLogWriter _logger;
    private readonly WhisperModelService _modelService;
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public WhisperNetTranscriptionProvider(string modelPath, FileLogWriter logger)
    {
        _modelPath = modelPath;
        _logger = logger;
        _modelService = new WhisperModelService(new WhisperNetModelDownloader());
        _audioPreparer = new TranscriptionAudioPreparer();
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
    {
        await EnsureModelAsync(cancellationToken);

        var audioLength = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0L;
        var modelLength = File.Exists(_modelPath) ? new FileInfo(_modelPath).Length : 0L;
        _logger.Log($"Starting transcription for '{audioPath}'. AudioBytes={audioLength}. ModelPath='{_modelPath}'. ModelBytes={modelLength}.");

        var preparedAudioPath = BuildPreparedAudioPath(audioPath);
        try
        {
            await _audioPreparer.PrepareAsync(audioPath, preparedAudioPath, cancellationToken);
            var preparedAudioLength = File.Exists(preparedAudioPath) ? new FileInfo(preparedAudioPath).Length : 0L;
            _logger.Log(
                $"Prepared transcription audio at '{preparedAudioPath}'. " +
                $"Format=PCM {TranscriptionAudioPreparer.WhisperBitsPerSample}-bit, " +
                $"{TranscriptionAudioPreparer.WhisperSampleRate}Hz, {TranscriptionAudioPreparer.WhisperChannelCount} channel(s). " +
                $"Bytes={preparedAudioLength}.");

            using var whisperFactory = WhisperFactory.FromPath(_modelPath);
            using var processor = whisperFactory
                .CreateBuilder()
                .WithLanguage("auto")
                .Build();

            await using var audioStream = File.OpenRead(preparedAudioPath);
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
        finally
        {
            if (File.Exists(preparedAudioPath))
            {
                File.Delete(preparedAudioPath);
            }
        }
    }

    private static string BuildPreparedAudioPath(string audioPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderTranscription");
        Directory.CreateDirectory(tempDirectory);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(audioPath);
        return Path.Combine(tempDirectory, $"{fileNameWithoutExtension}-{Guid.NewGuid():N}.wav");
    }

    private async Task EnsureModelAsync(CancellationToken cancellationToken)
    {
        var status = _modelService.Inspect(_modelPath);
        if (status.Kind == WhisperModelStatusKind.Valid)
        {
            _logger.Log($"Using existing Whisper model at '{_modelPath}' ({status.FileSizeBytes} bytes).");
            return;
        }

        if (status.Kind == WhisperModelStatusKind.Invalid)
        {
            _logger.Log(status.Message);
            throw new InvalidOperationException(status.Message);
        }

        _logger.Log($"Transcription model '{_modelPath}' was not found. Attempting first-run download.");

        try
        {
            var installed = await _modelService.DownloadBaseModelAsync(_modelPath, cancellationToken);
            _logger.Log($"Downloaded Whisper model to '{installed.ModelPath}' ({installed.FileSizeBytes} bytes).");
        }
        catch (Exception exception)
        {
            _logger.Log($"Model download failed: {exception.Message}");
            throw new InvalidOperationException(
                $"The transcription model was not found and automatic download failed. Place a Whisper model at '{_modelPath}' or update the configured transcriptionModelPath.",
                exception);
        }
    }
}
