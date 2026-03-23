using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed class InstallerRelaunchCoordinator
{
    public const string EnvironmentVariableName = "MEETINGRECORDER_INSTALLER_RELAUNCH";
    public const string RelaunchMarkerFileName = "installer-relaunch.flag";

    private readonly Func<bool> _isInstallerRelaunchRequested;
    private readonly Func<bool> _trySignalInstallerShutdown;
    private readonly Func<TimeSpan, bool> _waitForPrimaryInstanceRelease;
    private readonly Action<TimeSpan> _delay;

    public InstallerRelaunchCoordinator(
        Func<bool> isInstallerRelaunchRequested,
        Func<bool> trySignalInstallerShutdown,
        Func<TimeSpan, bool> waitForPrimaryInstanceRelease,
        Action<TimeSpan> delay)
    {
        _isInstallerRelaunchRequested = isInstallerRelaunchRequested ?? throw new ArgumentNullException(nameof(isInstallerRelaunchRequested));
        _trySignalInstallerShutdown = trySignalInstallerShutdown ?? throw new ArgumentNullException(nameof(trySignalInstallerShutdown));
        _waitForPrimaryInstanceRelease = waitForPrimaryInstanceRelease ?? throw new ArgumentNullException(nameof(waitForPrimaryInstanceRelease));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public bool TryRecoverPrimaryInstance(TimeSpan primaryReleaseWait, TimeSpan postSignalDelay = default)
    {
        if (!_isInstallerRelaunchRequested())
        {
            return false;
        }

        _trySignalInstallerShutdown();

        if (postSignalDelay > TimeSpan.Zero)
        {
            _delay(postSignalDelay);
        }

        return _waitForPrimaryInstanceRelease(primaryReleaseWait);
    }

    public static bool IsInstallerRelaunchRequestedFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRelaunchMarkerPath(string? localApplicationDataRootOverride = null)
    {
        return Path.Combine(
            AppDataPaths.GetManagedAppRoot(localApplicationDataRootOverride),
            RelaunchMarkerFileName);
    }

    public static bool TryConsumeInstallerRelaunchMarker(string? localApplicationDataRootOverride = null)
    {
        if (IsInstallerRelaunchRequestedFromEnvironment())
        {
            return true;
        }

        var markerPath = GetRelaunchMarkerPath(localApplicationDataRootOverride);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            File.Delete(markerPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return true;
    }
}
