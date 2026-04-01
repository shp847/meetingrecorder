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

    public string ResolveSeedSourcePath(string installRoot, CuratedModelArtifact artifact)
    {
        if (!artifact.IsBundled)
        {
            throw new InvalidOperationException($"Artifact '{artifact.FileName}' is not bundled in the installer.");
        }

        return Path.GetFullPath(Path.Combine(installRoot, artifact.SeedRelativePath!));
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
            ResolveManagedPath(modelCacheDir, catalog.Transcription.StandardIncluded),
            StringComparison.OrdinalIgnoreCase))
        {
            return TranscriptionModelProfilePreference.StandardIncluded;
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
        var normalizedPath = Path.GetFullPath(assetPath);
        if (string.Equals(
            normalizedPath,
            ResolveManagedPath(modelCacheDir, catalog.SpeakerLabeling.StandardIncluded),
            StringComparison.OrdinalIgnoreCase))
        {
            return SpeakerLabelingModelProfilePreference.StandardIncluded;
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
                StandardIncluded: new CuratedModelArtifact(
                    FileName: "ggml-base.en-q8_0.bin",
                    Label: "Standard",
                    Description: "Included with the installer. Best balance of setup speed, download size, and transcript quality.",
                    ManagedRelativePath: "asr/ggml-base.en-q8_0.bin",
                    SeedRelativePath: "model-seed/transcription/ggml-base.en-q8_0.bin"),
                HighAccuracy: new CuratedModelArtifact(
                    FileName: "ggml-small.en-q8_0.bin",
                    Label: "Higher Accuracy",
                    Description: "Optional larger download for better transcript quality after install.",
                    ManagedRelativePath: "asr/ggml-small.en-q8_0.bin",
                    SeedRelativePath: null)),
            SpeakerLabeling: new CuratedModelCatalogSection(
                StandardIncluded: new CuratedModelArtifact(
                    FileName: "meeting-recorder-diarization-bundle-standard-win-x64.zip",
                    Label: "Standard",
                    Description: "Included with the installer so speaker labeling can be ready even when downloads are unavailable.",
                    ManagedRelativePath: "diarization/standard",
                    SeedRelativePath: "model-seed/speaker-labeling/meeting-recorder-diarization-bundle-standard-win-x64.zip"),
                HighAccuracy: new CuratedModelArtifact(
                    FileName: "meeting-recorder-diarization-bundle-accurate-win-x64.zip",
                    Label: "Higher Accuracy",
                    Description: "Optional larger download for stronger speaker separation after install.",
                    ManagedRelativePath: "diarization/high-accuracy",
                    SeedRelativePath: null)));
    }
}

public sealed record CuratedModelCatalogSection(
    CuratedModelArtifact StandardIncluded,
    CuratedModelArtifact HighAccuracy);

public sealed record CuratedModelArtifact(
    string FileName,
    string Label,
    string Description,
    string ManagedRelativePath,
    string? SeedRelativePath)
{
    public bool IsBundled => !string.IsNullOrWhiteSpace(SeedRelativePath);
}
