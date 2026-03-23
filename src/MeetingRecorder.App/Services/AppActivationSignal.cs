using System.Diagnostics;

namespace MeetingRecorder.App.Services;

internal sealed class AppActivationSignal : IDisposable
{
    private readonly EventWaitHandle _eventHandle;
    private readonly EventWaitHandle _acknowledgementHandle;

    public AppActivationSignal(string signalName, string? acknowledgementSignalName = null)
    {
        if (string.IsNullOrWhiteSpace(signalName))
        {
            throw new ArgumentException("A non-empty signal name is required.", nameof(signalName));
        }

        SignalName = signalName;
        _eventHandle = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: signalName);
        AcknowledgementSignalName = string.IsNullOrWhiteSpace(acknowledgementSignalName)
            ? signalName + ".Acknowledged"
            : acknowledgementSignalName;
        _acknowledgementHandle = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: AcknowledgementSignalName);
    }

    public static string DefaultSignalName => @"Local\MeetingRecorder.Activate";

    public static string DefaultAcknowledgementSignalName => @"Local\MeetingRecorder.Activate.Acknowledged";

    public string SignalName { get; }

    public string AcknowledgementSignalName { get; }

    public async Task<bool> WaitForSignalAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                var signaledIndex = WaitHandle.WaitAny(
                    new[] { _eventHandle, cancellationToken.WaitHandle },
                    Timeout.Infinite);
                return signaledIndex == 0;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static bool TrySignal(string signalName)
    {
        if (string.IsNullOrWhiteSpace(signalName))
        {
            return false;
        }

        try
        {
            using var existingHandle = EventWaitHandle.OpenExisting(signalName);
            return existingHandle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public bool TryAcknowledge()
    {
        try
        {
            return _acknowledgementHandle.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public static bool TrySignalAndWaitForAcknowledgement(
        string signalName,
        string acknowledgementSignalName,
        TimeSpan acknowledgementTimeout)
    {
        if (string.IsNullOrWhiteSpace(signalName) ||
            string.IsNullOrWhiteSpace(acknowledgementSignalName) ||
            acknowledgementTimeout < TimeSpan.Zero)
        {
            return false;
        }

        var timeoutClock = Stopwatch.StartNew();
        try
        {
            while (timeoutClock.Elapsed < acknowledgementTimeout)
            {
                try
                {
                    using var existingHandle = EventWaitHandle.OpenExisting(signalName);
                    using var acknowledgementHandle = EventWaitHandle.OpenExisting(acknowledgementSignalName);
                    existingHandle.Set();
                    var remaining = acknowledgementTimeout - timeoutClock.Elapsed;
                    return remaining > TimeSpan.Zero && acknowledgementHandle.WaitOne(remaining);
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    var remaining = acknowledgementTimeout - timeoutClock.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    var retryDelay = remaining > TimeSpan.FromMilliseconds(50)
                        ? TimeSpan.FromMilliseconds(50)
                        : remaining;
                    Thread.Sleep(retryDelay);
                }
            }

            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _eventHandle.Dispose();
        _acknowledgementHandle.Dispose();
    }
}
