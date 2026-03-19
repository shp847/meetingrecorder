using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class WhisperModelReleaseCatalogServiceTests
{
    [Fact]
    public async Task ListAvailableRemoteModelsAsync_Returns_GitHub_Model_Assets_In_Recommended_Order()
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
                  "name": "ggml-medium.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-medium.en-q8_0.bin",
                  "size": 823000000
                },
                {
                  "name": "ggml-small.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-small.en-q8_0.bin",
                  "size": 252000000
                },
                {
                  "name": "ggml-base.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-base.en-q8_0.bin",
                  "size": 78000000
                },
                {
                  "name": "ggml-tiny.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-tiny.en-q8_0.bin",
                  "size": 43000000
                },
                {
                  "name": "release-notes.txt",
                  "browser_download_url": "https://example.com/release-notes.txt",
                  "size": 400
                }
              ]
            }
            """;

        var service = new WhisperModelReleaseCatalogService(
            new FakeFeedClient(payload, Array.Empty<byte>()),
            new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));

        var models = await service.ListAvailableRemoteModelsAsync("https://example.com/releases/latest");

        Assert.Collection(
            models,
            item =>
            {
                Assert.Equal("ggml-base.en-q8_0.bin", item.FileName);
                Assert.Equal(78_000_000L, item.FileSizeBytes);
                Assert.True(item.IsRecommended);
                Assert.Equal(
                    "Recommended default for most laptops: balanced accuracy, download size, and CPU speed.",
                    item.Description);
            },
            item =>
            {
                Assert.Equal("ggml-small.en-q8_0.bin", item.FileName);
                Assert.Equal(252_000_000L, item.FileSizeBytes);
                Assert.False(item.IsRecommended);
                Assert.Equal(
                    "Better accuracy than base, but slower and larger; good when transcript quality matters more than speed.",
                    item.Description);
            },
            item =>
            {
                Assert.Equal("ggml-tiny.en-q8_0.bin", item.FileName);
                Assert.Equal(43_000_000L, item.FileSizeBytes);
                Assert.False(item.IsRecommended);
                Assert.Equal(
                    "Smallest and fastest option; best for quickest setup or lighter machines, with the lowest accuracy.",
                    item.Description);
            },
            item =>
            {
                Assert.Equal("ggml-medium.en-q8_0.bin", item.FileName);
                Assert.Equal(823_000_000L, item.FileSizeBytes);
                Assert.False(item.IsRecommended);
                Assert.Equal(
                    "Most accurate of the four q8_0 options, but much larger and slower; best for stronger machines.",
                    item.Description);
            });
    }

    [Fact]
    public async Task DownloadRemoteModelIntoManagedDirectoryAsync_Downloads_And_Validates_Selected_Model()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var payload = new byte[WhisperModelService.MinimumExpectedModelBytes + 5000];
        var feedClient = new FakeFeedClient("{}", payload);
        var service = new WhisperModelReleaseCatalogService(
            feedClient,
            new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));
        var model = new WhisperRemoteModelAsset(
            "ggml-base.en-q8_0.bin",
            "https://example.com/ggml-base.en-q8_0.bin",
            payload.LongLength,
            true,
            "Recommended default for most laptops.");

        var installed = await service.DownloadRemoteModelIntoManagedDirectoryAsync(model, modelCacheDir);

        Assert.Equal(Path.Combine(modelCacheDir, "asr", "ggml-base.en-q8_0.bin"), installed.ModelPath);
        Assert.True(File.Exists(installed.ModelPath));
        Assert.Equal(WhisperModelStatusKind.Valid, installed.Status.Kind);
        Assert.Equal("https://example.com/ggml-base.en-q8_0.bin", feedClient.LastDownloadedUrl);
    }

    [Fact]
    public async Task DownloadRemoteModelIntoManagedDirectoryAsync_Reports_Download_Progress()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var payload = new byte[WhisperModelService.MinimumExpectedModelBytes + 5000];
        var feedClient = new FakeFeedClient("{}", payload);
        var service = new WhisperModelReleaseCatalogService(
            feedClient,
            new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));
        var model = new WhisperRemoteModelAsset(
            "ggml-base.en-q8_0.bin",
            "https://example.com/ggml-base.en-q8_0.bin",
            payload.LongLength,
            true,
            "Recommended default for most laptops.");
        var progressUpdates = new List<FileDownloadProgress>();

        await service.DownloadRemoteModelIntoManagedDirectoryAsync(
            model,
            modelCacheDir,
            new Progress<FileDownloadProgress>(update => progressUpdates.Add(update)));

        Assert.True(progressUpdates.Count >= 2);
        Assert.Equal(payload.LongLength, progressUpdates[^1].BytesDownloaded);
        Assert.Equal(payload.LongLength, progressUpdates[^1].TotalBytes);
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

        public string? LastDownloadedUrl { get; private set; }

        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_payload);
        }

        public async Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            await DownloadFileAsync(downloadUrl, destinationPath, progress: null, cancellationToken);
        }

        public async Task DownloadFileAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<FileDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastDownloadedUrl = downloadUrl;
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var halfway = Math.Max(1, _downloadBytes.Length / 2);
            await using var stream = File.Create(destinationPath);
            await stream.WriteAsync(_downloadBytes.AsMemory(0, halfway), cancellationToken);
            progress?.Report(new FileDownloadProgress(halfway, _downloadBytes.LongLength));
            await stream.WriteAsync(_downloadBytes.AsMemory(halfway), cancellationToken);
            progress?.Report(new FileDownloadProgress(_downloadBytes.LongLength, _downloadBytes.LongLength));
        }
    }

    private sealed class FakeWhisperModelDownloader : IWhisperModelDownloader
    {
        private readonly byte[] _payload;

        public FakeWhisperModelDownloader(byte[] payload)
        {
            _payload = payload;
        }

        public Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(_payload, writable: false);
            return Task.FromResult(stream);
        }
    }
}
