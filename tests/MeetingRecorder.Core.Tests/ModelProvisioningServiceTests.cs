using System.IO.Compression;
using System.Text.Json;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ModelProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_Downloads_The_Standard_Profiles_For_A_Fresh_Install()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false));
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.False(result.ExistingConfigDetected);
        Assert.Equal(TranscriptionModelProfilePreference.Standard, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.StandardTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Standard, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.StandardSpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.Equal(TranscriptionModelProfilePreference.Standard, result.Result.Transcription.ActiveProfile);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Standard, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.True(result.Result.SpeakerLabeling.IsReady);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
        Assert.True(File.Exists(fixture.StandardTranscriptionTargetPath));
        Assert.True(Directory.Exists(fixture.StandardSpeakerLabelingTargetPath));
        Assert.True(File.Exists(fixture.ResultStorePath));
    }

    [Fact]
    public async Task ProvisionAsync_Completes_Install_But_Requires_FirstLaunch_Setup_When_Standard_Transcription_Download_Fails()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: false,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            failingDownloads: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                fixture.StandardTranscriptionDownloadUrl,
            });
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.Equal(TranscriptionModelProfilePreference.Standard, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.StandardTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.Standard, result.Result.Transcription.ActiveProfile);
        Assert.False(result.Result.Transcription.IsReady);
        Assert.True(result.Result.Transcription.RetryRecommended);
        Assert.Contains("resume setup at first launch", result.Result.Transcription.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Result.RequiresFirstLaunchSetupBeforeRecording);
        Assert.True(result.Result.SpeakerLabeling.IsReady);
        Assert.False(File.Exists(fixture.StandardTranscriptionTargetPath));
    }

    [Fact]
    public async Task ProvisionAsync_Downloads_The_Higher_Accuracy_Whisper_Model_When_Requested_On_A_Fresh_Install()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false));
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.HighAccuracyTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Result.Transcription.ActiveProfile);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
        Assert.True(File.Exists(fixture.HighAccuracyTranscriptionTargetPath));
    }

    [Fact]
    public async Task ProvisionAsync_Falls_Back_To_Standard_Transcription_When_The_Optional_Whisper_Download_Fails()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
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
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.StandardTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.Standard, result.Result.Transcription.ActiveProfile);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.True(result.Result.Transcription.RetryRecommended);
        Assert.Contains("Retry it from Settings > Setup", result.Result.Transcription.Detail, StringComparison.Ordinal);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
    }

    [Fact]
    public async Task ProvisionAsync_Keeps_Recording_Ready_When_The_Standard_SpeakerLabeling_Download_Fails()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: false),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: false,
                includeStandardSpeakerLabeling: false,
                includeHighAccuracySpeakerLabeling: false),
            failingDownloads: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                fixture.StandardSpeakerLabelingDownloadUrl,
            });
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.True(result.Result.Transcription.IsReady);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Standard, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.False(result.Result.SpeakerLabeling.IsReady);
        Assert.True(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.Contains("Speaker labeling stays optional", result.Result.SpeakerLabeling.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_Replaces_Existing_Standard_Transcription_Model_When_Higher_Accuracy_Is_Requested()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StandardTranscriptionTargetPath)!);
        await File.WriteAllBytesAsync(fixture.StandardTranscriptionTargetPath, fixture.CreateWhisperModelBytes());

        var seedConfigStore = fixture.CreateConfigStore();
        await seedConfigStore.SaveAsync(new AppConfig
        {
            AudioOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Recordings"),
            TranscriptOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Transcripts"),
            WorkDir = Path.Combine(fixture.AppRoot, "work"),
            ModelCacheDir = fixture.ModelCacheRoot,
            TranscriptionModelPath = fixture.StandardTranscriptionTargetPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Standard,
            DiarizationAssetPath = fixture.StandardSpeakerLabelingTargetPath,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Standard,
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

        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true));
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                SpeakerLabelingModelProfilePreference.Standard,
                RespectExistingConfigPreferences: false));

        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.HighAccuracyTranscriptionTargetPath, result.Config.TranscriptionModelPath);
        Assert.Equal(TranscriptionModelProfilePreference.HighAccuracyDownloaded, result.Result.Transcription.ActiveProfile);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.True(File.Exists(fixture.HighAccuracyTranscriptionTargetPath));
    }

    [Fact]
    public async Task ProvisionAsync_Replaces_Existing_Standard_SpeakerLabeling_Bundle_When_Higher_Accuracy_Is_Requested()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        await fixture.CreateDiarizationBundleDirectoryAsync(fixture.StandardSpeakerLabelingTargetPath, "standard-existing");

        var seedConfigStore = fixture.CreateConfigStore();
        await seedConfigStore.SaveAsync(new AppConfig
        {
            AudioOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Recordings"),
            TranscriptOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Transcripts"),
            WorkDir = Path.Combine(fixture.AppRoot, "work"),
            ModelCacheDir = fixture.ModelCacheRoot,
            TranscriptionModelPath = fixture.StandardTranscriptionTargetPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Standard,
            DiarizationAssetPath = fixture.StandardSpeakerLabelingTargetPath,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Standard,
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

        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true));
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                RespectExistingConfigPreferences: false));

        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.HighAccuracySpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.True(result.Result.SpeakerLabeling.IsReady);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.True(Directory.Exists(fixture.HighAccuracySpeakerLabelingTargetPath));
    }

    [Fact]
    public async Task ProvisionAsync_Falls_Back_To_The_Default_Bundled_Catalog_When_The_Catalog_File_Is_Missing()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        await fixture.CreateDiarizationBundleDirectoryAsync(fixture.StandardSpeakerLabelingTargetPath, "standard-existing");
        File.Delete(fixture.ModelCatalogPath);

        var seedConfigStore = fixture.CreateConfigStore();
        await seedConfigStore.SaveAsync(new AppConfig
        {
            AudioOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Recordings"),
            TranscriptOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Transcripts"),
            WorkDir = Path.Combine(fixture.AppRoot, "work"),
            ModelCacheDir = fixture.ModelCacheRoot,
            TranscriptionModelPath = fixture.StandardTranscriptionTargetPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Standard,
            DiarizationAssetPath = fixture.StandardSpeakerLabelingTargetPath,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Standard,
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

        var feedClient = new StubAppUpdateFeedClient(
            payload: fixture.CreateReleasePayload(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true),
            downloads: await fixture.CreateDownloadsAsync(
                includeStandardTranscription: true,
                includeHighAccuracyTranscription: true,
                includeStandardSpeakerLabeling: true,
                includeHighAccuracySpeakerLabeling: true));
        var service = fixture.CreateService(feedClient);

        var result = await service.ProvisionAsync(
            new ModelProvisioningRequest(
                fixture.InstallRoot,
                fixture.ModelCatalogPath,
                "https://example.com/releases/latest",
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                RespectExistingConfigPreferences: false));

        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.HighAccuracySpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.True(result.Result.SpeakerLabeling.IsReady);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
    }

    [Fact]
    public async Task ProvisionAsync_Allows_SpeakerLabeling_To_Stay_Off_For_Now()
    {
        var fixture = await ProvisioningFixture.CreateAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.CustomTranscriptionPath)!);
        await File.WriteAllBytesAsync(fixture.CustomTranscriptionPath, fixture.CreateWhisperModelBytes());
        await fixture.CreateConfigStore().SaveAsync(new AppConfig
        {
            AudioOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Recordings"),
            TranscriptOutputDir = Path.Combine(fixture.DocumentsRoot, "Meetings", "Transcripts"),
            WorkDir = Path.Combine(fixture.AppRoot, "work"),
            ModelCacheDir = fixture.ModelCacheRoot,
            TranscriptionModelPath = fixture.CustomTranscriptionPath,
            TranscriptionModelProfilePreference = TranscriptionModelProfilePreference.Custom,
            DiarizationAssetPath = string.Empty,
            SpeakerLabelingModelProfilePreference = SpeakerLabelingModelProfilePreference.Disabled,
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
                TranscriptionModelProfilePreference.Custom,
                SpeakerLabelingModelProfilePreference.Disabled));

        Assert.True(result.ExistingConfigDetected);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Disabled, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(string.Empty, result.Config.DiarizationAssetPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Disabled, result.Result.SpeakerLabeling.RequestedProfile);
        Assert.Equal(SpeakerLabelingModelProfilePreference.Disabled, result.Result.SpeakerLabeling.ActiveProfile);
        Assert.False(result.Result.SpeakerLabeling.IsReady);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.Contains("turned off for now", result.Result.SpeakerLabeling.Detail, StringComparison.OrdinalIgnoreCase);
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
                TranscriptionModelProfilePreference.Standard,
                SpeakerLabelingModelProfilePreference.Standard));

        Assert.True(result.ExistingConfigDetected);
        Assert.Equal(TranscriptionModelProfilePreference.Custom, result.Config.TranscriptionModelProfilePreference);
        Assert.Equal(fixture.CustomTranscriptionPath, result.Config.TranscriptionModelPath);
        Assert.Equal(SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded, result.Config.SpeakerLabelingModelProfilePreference);
        Assert.Equal(fixture.HighAccuracySpeakerLabelingTargetPath, result.Config.DiarizationAssetPath);
        Assert.True(result.Result.Transcription.IsReady);
        Assert.True(result.Result.SpeakerLabeling.IsReady);
        Assert.False(result.Result.Transcription.RetryRecommended);
        Assert.False(result.Result.SpeakerLabeling.RetryRecommended);
        Assert.False(result.Result.RequiresFirstLaunchSetupBeforeRecording);
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

        public string StandardTranscriptionDownloadUrl => "https://example.com/models/ggml-base.en-q8_0.bin";

        public string HighAccuracyTranscriptionDownloadUrl => "https://example.com/models/ggml-small.en-q8_0.bin";

        public string StandardSpeakerLabelingDownloadUrl => "https://example.com/models/meeting-recorder-diarization-bundle-standard-win-x64.zip";

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

        public async Task<IReadOnlyDictionary<string, byte[]>> CreateDownloadsAsync(
            bool includeStandardTranscription,
            bool includeHighAccuracyTranscription,
            bool includeStandardSpeakerLabeling,
            bool includeHighAccuracySpeakerLabeling)
        {
            var downloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (includeStandardTranscription)
            {
                downloads[StandardTranscriptionDownloadUrl] = CreateWhisperModelBytes();
            }

            if (includeHighAccuracyTranscription)
            {
                downloads[HighAccuracyTranscriptionDownloadUrl] = CreateWhisperModelBytes();
            }

            if (includeStandardSpeakerLabeling)
            {
                downloads[StandardSpeakerLabelingDownloadUrl] = await CreateDiarizationBundleZipBytesAsync("standard-download");
            }

            if (includeHighAccuracySpeakerLabeling)
            {
                downloads[HighAccuracySpeakerLabelingDownloadUrl] = await CreateDiarizationBundleZipBytesAsync("accurate-download");
            }

            return downloads;
        }

        public string CreateReleasePayload(
            bool includeStandardTranscription,
            bool includeHighAccuracyTranscription,
            bool includeStandardSpeakerLabeling,
            bool includeHighAccuracySpeakerLabeling)
        {
            var assets = new List<string>();
            if (includeStandardTranscription)
            {
                assets.Add(
                    $$"""
                    {
                      "name": "ggml-base.en-q8_0.bin",
                      "browser_download_url": "{{StandardTranscriptionDownloadUrl}}",
                      "size": {{CreateWhisperModelBytes().Length}}
                    }
                    """);
            }

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

            if (includeStandardSpeakerLabeling)
            {
                assets.Add(
                    $$"""
                    {
                      "name": "meeting-recorder-diarization-bundle-standard-win-x64.zip",
                      "browser_download_url": "{{StandardSpeakerLabelingDownloadUrl}}",
                      "size": 4096
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

        public async Task<byte[]> CreateDiarizationBundleZipBytesAsync(string bundleVersion)
        {
            var zipPath = Path.Combine(Path.GetTempPath(), "MeetingRecorderProvisioningBundle", Guid.NewGuid().ToString("N") + ".zip");
            await CreateDiarizationBundleZipAsync(zipPath, bundleVersion);
            try
            {
                return await File.ReadAllBytesAsync(zipPath);
            }
            finally
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
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
