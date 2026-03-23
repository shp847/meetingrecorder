using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerRelaunchCoordinatorTests
{
    [Fact]
    public void TryConsumeInstallerRelaunchMarker_Returns_True_And_Deletes_The_Marker_File()
    {
        var localAppDataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var markerPath = InstallerRelaunchCoordinator.GetRelaunchMarkerPath(localAppDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "relaunch");

        var consumed = InstallerRelaunchCoordinator.TryConsumeInstallerRelaunchMarker(localAppDataRoot);

        Assert.True(consumed);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void TryRecoverPrimaryInstance_Returns_True_When_Installer_Relaunch_Is_Requested_And_The_Primary_Instance_Releases()
    {
        var signalAttempted = false;
        var coordinator = new InstallerRelaunchCoordinator(
            isInstallerRelaunchRequested: () => true,
            trySignalInstallerShutdown: () =>
            {
                signalAttempted = true;
                return true;
            },
            waitForPrimaryInstanceRelease: timeout => timeout == TimeSpan.FromSeconds(20),
            delay: _ => { });

        var recovered = coordinator.TryRecoverPrimaryInstance(TimeSpan.FromSeconds(20));

        Assert.True(recovered);
        Assert.True(signalAttempted);
    }

    [Fact]
    public void TryRecoverPrimaryInstance_Returns_False_When_Installer_Relaunch_Was_Not_Requested()
    {
        var signalAttempted = false;
        var coordinator = new InstallerRelaunchCoordinator(
            isInstallerRelaunchRequested: () => false,
            trySignalInstallerShutdown: () =>
            {
                signalAttempted = true;
                return true;
            },
            waitForPrimaryInstanceRelease: _ => true,
            delay: _ => { });

        var recovered = coordinator.TryRecoverPrimaryInstance(TimeSpan.FromSeconds(20));

        Assert.False(recovered);
        Assert.False(signalAttempted);
    }
}
