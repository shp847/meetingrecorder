using MeetingRecorder.Core.Services;

namespace MeetingRecorder.ProcessingWorker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
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
            var processor = new SessionProcessor(
                manifestStore,
                pathBuilder,
                new WaveChunkMerger(),
                new WhisperNetTranscriptionProvider(config.TranscriptionModelPath, transcriptionThreadCount, logger),
                new LocalSpeakerDiarizationProvider(
                    config.DiarizationAssetPath,
                    config.DiarizationAccelerationPreference,
                    diarizationThreadCount,
                    logger),
                new TranscriptRenderer(),
                new FilePublishService());

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
}
