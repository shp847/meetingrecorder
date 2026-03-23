using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ModelsTabGuidanceTests
{
    [Fact]
    public void ResolveSpeakerLabelingSetupGuidePath_Prefers_Local_SETUP_Md_Within_Five_Parent_Levels()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "a", "b", "c", "d");
        Directory.CreateDirectory(appBaseDirectory);
        var localSetupPath = Path.Combine(root, "SETUP.md");
        File.WriteAllText(localSetupPath, "# local setup");

        var result = ModelsTabGuidance.ResolveSpeakerLabelingSetupGuidePath(
            appBaseDirectory,
            new Uri("https://github.com/example/MeetingRecorder/blob/main/SETUP.md", UriKind.Absolute));

        Assert.Equal(localSetupPath, result.LocalPath);
        Assert.Equal(new Uri(localSetupPath), result.Uri);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void BuildAlternatePublicDownloadLocationsState_Returns_Explicit_None_Configured_State()
    {
        var result = ModelsTabGuidance.BuildAlternatePublicDownloadLocationsState(
            Array.Empty<AlternatePublicDownloadLocation>());

        Assert.Equal("Alternate public download locations", result.Heading);
        Assert.Equal("No vetted public mirror configured yet.", result.EmptyStateText);
        Assert.Empty(result.Locations);
    }
}
