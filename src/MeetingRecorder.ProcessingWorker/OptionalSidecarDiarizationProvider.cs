using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using SherpaOnnx;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class LocalSpeakerDiarizationProvider : IDiarizationProvider
{
    private const string DirectMlUnavailableMessage =
        "DirectML acceleration is not packaged in this managed build to avoid endpoint-protection process-memory prompts.";
    private static readonly float[] AdaptiveClusteringThresholds = [0.5f, 0.45f, 0.4f, 0.35f, 0.3f, 0.25f];

    private readonly string _diarizationAssetPath;
    private readonly InferenceAccelerationPreference _accelerationPreference;
    private readonly SpeakerNameLearningMode _speakerNameLearningMode;
    private readonly SpeakerNameRecognitionOptions _speakerNameRecognitionOptions;
    private readonly int _threadCount;
    private readonly FileLogWriter _logger;
    private readonly DiarizationAssetCatalogService _assetCatalogService;
    private readonly TranscriptionAudioPreparer _audioPreparer;
    private readonly TranscriptSpeakerAttributionService _speakerAttributionService;
    private readonly DiarizationClusterSelectionService _clusterSelectionService;
    private readonly SherpaSpeakerEmbeddingService _speakerEmbeddingService;
    private readonly VoiceProfileMatcher _voiceProfileMatcher;
    private readonly VoiceProfileStore _voiceProfileStore;
    private readonly SpeakerNameLearningService _speakerNameLearningService;

    public LocalSpeakerDiarizationProvider(
        string diarizationAssetPath,
        InferenceAccelerationPreference accelerationPreference,
        int threadCount,
        FileLogWriter logger,
        SpeakerNameLearningMode speakerNameLearningMode = SpeakerNameLearningMode.LocalAutoLearn,
        SpeakerNameRecognitionOptions? speakerNameRecognitionOptions = null,
        string? voiceProfileStorePath = null)
        : this(
            diarizationAssetPath,
            accelerationPreference,
            speakerNameLearningMode,
            speakerNameRecognitionOptions ?? new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d),
            threadCount,
            logger,
            new DiarizationAssetCatalogService(),
            new TranscriptionAudioPreparer(),
            new TranscriptSpeakerAttributionService(),
            new DiarizationClusterSelectionService(),
            new SherpaSpeakerEmbeddingService(),
            new VoiceProfileMatcher(),
            new VoiceProfileStore(voiceProfileStorePath ?? AppDataPaths.GetVoiceProfileStorePath()))
    {
    }

    internal LocalSpeakerDiarizationProvider(
        string diarizationAssetPath,
        InferenceAccelerationPreference accelerationPreference,
        SpeakerNameLearningMode speakerNameLearningMode,
        SpeakerNameRecognitionOptions speakerNameRecognitionOptions,
        int threadCount,
        FileLogWriter logger,
        DiarizationAssetCatalogService assetCatalogService,
        TranscriptionAudioPreparer audioPreparer,
        TranscriptSpeakerAttributionService speakerAttributionService,
        DiarizationClusterSelectionService clusterSelectionService,
        SherpaSpeakerEmbeddingService speakerEmbeddingService,
        VoiceProfileMatcher voiceProfileMatcher,
        VoiceProfileStore voiceProfileStore)
    {
        _diarizationAssetPath = diarizationAssetPath;
        _accelerationPreference = accelerationPreference;
        _speakerNameLearningMode = speakerNameLearningMode;
        _speakerNameRecognitionOptions = speakerNameRecognitionOptions;
        _threadCount = Math.Max(1, threadCount);
        _logger = logger;
        _assetCatalogService = assetCatalogService;
        _audioPreparer = audioPreparer;
        _speakerAttributionService = speakerAttributionService;
        _clusterSelectionService = clusterSelectionService;
        _speakerEmbeddingService = speakerEmbeddingService;
        _voiceProfileMatcher = voiceProfileMatcher;
        _voiceProfileStore = voiceProfileStore;
        _speakerNameLearningService = new SpeakerNameLearningService(_voiceProfileStore);
    }

    public async Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken)
    {
        var installedAssets = _assetCatalogService.InspectInstalledAssets(_diarizationAssetPath);
        if (!installedAssets.IsReady ||
            string.IsNullOrWhiteSpace(installedAssets.SegmentationModelPath) ||
            string.IsNullOrWhiteSpace(installedAssets.EmbeddingModelPath))
        {
            _logger.Log($"Diarization model bundle is not ready. {installedAssets.DetailsText} Publishing transcript without speaker labels.");
            return new DiarizationResult(transcriptSegments, false, "Diarization model bundle unavailable.");
        }

        var preparedAudioPath = BuildPreparedAudioPath(audioPath);
        try
        {
            await _audioPreparer.PrepareAsync(audioPath, preparedAudioPath, cancellationToken);
            var accelerationDecision = ResolveAccelerationDecision();

            var diarizationOutcome = await RunDiarizationAsync(
                preparedAudioPath,
                transcriptSegments,
                installedAssets,
                accelerationDecision,
                cancellationToken);

            await _assetCatalogService.WriteRuntimeStatusAsync(
                _diarizationAssetPath,
                new DiarizationRuntimeStatus(
                    accelerationDecision.GpuAccelerationAvailable,
                    diarizationOutcome.Metadata!.ExecutionProvider,
                    diarizationOutcome.Metadata.DiagnosticMessage,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            return diarizationOutcome;
        }
        finally
        {
            if (File.Exists(preparedAudioPath))
            {
                File.Delete(preparedAudioPath);
            }
        }
    }

    private async Task<DiarizationResult> RunDiarizationAsync(
        string preparedAudioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        DiarizationAssetInstallStatus installedAssets,
        DiarizationAccelerationDecision accelerationDecision,
        CancellationToken cancellationToken)
    {
        return await RunWithProviderAsync(
            preparedAudioPath,
            transcriptSegments,
            installedAssets,
            DiarizationExecutionProvider.Cpu,
            accelerationDecision.GpuAccelerationRequested,
            accelerationDecision.GpuAccelerationAvailable,
            accelerationDecision.DiagnosticMessage,
            cancellationToken);
    }

    private DiarizationAccelerationDecision ResolveAccelerationDecision()
    {
        return _accelerationPreference == InferenceAccelerationPreference.Auto
            ? DiarizationAccelerationPolicy.Resolve(
                _accelerationPreference,
                directMlAvailable: false,
                DirectMlUnavailableMessage)
            : DiarizationAccelerationPolicy.Resolve(
                _accelerationPreference,
                directMlAvailable: false);
    }

    private async Task<DiarizationResult> RunWithProviderAsync(
        string preparedAudioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        DiarizationAssetInstallStatus installedAssets,
        DiarizationExecutionProvider executionProvider,
        bool gpuAccelerationRequested,
        bool gpuAccelerationAvailable,
        string? diagnosticMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var providerName = executionProvider == DiarizationExecutionProvider.Directml ? "directml" : "cpu";
        var segmentationModelFileName = Path.GetFileName(installedAssets.SegmentationModelPath)
            ?? throw new InvalidOperationException("The diarization segmentation model file name could not be resolved.");
        var embeddingModelFileName = Path.GetFileName(installedAssets.EmbeddingModelPath)
            ?? throw new InvalidOperationException("The diarization embedding model file name could not be resolved.");
        var clusterSelection = ProcessSpeakerTurns(
            preparedAudioPath,
            installedAssets,
            providerName,
            _threadCount,
            _clusterSelectionService,
            cancellationToken);
        var speakerTurns = clusterSelection.Candidate.SpeakerTurns;
        var speakers = _speakerAttributionService.BuildSpeakerCatalog(speakerTurns);
        IReadOnlyList<SpeakerVoiceSample> speakerVoiceSamples = Array.Empty<SpeakerVoiceSample>();
        string? voiceProfileDiagnosticMessage = null;
        if (_speakerNameLearningMode == SpeakerNameLearningMode.LocalAutoLearn &&
            speakerTurns.Count > 0)
        {
            try
            {
                speakerVoiceSamples = _speakerEmbeddingService.ExtractSpeakerVoiceSamples(
                    preparedAudioPath,
                    speakerTurns,
                    installedAssets,
                    providerName,
                    _threadCount,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (speakerVoiceSamples.Count > 0)
                {
                    var profileDocument = await _voiceProfileStore.LoadOrCreateAsync(cancellationToken);
                    var predictions = _voiceProfileMatcher.Match(
                        speakerVoiceSamples,
                        profileDocument.Profiles,
                        _speakerNameRecognitionOptions);
                    speakers = _voiceProfileMatcher.ApplyPredictions(speakers, predictions);
                    await _speakerNameLearningService.UpdateLastMatchedAsync(
                        predictions,
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                voiceProfileDiagnosticMessage = $"Speaker name profile matching skipped: {exception.Message}";
                _logger.Log(voiceProfileDiagnosticMessage);
            }
        }

        var displayNamesBySpeakerId = speakers.ToDictionary(speaker => speaker.Id, speaker => speaker.DisplayName, StringComparer.Ordinal);
        var attributedSegments = _speakerAttributionService.ApplySpeakerTurns(
            transcriptSegments,
            speakerTurns,
            displayNamesBySpeakerId);
        var combinedDiagnosticMessage = CombineDiagnosticMessages(diagnosticMessage, clusterSelection.DiagnosticMessage, voiceProfileDiagnosticMessage);

        var metadata = new DiarizationMetadata(
            provider: "sherpa-onnx",
            segmentationModelFileName: segmentationModelFileName,
            embeddingModelFileName: embeddingModelFileName,
            bundleVersion: installedAssets.BundleVersion ?? "unknown",
            attributionMode: DiarizationAttributionMode.SegmentOverlap,
            executionProvider: executionProvider,
            gpuAccelerationRequested: gpuAccelerationRequested,
            gpuAccelerationAvailable: gpuAccelerationAvailable,
            diagnosticMessage: combinedDiagnosticMessage);

        var appliedSpeakerLabels = attributedSegments.Any(segment =>
            !string.IsNullOrWhiteSpace(segment.SpeakerId) ||
            !string.IsNullOrWhiteSpace(segment.SpeakerLabel));
        var message = executionProvider == DiarizationExecutionProvider.Directml
            ? "Speaker labeling completed with DirectML acceleration."
            : gpuAccelerationRequested
                ? "Speaker labeling completed on CPU after GPU fallback."
                : "Speaker labeling completed on CPU.";

        _logger.Log(
            $"Speaker labeling completed. Provider={executionProvider}. Speakers={speakers.Count}. Turns={speakerTurns.Count}. " +
            $"GpuRequested={gpuAccelerationRequested}. GpuAvailable={gpuAccelerationAvailable}. Diagnostic='{combinedDiagnosticMessage ?? "<none>"}'.");

        return new DiarizationResult(
            attributedSegments,
            appliedSpeakerLabels,
            message,
            speakers,
            speakerTurns,
            metadata,
            speakerVoiceSamples);
    }

    private static DiarizationClusterSelection ProcessSpeakerTurns(
        string preparedAudioPath,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        DiarizationClusterSelectionService clusterSelectionService,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(preparedAudioPath);
        var samples = ReadSamples(reader, cancellationToken);
        var candidates = new List<DiarizationClusterCandidate>();

        foreach (var threshold in AdaptiveClusteringThresholds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var speakerTurns = ProcessSpeakerTurnsAtThreshold(
                samples,
                reader.WaveFormat.SampleRate,
                installedAssets,
                provider,
                threadCount,
                threshold);
            var candidate = new DiarizationClusterCandidate(threshold, speakerTurns);
            candidates.Add(candidate);

            var currentSelection = clusterSelectionService.SelectBestCandidate(candidates);
            if (ReferenceEquals(currentSelection.Candidate, candidate) &&
                currentSelection.SupportedSpeakerCount >= 2)
            {
                return currentSelection;
            }
        }

        return clusterSelectionService.SelectBestCandidate(candidates);
    }

    private static SpeakerTurn[] ProcessSpeakerTurnsAtThreshold(
        float[] samples,
        int preparedAudioSampleRate,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        float clusteringThreshold)
    {
        using var diarizer = CreateDiarizer(installedAssets, provider, threadCount, clusteringThreshold);
        if (preparedAudioSampleRate != diarizer.SampleRate)
        {
            throw new InvalidOperationException(
                $"Prepared diarization audio sample rate {preparedAudioSampleRate} did not match expected {diarizer.SampleRate}.");
        }

        return diarizer.Process(samples)
            .Where(segment => segment.End > segment.Start)
            .Select(segment => new SpeakerTurn(
                $"speaker_{segment.Speaker:00}",
                TimeSpan.FromSeconds(segment.Start),
                TimeSpan.FromSeconds(segment.End)))
            .ToArray();
    }

    private static float[] ReadSamples(AudioFileReader reader, CancellationToken cancellationToken)
    {
        var samples = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate];
        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.AddRange(buffer.Take(samplesRead));
        }

        return samples.ToArray();
    }

    private static OfflineSpeakerDiarization CreateDiarizer(
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        float clusteringThreshold)
    {
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = installedAssets.SegmentationModelPath
            ?? throw new InvalidOperationException("Diarization segmentation model path was missing.");
        config.Segmentation.Provider = provider;
        config.Segmentation.NumThreads = Math.Max(1, threadCount);
        config.Segmentation.Debug = 0;
        config.Embedding.Model = installedAssets.EmbeddingModelPath
            ?? throw new InvalidOperationException("Diarization embedding model path was missing.");
        config.Embedding.Provider = provider;
        config.Embedding.NumThreads = Math.Max(1, threadCount);
        config.Embedding.Debug = 0;
        config.Clustering.Threshold = clusteringThreshold;
        config.Clustering.NumClusters = -1;
        config.MinDurationOn = 0.2f;
        config.MinDurationOff = 0.2f;
        return new OfflineSpeakerDiarization(config);
    }

    private static string? CombineDiagnosticMessages(params string?[] messages)
    {
        var combinedMessages = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => message!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return combinedMessages.Length == 0
            ? null
            : string.Join(" ", combinedMessages);
    }

    private static string BuildPreparedAudioPath(string audioPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization");
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(
            tempDirectory,
            $"{Path.GetFileNameWithoutExtension(audioPath)}-{Guid.NewGuid():N}.wav");
    }

}
