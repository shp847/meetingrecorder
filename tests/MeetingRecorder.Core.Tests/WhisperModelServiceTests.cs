using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class WhisperModelServiceTests
{
    [Fact]
    public void Inspect_Returns_Missing_When_Model_File_Does_Not_Exist()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelPath = Path.Combine(root, "models", "asr", "ggml-base.bin");
        var service = new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>()));

        var status = service.Inspect(modelPath);

        Assert.Equal(WhisperModelStatusKind.Missing, status.Kind);
        Assert.Equal(0L, status.FileSizeBytes);
    }

    [Fact]
    public async Task Inspect_Returns_Invalid_When_Model_File_Is_Too_Small()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelPath = Path.Combine(root, "models", "asr", "ggml-base.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        await File.WriteAllBytesAsync(modelPath, new byte[8192]);
        var service = new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>()));

        var status = service.Inspect(modelPath);

        Assert.Equal(WhisperModelStatusKind.Invalid, status.Kind);
        Assert.Contains("not a valid ggml model", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportModelAsync_Copies_Valid_Model_To_Target_Path()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source", "ggml-base.bin");
        var targetPath = Path.Combine(root, "target", "ggml-base.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await CreateFileWithLengthAsync(sourcePath, WhisperModelService.MinimumExpectedModelBytes + 5000);
        var service = new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>()));

        var result = await service.ImportModelAsync(sourcePath, targetPath);

        Assert.Equal(targetPath, result.ModelPath);
        Assert.True(File.Exists(targetPath));
        Assert.True(new FileInfo(targetPath).Length > WhisperModelService.MinimumExpectedModelBytes);
    }

    [Fact]
    public async Task DownloadBaseModelAsync_Writes_Validated_Model_To_Target_Path()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var targetPath = Path.Combine(root, "models", "asr", "ggml-base.bin");
        var downloader = new FakeWhisperModelDownloader(new byte[WhisperModelService.MinimumExpectedModelBytes + 5000]);
        var service = new WhisperModelService(downloader);

        var result = await service.DownloadBaseModelAsync(targetPath);

        Assert.Equal(targetPath, result.ModelPath);
        Assert.True(File.Exists(targetPath));
        Assert.Equal(1, downloader.DownloadCount);
    }

    private static async Task CreateFileWithLengthAsync(string path, long length)
    {
        await using var stream = File.Create(path);
        stream.SetLength(length);
        await stream.FlushAsync();
    }

    private sealed class FakeWhisperModelDownloader : IWhisperModelDownloader
    {
        private readonly byte[] _payload;

        public FakeWhisperModelDownloader(byte[] payload)
        {
            _payload = payload;
        }

        public int DownloadCount { get; private set; }

        public Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadCount++;
            Stream stream = new MemoryStream(_payload, writable: false);
            return Task.FromResult(stream);
        }
    }
}
