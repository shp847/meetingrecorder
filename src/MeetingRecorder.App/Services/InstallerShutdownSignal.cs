namespace MeetingRecorder.App.Services;

internal sealed class InstallerShutdownSignal : IDisposable
{
    private readonly EventWaitHandle _eventHandle;

    public InstallerShutdownSignal(string signalName)
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
    }

    public static string DefaultSignalName => @"Local\MeetingRecorder.InstallerShutdown";

    public string SignalName { get; }

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

    public void Dispose()
    {
        _eventHandle.Dispose();
    }
}
