using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerShutdownSignalTests
{
    [Fact]
    public async Task WaitForSignalAsync_Completes_When_Installer_Signal_Is_Raised()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        using var signal = new InstallerShutdownSignal(signalName);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = signal.WaitForSignalAsync(cancellationTokenSource.Token);
        InstallerShutdownSignal.TrySignal(signalName);

        var signaled = await waitTask;

        Assert.True(signaled);
    }

    [Fact]
    public async Task WaitForSignalAsync_Returns_False_When_Cancelled_First()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        using var signal = new InstallerShutdownSignal(signalName);
        using var cancellationTokenSource = new CancellationTokenSource();

        var waitTask = signal.WaitForSignalAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        var signaled = await waitTask;

        Assert.False(signaled);
    }
}
