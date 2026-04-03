using MeetingRecorder.Core.Configuration;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class MeetingRecorderModelCatalogService
{
    public const string CatalogFileName = "model-catalog.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public MeetingRecorderModelCatalog Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A model catalog path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The model catalog could not be found.", path);
        }

        var catalog = JsonSerializer.Deserialize<MeetingRecorderModelCatalog>(
            File.ReadAllText(path),
            SerializerOptions);

        if (catalog is null)
        {
            throw new InvalidOperationException($"The model catalog at '{path}' could not be parsed.");
        }

        return catalog;
    }

    public MeetingRecorderModelCatalog LoadBundledCatalog(string? baseDirectory = null)
    {
        var catalogPath = GetBundledCatalogPath(baseDirectory);
        return File.Exists(catalogPath)
            ? Load(catalogPath)
            : MeetingRecorderModelCatalog.CreateDefault();
    }

    public string GetBundledCatalogPath(string? baseDirectory = null)
    {
        var resolvedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        return Path.Combine(resolvedBaseDirectory, CatalogFileName);
    }

    public string ResolveManagedPath(string modelCacheDir, CuratedModelArtifact artifact)
    {
        return Path.GetFullPath(
            Path.Combine(
                modelCacheDir,
                artifact.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public TranscriptionModelProfilePreference ResolveTranscriptionProfilePreference(
        MeetingRecorderModelCatalog catalog,
        string modelCacheDir,
        string modelPath)
    {
        var normalizedPath = Path.GetFullPath(modelPath);
        if (string.Equals(
            normalizedPath,
            ResolveManagedPath(modelCacheDir, catalog.Transcription.Standard),
            StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionModelProfilePreference.Standard;
        }

        if (string.Equals(
            normalizedPath,
            ResolveManagedPath(modelCacheDir, catalog.Transcription.HighAccuracy),
            StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionModelProfilePreference.HighAccuracyDownloaded;
        }

        return TranscriptionModelProfilePreference.Custom;
    }

    public SpeakerLabelingModelProfilePreference ResolveSpeakerLabelingProfilePreference(
        MeetingRecorderModelCatalog catalog,
        string modelCacheDir,
        string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return SpeakerLabelingModelProfilePreference.Disabled;
        }

        var normalizedPath = Path.GetFullPath(assetPath);
        if (string.Equals(
            normalizedPath,
            ResolveManagedPath(modelCacheDir, catalog.SpeakerLabeling.Standard),
            StringComparison.OrdinalIgnoreCase))
        {
            return SpeakerLabelingModelProfilePreference.Standard;
        }

        if (string.Equals(
            normalizedPath,
            ResolveManagedPath(modelCacheDir, catalog.SpeakerLabeling.HighAccuracy),
            StringComparison.OrdinalIgnoreCase))
        {
            return SpeakerLabelingModelProfilePreference.HighAccuracyDownloaded;
        }

        return SpeakerLabelingModelProfilePreference.Custom;
    }

    public WhisperRemoteModelAsset? FindTranscriptionHighAccuracyAsset(
        MeetingRecorderModelCatalog catalog,
        IReadOnlyList<WhisperRemoteModelAsset> remoteModels)
    {
        return remoteModels.FirstOrDefault(asset =>
            string.Equals(
                asset.FileName,
                catalog.Transcription.HighAccuracy.FileName,
                StringComparison.OrdinalIgnoreCase));
    }

    public WhisperRemoteModelAsset? FindTranscriptionStandardAsset(
        MeetingRecorderModelCatalog catalog,
        IReadOnlyList<WhisperRemoteModelAsset> remoteModels)
    {
        return remoteModels.FirstOrDefault(asset =>
            string.Equals(
                asset.FileName,
                catalog.Transcription.Standard.FileName,
                StringComparison.OrdinalIgnoreCase));
    }

    public DiarizationRemoteAsset? FindSpeakerLabelingHighAccuracyAsset(
        MeetingRecorderModelCatalog catalog,
        IReadOnlyList<DiarizationRemoteAsset> remoteAssets)
    {
        return remoteAssets.FirstOrDefault(asset =>
            string.Equals(
                asset.FileName,
                catalog.SpeakerLabeling.HighAccuracy.FileName,
                StringComparison.OrdinalIgnoreCase));
    }

    public DiarizationRemoteAsset? FindSpeakerLabelingStandardAsset(
        MeetingRecorderModelCatalog catalog,
        IReadOnlyList<DiarizationRemoteAsset> remoteAssets)
    {
        return remoteAssets.FirstOrDefault(asset =>
            string.Equals(
                asset.FileName,
                catalog.SpeakerLabeling.Standard.FileName,
                StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record MeetingRecorderModelCatalog(
    int FormatVersion,
    CuratedModelCatalogSection Transcription,
    CuratedModelCatalogSection SpeakerLabeling)
{
    public static MeetingRecorderModelCatalog CreateDefault()
    {
        return new MeetingRecorderModelCatalog(
            FormatVersion: 1,
            Transcription: new CuratedModelCatalogSection(
                Standard: new CuratedModelArtifact(
                    FileName: "ggml-base.en-q8_0.bin",
                    Label: "Standard",
                    Description: "Recommended default download for most laptops. Best balance of setup speed, download size, and transcript quality.",
                    ManagedRelativePath: "asr/ggml-base.en-q8_0.bin"),
                HighAccuracy: new CuratedModelArtifact(
                    FileName: "ggml-small.en-q8_0.bin",
                    Label: "Higher Accuracy",
                    Description: "Optional larger download for better transcript quality after install.",
                    ManagedRelativePath: "asr/ggml-small.en-q8_0.bin")),
            SpeakerLabeling: new CuratedModelCatalogSection(
                Standard: new CuratedModelArtifact(
                    FileName: "meeting-recorder-diarization-bundle-standard-win-x64.zip",
                    Label: "Standard",
                    Description: "Recommended default download when you want speaker labeling ready after setup.",
                    ManagedRelativePath: "diarization/standard"),
                HighAccuracy: new CuratedModelArtifact(
                    FileName: "meeting-recorder-diarization-bundle-accurate-win-x64.zip",
                    Label: "Higher Accuracy",
                    Description: "Optional larger download for stronger speaker separation after install.",
                    ManagedRelativePath: "diarization/high-accuracy")));
    }
}

public sealed record CuratedModelCatalogSection(
    CuratedModelArtifact Standard,
    CuratedModelArtifact HighAccuracy);

public sealed record CuratedModelArtifact(
    string FileName,
    string Label,
    string Description,
    string ManagedRelativePath);
