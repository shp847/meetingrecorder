using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class SessionProcessor
{
    private static readonly IReadOnlyList<SpeakerIdentity> EmptySpeakers = Array.Empty<SpeakerIdentity>();
    private static readonly IReadOnlyList<SpeakerTurn> EmptySpeakerTurns = Array.Empty<SpeakerTurn>();
    private static readonly JsonSerializerOptions TranscriptionSnapshotSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public SessionProcessor(
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        WaveChunkMerger waveChunkMerger,
        ITranscriptionProvider transcriptionProvider,
        IDiarizationProvider diarizationProvider,
        TranscriptRenderer transcriptRenderer,
        FilePublishService publishService)
    {
        ManifestStore = manifestStore;
        PathBuilder = pathBuilder;
        WaveChunkMerger = waveChunkMerger;
        TranscriptionProvider = transcriptionProvider;
        DiarizationProvider = diarizationProvider;
        TranscriptRenderer = transcriptRenderer;
        PublishService = publishService;
    }

    public SessionManifestStore ManifestStore { get; }

    public ArtifactPathBuilder PathBuilder { get; }

    public WaveChunkMerger WaveChunkMerger { get; }

    public ITranscriptionProvider TranscriptionProvider { get; }

    public IDiarizationProvider DiarizationProvider { get; }

    public TranscriptRenderer TranscriptRenderer { get; }

    public FilePublishService PublishService { get; }

    public Task<PublishedArtifactSet> ProcessAsync(
        string manifestPath,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        return ProcessInternalAsync(manifestPath, config, cancellationToken);
    }

    internal static string GetPersistedTranscriptionSnapshotPath(string processingRoot)
    {
        return BuildTranscriptionSnapshotPath(processingRoot, string.Empty);
    }

    internal static Task<TranscriptionResult?> LoadPersistedTranscriptionSnapshotAsync(
        string transcriptionSnapshotPath,
        CancellationToken cancellationToken = default)
    {
        return TryLoadPersistedTranscriptionAsync(transcriptionSnapshotPath, cancellationToken);
    }

    private async Task<PublishedArtifactSet> ProcessInternalAsync(
        string manifestPath,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var manifest = await ManifestStore.LoadAsync(manifestPath, cancellationToken);
        var sessionRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Manifest path must include a session directory.");
        var processingRoot = Path.Combine(sessionRoot, "processing");
        Directory.CreateDirectory(processingRoot);

        var stem = PathBuilder.BuildFileStem(
            manifest.Platform,
            manifest.StartedAtUtc,
            string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle);
        string sourceAudioPath;
        try
        {
            sourceAudioPath = await ResolveSourceAudioPathAsync(manifest, processingRoot, stem, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var now = DateTimeOffset.UtcNow;
            manifest = manifest with
            {
                State = SessionState.Failed,
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Failed, now, exception.Message),
                DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Skipped, now, "Skipped because source audio processing failed."),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Skipped, now, "Skipped because source audio processing failed."),
                ErrorSummary = exception.Message,
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
            throw;
        }

        var transcriptionSnapshotPath = BuildTranscriptionSnapshotPath(processingRoot, stem);
        var persistedTranscription = await TryLoadPersistedTranscriptionAsync(
            transcriptionSnapshotPath,
            cancellationToken);

        var initialTranscriptionStatus = persistedTranscription is null
            ? new ProcessingStageStatus("transcription", StageExecutionState.Queued, DateTimeOffset.UtcNow, null)
            : new ProcessingStageStatus(
                "transcription",
                StageExecutionState.Succeeded,
                DateTimeOffset.UtcNow,
                "Loaded persisted transcript snapshot from a prior attempt.");

        await PublishService.PublishAudioAsync(sourceAudioPath, config.AudioOutputDir, stem, cancellationToken);

        manifest = manifest with
        {
            State = SessionState.Processing,
            MergedAudioPath = sourceAudioPath,
            TranscriptionStatus = initialTranscriptionStatus,
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        TranscriptionResult transcription;
        if (persistedTranscription is not null)
        {
            transcription = persistedTranscription;
        }
        else
        {
            manifest = manifest with
            {
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

            try
            {
                transcription = await TranscriptionProvider.TranscribeAsync(sourceAudioPath, cancellationToken);
                await SavePersistedTranscriptionAsync(transcriptionSnapshotPath, transcription, cancellationToken);
                manifest = manifest with
                {
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, transcription.Message),
                };
            }
            catch (Exception exception)
            {
                manifest = manifest with
                {
                    State = SessionState.Failed,
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Failed, DateTimeOffset.UtcNow, exception.Message),
                    PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Skipped, DateTimeOffset.UtcNow, "Merged audio was published, but transcript artifacts were not generated."),
                    ErrorSummary = exception.Message,
                };
                await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
                throw;
            }

            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
        }

        IReadOnlyList<TranscriptSegment> transcriptSegments = transcription.Segments;
        IReadOnlyList<SpeakerIdentity> speakers = EmptySpeakers;
        IReadOnlyList<SpeakerTurn> speakerTurns = EmptySpeakerTurns;
        DiarizationMetadata? diarizationMetadata = null;
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true)
        {
            manifest = manifest with
            {
                DiarizationStatus = new ProcessingStageStatus(
                    "diarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    "Speaker labeling skipped by processing override."),
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
        }
        else
        {
            try
            {
                manifest = manifest with
                {
                    DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
                };
                await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

                var diarization = await DiarizationProvider.ApplySpeakerLabelsAsync(sourceAudioPath, transcription.Segments, cancellationToken);
                transcriptSegments = diarization.Segments;
                speakers = diarization.Speakers ?? EmptySpeakers;
                speakerTurns = diarization.SpeakerTurns ?? EmptySpeakerTurns;
                diarizationMetadata = diarization.Metadata;
                manifest = manifest with
                {
                    DiarizationStatus = new ProcessingStageStatus(
                        "diarization",
                        diarization.AppliedSpeakerLabels ? StageExecutionState.Succeeded : StageExecutionState.Skipped,
                        DateTimeOffset.UtcNow,
                        diarization.Message),
                };
            }
            catch (Exception exception)
            {
                manifest = manifest with
                {
                    DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Failed, DateTimeOffset.UtcNow, exception.Message),
                };
            }
        }

        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        manifest = manifest with
        {
            ProcessingMetadata = new MeetingProcessingMetadata(
                Path.GetFileName(config.TranscriptionModelPath),
                transcriptSegments.Any(segment =>
                    !string.IsNullOrWhiteSpace(segment.SpeakerId) ||
                    !string.IsNullOrWhiteSpace(segment.SpeakerLabel)),
                speakers,
                speakerTurns,
                diarizationMetadata),
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        var markdownPath = Path.Combine(processingRoot, $"{stem}.md");
        var jsonPath = Path.Combine(processingRoot, $"{stem}.json");
        await File.WriteAllTextAsync(markdownPath, TranscriptRenderer.RenderMarkdown(manifest, transcriptSegments), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(jsonPath, TranscriptRenderer.RenderJson(manifest, transcriptSegments), Encoding.UTF8, cancellationToken);

        try
        {
            manifest = manifest with
            {
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

            var published = await PublishService.PublishAsync(
                sourceAudioPath,
                markdownPath,
                jsonPath,
                config.AudioOutputDir,
                config.TranscriptOutputDir,
                stem,
                cancellationToken);

            manifest = manifest with
            {
                State = SessionState.Published,
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "Artifacts published."),
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
            await PublishedSessionWorkCleanupService.PrunePublishedSessionAsync(
                ManifestStore,
                manifestPath,
                manifest,
                config.AudioOutputDir,
                cancellationToken);

            return published;
        }
        catch (Exception exception)
        {
            manifest = manifest with
            {
                State = SessionState.Failed,
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Failed, DateTimeOffset.UtcNow, exception.Message),
                ErrorSummary = exception.Message,
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
            throw;
        }
    }

    private static string BuildTranscriptionSnapshotPath(string processingRoot, string stem)
    {
        _ = stem;
        return Path.Combine(processingRoot, "transcription.snapshot.json");
    }

    private static async Task<TranscriptionResult?> TryLoadPersistedTranscriptionAsync(
        string transcriptionSnapshotPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(transcriptionSnapshotPath))
        {
            return null;
        }

        try
        {
            var snapshotJson = await File.ReadAllTextAsync(transcriptionSnapshotPath, cancellationToken);
            using var snapshotDocument = JsonDocument.Parse(snapshotJson);
            var root = snapshotDocument.RootElement;
            if (!root.TryGetProperty("segments", out var segmentsElement) || segmentsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var segments = new List<TranscriptSegment>();
            foreach (var segmentElement in segmentsElement.EnumerateArray())
            {
                var startText = segmentElement.GetProperty("start").GetString();
                var endText = segmentElement.GetProperty("end").GetString();
                var text = segmentElement.GetProperty("text").GetString();
                if (string.IsNullOrWhiteSpace(startText) ||
                    string.IsNullOrWhiteSpace(endText) ||
                    string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                segments.Add(new TranscriptSegment(
                    TimeSpan.Parse(startText, CultureInfo.InvariantCulture),
                    TimeSpan.Parse(endText, CultureInfo.InvariantCulture),
                    segmentElement.TryGetProperty("speakerId", out var speakerIdElement) && speakerIdElement.ValueKind != JsonValueKind.Null
                        ? speakerIdElement.GetString()
                        : null,
                    segmentElement.TryGetProperty("speakerLabel", out var speakerLabelElement) && speakerLabelElement.ValueKind != JsonValueKind.Null
                        ? speakerLabelElement.GetString()
                        : null,
                    text));
            }

            var language = root.TryGetProperty("language", out var languageElement) && languageElement.ValueKind != JsonValueKind.Null
                ? languageElement.GetString() ?? string.Empty
                : string.Empty;
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind != JsonValueKind.Null
                ? messageElement.GetString()
                : null;
            return new TranscriptionResult(segments, language, message);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SavePersistedTranscriptionAsync(
        string transcriptionSnapshotPath,
        TranscriptionResult transcription,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptionSnapshotPath)
            ?? throw new InvalidOperationException("The transcription snapshot path must include a directory."));

        await using var stream = File.Create(transcriptionSnapshotPath);
        await JsonSerializer.SerializeAsync(
            stream,
            new PersistedTranscriptionSnapshot(
                transcription.Segments.ToArray(),
                transcription.Language,
                transcription.Message),
            TranscriptionSnapshotSerializerOptions,
            cancellationToken);
    }

    private async Task<string> ResolveSourceAudioPathAsync(
        MeetingSessionManifest manifest,
        string processingRoot,
        string stem,
        CancellationToken cancellationToken)
    {
        if (manifest.RawChunkPaths.Count > 0 || manifest.MicrophoneCaptureSegments.Count > 0)
        {
            var mergedAudioPath = Path.Combine(processingRoot, $"{stem}.wav");
            if (manifest.MicrophoneCaptureSegments.Count > 0)
            {
                await WaveChunkMerger.MergeAsync(
                    manifest.RawChunkPaths,
                    manifest.MicrophoneCaptureSegments,
                    manifest.StartedAtUtc,
                    manifest.EndedAtUtc,
                    mergedAudioPath,
                    cancellationToken);
            }
            else
            {
                await WaveChunkMerger.MergeAsync(
                    manifest.RawChunkPaths,
                    manifest.MicrophoneChunkPaths,
                    mergedAudioPath,
                    cancellationToken);
            }
            return mergedAudioPath;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MergedAudioPath) && File.Exists(manifest.MergedAudioPath))
        {
            return manifest.MergedAudioPath;
        }

        throw new InvalidOperationException(
            "No raw audio chunks were available, and no existing merged audio file could be found for this session.");
    }
}

internal sealed record PersistedTranscriptionSnapshot(
    TranscriptSegment[] Segments,
    string Language,
    string? Message);
