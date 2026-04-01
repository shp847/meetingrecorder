using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingRecorderModelCatalogServiceTests
{
    [Fact]
    public void LoadBundledCatalog_Falls_Back_To_The_Default_Curated_Model_Catalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var service = new MeetingRecorderModelCatalogService();

        var catalog = service.LoadBundledCatalog(root);

        Assert.Equal(1, catalog.FormatVersion);
        Assert.Equal("ggml-base.en-q8_0.bin", catalog.Transcription.StandardIncluded.FileName);
        Assert.Equal("Standard", catalog.Transcription.StandardIncluded.Label);
        Assert.Equal("ggml-small.en-q8_0.bin", catalog.Transcription.HighAccuracy.FileName);
        Assert.Equal("Higher Accuracy", catalog.Transcription.HighAccuracy.Label);
        Assert.Equal("meeting-recorder-diarization-bundle-standard-win-x64.zip", catalog.SpeakerLabeling.StandardIncluded.FileName);
        Assert.Equal("Standard", catalog.SpeakerLabeling.StandardIncluded.Label);
        Assert.Equal("meeting-recorder-diarization-bundle-accurate-win-x64.zip", catalog.SpeakerLabeling.HighAccuracy.FileName);
        Assert.Equal("Higher Accuracy", catalog.SpeakerLabeling.HighAccuracy.Label);
        Assert.Equal("model-seed/transcription/ggml-base.en-q8_0.bin", catalog.Transcription.StandardIncluded.SeedRelativePath);
        Assert.Equal("model-seed/speaker-labeling/meeting-recorder-diarization-bundle-standard-win-x64.zip", catalog.SpeakerLabeling.StandardIncluded.SeedRelativePath);
        Assert.Equal("diarization/high-accuracy", catalog.SpeakerLabeling.HighAccuracy.ManagedRelativePath);
    }

    [Fact]
    public void ResolveManagedPath_Uses_The_Curated_Runtime_Locations()
    {
        var service = new MeetingRecorderModelCatalogService();
        var catalog = MeetingRecorderModelCatalog.CreateDefault();
        var modelCacheDir = Path.Combine("C:\\", "Users", "Test", "AppData", "Local", "MeetingRecorder", "models");

        var transcriptionPath = service.ResolveManagedPath(modelCacheDir, catalog.Transcription.StandardIncluded);
        var speakerLabelingPath = service.ResolveManagedPath(modelCacheDir, catalog.SpeakerLabeling.StandardIncluded);

        Assert.Equal(
            Path.Combine(modelCacheDir, "asr", "ggml-base.en-q8_0.bin"),
            transcriptionPath);
        Assert.Equal(
            Path.Combine(modelCacheDir, "diarization", "standard"),
            speakerLabelingPath);
    }
}
