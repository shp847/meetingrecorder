using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using System.Text;

namespace MeetingRecorder.Core.Services;

public sealed class SessionProcessor
{
    private static readonly IReadOnlyList<SpeakerIdentity> EmptySpeakers = Array.Empty<SpeakerIdentity>();
    private static readonly IReadOnlyList<SpeakerTurn> EmptySpeakerTurns = Array.Empty<SpeakerTurn>();

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

        manifest = manifest with
        {
            State = SessionState.Processing,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Queued, DateTimeOffset.UtcNow, null),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        var stem = PathBuilder.BuildFileStem(
            manifest.Platform,
            manifest.StartedAtUtc,
            string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle);
        var sourceAudioPath = await ResolveSourceAudioPathAsync(manifest, processingRoot, stem, cancellationToken);
        await PublishService.PublishAudioAsync(sourceAudioPath, config.AudioOutputDir, stem, cancellationToken);

        manifest = manifest with
        {
            MergedAudioPath = sourceAudioPath,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        TranscriptionResult transcription;
        try
        {
            transcription = await TranscriptionProvider.TranscribeAsync(sourceAudioPath, cancellationToken);
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

        IReadOnlyList<TranscriptSegment> transcriptSegments = transcription.Segments;
        IReadOnlyList<SpeakerIdentity> speakers = EmptySpeakers;
        IReadOnlyList<SpeakerTurn> speakerTurns = EmptySpeakerTurns;
        DiarizationMetadata? diarizationMetadata = null;
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

    private async Task<string> ResolveSourceAudioPathAsync(
        MeetingSessionManifest manifest,
        string processingRoot,
        string stem,
        CancellationToken cancellationToken)
    {
        if (manifest.RawChunkPaths.Count > 0 || manifest.MicrophoneChunkPaths.Count > 0)
        {
            var mergedAudioPath = Path.Combine(processingRoot, $"{stem}.wav");
            await WaveChunkMerger.MergeAsync(
                manifest.RawChunkPaths,
                manifest.MicrophoneChunkPaths,
                mergedAudioPath,
                cancellationToken);
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
