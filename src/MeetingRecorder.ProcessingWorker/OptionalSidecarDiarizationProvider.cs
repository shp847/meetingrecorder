using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using SherpaOnnx;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class LocalSpeakerDiarizationProvider : IDiarizationProvider
{
    private const string DirectMlFallbackMessage =
        "DirectML GPU initialization failed; speaker labeling retried on CPU.";
    private const string DirectMlUnsupportedClusterSkipMessage =
        "DirectML speaker clustering was outside the supported automatic range; speaker labeling skipped without CPU retry.";
    private const string SherpaDirectMlUnavailableMessage =
        "This Meeting Recorder build does not include a DirectML-enabled speaker-labeling runtime; speaker labeling used CPU.";
    private const string SherpaDirectMlFallbackOnlyMarker = "DirectML is for Windows only";
    private const string SherpaDirectMlEnabledMarker = "Failed to enable DirectML";
    private const string UnsupportedClusterMessagePrefix =
        "Speaker labeling skipped because clustering detected";

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
    private readonly SpeakerClusterMergeService _speakerClusterMergeService;
    private readonly DiarizationOversegmentedClusterRecoveryService _oversegmentedClusterRecoveryService;
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
            new SpeakerClusterMergeService(),
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
        SpeakerClusterMergeService speakerClusterMergeService,
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
        _speakerClusterMergeService = speakerClusterMergeService;
        _oversegmentedClusterRecoveryService = new DiarizationOversegmentedClusterRecoveryService(
            _clusterSelectionService,
            _speakerClusterMergeService);
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

            var diarizationRun = await RunDiarizationAsync(
                preparedAudioPath,
                transcriptSegments,
                installedAssets,
                cancellationToken);

            await _assetCatalogService.WriteRuntimeStatusAsync(
                _diarizationAssetPath,
                new DiarizationRuntimeStatus(
                    diarizationRun.GpuAccelerationAvailable,
                    diarizationRun.EffectiveExecutionProvider,
                    diarizationRun.AccelerationDiagnosticMessage,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            return diarizationRun.Result;
        }
        finally
        {
            if (File.Exists(preparedAudioPath))
            {
                File.Delete(preparedAudioPath);
            }
        }
    }

    private async Task<DiarizationProviderRun> RunDiarizationAsync(
        string preparedAudioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        DiarizationAssetInstallStatus installedAssets,
        CancellationToken cancellationToken)
    {
        if (_accelerationPreference == InferenceAccelerationPreference.Auto)
        {
            if (!IsSherpaDirectMlRuntimeEnabled())
            {
                _logger.Log("DirectML speaker-labeling runtime is not enabled in the bundled Sherpa native library. Using CPU fallback.");
                return await RunCpuFallbackAsync(
                    preparedAudioPath,
                    transcriptSegments,
                    installedAssets,
                    SherpaDirectMlUnavailableMessage,
                    cancellationToken);
            }

            var directMlDecision = DiarizationAccelerationPolicy.Resolve(
                _accelerationPreference,
                directMlAvailable: true);

            try
            {
                var directMlResult = await RunWithProviderAsync(
                    preparedAudioPath,
                    transcriptSegments,
                    installedAssets,
                    DiarizationExecutionProvider.Directml,
                    directMlDecision.GpuAccelerationRequested,
                    directMlDecision.GpuAccelerationAvailable,
                    directMlDecision.DiagnosticMessage,
                    cancellationToken);

                if (!IsUnsupportedClusterResult(directMlResult))
                {
                    return new DiarizationProviderRun(
                        directMlResult,
                        GpuAccelerationAvailable: true,
                        EffectiveExecutionProvider: DiarizationExecutionProvider.Directml,
                        AccelerationDiagnosticMessage: null);
                }

                _logger.Log("DirectML speaker clustering was outside the supported automatic range. Skipping CPU retry so transcript publishing can continue.");
                return new DiarizationProviderRun(
                    directMlResult with { Message = CombineDiagnosticMessages(directMlResult.Message, DirectMlUnsupportedClusterSkipMessage) },
                    GpuAccelerationAvailable: true,
                    EffectiveExecutionProvider: DiarizationExecutionProvider.Directml,
                    AccelerationDiagnosticMessage: DirectMlUnsupportedClusterSkipMessage);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Log($"DirectML speaker-labeling attempt failed safely with {exception.GetType().Name}. Retrying on CPU.");
                return await RunCpuFallbackAsync(
                    preparedAudioPath,
                    transcriptSegments,
                    installedAssets,
                    DirectMlFallbackMessage,
                    cancellationToken);
            }
        }

        var cpuDecision = DiarizationAccelerationPolicy.Resolve(
            _accelerationPreference,
            directMlAvailable: false);
        var cpuResult = await RunWithProviderAsync(
            preparedAudioPath,
            transcriptSegments,
            installedAssets,
            DiarizationExecutionProvider.Cpu,
            cpuDecision.GpuAccelerationRequested,
            cpuDecision.GpuAccelerationAvailable,
            cpuDecision.DiagnosticMessage,
            cancellationToken);

        return new DiarizationProviderRun(
            cpuResult,
            cpuDecision.GpuAccelerationAvailable,
            DiarizationExecutionProvider.Cpu,
            cpuDecision.DiagnosticMessage);
    }

    private async Task<DiarizationProviderRun> RunCpuFallbackAsync(
        string preparedAudioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        DiarizationAssetInstallStatus installedAssets,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        var fallbackDecision = DiarizationAccelerationPolicy.Resolve(
            InferenceAccelerationPreference.Auto,
            directMlAvailable: false,
            fallbackMessage);
        var fallbackResult = await RunWithProviderAsync(
            preparedAudioPath,
            transcriptSegments,
            installedAssets,
            DiarizationExecutionProvider.Cpu,
            fallbackDecision.GpuAccelerationRequested,
            fallbackDecision.GpuAccelerationAvailable,
            fallbackDecision.DiagnosticMessage,
            cancellationToken);

        return new DiarizationProviderRun(
            fallbackResult,
            fallbackDecision.GpuAccelerationAvailable,
            DiarizationExecutionProvider.Cpu,
            fallbackDecision.DiagnosticMessage);
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
        var baseDiagnosticMessage = CombineDiagnosticMessages(
            diagnosticMessage,
            clusterSelection.DiagnosticMessage);

        var speakerTurns = clusterSelection.Candidate.SpeakerTurns;
        IReadOnlyList<SpeakerVoiceSample> speakerVoiceSamples = Array.Empty<SpeakerVoiceSample>();
        string? voiceProfileDiagnosticMessage = null;
        if (speakerTurns.Count > 0 &&
            ShouldExtractSpeakerVoiceSamples(clusterSelection))
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
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                voiceProfileDiagnosticMessage = $"Speaker cluster embedding extraction skipped: {exception.Message}";
                _logger.Log(voiceProfileDiagnosticMessage);
            }
        }

        var speakerClusterMergeDiagnosticMessage = default(string);
        var oversegmentedRecovery = _oversegmentedClusterRecoveryService.TryRecover(
            clusterSelection,
            speakerVoiceSamples);
        if (oversegmentedRecovery.Recovered)
        {
            clusterSelection = oversegmentedRecovery.Selection;
            speakerTurns = clusterSelection.Candidate.SpeakerTurns;
            speakerVoiceSamples = oversegmentedRecovery.VoiceSamples;
            speakerClusterMergeDiagnosticMessage = oversegmentedRecovery.DiagnosticMessage;
        }
        else if (clusterSelection.IsAutomaticSpeakerCountSupported)
        {
            var mergeResult = _speakerClusterMergeService.MergeSimilarClusters(speakerTurns, speakerVoiceSamples);
            speakerTurns = mergeResult.SpeakerTurns;
            speakerVoiceSamples = RemapVoiceSamples(speakerVoiceSamples, mergeResult.SpeakerIdMap);
            speakerClusterMergeDiagnosticMessage = mergeResult.DiagnosticMessage;
        }
        else
        {
            speakerClusterMergeDiagnosticMessage = oversegmentedRecovery.DiagnosticMessage;
        }

        if (!clusterSelection.IsAutomaticSpeakerCountSupported)
        {
            var skippedDiagnosticMessage = CombineDiagnosticMessages(
                baseDiagnosticMessage,
                speakerClusterMergeDiagnosticMessage,
                voiceProfileDiagnosticMessage);
            var skippedMetadata = new DiarizationMetadata(
                provider: "sherpa-onnx",
                segmentationModelFileName: segmentationModelFileName,
                embeddingModelFileName: embeddingModelFileName,
                bundleVersion: installedAssets.BundleVersion ?? "unknown",
                attributionMode: DiarizationAttributionMode.SegmentOverlap,
                executionProvider: executionProvider,
                gpuAccelerationRequested: gpuAccelerationRequested,
                gpuAccelerationAvailable: gpuAccelerationAvailable,
                diagnosticMessage: skippedDiagnosticMessage);
            var skippedMessage =
                $"{UnsupportedClusterMessagePrefix} {clusterSelection.SupportedSpeakerCount} supported speakers, outside the supported automatic range of " +
                $"{DiarizationClusterSelectionService.MinimumAutomaticSpeakerCount}-{DiarizationClusterSelectionService.MaximumAutomaticSpeakerCount}.";

            _logger.Log(
                $"Speaker labeling skipped. Provider={executionProvider}. SupportedSpeakers={clusterSelection.SupportedSpeakerCount}. " +
                $"GpuRequested={gpuAccelerationRequested}. GpuAvailable={gpuAccelerationAvailable}. Diagnostic='{skippedDiagnosticMessage ?? "<none>"}'.");

            return new DiarizationResult(
                transcriptSegments,
                false,
                skippedMessage,
                Array.Empty<SpeakerIdentity>(),
                Array.Empty<SpeakerTurn>(),
                skippedMetadata,
                Array.Empty<SpeakerVoiceSample>());
        }

        var speakers = _speakerAttributionService.BuildSpeakerCatalog(speakerTurns);
        if (!string.IsNullOrWhiteSpace(speakerClusterMergeDiagnosticMessage))
        {
            _logger.Log(speakerClusterMergeDiagnosticMessage);
        }

        if (_speakerNameLearningMode == SpeakerNameLearningMode.LocalAutoLearn &&
            speakerVoiceSamples.Count > 0)
        {
            try
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
        var combinedDiagnosticMessage = CombineDiagnosticMessages(
            baseDiagnosticMessage,
            speakerClusterMergeDiagnosticMessage,
            voiceProfileDiagnosticMessage);

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

        var speakerVoiceSamplesForMetadata = _speakerNameLearningMode == SpeakerNameLearningMode.LocalAutoLearn
            ? speakerVoiceSamples
            : Array.Empty<SpeakerVoiceSample>();

        return new DiarizationResult(
            attributedSegments,
            appliedSpeakerLabels,
            message,
            speakers,
            speakerTurns,
            metadata,
            speakerVoiceSamplesForMetadata);
    }

    private static bool ShouldExtractSpeakerVoiceSamples(DiarizationClusterSelection clusterSelection)
    {
        return clusterSelection.IsAutomaticSpeakerCountSupported ||
            DiarizationOversegmentedClusterRecoveryService.ShouldAttemptRecovery(clusterSelection);
    }

    private static IReadOnlyList<SpeakerVoiceSample> RemapVoiceSamples(
        IReadOnlyList<SpeakerVoiceSample> speakerVoiceSamples,
        IReadOnlyDictionary<string, string> speakerIdMap)
    {
        if (speakerVoiceSamples.Count == 0 || speakerIdMap.Count == 0)
        {
            return speakerVoiceSamples;
        }

        return speakerVoiceSamples
            .Select(sample => speakerIdMap.TryGetValue(sample.SpeakerId, out var speakerId)
                ? sample with { SpeakerId = speakerId }
                : sample)
            .GroupBy(static sample => sample.SpeakerId, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static sample => sample.SpeechDuration)
                .First())
            .OrderBy(static sample => sample.SpeakerId, StringComparer.Ordinal)
            .ToArray();
    }

    private static DiarizationClusterSelection ProcessSpeakerTurns(
        string preparedAudioPath,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        DiarizationClusterSelectionService clusterSelectionService,
        CancellationToken cancellationToken)
    {
        var thresholdOptions = DiarizationCalibrationEnvironment.LoadDiarizationThresholdOptions();
        var selectionOptions = DiarizationCalibrationEnvironment.LoadClusterSelectionOptions();
        using var reader = new AudioFileReader(preparedAudioPath);
        var samples = ReadSamples(reader, cancellationToken);
        var candidates = new List<DiarizationClusterCandidate>();

        var defaultCandidate = BuildClusterCandidate(
            samples,
            reader.WaveFormat.SampleRate,
            installedAssets,
            provider,
            threadCount,
            thresholdOptions.DefaultClusteringThreshold);
        candidates.Add(defaultCandidate);
        var defaultSelection = clusterSelectionService.SelectBestCandidate(candidates);
        if (defaultSelection.IsAutomaticSpeakerCountSupported &&
            defaultSelection.SupportedSpeakerCount <= selectionOptions.MaximumPreferredAutomaticSpeakerCount)
        {
            return defaultSelection;
        }

        var thresholds = defaultSelection.SupportedSpeakerCount < DiarizationClusterSelectionService.MinimumAutomaticSpeakerCount
            ? thresholdOptions.CollapsedSpeakerClusteringThresholds
            : thresholdOptions.OverSegmentedSpeakerClusteringThresholds;
        foreach (var threshold in thresholds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = BuildClusterCandidate(samples, reader.WaveFormat.SampleRate, installedAssets, provider, threadCount, threshold);
            candidates.Add(candidate);
        }

        return clusterSelectionService.SelectBestCandidate(candidates);
    }

    private static DiarizationClusterCandidate BuildClusterCandidate(
        float[] samples,
        int preparedAudioSampleRate,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        float threshold)
    {
        return new DiarizationClusterCandidate(
            threshold,
            ProcessSpeakerTurnsAtThreshold(
                samples,
                preparedAudioSampleRate,
                installedAssets,
                provider,
                threadCount,
                threshold));
    }

    internal static void ProbeDirectMl(
        string diarizationAssetPath,
        int threadCount,
        FileLogWriter logger)
    {
        if (!IsSherpaDirectMlRuntimeEnabled())
        {
            throw new InvalidOperationException(SherpaDirectMlUnavailableMessage);
        }

        var assetCatalogService = new DiarizationAssetCatalogService();
        var installedAssets = assetCatalogService.InspectInstalledAssets(diarizationAssetPath);
        if (!installedAssets.IsReady)
        {
            throw new InvalidOperationException("Speaker-labeling assets are not ready.");
        }

        ProbeDirectMl(installedAssets, threadCount);
        logger.Log("DirectML speaker-labeling probe completed successfully.");
    }

    internal static void ProbeDirectMl(DiarizationAssetInstallStatus installedAssets, int threadCount)
    {
        using var diarizer = CreateDiarizer(
            installedAssets,
            "directml",
            Math.Max(1, threadCount),
            DiarizationCalibrationEnvironment.LoadDiarizationThresholdOptions().DefaultClusteringThreshold);
    }

    internal static bool IsSherpaDirectMlRuntimeEnabled()
    {
        var sherpaNativePath = Path.Combine(AppContext.BaseDirectory, "sherpa-onnx-c-api.dll");
        if (!File.Exists(sherpaNativePath))
        {
            return false;
        }

        var nativeText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(sherpaNativePath));
        return nativeText.Contains(SherpaDirectMlEnabledMarker, StringComparison.Ordinal) &&
            !nativeText.Contains(SherpaDirectMlFallbackOnlyMarker, StringComparison.Ordinal);
    }

    private static bool IsUnsupportedClusterResult(DiarizationResult result)
    {
        return !result.AppliedSpeakerLabels &&
            result.Message?.StartsWith(UnsupportedClusterMessagePrefix, StringComparison.Ordinal) == true;
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

internal sealed record DiarizationProviderRun(
    DiarizationResult Result,
    bool GpuAccelerationAvailable,
    DiarizationExecutionProvider EffectiveExecutionProvider,
    string? AccelerationDiagnosticMessage);
