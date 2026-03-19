using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void GetAppRoot_Uses_Portable_Data_Folder_When_PortableMarker_Is_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "portable.mode"), string.Empty);

        var appRoot = AppDataPaths.GetAppRoot(root);

        Assert.Equal(Path.Combine(root, "data"), appRoot);
        Assert.True(AppDataPaths.IsPortableMode(root));
    }

    [Fact]
    public void GetAppRoot_Uses_LocalAppData_When_PortableMarker_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var appRoot = AppDataPaths.GetAppRoot(root);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingRecorder"),
            appRoot);
        Assert.False(AppDataPaths.IsPortableMode(root));
    }
}
