using System.Diagnostics;

namespace MeetingRecorder.App.Services;

internal enum SecondaryLaunchRecoveryResult
{
    ActivatedExistingInstance,
    PrimaryInstanceReleased,
    TimedOut
}

internal sealed class SecondaryLaunchRecoveryCoordinator
{
    private readonly Func<TimeSpan, bool> _trySignalAndWaitForAcknowledgement;
    private readonly Func<int, int, TimeSpan, bool> _tryBringExistingWindowToFront;
    private readonly Func<TimeSpan, bool> _waitForPrimaryInstanceRelease;
    private readonly Action<TimeSpan> _delay;

    public SecondaryLaunchRecoveryCoordinator(
        Func<TimeSpan, bool> trySignalAndWaitForAcknowledgement,
        Func<int, int, TimeSpan, bool> tryBringExistingWindowToFront,
        Func<TimeSpan, bool> waitForPrimaryInstanceRelease,
        Action<TimeSpan> delay)
    {
        _trySignalAndWaitForAcknowledgement = trySignalAndWaitForAcknowledgement
            ?? throw new ArgumentNullException(nameof(trySignalAndWaitForAcknowledgement));
        _tryBringExistingWindowToFront = tryBringExistingWindowToFront
            ?? throw new ArgumentNullException(nameof(tryBringExistingWindowToFront));
        _waitForPrimaryInstanceRelease = waitForPrimaryInstanceRelease
            ?? throw new ArgumentNullException(nameof(waitForPrimaryInstanceRelease));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public SecondaryLaunchRecoveryResult Recover(
        int currentProcessId,
        TimeSpan initialActivationTimeout,
        TimeSpan recoveryWindow,
        TimeSpan retryDelay,
        TimeSpan finalPrimaryReleaseWait = default)
    {
        if (currentProcessId <= 0)
        {
            return SecondaryLaunchRecoveryResult.TimedOut;
        }

        if (_trySignalAndWaitForAcknowledgement(initialActivationTimeout))
        {
            return SecondaryLaunchRecoveryResult.ActivatedExistingInstance;
        }

        if (recoveryWindow <= TimeSpan.Zero)
        {
            return SecondaryLaunchRecoveryResult.TimedOut;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < recoveryWindow)
        {
            if (_tryBringExistingWindowToFront(currentProcessId, 1, TimeSpan.Zero))
            {
                return SecondaryLaunchRecoveryResult.ActivatedExistingInstance;
            }

            if (_waitForPrimaryInstanceRelease(TimeSpan.Zero))
            {
                return SecondaryLaunchRecoveryResult.PrimaryInstanceReleased;
            }

            var remaining = recoveryWindow - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var acknowledgementSlice = remaining > TimeSpan.FromMilliseconds(250)
                ? TimeSpan.FromMilliseconds(250)
                : remaining;
            if (acknowledgementSlice > TimeSpan.Zero &&
                _trySignalAndWaitForAcknowledgement(acknowledgementSlice))
            {
                return SecondaryLaunchRecoveryResult.ActivatedExistingInstance;
            }

            remaining = recoveryWindow - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delaySlice = retryDelay <= TimeSpan.Zero || retryDelay > remaining
                ? remaining
                : retryDelay;
            _delay(delaySlice);
        }

        if (finalPrimaryReleaseWait > TimeSpan.Zero &&
            _waitForPrimaryInstanceRelease(finalPrimaryReleaseWait))
        {
            return SecondaryLaunchRecoveryResult.PrimaryInstanceReleased;
        }

        return SecondaryLaunchRecoveryResult.TimedOut;
    }
}
