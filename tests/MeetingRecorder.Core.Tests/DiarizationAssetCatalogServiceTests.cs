using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationAssetCatalogServiceTests
{
    [Fact]
    public void InspectInstalledAssets_Returns_Ready_When_ModelBundle_Is_Complete()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "model.int8.onnx"), "segmentation");
        File.WriteAllText(Path.Combine(root, "nemo_en_titanet_small.onnx"), "embedding");
        File.WriteAllText(
            Path.Combine(root, "meeting-recorder-diarization-bundle.json"),
            JsonSerializer.Serialize(new
            {
                bundleVersion = "2026.03.21",
                segmentationModelFileName = "model.int8.onnx",
                embeddingModelFileName = "nemo_en_titanet_small.onnx",
            }));

        var service = new DiarizationAssetCatalogService();

        var status = service.InspectInstalledAssets(root);

        Assert.True(status.IsReady);
        Assert.Equal("2026.03.21", status.BundleVersion);
        Assert.Equal(Path.Combine(root, "model.int8.onnx"), status.SegmentationModelPath);
        Assert.Equal(Path.Combine(root, "nemo_en_titanet_small.onnx"), status.EmbeddingModelPath);
    }

    [Fact]
    public void InspectInstalledAssets_Returns_NotReady_When_BundleManifest_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "model.int8.onnx"), "segmentation");
        File.WriteAllText(Path.Combine(root, "nemo_en_titanet_small.onnx"), "embedding");

        var service = new DiarizationAssetCatalogService();

        var status = service.InspectInstalledAssets(root);

        Assert.False(status.IsReady);
        Assert.Contains("bundle manifest", status.DetailsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteDirectMlProbeStatusAsync_Preserves_Last_Run_Provider_And_Records_Last_Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var service = new DiarizationAssetCatalogService();
        var runStatusTime = DateTimeOffset.Parse("2026-05-22T14:30:00Z");
        var probeStatusTime = DateTimeOffset.Parse("2026-05-22T15:00:00Z");

        await service.WriteRuntimeStatusAsync(
            root,
            new DiarizationRuntimeStatus(
                GpuAccelerationAvailable: false,
                EffectiveExecutionProvider: DiarizationExecutionProvider.Cpu,
                DiagnosticMessage: "The last speaker-labeling run used CPU.",
                UpdatedAtUtc: runStatusTime));

        await service.WriteDirectMlProbeStatusAsync(
            root,
            succeeded: true,
            message: "DirectML probe succeeded.",
            atUtc: probeStatusTime);

        var status = service.InspectInstalledAssets(root);

        Assert.Equal(DiarizationExecutionProvider.Cpu, status.EffectiveExecutionProvider);
        Assert.False(status.GpuAccelerationAvailable);
        Assert.Equal("The last speaker-labeling run used CPU.", status.DiagnosticMessage);
        Assert.True(status.LastDirectMlProbeSucceeded);
        Assert.Equal(probeStatusTime, status.LastDirectMlProbeAtUtc);
        Assert.Equal("DirectML probe succeeded.", status.LastDirectMlProbeMessage);
    }
}
