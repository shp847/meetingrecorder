using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.Text.Json;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class OptionalSidecarDiarizationProvider : IDiarizationProvider
{
    private readonly string _diarizationAssetPath;
    private readonly FileLogWriter _logger;
    private readonly DiarizationAssetCatalogService _assetCatalogService;

    public OptionalSidecarDiarizationProvider(string diarizationAssetPath, FileLogWriter logger)
        : this(diarizationAssetPath, logger, new DiarizationAssetCatalogService())
    {
    }

    internal OptionalSidecarDiarizationProvider(
        string diarizationAssetPath,
        FileLogWriter logger,
        DiarizationAssetCatalogService assetCatalogService)
    {
        _diarizationAssetPath = diarizationAssetPath;
        _logger = logger;
        _assetCatalogService = assetCatalogService;
    }

    public async Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken)
    {
        var installedAssets = _assetCatalogService.InspectInstalledAssets(_diarizationAssetPath);
        var sidecarPath = installedAssets.SidecarExecutablePath;
        if (!installedAssets.IsReady || string.IsNullOrWhiteSpace(sidecarPath) || !File.Exists(sidecarPath))
        {
            _logger.Log($"Diarization sidecar not found. {installedAssets.DetailsText} Publishing transcript without speaker labels.");
            return new DiarizationResult(transcriptSegments, false, "Diarization sidecar unavailable.");
        }

        var inputPath = Path.ChangeExtension(audioPath, ".diarization-input.json");
        var outputPath = Path.ChangeExtension(audioPath, ".diarization-output.json");
        await File.WriteAllTextAsync(
            inputPath,
            JsonSerializer.Serialize(transcriptSegments, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = sidecarPath,
            Arguments = $"--audio \"{audioPath}\" --segments \"{inputPath}\" --output \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the diarization sidecar.");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.Log($"Diarization sidecar exited with code {process.ExitCode}. Publishing transcript without speaker labels.");
            return new DiarizationResult(transcriptSegments, false, "Diarization sidecar did not produce output.");
        }

        var payload = await File.ReadAllTextAsync(outputPath, cancellationToken);
        var segments = JsonSerializer.Deserialize<IReadOnlyList<TranscriptSegment>>(payload)
            ?? transcriptSegments;

        _logger.Log("Diarization sidecar applied speaker labels successfully.");
        return new DiarizationResult(segments, true, "Diarization sidecar completed.");
    }
}
