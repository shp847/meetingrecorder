using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class WhisperModelCatalogServiceTests
{
    [Fact]
    public async Task ListAvailableModels_Includes_Managed_Models_And_Marks_The_Configured_Model()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var baseModelPath = Path.Combine(modelCacheDir, "asr", "ggml-base.bin");
        var smallModelPath = Path.Combine(modelCacheDir, "asr", "ggml-small.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(baseModelPath)!);
        await CreateFileWithLengthAsync(baseModelPath, WhisperModelService.MinimumExpectedModelBytes + 5_000);
        await CreateFileWithLengthAsync(smallModelPath, WhisperModelService.MinimumExpectedModelBytes + 7_500);

        var service = new WhisperModelCatalogService(new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));

        var items = service.ListAvailableModels(modelCacheDir, smallModelPath);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => string.Equals(item.ModelPath, baseModelPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, item =>
            string.Equals(item.ModelPath, smallModelPath, StringComparison.OrdinalIgnoreCase) &&
            item.IsConfigured &&
            item.Status.Kind == WhisperModelStatusKind.Valid);
    }

    [Fact]
    public async Task ImportModelIntoManagedDirectoryAsync_Copies_Source_File_Using_The_Source_File_Name()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var sourcePath = Path.Combine(root, "incoming", "ggml-small.en.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await CreateFileWithLengthAsync(sourcePath, WhisperModelService.MinimumExpectedModelBytes + 9_000);

        var service = new WhisperModelCatalogService(new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));

        var imported = await service.ImportModelIntoManagedDirectoryAsync(sourcePath, modelCacheDir);

        Assert.Equal(Path.Combine(modelCacheDir, "asr", "ggml-small.en.bin"), imported.ModelPath);
        Assert.True(File.Exists(imported.ModelPath));
        Assert.Equal(WhisperModelStatusKind.Valid, imported.Status.Kind);
    }

    [Fact]
    public async Task ListAvailableModels_Includes_Configured_External_Model_When_Outside_Managed_Directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var externalModelPath = Path.Combine(root, "external", "ggml-custom.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(externalModelPath)!);
        await CreateFileWithLengthAsync(externalModelPath, WhisperModelService.MinimumExpectedModelBytes + 11_000);

        var service = new WhisperModelCatalogService(new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));

        var items = service.ListAvailableModels(modelCacheDir, externalModelPath);

        Assert.Single(items);
        Assert.Equal(externalModelPath, items[0].ModelPath);
        Assert.True(items[0].IsConfigured);
        Assert.False(items[0].IsManaged);
    }

    [Fact]
    public async Task ResolveConfiguredOrFallbackModel_Picks_A_Valid_Managed_Model_When_Configured_Path_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var modelCacheDir = Path.Combine(root, "models");
        var missingConfiguredPath = Path.Combine(root, "missing", "ggml-missing.bin");
        var baseModelPath = Path.Combine(modelCacheDir, "asr", "ggml-base.bin");
        var smallModelPath = Path.Combine(modelCacheDir, "asr", "ggml-small.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(baseModelPath)!);
        await CreateFileWithLengthAsync(baseModelPath, WhisperModelService.MinimumExpectedModelBytes + 5_000);
        await CreateFileWithLengthAsync(smallModelPath, WhisperModelService.MinimumExpectedModelBytes + 7_500);

        var service = new WhisperModelCatalogService(new WhisperModelService(new FakeWhisperModelDownloader(Array.Empty<byte>())));

        var resolution = service.ResolveConfiguredOrFallbackModel(modelCacheDir, missingConfiguredPath);

        Assert.NotNull(resolution.ActiveModel);
        Assert.True(resolution.UsedFallbackModel);
        Assert.Equal(baseModelPath, resolution.ActiveModel!.ModelPath);
        Assert.Equal(missingConfiguredPath, resolution.RequestedModelPath);
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

        public Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(_payload, writable: false);
            return Task.FromResult(stream);
        }
    }
}
