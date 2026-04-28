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
        var catalog = LoadCatalog(request);

        var requestedTranscriptionProfile = existingConfigDetected && request.RespectExistingConfigPreferences
            ? config.TranscriptionModelProfilePreference
            : request.TranscriptionProfile;
        var requestedSpeakerLabelingProfile = existingConfigDetected && request.RespectExistingConfigPreferences
            ? config.SpeakerLabelingModelProfilePreference
            : request.SpeakerLabelingProfile;

        IReadOnlyList<WhisperRemoteModelAsset>? remoteModels = null;
        IReadOnlyList<DiarizationRemoteAsset>? remoteDiarizationAssets = null;

        async Task<IReadOnlyList<WhisperRemoteModelAsset>> GetRemoteModelsAsync()
        {
            remoteModels ??= await _whisperModelReleaseCatalogService.ListAvailableRemoteModelsAsync(
                request.UpdateFeedUrl,
                cancellationToken);
            return remoteModels;
        }

        async Task<IReadOnlyList<DiarizationRemoteAsset>> GetRemoteDiarizationAssetsAsync()
        {
            remoteDiarizationAssets ??= await _diarizationAssetReleaseCatalogService.ListAvailableRemoteAssetsAsync(
                request.UpdateFeedUrl,
                cancellationToken);
            return remoteDiarizationAssets;
        }

        var transcriptionStatus = await ProvisionTranscriptionAsync(
            existingConfigDetected,
            config,
            catalog,
            requestedTranscriptionProfile,
            GetRemoteModelsAsync,
            cancellationToken);
        var speakerLabelingStatus = await ProvisionSpeakerLabelingAsync(
            existingConfigDetected,
            config,
            catalog,
            requestedSpeakerLabelingProfile,
            GetRemoteDiarizationAssetsAsync,
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
            speakerLabelingStatus,
            RequiresFirstLaunchSetupBeforeRecording: !transcriptionStatus.IsReady);
        await _resultStore.SaveAsync(result, cancellationToken);

        return new ModelProvisioningExecutionResult(updatedConfig, result, existingConfigDetected);
    }

    private MeetingRecorderModelCatalog LoadCatalog(ModelProvisioningRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ModelCatalogPath))
        {
            return _catalogService.LoadBundledCatalog(request.InstallRoot);
        }

        var bundledCatalogPath = _catalogService.GetBundledCatalogPath(request.InstallRoot);
        if (!File.Exists(request.ModelCatalogPath) &&
            string.Equals(
                Path.GetFullPath(request.ModelCatalogPath),
                Path.GetFullPath(bundledCatalogPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return _catalogService.LoadBundledCatalog(request.InstallRoot);
        }

        return _catalogService.Load(request.ModelCatalogPath);
    }

    private async Task<TranscriptionModelProvisioningStatus> ProvisionTranscriptionAsync(
        bool existingConfigDetected,
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        TranscriptionModelProfilePreference requestedProfile,
        Func<Task<IReadOnlyList<WhisperRemoteModelAsset>>> getRemoteModelsAsync,
        CancellationToken cancellationToken)
    {
        var configuredStatus = _whisperModelService.Inspect(config.TranscriptionModelPath);
        var standardTargetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.Transcription.Standard);
        var highAccuracyTargetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.Transcription.HighAccuracy);

        if (requestedProfile == TranscriptionModelProfilePreference.Custom &&
            configuredStatus.Kind == WhisperModelStatusKind.Valid)
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.Custom,
                retryRecommended: false,
                isReady: true,
                "Transcription is ready with your imported custom model.",
                "Your existing custom transcription model is still available and active.",
                config.TranscriptionModelPath);
        }

        if (requestedProfile == TranscriptionModelProfilePreference.HighAccuracyDownloaded &&
            configuredStatus.Kind == WhisperModelStatusKind.Valid &&
            string.Equals(
                Path.GetFullPath(config.TranscriptionModelPath),
                Path.GetFullPath(highAccuracyTargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                retryRecommended: false,
                isReady: true,
                "Transcription is ready with the Higher Accuracy model.",
                existingConfigDetected
                    ? "Your existing Higher Accuracy transcription model is still available and active."
                    : "The Higher Accuracy transcription download finished during setup.",
                config.TranscriptionModelPath);
        }

        if (requestedProfile == TranscriptionModelProfilePreference.Standard &&
            configuredStatus.Kind == WhisperModelStatusKind.Valid &&
            string.Equals(
                Path.GetFullPath(config.TranscriptionModelPath),
                Path.GetFullPath(standardTargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.Standard,
                retryRecommended: false,
                isReady: true,
                "Transcription is ready with the Standard model.",
                existingConfigDetected
                    ? "The Standard transcription model is still available and active."
                    : "The Standard transcription download finished during setup.",
                config.TranscriptionModelPath);
        }

        DownloadedWhisperModel? standardDownload = null;

        if (requestedProfile == TranscriptionModelProfilePreference.HighAccuracyDownloaded)
        {
            var highAccuracyDownload = await TryDownloadHighAccuracyTranscriptionAsync(
                config,
                catalog,
                getRemoteModelsAsync,
                cancellationToken);
            if (highAccuracyDownload.IsReady)
            {
                return BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.HighAccuracyDownloaded,
                    retryRecommended: false,
                    isReady: true,
                    "Transcription is ready with the Higher Accuracy model.",
                    "The Higher Accuracy transcription download finished during setup.",
                    highAccuracyDownload.ModelPath);
            }

            standardDownload = await TryDownloadStandardTranscriptionAsync(
                config,
                catalog,
                getRemoteModelsAsync,
                cancellationToken);
            if (standardDownload.IsReady)
            {
                return BuildTranscriptionStatus(
                    requestedProfile,
                    TranscriptionModelProfilePreference.Standard,
                    retryRecommended: true,
                    isReady: true,
                    "Transcription is ready with the Standard model.",
                    $"The Higher Accuracy transcription download did not finish during setup. Retry it from Settings > Setup. Details: {highAccuracyDownload.FailureMessage}",
                    standardDownload.ModelPath);
            }

            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.Standard,
                retryRecommended: true,
                isReady: false,
                "Transcription still needs setup.",
                    $"Meeting Recorder could not finish the Higher Accuracy or Standard transcription downloads. Recording stays blocked until you resume setup at first launch or import an approved model. Details: {highAccuracyDownload.FailureMessage} {standardDownload.FailureMessage}".Trim(),
                standardTargetPath);
        }

        standardDownload = await TryDownloadStandardTranscriptionAsync(
            config,
            catalog,
            getRemoteModelsAsync,
            cancellationToken);
        if (standardDownload.IsReady)
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.Standard,
                retryRecommended: false,
                isReady: true,
                "Transcription is ready with the Standard model.",
                "The Standard transcription download finished during setup.",
                standardDownload.ModelPath);
        }

        if (requestedProfile == TranscriptionModelProfilePreference.Custom)
        {
            return BuildTranscriptionStatus(
                requestedProfile,
                TranscriptionModelProfilePreference.Standard,
                retryRecommended: true,
                isReady: false,
                "Transcription still needs setup.",
                $"Your imported custom transcription model is unavailable, and Meeting Recorder could not finish the Standard transcription download. Recording stays blocked until you resume setup at first launch or import an approved model. Details: {standardDownload.FailureMessage}",
                standardTargetPath);
        }

        return BuildTranscriptionStatus(
            requestedProfile,
            TranscriptionModelProfilePreference.Standard,
            retryRecommended: true,
            isReady: false,
            "Transcription still needs setup.",
            $"Meeting Recorder could not finish the Standard transcription download during install. Recording stays blocked until you resume setup at first launch or import an approved model. Details: {standardDownload.FailureMessage}",
            standardTargetPath);
    }

    private async Task<SpeakerLabelingModelProvisioningStatus> ProvisionSpeakerLabelingAsync(
        bool existingConfigDetected,
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        SpeakerLabelingModelProfilePreference requestedProfile,
        Func<Task<IReadOnlyList<DiarizationRemoteAsset>>> getRemoteDiarizationAssetsAsync,
        CancellationToken cancellationToken)
    {
        if (requestedProfile == SpeakerLabelingModelProfilePreference.Disabled)
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Disabled,
                retryRecommended: false,
                isReady: false,
                "Optional",
                existingConfigDetected
                    ? "Speaker labeling is turned off for now. Recording stays available as long as transcription is ready."
                    : "Speaker labeling was skipped during setup. Recording stays available as long as transcription is ready, and you can add speaker labeling later from Settings > Setup.",
                string.Empty);
        }

        var configuredStatus = _diarizationAssetCatalogService.InspectInstalledAssets(config.DiarizationAssetPath);
        var standardTargetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.SpeakerLabeling.Standard);
        var highAccuracyTargetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.SpeakerLabeling.HighAccuracy);

        if (requestedProfile == SpeakerLabelingModelProfilePreference.Custom &&
            configuredStatus.IsReady)
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Custom,
                retryRecommended: false,
                isReady: true,
                "Speaker labeling is ready with your imported custom bundle.",
                "Your existing custom speaker-labeling bundle is still available and active.",
                config.DiarizationAssetPath);
        }

        if (requestedProfile == SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded &&
            configuredStatus.IsReady &&
            string.Equals(
                Path.GetFullPath(config.DiarizationAssetPath),
                Path.GetFullPath(highAccuracyTargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                retryRecommended: false,
                isReady: true,
                "Speaker labeling is ready with the Higher Accuracy bundle.",
                existingConfigDetected
                    ? "Your existing Higher Accuracy speaker-labeling bundle is still available and active."
                    : "The Higher Accuracy speaker-labeling download finished during setup.",
                config.DiarizationAssetPath);
        }

        if (requestedProfile == SpeakerLabelingModelProfilePreference.Standard &&
            configuredStatus.IsReady &&
            string.Equals(
                Path.GetFullPath(config.DiarizationAssetPath),
                Path.GetFullPath(standardTargetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Standard,
                retryRecommended: false,
                isReady: true,
                "Speaker labeling is ready with the Standard bundle.",
                existingConfigDetected
                    ? "The Standard speaker-labeling bundle is still available and active."
                    : "The Standard speaker-labeling download finished during setup.",
                config.DiarizationAssetPath);
        }

        DownloadedDiarizationAsset? standardDownload = null;

        if (requestedProfile == SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded)
        {
            var highAccuracyDownload = await TryDownloadHighAccuracySpeakerLabelingAsync(
                config,
                catalog,
                getRemoteDiarizationAssetsAsync,
                cancellationToken);
            if (highAccuracyDownload.IsReady)
            {
                return BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded,
                    retryRecommended: false,
                    isReady: true,
                    "Speaker labeling is ready with the Higher Accuracy bundle.",
                    "The Higher Accuracy speaker-labeling download finished during setup.",
                    highAccuracyDownload.AssetPath);
            }

            standardDownload = await TryDownloadStandardSpeakerLabelingAsync(
                config,
                catalog,
                getRemoteDiarizationAssetsAsync,
                cancellationToken);
            if (standardDownload.IsReady)
            {
                return BuildSpeakerLabelingStatus(
                    requestedProfile,
                    SpeakerLabelingModelProfilePreference.Standard,
                    retryRecommended: true,
                    isReady: true,
                    "Speaker labeling is ready with the Standard bundle.",
                    $"The Higher Accuracy speaker-labeling download did not finish during setup. Retry it from Settings > Setup. Details: {highAccuracyDownload.FailureMessage}",
                    standardDownload.AssetPath);
            }

            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Standard,
                retryRecommended: true,
                isReady: false,
                "Speaker labeling is optional right now.",
                $"Meeting Recorder could not finish the Higher Accuracy or Standard speaker-labeling downloads. Speaker labeling stays optional and can resume later from Settings > Setup. Details: {highAccuracyDownload.FailureMessage} {standardDownload.FailureMessage}".Trim(),
                standardTargetPath);
        }

        standardDownload = await TryDownloadStandardSpeakerLabelingAsync(
            config,
            catalog,
            getRemoteDiarizationAssetsAsync,
            cancellationToken);
        if (standardDownload.IsReady)
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Standard,
                retryRecommended: false,
                isReady: true,
                "Speaker labeling is ready with the Standard bundle.",
                "The Standard speaker-labeling download finished during setup.",
                standardDownload.AssetPath);
        }

        if (requestedProfile == SpeakerLabelingModelProfilePreference.Custom)
        {
            return BuildSpeakerLabelingStatus(
                requestedProfile,
                SpeakerLabelingModelProfilePreference.Standard,
                retryRecommended: true,
                isReady: false,
                "Speaker labeling is optional right now.",
                $"Your imported custom speaker-labeling bundle is unavailable, and Meeting Recorder could not finish the Standard speaker-labeling download. Speaker labeling stays optional and can resume later from Settings > Setup. Details: {standardDownload.FailureMessage}",
                standardTargetPath);
        }

        return BuildSpeakerLabelingStatus(
            requestedProfile,
            SpeakerLabelingModelProfilePreference.Standard,
            retryRecommended: true,
            isReady: false,
            "Speaker labeling is optional right now.",
            $"Meeting Recorder could not finish the Standard speaker-labeling download during install. Speaker labeling stays optional and can resume later from Settings > Setup. Details: {standardDownload.FailureMessage}",
            standardTargetPath);
    }

    private async Task<DownloadedWhisperModel> TryDownloadStandardTranscriptionAsync(
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        Func<Task<IReadOnlyList<WhisperRemoteModelAsset>>> getRemoteModelsAsync,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.Transcription.Standard);
        return await TryDownloadTranscriptionAsync(
            targetPath,
            catalog.Transcription.Standard.FileName,
            getRemoteModelsAsync,
            remoteModels => _catalogService.FindTranscriptionStandardAsset(catalog, remoteModels),
            config.ModelCacheDir,
            cancellationToken);
    }

    private async Task<DownloadedWhisperModel> TryDownloadHighAccuracyTranscriptionAsync(
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        Func<Task<IReadOnlyList<WhisperRemoteModelAsset>>> getRemoteModelsAsync,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.Transcription.HighAccuracy);
        return await TryDownloadTranscriptionAsync(
            targetPath,
            catalog.Transcription.HighAccuracy.FileName,
            getRemoteModelsAsync,
            remoteModels => _catalogService.FindTranscriptionHighAccuracyAsset(catalog, remoteModels),
            config.ModelCacheDir,
            cancellationToken);
    }

    private async Task<DownloadedWhisperModel> TryDownloadTranscriptionAsync(
        string targetPath,
        string expectedFileName,
        Func<Task<IReadOnlyList<WhisperRemoteModelAsset>>> getRemoteModelsAsync,
        Func<IReadOnlyList<WhisperRemoteModelAsset>, WhisperRemoteModelAsset?> selectAsset,
        string modelCacheDir,
        CancellationToken cancellationToken)
    {
        try
        {
            var remoteModels = await getRemoteModelsAsync();
            var asset = selectAsset(remoteModels)
                ?? throw new InvalidOperationException(
                    $"The transcription asset '{expectedFileName}' is not available in the current release.");
            var installed = await _whisperModelReleaseCatalogService.DownloadRemoteModelIntoManagedDirectoryAsync(
                asset,
                modelCacheDir,
                progress: null,
                cancellationToken);

            return new DownloadedWhisperModel(true, installed.ModelPath, string.Empty);
        }
        catch (Exception exception)
        {
            return new DownloadedWhisperModel(false, targetPath, exception.Message);
        }
    }

    private async Task<DownloadedDiarizationAsset> TryDownloadStandardSpeakerLabelingAsync(
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        Func<Task<IReadOnlyList<DiarizationRemoteAsset>>> getRemoteDiarizationAssetsAsync,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.SpeakerLabeling.Standard);
        return await TryDownloadSpeakerLabelingAsync(
            targetPath,
            catalog.SpeakerLabeling.Standard.FileName,
            getRemoteDiarizationAssetsAsync,
            remoteAssets => _catalogService.FindSpeakerLabelingStandardAsset(catalog, remoteAssets),
            cancellationToken);
    }

    private async Task<DownloadedDiarizationAsset> TryDownloadHighAccuracySpeakerLabelingAsync(
        AppConfig config,
        MeetingRecorderModelCatalog catalog,
        Func<Task<IReadOnlyList<DiarizationRemoteAsset>>> getRemoteDiarizationAssetsAsync,
        CancellationToken cancellationToken)
    {
        var targetPath = _catalogService.ResolveManagedPath(config.ModelCacheDir, catalog.SpeakerLabeling.HighAccuracy);
        return await TryDownloadSpeakerLabelingAsync(
            targetPath,
            catalog.SpeakerLabeling.HighAccuracy.FileName,
            getRemoteDiarizationAssetsAsync,
            remoteAssets => _catalogService.FindSpeakerLabelingHighAccuracyAsset(catalog, remoteAssets),
            cancellationToken);
    }

    private async Task<DownloadedDiarizationAsset> TryDownloadSpeakerLabelingAsync(
        string targetPath,
        string expectedFileName,
        Func<Task<IReadOnlyList<DiarizationRemoteAsset>>> getRemoteDiarizationAssetsAsync,
        Func<IReadOnlyList<DiarizationRemoteAsset>, DiarizationRemoteAsset?> selectAsset,
        CancellationToken cancellationToken)
    {
        try
        {
            var remoteAssets = await getRemoteDiarizationAssetsAsync();
            var asset = selectAsset(remoteAssets)
                ?? throw new InvalidOperationException(
                    $"The speaker-labeling asset '{expectedFileName}' is not available in the current release.");
            var installed = await _diarizationAssetReleaseCatalogService.DownloadRemoteAssetIntoDirectoryAsync(
                asset,
                targetPath,
                cancellationToken);

            if (!installed.IsReady)
            {
                throw new InvalidOperationException(installed.DetailsText);
            }

            return new DownloadedDiarizationAsset(true, installed.AssetRootPath, string.Empty);
        }
        catch (Exception exception)
        {
            return new DownloadedDiarizationAsset(false, targetPath, exception.Message);
        }
    }

    private static TranscriptionModelProvisioningStatus BuildTranscriptionStatus(
        TranscriptionModelProfilePreference requestedProfile,
        TranscriptionModelProfilePreference activeProfile,
        bool retryRecommended,
        bool isReady,
        string summary,
        string detail,
        string activeModelPath)
    {
        return new TranscriptionModelProvisioningStatus(
            requestedProfile,
            activeProfile,
            retryRecommended,
            isReady,
            summary,
            detail,
            activeModelPath);
    }

    private static SpeakerLabelingModelProvisioningStatus BuildSpeakerLabelingStatus(
        SpeakerLabelingModelProfilePreference requestedProfile,
        SpeakerLabelingModelProfilePreference activeProfile,
        bool retryRecommended,
        bool isReady,
        string summary,
        string detail,
        string activeAssetPath)
    {
        return new SpeakerLabelingModelProvisioningStatus(
            requestedProfile,
            activeProfile,
            retryRecommended,
            isReady,
            summary,
            detail,
            activeAssetPath);
    }

    private sealed record DownloadedWhisperModel(bool IsReady, string ModelPath, string FailureMessage);

    private sealed record DownloadedDiarizationAsset(bool IsReady, string AssetPath, string FailureMessage);
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
