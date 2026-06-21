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
    private static readonly JsonSerializerOptions SummarySnapshotSerializerOptions = new()
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
        FilePublishService publishService,
        IMeetingSummarizationProvider? summarizationProvider = null)
    {
        ManifestStore = manifestStore;
        PathBuilder = pathBuilder;
        WaveChunkMerger = waveChunkMerger;
        TranscriptionProvider = transcriptionProvider;
        DiarizationProvider = diarizationProvider;
        TranscriptRenderer = transcriptRenderer;
        PublishService = publishService;
        SummarizationProvider = summarizationProvider ?? NoOpMeetingSummarizationProvider.Instance;
    }

    public SessionManifestStore ManifestStore { get; }

    public ArtifactPathBuilder PathBuilder { get; }

    public WaveChunkMerger WaveChunkMerger { get; }

    public ITranscriptionProvider TranscriptionProvider { get; }

    public IDiarizationProvider DiarizationProvider { get; }

    public TranscriptRenderer TranscriptRenderer { get; }

    public FilePublishService PublishService { get; }

    public IMeetingSummarizationProvider SummarizationProvider { get; }

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

    internal static string GetPersistedSummarySnapshotPath(string processingRoot)
    {
        return BuildSummarySnapshotPath(processingRoot);
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
                SummarizationStatus = new ProcessingStageStatus("summarization", StageExecutionState.Skipped, now, "Skipped because source audio processing failed."),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Skipped, now, "Skipped because source audio processing failed."),
                ErrorSummary = exception.Message,
            };
            await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
            throw;
        }

        var transcriptionSnapshotPath = BuildTranscriptionSnapshotPath(processingRoot, stem);
        var forceTranscription = manifest.ProcessingOverrides?.ForceTranscription == true;
        var persistedTranscription = forceTranscription
            ? null
            : await TryLoadPersistedTranscriptionAsync(
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
            SummarizationStatus = new ProcessingStageStatus("summarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            Summary = null,
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
                await SavePersistedTranscriptionSnapshotAsync(transcriptionSnapshotPath, transcription, cancellationToken);
                manifest = manifest with
                {
                    ProcessingOverrides = ClearForceTranscriptionOverride(manifest.ProcessingOverrides),
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, transcription.Message),
                };
            }
            catch (Exception exception)
            {
                manifest = manifest with
                {
                    State = SessionState.Failed,
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Failed, DateTimeOffset.UtcNow, exception.Message),
                    SummarizationStatus = new ProcessingStageStatus("summarization", StageExecutionState.Skipped, DateTimeOffset.UtcNow, "Skipped because transcription failed."),
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
        IReadOnlyList<SpeakerVoiceSample> speakerVoiceSamples = Array.Empty<SpeakerVoiceSample>();
        DiarizationMetadata? diarizationMetadata = null;
        if (manifest.ProcessingOverrides?.SkipSpeakerLabeling == true ||
            BackgroundProcessingPolicy.IsTranscriptOnlyDrainActive(config))
        {
            var skipMessage = manifest.ProcessingOverrides?.SkipSpeakerLabeling == true
                ? "Speaker labeling skipped by processing override."
                : "Speaker labeling skipped by processing speed profile.";
            manifest = manifest with
            {
                DiarizationStatus = new ProcessingStageStatus(
                    "diarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    skipMessage),
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

                using var diarizationTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                diarizationTimeoutCts.CancelAfter(BackgroundProcessingPolicy.DiarizationTimeout);
                var diarization = await DiarizationProvider.ApplySpeakerLabelsAsync(
                    sourceAudioPath,
                    transcription.Segments,
                    diarizationTimeoutCts.Token);
                transcriptSegments = diarization.Segments;
                speakers = diarization.Speakers ?? EmptySpeakers;
                speakerTurns = diarization.SpeakerTurns ?? EmptySpeakerTurns;
                speakerVoiceSamples = diarization.SpeakerVoiceSamples ?? Array.Empty<SpeakerVoiceSample>();
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                manifest = manifest with
                {
                    DiarizationStatus = new ProcessingStageStatus(
                        "diarization",
                        StageExecutionState.Skipped,
                        DateTimeOffset.UtcNow,
                        $"Speaker labeling skipped after the {BackgroundProcessingPolicy.DiarizationTimeout.TotalMinutes:0}-minute optional processing limit."),
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
                diarizationMetadata,
                speakerVoiceSamples),
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        manifest = await ApplySummarizationAsync(
            manifest,
            manifestPath,
            processingRoot,
            transcriptSegments,
            config,
            cancellationToken);

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

    private static string BuildSummarySnapshotPath(string processingRoot)
    {
        return Path.Combine(processingRoot, "summary.snapshot.json");
    }

    private async Task<MeetingSessionManifest> ApplySummarizationAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        string processingRoot,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        if (transcriptSegments.Count == 0)
        {
            var skipped = manifest with
            {
                SummarizationStatus = new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    "Summary generation skipped because no transcript segments were available."),
                Summary = null,
            };
            await ManifestStore.SaveAsync(skipped, manifestPath, cancellationToken);
            return skipped;
        }

        if (config.SummaryGenerationMode != MeetingSummaryGenerationMode.Enabled ||
            BackgroundProcessingPolicy.ShouldSkipSummarizationInPrimaryPass(config))
        {
            var message = BackgroundProcessingPolicy.ShouldSkipSummarizationInPrimaryPass(config)
                ? "Summary generation skipped by processing speed profile."
                : "Summary generation disabled.";
            var skipped = manifest with
            {
                SummarizationStatus = new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    message),
                Summary = null,
            };
            await ManifestStore.SaveAsync(skipped, manifestPath, cancellationToken);
            return skipped;
        }

        var fingerprint = MeetingSummaryTranscriptFingerprint.Compute(transcriptSegments);
        var summarySnapshotPath = BuildSummarySnapshotPath(processingRoot);
        var persistedSummary = await TryLoadPersistedSummaryAsync(
            summarySnapshotPath,
            fingerprint,
            cancellationToken);
        if (persistedSummary is not null)
        {
            var loaded = manifest with
            {
                SummarizationStatus = new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Succeeded,
                    DateTimeOffset.UtcNow,
                    "Loaded persisted summary snapshot from a prior attempt."),
                Summary = persistedSummary,
            };
            await ManifestStore.SaveAsync(loaded, manifestPath, cancellationToken);
            return loaded;
        }

        manifest = manifest with
        {
            SummarizationStatus = new ProcessingStageStatus("summarization", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
            Summary = null,
        };
        await ManifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

        MeetingSummaryResult result;
        try
        {
            result = await SummarizationProvider.SummarizeAsync(
                new MeetingSummaryRequest(manifest, transcriptSegments, config),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new MeetingSummaryResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Failed,
                    DateTimeOffset.UtcNow,
                    BuildSafeUnexpectedSummaryFailureMessage(exception)),
                null);
        }

        var status = NormalizeSummaryStatus(result.Status);
        var summary = status.State == StageExecutionState.Succeeded
            ? result.Summary
            : null;
        if (status.State == StageExecutionState.Succeeded && summary is null)
        {
            status = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Failed,
                DateTimeOffset.UtcNow,
                "Summary provider reported success without summary content.");
        }

        if (status.State == StageExecutionState.Succeeded && summary is not null)
        {
            summary = summary.TranscriptFingerprint == fingerprint
                ? summary
                : summary with
                {
                    TranscriptFingerprint = fingerprint,
                };
            await SavePersistedSummaryAsync(summarySnapshotPath, summary, cancellationToken);
        }

        var updated = manifest with
        {
            SummarizationStatus = status,
            Summary = status.State == StageExecutionState.Succeeded ? summary : null,
        };
        await ManifestStore.SaveAsync(updated, manifestPath, cancellationToken);
        return updated;
    }

    private static ProcessingStageStatus NormalizeSummaryStatus(ProcessingStageStatus status)
    {
        return status with
        {
            StageName = "summarization",
        };
    }

    private static string BuildSafeUnexpectedSummaryFailureMessage(Exception exception)
    {
        return exception is HttpRequestException or TimeoutException
            ? exception.Message
            : "Summary generation failed before a usable summary was returned.";
    }

    private static async Task<MeetingSummary?> TryLoadPersistedSummaryAsync(
        string summarySnapshotPath,
        string expectedFingerprint,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(summarySnapshotPath))
        {
            return null;
        }

        try
        {
            var snapshotJson = await File.ReadAllTextAsync(summarySnapshotPath, cancellationToken);
            var summary = JsonSerializer.Deserialize<MeetingSummary>(
                snapshotJson,
                SummarySnapshotSerializerOptions);
            return string.Equals(summary?.TranscriptFingerprint, expectedFingerprint, StringComparison.Ordinal)
                ? summary
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SavePersistedSummaryAsync(
        string summarySnapshotPath,
        MeetingSummary summary,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(summarySnapshotPath)
            ?? throw new InvalidOperationException("The summary snapshot path must include a directory."));

        await using var stream = File.Create(summarySnapshotPath);
        await JsonSerializer.SerializeAsync(
            stream,
            summary,
            SummarySnapshotSerializerOptions,
            cancellationToken);
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

    internal static async Task SavePersistedTranscriptionSnapshotAsync(
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

    private static MeetingProcessingOverrides? ClearForceTranscriptionOverride(MeetingProcessingOverrides? overrides)
    {
        return overrides?.ForceTranscription == true
            ? overrides with { ForceTranscription = false }
            : overrides;
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
