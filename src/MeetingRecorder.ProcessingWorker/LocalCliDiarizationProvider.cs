using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class LocalCliDiarizationProvider : IDiarizationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _executablePath;
    private readonly string _argumentsTemplate;
    private readonly FileLogWriter _logger;
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public LocalCliDiarizationProvider(
        string executablePath,
        string argumentsTemplate,
        FileLogWriter logger)
        : this(executablePath, argumentsTemplate, logger, new TranscriptionAudioPreparer())
    {
    }

    internal LocalCliDiarizationProvider(
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

    public async Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken)
    {
        var preparedAudioPath = BuildPreparedAudioPath(audioPath);
        var transcriptPath = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderDiarization",
            $"transcript-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);

        try
        {
            await _audioPreparer.PrepareAsync(audioPath, preparedAudioPath, cancellationToken);
            await File.WriteAllTextAsync(
                transcriptPath,
                JsonSerializer.Serialize(transcriptSegments, JsonOptions),
                cancellationToken);

            var arguments = CliProviderProcess.BuildArguments(
                string.IsNullOrWhiteSpace(_argumentsTemplate)
                    ? "--audio {audioPath} --transcript {transcriptPath}"
                    : _argumentsTemplate,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["audio"] = preparedAudioPath,
                    ["audioPath"] = preparedAudioPath,
                    ["transcript"] = transcriptPath,
                    ["transcriptPath"] = transcriptPath,
                });

            _logger.Log($"Starting local CLI diarization provider '{_executablePath}'.");
            var result = await CliProviderProcess.RunAsync(_executablePath, arguments, cancellationToken);
            if (result.ExitCode != 0)
            {
                _logger.Log($"Local CLI diarization provider failed. ExitCode={result.ExitCode}. StandardError={result.StandardError}");
                throw new InvalidOperationException("Local CLI diarization provider failed.");
            }

            CliDiarizationResultDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<CliDiarizationResultDto>(
                    result.StandardOutput,
                    JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Local CLI diarization provider returned invalid JSON.", exception);
            }
            if (dto is null)
            {
                throw new InvalidOperationException("Local CLI diarization provider returned invalid JSON.");
            }

            var segments = dto.Segments?.Select(segment => segment.ToTranscriptSegment()).ToArray()
                ?? Array.Empty<TranscriptSegment>();
            var diarization = new DiarizationResult(
                segments,
                dto.AppliedSpeakerLabels,
                dto.Message,
                dto.Speakers,
                dto.SpeakerTurns,
                dto.Metadata,
                dto.SpeakerVoiceSamples);

            if (diarization.Segments.Count == 0)
            {
                diarization = diarization with { Segments = transcriptSegments };
            }

            if (diarization.AppliedSpeakerLabels &&
                (diarization.Speakers is null ||
                 diarization.Speakers.Count == 0 ||
                 diarization.SpeakerTurns is null ||
                 diarization.SpeakerTurns.Count == 0 ||
                 diarization.Metadata is null))
            {
                throw new InvalidOperationException("Local CLI diarization provider returned labels without required speaker metadata.");
            }

            if (!diarization.AppliedSpeakerLabels &&
                string.IsNullOrWhiteSpace(diarization.Message) &&
                string.IsNullOrWhiteSpace(diarization.Metadata?.DiagnosticMessage))
            {
                throw new InvalidOperationException("Local CLI diarization provider skipped labels without a diagnostic reason.");
            }

            var diagnostics = diarization.Metadata?.DiagnosticMessage ?? diarization.Message ?? string.Empty;
            _logger.Log(
                "Local CLI diarization provider completed. " +
                $"AppliedSpeakerLabels={diarization.AppliedSpeakerLabels}. " +
                $"Speakers={diarization.Speakers?.Count ?? 0}. " +
                $"Turns={diarization.SpeakerTurns?.Count ?? 0}. " +
                $"Diagnostics={diagnostics}");
            return diarization;
        }
        finally
        {
            if (File.Exists(preparedAudioPath))
            {
                File.Delete(preparedAudioPath);
            }

            if (File.Exists(transcriptPath))
            {
                File.Delete(transcriptPath);
            }
        }
    }

    private static string BuildPreparedAudioPath(string audioPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization");
        Directory.CreateDirectory(tempDirectory);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(audioPath);
        return Path.Combine(tempDirectory, $"{fileNameWithoutExtension}-cli-{Guid.NewGuid():N}.wav");
    }

    private sealed record CliDiarizationResultDto
    {
        public IReadOnlyList<CliTranscriptSegmentDto>? Segments { get; init; }

        public bool AppliedSpeakerLabels { get; init; }

        public string? Message { get; init; }

        public IReadOnlyList<SpeakerIdentity>? Speakers { get; init; }

        public IReadOnlyList<SpeakerTurn>? SpeakerTurns { get; init; }

        public DiarizationMetadata? Metadata { get; init; }

        public IReadOnlyList<SpeakerVoiceSample>? SpeakerVoiceSamples { get; init; }
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
