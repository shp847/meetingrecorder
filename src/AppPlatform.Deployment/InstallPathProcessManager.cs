namespace AppPlatform.Deployment;

public sealed class InstallPathProcessManager
{
    private static readonly TimeSpan ShutdownSignalWait = TimeSpan.FromSeconds(3);
    private const int ShutdownSignalAttemptCount = 3;

    private readonly IInstallPathProcessController _processController;
    private readonly IDeploymentLogger _logger;

    public InstallPathProcessManager()
        : this(new InstallPathProcessController(), NullDeploymentLogger.Instance)
    {
    }

    public InstallPathProcessManager(IInstallPathProcessController processController)
        : this(processController, NullDeploymentLogger.Instance)
    {
    }

    public InstallPathProcessManager(
        IInstallPathProcessController processController,
        IDeploymentLogger logger)
    {
        _processController = processController;
        _logger = logger;
    }

    public async Task EnsureInstallPathReleasedAsync(string installRoot, CancellationToken cancellationToken)
    {
        _logger.Info($"Requesting app shutdown before replacing '{installRoot}'.");

        var signalSent = _processController.TrySignalInstallerShutdown();
        if (!signalSent)
        {
            _logger.Info("No running app was listening for the installer shutdown signal.");
            return;
        }

        for (var attempt = 1; attempt <= ShutdownSignalAttemptCount; attempt++)
        {
            _logger.Info(
                $"Installer shutdown signal attempt {attempt} of {ShutdownSignalAttemptCount} sent. Waiting for the primary instance to release for up to {ShutdownSignalWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} seconds.");
            var released = await _processController.WaitForPrimaryInstanceReleaseAsync(ShutdownSignalWait, cancellationToken);
            if (released)
            {
                _logger.Info("The primary instance released the install path.");
                return;
            }

            if (attempt == ShutdownSignalAttemptCount)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            signalSent = _processController.TrySignalInstallerShutdown();
            if (!signalSent)
            {
                _logger.Info("The app stopped listening for installer shutdown requests before the final retry window.");
                break;
            }
        }

        throw new InvalidOperationException(
            $"Could not update files under '{installRoot}' because Meeting Recorder is still running. Close the app or stop the active recording/processing work, then try again.");
    }

    public bool TrySignalInstallerShutdown()
    {
        return _processController.TrySignalInstallerShutdown();
    }
}

public interface IInstallPathProcessController
{
    bool TrySignalInstallerShutdown();

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class InstallPathProcessController : IInstallPathProcessController
{
    private const string InstallerShutdownSignalName = @"Local\MeetingRecorder.InstallerShutdown";
    private const string PrimaryInstanceMutexName = @"Local\MeetingRecorder.PrimaryInstance";

    public bool TrySignalInstallerShutdown()
    {
        try
        {
            using var existingHandle = EventWaitHandle.OpenExisting(InstallerShutdownSignalName);
            return existingHandle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }

    public async Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.Zero)
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var mutex = new Mutex(initiallyOwned: false, name: PrimaryInstanceMutexName);
                try
                {
                    var acquired = mutex.WaitOne(timeout, exitContext: false);
                    if (!acquired)
                    {
                        return false;
                    }
                }
                catch (AbandonedMutexException)
                {
                }

                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }, cancellationToken);
    }
}
