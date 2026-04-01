using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using SherpaOnnx;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class LocalSpeakerDiarizationProvider : IDiarizationProvider
{
    private readonly string _diarizationAssetPath;
    private readonly InferenceAccelerationPreference _accelerationPreference;
    private readonly int _threadCount;
    private readonly FileLogWriter _logger;
    private readonly DiarizationAssetCatalogService _assetCatalogService;
    private readonly TranscriptionAudioPreparer _audioPreparer;
    private readonly TranscriptSpeakerAttributionService _speakerAttributionService;

    public LocalSpeakerDiarizationProvider(
        string diarizationAssetPath,
        InferenceAccelerationPreference accelerationPreference,
        int threadCount,
        FileLogWriter logger)
        : this(
            diarizationAssetPath,
            accelerationPreference,
            threadCount,
            logger,
            new DiarizationAssetCatalogService(),
            new TranscriptionAudioPreparer(),
            new TranscriptSpeakerAttributionService())
    {
    }

    internal LocalSpeakerDiarizationProvider(
        string diarizationAssetPath,
        InferenceAccelerationPreference accelerationPreference,
        int threadCount,
        FileLogWriter logger,
        DiarizationAssetCatalogService assetCatalogService,
        TranscriptionAudioPreparer audioPreparer,
        TranscriptSpeakerAttributionService speakerAttributionService)
    {
        _diarizationAssetPath = diarizationAssetPath;
        _accelerationPreference = accelerationPreference;
        _threadCount = Math.Max(1, threadCount);
        _logger = logger;
        _assetCatalogService = assetCatalogService;
        _audioPreparer = audioPreparer;
        _speakerAttributionService = speakerAttributionService;
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
            var directMlProbe = ProbeDirectMlAvailability(installedAssets);
            var accelerationDecision = DiarizationAccelerationPolicy.Resolve(
                _accelerationPreference,
                directMlProbe.IsAvailable,
                directMlProbe.DiagnosticMessage);

            var diarizationOutcome = await RunDiarizationAsync(
                preparedAudioPath,
                transcriptSegments,
                installedAssets,
                accelerationDecision,
                cancellationToken);

            await _assetCatalogService.WriteRuntimeStatusAsync(
                _diarizationAssetPath,
                new DiarizationRuntimeStatus(
                    directMlProbe.IsAvailable,
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
        if (accelerationDecision.ExecutionProvider == DiarizationExecutionProvider.Directml)
        {
            try
            {
                return await RunWithProviderAsync(
                    preparedAudioPath,
                    transcriptSegments,
                    installedAssets,
                    DiarizationExecutionProvider.Directml,
                    accelerationDecision.GpuAccelerationRequested,
                    accelerationDecision.GpuAccelerationAvailable,
                    accelerationDecision.DiagnosticMessage,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                var fallbackMessage = $"DirectML diarization failed and the worker fell back to CPU. {exception.Message}";
                _logger.Log(fallbackMessage);

                return await RunWithProviderAsync(
                    preparedAudioPath,
                    transcriptSegments,
                    installedAssets,
                    DiarizationExecutionProvider.Cpu,
                    accelerationDecision.GpuAccelerationRequested,
                    accelerationDecision.GpuAccelerationAvailable,
                    fallbackMessage,
                    cancellationToken);
            }
        }

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

    private Task<DiarizationResult> RunWithProviderAsync(
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
        var diarizationSegments = ProcessSpeakerTurns(preparedAudioPath, installedAssets, providerName, _threadCount, cancellationToken);
        var speakerTurns = diarizationSegments
            .Where(segment => segment.End > segment.Start)
            .Select(segment => new SpeakerTurn(
                $"speaker_{segment.Speaker:00}",
                TimeSpan.FromSeconds(segment.Start),
                TimeSpan.FromSeconds(segment.End)))
            .ToArray();
        var speakers = _speakerAttributionService.BuildSpeakerCatalog(speakerTurns);
        var displayNamesBySpeakerId = speakers.ToDictionary(speaker => speaker.Id, speaker => speaker.DisplayName, StringComparer.Ordinal);
        var attributedSegments = _speakerAttributionService.ApplySpeakerTurns(
            transcriptSegments,
            speakerTurns,
            displayNamesBySpeakerId);

        var metadata = new DiarizationMetadata(
            provider: "sherpa-onnx",
            segmentationModelFileName: segmentationModelFileName,
            embeddingModelFileName: embeddingModelFileName,
            bundleVersion: installedAssets.BundleVersion ?? "unknown",
            attributionMode: DiarizationAttributionMode.SegmentOverlap,
            executionProvider: executionProvider,
            gpuAccelerationRequested: gpuAccelerationRequested,
            gpuAccelerationAvailable: gpuAccelerationAvailable,
            diagnosticMessage: diagnosticMessage);

        var appliedSpeakerLabels = attributedSegments.Any(segment =>
            !string.IsNullOrWhiteSpace(segment.SpeakerId) ||
            !string.IsNullOrWhiteSpace(segment.SpeakerLabel));
        var message = executionProvider == DiarizationExecutionProvider.Directml
            ? "Speaker labeling completed with DirectML acceleration."
            : gpuAccelerationRequested
                ? "Speaker labeling completed on CPU after GPU fallback."
                : "Speaker labeling completed on CPU.";

        _logger.Log(
            $"Speaker labeling completed. Provider={executionProvider}. Speakers={speakers.Count}. Turns={speakerTurns.Length}. " +
            $"GpuRequested={gpuAccelerationRequested}. GpuAvailable={gpuAccelerationAvailable}. Diagnostic='{diagnosticMessage ?? "<none>"}'.");

        return Task.FromResult(new DiarizationResult(
            attributedSegments,
            appliedSpeakerLabels,
            message,
            speakers,
            speakerTurns,
            metadata));
    }

    private ProviderProbeResult ProbeDirectMlAvailability(DiarizationAssetInstallStatus installedAssets)
    {
        if (_accelerationPreference == InferenceAccelerationPreference.CpuOnly)
        {
            return new ProviderProbeResult(
                installedAssets.GpuAccelerationAvailable == true,
                installedAssets.DiagnosticMessage);
        }

        try
        {
            using var diarizer = CreateDiarizer(installedAssets, provider: "directml", _threadCount);
            _logger.Log("DirectML diarization probe succeeded.");
            return new ProviderProbeResult(true, null);
        }
        catch (Exception exception)
        {
            _logger.Log($"DirectML diarization probe failed: {exception.Message}");
            return new ProviderProbeResult(false, exception.Message);
        }
    }

    private static OfflineSpeakerDiarizationSegment[] ProcessSpeakerTurns(
        string preparedAudioPath,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        CancellationToken cancellationToken)
    {
        using var diarizer = CreateDiarizer(installedAssets, provider, threadCount);
        using var reader = new AudioFileReader(preparedAudioPath);
        if (reader.WaveFormat.SampleRate != diarizer.SampleRate)
        {
            throw new InvalidOperationException(
                $"Prepared diarization audio sample rate {reader.WaveFormat.SampleRate} did not match expected {diarizer.SampleRate}.");
        }

        var samples = ReadSamples(reader, cancellationToken);
        return diarizer.Process(samples);
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
        int threadCount)
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
        config.Clustering.Threshold = 0.5f;
        config.Clustering.NumClusters = -1;
        config.MinDurationOn = 0.2f;
        config.MinDurationOff = 0.2f;
        return new OfflineSpeakerDiarization(config);
    }

    private static string BuildPreparedAudioPath(string audioPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderDiarization");
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(
            tempDirectory,
            $"{Path.GetFileNameWithoutExtension(audioPath)}-{Guid.NewGuid():N}.wav");
    }

    private sealed record ProviderProbeResult(bool IsAvailable, string? DiagnosticMessage);
}
