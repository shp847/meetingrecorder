using MeetingRecorder.Core.Services;
using System.IO.Compression;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationAssetReleaseCatalogServiceTests
{
    [Fact]
    public async Task ListAvailableRemoteAssetsAsync_Returns_Diarization_Assets_In_Recommended_Order()
    {
        const string payload = """
            {
              "tag_name": "v0.2",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://example.com/app.zip",
                  "size": 1200
                },
                {
                  "name": "meeting-recorder-diarization-bundle-win-x64.zip",
                  "browser_download_url": "https://example.com/meeting-recorder-diarization-bundle-win-x64.zip",
                  "size": 24000000
                },
                {
                  "name": "meeting-recorder-diarization-model.int8.onnx",
                  "browser_download_url": "https://example.com/meeting-recorder-diarization-model.int8.onnx",
                  "size": 11000000
                },
                {
                  "name": "meeting-recorder-diarization-bundle.json",
                  "browser_download_url": "https://example.com/meeting-recorder-diarization-bundle.json",
                  "size": 400
                },
                {
                  "name": "release-notes.txt",
                  "browser_download_url": "https://example.com/release-notes.txt",
                  "size": 400
                }
              ]
            }
            """;

        var service = new DiarizationAssetReleaseCatalogService(
            new FakeFeedClient(payload, Array.Empty<byte>()),
            new DiarizationAssetCatalogService());

        var assets = await service.ListAvailableRemoteAssetsAsync("https://example.com/releases/latest");

        Assert.Collection(
            assets,
            asset =>
            {
                Assert.Equal("meeting-recorder-diarization-bundle-win-x64.zip", asset.FileName);
                Assert.Equal(DiarizationRemoteAssetKind.Bundle, asset.Kind);
                Assert.True(asset.IsRecommended);
            },
            asset =>
            {
                Assert.Equal("meeting-recorder-diarization-model.int8.onnx", asset.FileName);
                Assert.Equal(DiarizationRemoteAssetKind.Model, asset.Kind);
                Assert.False(asset.IsRecommended);
            },
            asset =>
            {
                Assert.Equal("meeting-recorder-diarization-bundle.json", asset.FileName);
                Assert.Equal(DiarizationRemoteAssetKind.Config, asset.Kind);
                Assert.False(asset.IsRecommended);
            });
    }

    [Fact]
    public void InspectInstalledAssets_Returns_Ready_When_ModelBundle_Exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var assetRoot = Path.Combine(root, "models", "diarization");
        Directory.CreateDirectory(assetRoot);
        File.WriteAllText(Path.Combine(assetRoot, "model.int8.onnx"), "stub");
        File.WriteAllText(Path.Combine(assetRoot, "nemo_en_titanet_small.onnx"), "stub");
        File.WriteAllText(
            Path.Combine(assetRoot, DiarizationAssetCatalogService.BundleManifestFileName),
            """
            {
              "bundleVersion": "2026.03.21",
              "segmentationModelFileName": "model.int8.onnx",
              "embeddingModelFileName": "nemo_en_titanet_small.onnx"
            }
            """);

        var status = new DiarizationAssetCatalogService().InspectInstalledAssets(assetRoot);

        Assert.True(status.IsReady);
        Assert.Equal(Path.Combine(assetRoot, "model.int8.onnx"), status.SegmentationModelPath);
        Assert.Equal(Path.Combine(assetRoot, "nemo_en_titanet_small.onnx"), status.EmbeddingModelPath);
        Assert.Equal("2026.03.21", status.BundleVersion);
    }

    [Fact]
    public async Task DownloadRemoteAssetIntoManagedDirectoryAsync_Extracts_Bundle_Into_Diarization_Folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var bundleBytes = BuildZipBundleBytes(new Dictionary<string, string>
        {
            ["model.int8.onnx"] = "stub",
            ["nemo_en_titanet_small.onnx"] = "stub",
            [DiarizationAssetCatalogService.BundleManifestFileName] =
                """
                {
                  "bundleVersion": "2026.03.21",
                  "segmentationModelFileName": "model.int8.onnx",
                  "embeddingModelFileName": "nemo_en_titanet_small.onnx"
                }
                """,
        });
        var service = new DiarizationAssetReleaseCatalogService(
            new FakeFeedClient("{}", bundleBytes),
            new DiarizationAssetCatalogService());
        var asset = new DiarizationRemoteAsset(
            "meeting-recorder-diarization-bundle-win-x64.zip",
            "https://example.com/meeting-recorder-diarization-bundle-win-x64.zip",
            bundleBytes.LongLength,
            DiarizationRemoteAssetKind.Bundle,
            true,
            "Recommended diarization model bundle.");

        var installed = await service.DownloadRemoteAssetIntoManagedDirectoryAsync(asset, modelCacheDir);

        Assert.True(installed.IsReady);
        Assert.Equal(
            Path.Combine(modelCacheDir, "diarization", "model.int8.onnx"),
            installed.SegmentationModelPath);
        Assert.Equal(
            Path.Combine(modelCacheDir, "diarization", "nemo_en_titanet_small.onnx"),
            installed.EmbeddingModelPath);
    }

    private static byte[] BuildZipBundleBytes(IReadOnlyDictionary<string, string> files)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Value);
            }
        }

        return memoryStream.ToArray();
    }

    private sealed class FakeFeedClient : IAppUpdateFeedClient
    {
        private readonly string _payload;
        private readonly byte[] _downloadBytes;

        public FakeFeedClient(string payload, byte[] downloadBytes)
        {
            _payload = payload;
            _downloadBytes = downloadBytes;
        }

        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_payload);
        }

        public async Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await File.WriteAllBytesAsync(destinationPath, _downloadBytes, cancellationToken);
        }
    }
}
