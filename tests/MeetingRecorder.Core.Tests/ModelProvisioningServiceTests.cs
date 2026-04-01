using System.IO.Compression;
using System.Text.Json;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ModelProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_Seeds_The_Standard_Profiles_For_A_Fresh_Offline_Install()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var service = fixture.CreateService(new StubAppUpdateFeedClient(throwOnFeedAccess: true));

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.StandardIncluded,
                SpeakerLabelingModelProfilePreference.StandardIncluded));

        Assert.False(result.ExistingConfigDetected);
        Assert.Equal(TranscriptionModelProfilePreference.StandardIncluded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.StandardTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.StandardIncluded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.StandardSpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.True(File.Exists(fixture.StandardTranscriptionTargetPath));
        Assert.True(Directory.Exists(fixture.StandardSpeakerLabelingTargetPath));
        Assert.True(File.Exists(fixture.ResultStorePath));
    }

    [Fact]
    public async Task ProvisionAsync_Downloads_The_Higher_Accuracy_Whisper_Model_When_Requested_On_A_Fresh_Install()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(includeHighAccuracyTranscription: true, includeHighAccuracySpeakerLabeling: false),
            downloads: new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [fixture.HighAccuracyTranscriptionDownloadUrl] = fixture.CreateWhisperModelBytes(),
            });
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                SpeakerLabelingModelProfilePreference.StandardIncluded));

        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.HighAccuracyTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Result.Transcription.ActiveProfile);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.True(File.Exists(fixture.HighAccuracyTranscriptionTargetPath));
    }

    [Fact]
    public async Task ProvisionAsync_Falls_Back_To_Standard_Transcription_When_The_Optional_Whisper_Download_Fails()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(includeHighAccuracyTranscription: true, includeHighAccuracySpeakerLabeling: false),
            downloads: new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
            failingDownloads: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                fixture.HighAccuracyTranscriptionDownloadUrl,
            });
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                SpeakerLabelingModelProfilePreference.StandardIncluded));

        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.StandardTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.StandardIncluded, result.Result.Transcription.ActiveProfile);
        Assert.True(result.Result.Transcription.RetryRecommended);
        Assert.Contains("Retry it from Settings > Setup", result.Result.Transcription.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_Falls_Back_To_Standard_Speaker_Labeling_When_The_Optional_Bundle_Download_Fails()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(includeHighAccuracyTranscription: false, includeHighAccuracySpeakerLabeling: true),
            downloads: new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
            failingDownloads: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                fixture.HighAccuracySpeakerLabelingDownloadUrl,
            });
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.StandardIncluded,
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded));

        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.StandardSpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.StandardIncluded, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.True(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.Contains("Retry it from Settings > Setup", result.Result.SpeakerLabeling.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_Preserves_Existing_Custom_And_High_Accuracy_Assets_On_Update()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.CustomTranscriptionPath)!);
        await File.WriteAllBytesAsync(fixture.CustomTranscriptionPath, fixture.CreateWhisperModelBytes());
        await fixture.CreateDiarizationBundleDirectoryAsync(fixture.HighAccuracySpeakerLabelingTargetPath, "accurate-existing");

        var seedConfigStore = fixture.CreateConfigStore();
        await seedConfigStore.SaveAsync(new AppConfig
        {
            AudioOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Recordings"),
            TranscriptOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Transcripts"),
            WorkDir = Path.Combine(fixture.AppRoot, "work"),
            ModelCacheDir = fixture.ModelCacheRoot,
            TranscriptionModelPath = fixture.CustomTranscriptionPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Custom,
            DiarizationAssetPath = fixture.HighAccuracySpeakerLabelingTargetPath,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
            DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
            MicCaptureEnabled = true,
            LaunchOnLoginEnabled = true,
            AutoDetectEnabled = false,
            AutoDetectSecurityPromptMigrationApplied = true,
            CalendarTitleFallbackEnabled = false,
            MeetingAttendeeEnrichmentEnabled = true,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = true,
            UpdateFeedUrl = "https://example.com/releases/latest",
        });

        var service = fixture.CreateService(new StubAppUpdateFeedClient(throwOnFeedAccess: true));
        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.StandardIncluded,
                SpeakerLabelingModelProfilePreference.StandardIncluded));

        Assert.True(result.ExistingConfigDetected);
        Assert.Equal(TranscriptionModelProfilePreference.Custom, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.CustomTranscriptionPath, result.Config.TranscriptionModelPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.HighAccuracySpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
    }

    private sealed class ProvisioningFixture
    {
        private ProvisioningFixture(string root)
        {
            Root = root;
            InstallRoot = Path.Combine(root, "install");
            AppRoot = Path.Combine(root, "appdata");
            DocumentsRoot = Path.Combine(root, "documents");
            ConfigPath = Path.Combine(AppRoot, "config", "appsettings.json");
            ResultStorePath = Path.Combine(AppRoot, "config", "model-provisioning-result.json");
            ModelCacheRoot = Path.Combine(AppRoot, "models");
            ModelCatalogPath = Path.Combine(InstallRoot, MeetingRecorderModelCatalogService.CatalogFileName);
            StandardTranscriptionTargetPath = Path.Combine(ModelCacheRoot, "asr", "ggml-base.en-q8_0.bin");
            HighAccuracyTranscriptionTargetPath = Path.Combine(ModelCacheRoot, "asr", "ggml-small.en-q8_0.bin");
            StandardSpeakerLabelingTargetPath = Path.Combine(ModelCacheRoot, "diarization", "standard");
            HighAccuracySpeakerLabelingTargetPath = Path.Combine(ModelCacheRoot, "diarization", "high-accuracy");
            CustomTranscriptionPath = Path.Combine(root, "custom-models", "custom-whisper.bin");
        }

        public string Root { get; }

        public string InstallRoot { get; }

        public string AppRoot { get; }

        public string DocumentsRoot { get; }

        public string ConfigPath { get; }

        public string ResultStorePath { get; }

        public string ModelCacheRoot { get; }

        public string ModelCatalogPath { get; }

        public string StandardTranscriptionTargetPath { get; }

        public string HighAccuracyTranscriptionTargetPath { get; }

        public string StandardSpeakerLabelingTargetPath { get; }

        public string HighAccuracySpeakerLabelingTargetPath { get; }

        public string CustomTranscriptionPath { get; }

        public string HighAccuracyTranscriptionDownloadUrl => "https://example.com/models/ggml-small.en-q8_0.bin";

        public string HighAccuracySpeakerLabelingDownloadUrl => "https://example.com/models/meeting-recorder-diarization-bundle-accurate-win-x64.zip";

        public static async Task<ProvisioningFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderProvisioningTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var fixture = new ProvisioningFixture(root);
            Directory.CreateDirectory(fixture.InstallRoot);
            Directory.CreateDirectory(fixture.AppRoot);
            Directory.CreateDirectory(fixture.DocumentsRoot);

            var catalog = MeetingRecorderModelCatalog.CreateDefault();
            await File.WriteAllTextAsync(
                fixture.ModelCatalogPath,
                JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));

            var standardTranscriptionSeedPath = Path.Combine(
                fixture.InstallRoot,
                "model-seed",
                "transcription",
                catalog.Transcription.StandardIncluded.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(standardTranscriptionSeedPath)!);
            await File.WriteAllBytesAsync(standardTranscriptionSeedPath, fixture.CreateWhisperModelBytes());

            var standardSpeakerSeedPath = Path.Combine(
                fixture.InstallRoot,
                "model-seed",
                "speaker-labeling",
                catalog.SpeakerLabeling.StandardIncluded.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(standardSpeakerSeedPath)!);
            await fixture.CreateDiarizationBundleZipAsync(standardSpeakerSeedPath, "standard-included");

            return fixture;
        }

        public ModelProvisioningService CreateService(StubAppUpdateFeedClient feedClient)
        {
            var configStore = CreateConfigStore();
            var resultStore = new ModelProvisioningResultStore(ConfigPath);
            var catalogService = new MeetingRecorderModelCatalogService();
            var whisperModelService = new WhisperModelService(new NeverCalledWhisperModelDownloader());
            var whisperReleaseCatalogService = new WhisperModelReleaseCatalogService(feedClient, whisperModelService);
            var diarizationCatalogService = new DiarizationAssetCatalogService();
            var diarizationReleaseCatalogService = new DiarizationAssetReleaseCatalogService(feedClient, diarizationCatalogService);

            return new ModelProvisioningService(
                configStore,
                resultStore,
                catalogService,
                whisperModelService,
                whisperReleaseCatalogService,
                diarizationCatalogService,
                diarizationReleaseCatalogService);
        }

        public AppConfigStore CreateConfigStore()
        {
            return new AppConfigStore(ConfigPath, DocumentsRoot);
        }

        public byte[] CreateWhisperModelBytes()
        {
            return Enumerable.Repeat((byte)0x7A, (int)WhisperModelService.MinimumExpectedModelBytes + 2048).ToArray();
        }

        public string CreateReleasePayload(bool includeHighAccuracyTranscription, bool includeHighAccuracySpeakerLabeling)
        {
            var assets = new List<string>();
            if (includeHighAccuracyTranscription)
            {
                assets.Add(
                    $$"""
                    {
                      "name": "ggml-small.en-q8_0.bin",
                      "browser_download_url": "{{HighAccuracyTranscriptionDownloadUrl}}",
                      "size": {{CreateWhisperModelBytes().Length}}
                    }
                    """);
            }

            if (includeHighAccuracySpeakerLabeling)
            {
                assets.Add(
                    $$"""
                    {
                      "name": "meeting-recorder-diarization-bundle-accurate-win-x64.zip",
                      "browser_download_url": "{{HighAccuracySpeakerLabelingDownloadUrl}}",
                      "size": 4096
                    }
                    """);
            }

            return $$"""
            {
              "assets": [
                {{string.Join("," + Environment.NewLine, assets)}}
              ]
            }
            """;
        }

        public async Task CreateDiarizationBundleZipAsync(string zipPath, string bundleVersion)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            var stagingRoot = Path.Combine(Path.GetTempPath(), "MeetingRecorderProvisioningBundle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(stagingRoot, DiarizationAssetCatalogService.BundleManifestFileName),
                    """
                    {
                      "bundleVersion": "placeholder",
                      "segmentationModelFileName": "model.int8.onnx",
                      "embeddingModelFileName": "nemo_en_titanet_small.onnx"
                    }
                    """.Replace("placeholder", bundleVersion, StringComparison.Ordinal));
                await File.WriteAllTextAsync(Path.Combine(stagingRoot, "model.int8.onnx"), "segmentation");
                await File.WriteAllTextAsync(Path.Combine(stagingRoot, "nemo_en_titanet_small.onnx"), "embedding");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(stagingRoot, zipPath);
            }
            finally
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
        }

        public async Task CreateDiarizationBundleDirectoryAsync(string directoryPath, string bundleVersion)
        {
            Directory.CreateDirectory(directoryPath);
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, DiarizationAssetCatalogService.BundleManifestFileName),
                """
                {
                  "bundleVersion": "placeholder",
                  "segmentationModelFileName": "model.int8.onnx",
                  "embeddingModelFileName": "nemo_en_titanet_small.onnx"
                }
                """.Replace("placeholder", bundleVersion, StringComparison.Ordinal));
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "model.int8.onnx"), "segmentation");
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "nemo_en_titanet_small.onnx"), "embedding");
        }
    }

    private sealed class StubAppUpdateFeedClient : IAppUpdateFeedClient
    {
        private readonly string _payload;
        private readonly IReadOnlyDictionary<string, byte[]> _downloads;
        private readonly IReadOnlySet<string> _failingDownloads;
        private readonly bool _throwOnFeedAccess;

        public StubAppUpdateFeedClient(
            string payload = """{ "assets": [] }""",
            IReadOnlyDictionary<string, byte[]>? downloads = null,
            IReadOnlySet<string>? failingDownloads = null,
            bool throwOnFeedAccess = false)
        {
            _payload = payload;
            _downloads = downloads ?? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            _failingDownloads = failingDownloads ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _throwOnFeedAccess = throwOnFeedAccess;
        }

        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            if (_throwOnFeedAccess)
            {
                throw new InvalidOperationException("Feed access should not happen for this test.");
            }

            return Task.FromResult(_payload);
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            return DownloadFileAsync(downloadUrl, destinationPath, progress: null, cancellationToken);
        }

        public async Task DownloadFileAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<FileDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (_failingDownloads.Contains(downloadUrl))
            {
                throw new InvalidOperationException("Simulated download failure.");
            }

            if (!_downloads.TryGetValue(downloadUrl, out var payload))
            {
                throw new InvalidOperationException($"No payload was registered for '{downloadUrl}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, payload, cancellationToken);
            progress?.Report(new FileDownloadProgress(payload.LongLength, payload.LongLength));
        }
    }

    private sealed class NeverCalledWhisperModelDownloader : IWhisperModelDownloader
    {
        public Task<Stream> DownloadBaseModelAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The base Whisper downloader should not be used during provisioning tests.");
        }
    }
}
