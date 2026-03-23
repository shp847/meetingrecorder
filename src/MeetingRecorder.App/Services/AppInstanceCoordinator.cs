namespace MeetingRecorder.App.Services;

internal sealed class AppInstanceCoordinator : IDisposable
{
    private static readonly object OwnedNamesGate = new();
    private static readonly HashSet<string> OwnedNames = new(StringComparer.Ordinal);

    private readonly Mutex _mutex;
    private readonly string _instanceName;
    private bool _ownsMutex;

    public AppInstanceCoordinator(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            throw new ArgumentException("A non-empty instance name is required.", nameof(instanceName));
        }

        _instanceName = instanceName;
        _mutex = new Mutex(initiallyOwned: false, name: instanceName);
    }

    public static string DefaultInstanceName => @"Local\MeetingRecorder.PrimaryInstance";

    public bool TryAcquirePrimaryInstance()
    {
        if (_ownsMutex)
        {
            return true;
        }

        lock (OwnedNamesGate)
        {
            if (OwnedNames.Contains(_instanceName))
            {
                return false;
            }
        }

        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (_ownsMutex)
        {
            lock (OwnedNamesGate)
            {
                OwnedNames.Add(_instanceName);
            }
        }

        return _ownsMutex;
    }

    public bool WaitForPrimaryInstanceRelease(TimeSpan timeout)
    {
        if (_ownsMutex)
        {
            return true;
        }

        if (timeout < TimeSpan.Zero)
        {
            return false;
        }

        var timeoutClock = System.Diagnostics.Stopwatch.StartNew();
        while (timeoutClock.Elapsed < timeout)
        {
            lock (OwnedNamesGate)
            {
                if (!OwnedNames.Contains(_instanceName))
                {
                    break;
                }
            }

            var remaining = timeout - timeoutClock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            var retryDelay = remaining > TimeSpan.FromMilliseconds(50)
                ? TimeSpan.FromMilliseconds(50)
                : remaining;
            Thread.Sleep(retryDelay);
        }

        var acquisitionTimeout = timeout - timeoutClock.Elapsed;
        if (acquisitionTimeout < TimeSpan.Zero)
        {
            acquisitionTimeout = TimeSpan.Zero;
        }

        try
        {
            _ownsMutex = _mutex.WaitOne(acquisitionTimeout, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (_ownsMutex)
        {
            lock (OwnedNamesGate)
            {
                OwnedNames.Add(_instanceName);
            }
        }

        return _ownsMutex;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            lock (OwnedNamesGate)
            {
                OwnedNames.Remove(_instanceName);
            }

            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The OS may have already released the mutex during shutdown.
            }
        }

        _mutex.Dispose();
    }
}
