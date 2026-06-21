using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class LocalCliTranscriptionProvider : ITranscriptionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _executablePath;
    private readonly string _argumentsTemplate;
    private readonly FileLogWriter _logger;
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public LocalCliTranscriptionProvider(
        string executablePath,
        string argumentsTemplate,
        FileLogWriter logger)
        : this(executablePath, argumentsTemplate, logger, new TranscriptionAudioPreparer())
    {
    }

    internal LocalCliTranscriptionProvider(
        string executablePath,
        string argumentsTemplate,
        FileLogWriter logger,
        TranscriptionAudioPreparer audioPreparer)
    {
        _executablePath = executablePath;
        _argumentsTemplate = argumentsTemplate;
        _logger = logger;
        _audioPreparer = audioPreparer;
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
    {
        var preparedAudioPath = BuildPreparedAudioPath(audioPath);
        try
        {
            await _audioPreparer.PrepareAsync(audioPath, preparedAudioPath, cancellationToken);
            var arguments = CliProviderProcess.BuildArguments(
                string.IsNullOrWhiteSpace(_argumentsTemplate) ? "--audio {audioPath}" : _argumentsTemplate,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["audio"] = preparedAudioPath,
                    ["audioPath"] = preparedAudioPath,
                });

            _logger.Log($"Starting local CLI transcription provider '{_executablePath}'.");
            var result = await CliProviderProcess.RunAsync(_executablePath, arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                _logger.Log($"Local CLI transcription provider failed. ExitCode={result.ExitCode}. StandardError={result.StandardError}");
                throw new InvalidOperationException("Local CLI transcription provider failed.");
            }

            CliTranscriptionResultDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<CliTranscriptionResultDto>(
                    result.StandardOutput,
                    JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Local CLI transcription provider returned invalid JSON.", exception);
            }
            if (dto is null || dto.Segments is null || dto.Segments.Count == 0)
            {
                throw new InvalidOperationException("Local CLI transcription provider returned no transcript segments.");
            }

            if (string.IsNullOrWhiteSpace(dto.Language))
            {
                throw new InvalidOperationException("Local CLI transcription provider returned no language.");
            }

            var segments = dto.Segments.Select(segment => segment.ToTranscriptSegment()).ToArray();
            _logger.Log($"Local CLI transcription provider completed with {segments.Length} segments.");
            return new TranscriptionResult(
                segments,
                dto.Language,
                dto.Message ?? "Local CLI transcription completed.");
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
        return Path.Combine(tempDirectory, $"{fileNameWithoutExtension}-cli-{Guid.NewGuid():N}.wav");
    }

    private sealed record CliTranscriptionResultDto
    {
        public IReadOnlyList<CliTranscriptSegmentDto>? Segments { get; init; }

        public string Language { get; init; } = string.Empty;

        public string? Message { get; init; }
    }

    private sealed record CliTranscriptSegmentDto
    {
        public TimeSpan Start { get; init; }

        public TimeSpan End { get; init; }

        public string? SpeakerId { get; init; }

        public string? SpeakerLabel { get; init; }

        public string Text { get; init; } = string.Empty;

        public TranscriptSegment ToTranscriptSegment()
        {
            return new TranscriptSegment(Start, End, SpeakerId, SpeakerLabel, Text);
        }
    }
}
