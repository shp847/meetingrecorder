using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public sealed class ModelProvisioningService
{
    private readonly AppConfigStore _configStore;
    private readonly ModelProvisioningResultStore _resultStore;
    private readonly MeetingRecorderModelCatalogService _catalogService;
    private readonly WhisperModelService _whisperModelService;
    private readonly WhisperModelReleaseCatalogService _whisperModelReleaseCatalogService;
    private readonly DiarizationAssetCatalogService _diarizationAssetCatalogService;
    private readonly DiarizationAssetReleaseCatalogService _diarizationAssetReleaseCatalogService;

    public ModelProvisioningService(
        AppConfigStore configStore,
        ModelProvisioningResultStore resultStore,
        MeetingRecorderModelCatalogService catalogService,
        WhisperModelService whisperModelService,
        WhisperModelReleaseCatalogService whisperModelReleaseCatalogService,
        DiarizationAssetCatalogService diarizationAssetCatalogService,
        DiarizationAssetReleaseCatalogService diarizationAssetReleaseCatalogService)
    {
        _configStore = configStore;
        _resultStore = resultStore;
        _catalogService = catalogService;
        _whisperModelService = whisperModelService;
        _whisperModelReleaseCatalogService = whisperModelReleaseCatalogService;
        _diarizationAssetCatalogService = diarizationAssetCatalogService;
        _diarizationAssetReleaseCatalogService = diarizationAssetReleaseCatalogService;
    }

    public async Task<ModelProvisioningExecutionResult> ProvisionAsync(
        ModelProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();
        var existingConfigDetected = File.Exists(_configStore.ConfigPath);
        var config = await _configStore.LoadOrCreateAsync(cancellationToken);
        var catalog = _catalogService.Load(request.ModelCatalogPath);

        var standardTranscriptionPath = await SeedStandardTranscriptionAsync(
            request.InstallRoot,
            config.ModelCacheDir,
            catalog.Transcription.StandardIncluded,
            cancellationToken);
        var standardSpeakerLabelingPath = await SeedStandardSpeakerLabelingAsync(
            request.InstallRoot,
            config.ModelCacheDir,
            catalog.SpeakerLabeling.StandardIncluded,
            cancellationToken);

        var requestedTranscriptionProfile = existingConfigDetected && request.RespectExistingConfigPreferences
            ? config.TranscriptionModelProfilePreference
            : request.TranscriptionProfile;
        var requestedSpeakerLabelingProfile = existingConfigDetected && request.RespectExistingConfigPreferences
            ? config.SpeakerLabelingModelProfilePreference
            : request.SpeakerLabelingProfile;

        var transcriptionStatus = await ProvisionTranscriptionAsync(
            existingConfigDetected,
            config,
            catalog,
            request.UpdateFeedUrl,
            requestedTranscriptionProfile,
            standardTranscriptionPath,
            cancellationToken);
        var speakerLabelingStatus = await ProvisionSpeakerLabelingAsync(
            existingConfigDetected,
            config,
            catalog,
            request.UpdateFeedUrl,
            requestedSpeakerLabelingProfile,
            standardSpeakerLabelingPath,
            cancellationToken);

        var updatedConfig = await _configStore.SaveAsync(config with
        {
            TranscriptionModelPath = transcriptionStatus.ActiveModelPath,
            TranscriptionModelProfilePreference = transcriptionStatus.RequestedProfile,
            DiarizationAssetPath = speakerLabelingStatus.ActiveAssetPath,
            SpeakerLabelingModelProfilePreference = speakerLabelingStatus.RequestedProfile,
        }, cancellationToken);

        var result = new ModelProvisioningResult(
            DateTimeOffset.UtcNow,
            transcriptionStatus,
            speakerLabelingStatus);
        await _resultStore.SaveAsync(result, cancellationToken);

        return new ModelProvisioningExecutionResult(updatedConfig, result, existingConfigDetected);
    }

    private async Task<string> SeedStandardTranscriptionAsync(
        string installRoot,
        string modelCacheDir,
        CuratedModelArtifact standardArtifact,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(modelCacheDir, standardArtifact);
        var currentStatus = _whisperModelService.Inspect(targetPath);
        if (currentStatus.Kind == WhisperModelStatusKind.Valid)
        {
            return targetPath;
        }

        var sourcePath = _catalogService.ResolveSeedSourcePath(installRoot, standardArtifact);
        await _whisperModelService.ImportModelAsync(sourcePath, targetPath, cancellationToken);
        return targetPath;
    }

    private async Task<string> SeedStandardSpeakerLabelingAsync(
        string installRoot,
        string modelCacheDir,
        CuratedModelArtifact standardArtifact,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(modelCacheDir, standardArtifact);
        var currentStatus = _diarizationAssetCatalogService.InspectInstalledAssets(targetPath);
        if (currentStatus.IsReady)
        {
            return targetPath;
        }

        var sourcePath = _catalogService.ResolveSeedSourcePath(installRoot, standardArtifact);
        var installed = await _diarizationAssetCatalogService.ImportAssetIntoDirectoryAsync(
            sourcePath,
            targetPath,
            cancellationToken);
        if (!installed.IsReady)
        {
            throw new InvalidOperationException(installed.DetailsText);
        }

        return installed.AssetRootPath;
    }

    private async Task<TranscriptionModelProvisioningStatus> ProvisionTranscriptionAsync(
        bool existingConfigDetected,
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        string updateFeedUrl,
        TranscriptionModelProfilePreference requestedProfile,
        string standardTranscriptionPath,
        CancellationToken cancellationToken)
    {
        var configuredStatus = _whisperModelService.Inspect(config.TranscriptionModelPath);

        if (existingConfigDetected)
        {
            return requestedProfile switch
            {
                TranscriptionModelProfilePreference.StandardIncluded => BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.StandardIncluded,
                    retryRecommended: false,
                    "Transcription is ready with the included Standard model.",
                    "The bundled Standard transcription model is installed and active.",
                    standardTranscriptionPath),
                TranscriptionModelProfilePreference.HighAccuracyDownloaded when configuredStatus.Kind == WhisperModelStatusKind.Valid => BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                    retryRecommended: false,
                    "Transcription is ready with the Higher Accuracy model.",
                    "Your existing Higher Accuracy transcription model is still available and active.",
                    config.TranscriptionModelPath),
                TranscriptionModelProfilePreference.HighAccuracyDownloaded => BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.StandardIncluded,
                    retryRecommended: true,
                    "Transcription is ready with the included Standard model.",
                    "The previous Higher Accuracy transcription model is unavailable right now. You can retry it from Settings > Setup.",
                    standardTranscriptionPath),
                TranscriptionModelProfilePreference.Custom when configuredStatus.Kind == WhisperModelStatusKind.Valid => BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.Custom,
                    retryRecommended: false,
                    "Transcription is ready with your imported custom model.",
                    "Your existing custom transcription model is still available and active.",
                    config.TranscriptionModelPath),
                TranscriptionModelProfilePreference.Custom => BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.StandardIncluded,
                    retryRecommended: true,
                    "Transcription is ready with the included Standard model.",
                    "Your imported custom transcription model is unavailable right now. You can import it again from Settings > Setup.",
                    standardTranscriptionPath),
                _ => BuildTranscriptionStatus(
                    TranscriptionModelProfilePreference.StandardIncluded,
                    TranscriptionModelProfilePreference.StandardIncluded,
                    retryRecommended: false,
                    "Transcription is ready with the included Standard model.",
                    "The bundled Standard transcription model is installed and active.",
                    standardTranscriptionPath),
            };
        }

        if (requestedProfile != TranscriptionModelProfilePreference.HighAccuracyDownloaded)
        {
            return BuildTranscriptionStatus(
                TranscriptionModelProfilePreference.StandardIncluded,
                TranscriptionModelProfilePreference.StandardIncluded,
                retryRecommended: false,
                "Transcription is ready with the included Standard model.",
                "The bundled Standard transcription model was installed during setup.",
                standardTranscriptionPath);
        }

        try
        {
            var remoteModels = await _whisperModelReleaseCatalogService.ListAvailableRemoteModelsAsync(
                updateFeedUrl,
                cancellationToken);
            var highAccuracyAsset = _catalogService.FindTranscriptionHighAccuracyAsset(catalog, remoteModels)
                ?? throw new InvalidOperationException(
                    $"The Higher Accuracy transcription asset '{catalog.Transcription.HighAccuracy.FileName}' is not available in the current release.");
            var installed = await _whisperModelReleaseCatalogService.DownloadRemoteModelIntoManagedDirectoryAsync(
                highAccuracyAsset,
                config.ModelCacheDir,
                progress: null,
                cancellationToken);

            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                retryRecommended: false,
                "Transcription is ready with the Higher Accuracy model.",
                "The optional Higher Accuracy transcription download finished during setup.",
                installed.ModelPath);
        }
        catch (Exception exception)
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.StandardIncluded,
                retryRecommended: true,
                "Transcription is ready with the included Standard model.",
                $"The optional Higher Accuracy transcription download did not finish during setup. Retry it from Settings > Setup. Details: {exception.Message}",
                standardTranscriptionPath);
        }
    }

    private async Task<SpeakerLabelingModelProvisioningStatus> ProvisionSpeakerLabelingAsync(
        bool existingConfigDetected,
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        string updateFeedUrl,
        SpeakerLabelingModelProfilePreference requestedProfile,
        string standardSpeakerLabelingPath,
        CancellationToken cancellationToken)
    {
        var configuredStatus = _diarizationAssetCatalogService.InspectInstalledAssets(config.DiarizationAssetPath);

        if (existingConfigDetected)
        {
            return requestedProfile switch
            {
                SpeakerLabelingModelProfilePreference.StandardIncluded => BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.StandardIncluded,
                    retryRecommended: false,
                    "Speaker labeling is ready with the included Standard bundle.",
                    "The bundled Standard speaker-labeling bundle is installed and active.",
                    standardSpeakerLabelingPath),
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded when configuredStatus.IsReady => BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                    retryRecommended: false,
                    "Speaker labeling is ready with the Higher Accuracy bundle.",
                    "Your existing Higher Accuracy speaker-labeling bundle is still available and active.",
                    config.DiarizationAssetPath),
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded => BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.StandardIncluded,
                    retryRecommended: true,
                    "Speaker labeling is ready with the included Standard bundle.",
                    "The previous Higher Accuracy speaker-labeling bundle is unavailable right now. You can retry it from Settings > Setup.",
                    standardSpeakerLabelingPath),
                SpeakerLabelingModelProfilePreference.Custom when configuredStatus.IsReady => BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.Custom,
                    retryRecommended: false,
                    "Speaker labeling is ready with your imported custom bundle.",
                    "Your existing custom speaker-labeling bundle is still available and active.",
                    config.DiarizationAssetPath),
                SpeakerLabelingModelProfilePreference.Custom => BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.StandardIncluded,
                    retryRecommended: true,
                    "Speaker labeling is ready with the included Standard bundle.",
                    "Your imported custom speaker-labeling bundle is unavailable right now. You can import it again from Settings > Setup.",
                    standardSpeakerLabelingPath),
                _ => BuildSpeakerLabelingStatus(
                    SpeakerLabelingModelProfilePreference.StandardIncluded,
                    SpeakerLabelingModelProfilePreference.StandardIncluded,
                    retryRecommended: false,
                    "Speaker labeling is ready with the included Standard bundle.",
                    "The bundled Standard speaker-labeling bundle is installed and active.",
                    standardSpeakerLabelingPath),
            };
        }

        if (requestedProfile != SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded)
        {
            return BuildSpeakerLabelingStatus(
                SpeakerLabelingModelProfilePreference.StandardIncluded,
                SpeakerLabelingModelProfilePreference.StandardIncluded,
                retryRecommended: false,
                "Speaker labeling is ready with the included Standard bundle.",
                "The bundled Standard speaker-labeling bundle was installed during setup.",
                standardSpeakerLabelingPath);
        }

        try
        {
            var remoteAssets = await _diarizationAssetReleaseCatalogService.ListAvailableRemoteAssetsAsync(
                updateFeedUrl,
                cancellationToken);
            var highAccuracyAsset = _catalogService.FindSpeakerLabelingHighAccuracyAsset(catalog, remoteAssets)
                ?? throw new InvalidOperationException(
                    $"The Higher Accuracy speaker-labeling asset '{catalog.SpeakerLabeling.HighAccuracy.FileName}' is not available in the current release.");
            var highAccuracyPath = _catalogService.ResolveManagedPath(
                config.ModelCacheDir,
                catalog.SpeakerLabeling.HighAccuracy);
            var installed = await _diarizationAssetReleaseCatalogService.DownloadRemoteAssetIntoDirectoryAsync(
                highAccuracyAsset,
                highAccuracyPath,
                cancellationToken);

            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                retryRecommended: false,
                "Speaker labeling is ready with the Higher Accuracy bundle.",
                "The optional Higher Accuracy speaker-labeling download finished during setup.",
                installed.AssetRootPath);
        }
        catch (Exception exception)
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.StandardIncluded,
                retryRecommended: true,
                "Speaker labeling is ready with the included Standard bundle.",
                $"The optional Higher Accuracy speaker-labeling download did not finish during setup. Retry it from Settings > Setup. Details: {exception.Message}",
                standardSpeakerLabelingPath);
        }
    }

    private static TranscriptionModelProvisioningStatus BuildTranscriptionStatus(
        TranscriptionModelProfilePreference requestedProfile,
        TranscriptionModelProfilePreference activeProfile,
        bool retryRecommended,
        string summary,
        string detail,
        string activeModelPath)
    {
        return new TranscriptionModelProvisioningStatus(
            requestedProfile,
            activeProfile,
            retryRecommended,
            summary,
            detail,
            activeModelPath);
    }

    private static SpeakerLabelingModelProvisioningStatus BuildSpeakerLabelingStatus(
        SpeakerLabelingModelProfilePreference requestedProfile,
        SpeakerLabelingModelProfilePreference activeProfile,
        bool retryRecommended,
        string summary,
        string detail,
        string activeAssetPath)
    {
        return new SpeakerLabelingModelProvisioningStatus(
            requestedProfile,
            activeProfile,
            retryRecommended,
            summary,
            detail,
            activeAssetPath);
    }
}

public sealed record ModelProvisioningRequest(
    string InstallRoot,
    string ModelCatalogPath,
    string UpdateFeedUrl,
    TranscriptionModelProfilePreference TranscriptionProfile,
    SpeakerLabelingModelProfilePreference SpeakerLabelingProfile,
    bool RespectExistingConfigPreferences = true);

public sealed record ModelProvisioningExecutionResult(
    AppConfig Config,
    ModelProvisioningResult Result,
    bool ExistingConfigDetected);
