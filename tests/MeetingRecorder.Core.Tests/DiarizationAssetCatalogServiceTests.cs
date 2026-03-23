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
}
