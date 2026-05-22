using MeetingRecorder.Core.Services;

namespace MeetingRecorder.ProcessingWorker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (HasArgument(args, "--probe-directml"))
        {
            return await RunDirectMlProbeAsync(args);
        }

        if (!TryParseArguments(args, out var manifestPath, out var configPath, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            return 1;
        }

        var resolvedConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? AppDataPaths.GetConfigPath()
            : configPath;
        var sessionRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Manifest path must include a session directory.");
        var logger = new FileLogWriter(Path.Combine(sessionRoot, "logs", "processing.log"));

        logger.Log($"Worker starting for manifest '{manifestPath}'.");

        try
        {
            var configStore = new AppConfigStore(resolvedConfigPath);
            var config = await configStore.LoadOrCreateAsync();
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var transcriptionThreadCount = BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, Environment.ProcessorCount);
            var diarizationThreadCount = BackgroundProcessingPolicy.GetDiarizationThreadCount(config, Environment.ProcessorCount);
            using var summaryHttpClient = new HttpClient();
            var summarizationProvider = new MeetingSummarizationProvider(
                FileSummarySecretStore.CreateDefault(),
                new SummaryChatClient(summaryHttpClient));
            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                new WhisperNetTranscriptionProvider(config.TranscriptionModelPath, transcriptionThreadCount, logger),
                new LocalSpeakerDiarizationProvider(
                    config.DiarizationAssetPath,
                    config.DiarizationAccelerationPreference,
                    diarizationThreadCount,
                    logger,
                    config.SpeakerNameLearningMode,
                    new SpeakerNameRecognitionOptions(
                        config.SpeakerNameAutoApplyConfidenceThreshold,
                        config.SpeakerNameSuggestionConfidenceThreshold,
                        config.SpeakerNameMatchMarginThreshold),
                    AppDataPaths.GetVoiceProfileStorePath()),
                new TranscriptRenderer(),
                new FilePublishService(),
                summarizationProvider);

            var published = await processor.ProcessAsync(manifestPath, config);
            logger.Log($"Processing completed successfully. Ready marker: '{published.ReadyMarkerPath}'.");
            Console.WriteLine(published.ReadyMarkerPath);
            return 0;
        }
        catch (Exception exception)
        {
            logger.Log($"Processing failed: {exception}");
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunDirectMlProbeAsync(IReadOnlyList<string> args)
    {
        var configPath = TryGetOption(args, "--config") ?? AppDataPaths.GetConfigPath();
        var logger = new FileLogWriter(AppDataPaths.GetGlobalLogPath());
        var assetCatalogService = new DiarizationAssetCatalogService();
        string? diarizationAssetPath = null;

        try
        {
            var configStore = new AppConfigStore(configPath);
            var config = await configStore.LoadOrCreateAsync();
            diarizationAssetPath = config.DiarizationAssetPath;
            var diarizationThreadCount = BackgroundProcessingPolicy.GetDiarizationThreadCount(config, Environment.ProcessorCount);
            LocalSpeakerDiarizationProvider.ProbeDirectMl(diarizationAssetPath, diarizationThreadCount, logger);
            await TryWriteDirectMlProbeStatusAsync(
                assetCatalogService,
                diarizationAssetPath,
                succeeded: true,
                message: "DirectML probe succeeded.",
                logger);
            Console.WriteLine("DirectML probe succeeded.");
            return 0;
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("DirectML-enabled speaker-labeling runtime", StringComparison.OrdinalIgnoreCase))
        {
            logger.Log("DirectML probe unavailable: DirectML-enabled Sherpa runtime is not packaged.");
            await TryWriteDirectMlProbeStatusAsync(
                assetCatalogService,
                diarizationAssetPath,
                succeeded: false,
                message: exception.Message,
                logger);
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Log($"DirectML probe failed safely: {exception.GetType().Name}");
            await TryWriteDirectMlProbeStatusAsync(
                assetCatalogService,
                diarizationAssetPath,
                succeeded: false,
                message: "DirectML probe failed.",
                logger);
            Console.Error.WriteLine("DirectML probe failed.");
            return 2;
        }
    }

    private static async Task TryWriteDirectMlProbeStatusAsync(
        DiarizationAssetCatalogService assetCatalogService,
        string? diarizationAssetPath,
        bool succeeded,
        string message,
        FileLogWriter logger)
    {
        if (string.IsNullOrWhiteSpace(diarizationAssetPath))
        {
            return;
        }

        try
        {
            await assetCatalogService.WriteDirectMlProbeStatusAsync(
                diarizationAssetPath,
                succeeded,
                message,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Log($"Unable to write DirectML probe status: {exception.Message}");
        }
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out string manifestPath,
        out string? configPath,
        out string? error)
    {
        manifestPath = string.Empty;
        configPath = null;
        error = null;

        for (var index = 0; index < args.Count; index++)
        {
            var current = args[index];
            if (string.Equals(current, "--manifest", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                manifestPath = args[++index];
                continue;
            }

            if (string.Equals(current, "--config", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                configPath = args[++index];
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            error = "Missing required argument: --manifest <path>";
            return false;
        }

        return true;
    }

    private static bool HasArgument(IReadOnlyList<string> args, string name)
    {
        return args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
