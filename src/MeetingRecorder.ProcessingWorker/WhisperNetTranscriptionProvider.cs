using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using Whisper.net;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class WhisperNetTranscriptionProvider : ITranscriptionProvider
{
    private const string AutoLanguage = "auto";
    private const string EnglishLanguage = "en";
    private const double MinimumDurationForSparseRetrySeconds = 120d;
    private const double SparseTranscriptCharactersPerMinuteThreshold = 120d;
    private const double ActiveAudioRatioThreshold = 0.20d;
    private const double EnglishFallbackImprovementRatio = 1.5d;
    private const int EnglishFallbackMinimumAdditionalCharacters = 500;
    private const float ActiveSampleThreshold = 500f / short.MaxValue;
    private readonly string _modelPath;
    private readonly int _threadCount;
    private readonly FileLogWriter _logger;
    private readonly WhisperModelService _modelService;
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public WhisperNetTranscriptionProvider(string modelPath, int threadCount, FileLogWriter logger)
    {
        _modelPath = modelPath;
        _threadCount = Math.Max(1, threadCount);
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
            var audioActivity = AnalyzeAudioActivity(preparedAudioPath);
            var segments = await TranscribePreparedAudioAsync(
                whisperFactory,
                preparedAudioPath,
                AutoLanguage,
                cancellationToken);

            var language = AutoLanguage;
            var message = "Whisper transcription completed.";
            if (ShouldRetryWithEnglishFallback(segments, audioActivity))
            {
                _logger.Log(
                    "Auto-language transcription looked sparse for active audio. " +
                    $"Segments={segments.Count}. TextCharacters={CountTextCharacters(segments)}. " +
                    $"DurationSeconds={audioActivity.Duration.TotalSeconds:F1}. ActiveRatio={audioActivity.ActiveRatio:F2}. " +
                    "Retrying with English language hint.");
                var englishSegments = await TranscribePreparedAudioAsync(
                    whisperFactory,
                    preparedAudioPath,
                    EnglishLanguage,
                    cancellationToken);
                if (IsMeaningfullyRicher(englishSegments, segments))
                {
                    segments = englishSegments;
                    language = EnglishLanguage;
                    message = "Whisper transcription completed with English fallback after sparse auto-language output.";
                    _logger.Log(
                        "English fallback produced a richer transcript. " +
                        $"TextCharacters={CountTextCharacters(segments)}.");
                }
                else
                {
                    message = "Whisper transcription completed, but the transcript appears sparse for active audio even after English fallback.";
                    _logger.Log(
                        "English fallback did not materially improve the transcript. " +
                        $"AutoTextCharacters={CountTextCharacters(segments)}. " +
                        $"EnglishTextCharacters={CountTextCharacters(englishSegments)}.");
                }
            }

            _logger.Log($"Transcription completed with {segments.Count} segments.");
            return new TranscriptionResult(segments, language, message);
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

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribePreparedAudioAsync(
        WhisperFactory whisperFactory,
        string preparedAudioPath,
        string language,
        CancellationToken cancellationToken)
    {
        using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage(language)
            .WithThreads(_threadCount)
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

        return segments;
    }

    private static bool ShouldRetryWithEnglishFallback(
        IReadOnlyList<TranscriptSegment> segments,
        AudioActivitySummary audioActivity)
    {
        if (audioActivity.Duration.TotalSeconds < MinimumDurationForSparseRetrySeconds ||
            audioActivity.ActiveRatio < ActiveAudioRatioThreshold)
        {
            return false;
        }

        var charactersPerMinute = CountTextCharacters(segments) / Math.Max(1d, audioActivity.Duration.TotalMinutes);
        return charactersPerMinute < SparseTranscriptCharactersPerMinuteThreshold;
    }

    private static bool IsMeaningfullyRicher(
        IReadOnlyList<TranscriptSegment> candidate,
        IReadOnlyList<TranscriptSegment> baseline)
    {
        var candidateCharacters = CountTextCharacters(candidate);
        var baselineCharacters = CountTextCharacters(baseline);
        return candidateCharacters >= baselineCharacters + EnglishFallbackMinimumAdditionalCharacters &&
               candidateCharacters >= baselineCharacters * EnglishFallbackImprovementRatio;
    }

    private static int CountTextCharacters(IReadOnlyList<TranscriptSegment> segments)
    {
        return segments.Sum(segment => string.IsNullOrWhiteSpace(segment.Text) ? 0 : segment.Text.Length);
    }

    private static AudioActivitySummary AnalyzeAudioActivity(string preparedAudioPath)
    {
        using var reader = new AudioFileReader(preparedAudioPath);
        var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        long activeSamples = 0;
        long totalSamples = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalSamples += read;
            for (var index = 0; index < read; index++)
            {
                if (Math.Abs(buffer[index]) >= ActiveSampleThreshold)
                {
                    activeSamples++;
                }
            }
        }

        var activeRatio = totalSamples == 0
            ? 0d
            : activeSamples / (double)totalSamples;
        return new AudioActivitySummary(reader.TotalTime, activeRatio);
    }

    private sealed record AudioActivitySummary(TimeSpan Duration, double ActiveRatio);

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
