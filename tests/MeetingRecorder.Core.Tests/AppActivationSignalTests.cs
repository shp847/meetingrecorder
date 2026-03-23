using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppActivationSignalTests
{
    [Fact]
    public async Task WaitForSignalAsync_Completes_When_Activation_Signal_Is_Raised()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        using var signal = new AppActivationSignal(signalName);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = signal.WaitForSignalAsync(cancellationTokenSource.Token);
        AppActivationSignal.TrySignal(signalName);

        var signaled = await waitTask;

        Assert.True(signaled);
    }

    [Fact]
    public async Task WaitForSignalAsync_Returns_False_When_Cancelled_First()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        using var signal = new AppActivationSignal(signalName);
        using var cancellationTokenSource = new CancellationTokenSource();

        var waitTask = signal.WaitForSignalAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        var signaled = await waitTask;

        Assert.False(signaled);
    }

    [Fact]
    public async Task TrySignalAndWaitForAcknowledgement_Returns_True_When_Primary_Instance_Acknowledges()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        var acknowledgementSignalName = signalName + ".Ack";
        using var signal = new AppActivationSignal(signalName, acknowledgementSignalName);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var waitTask = signal.WaitForSignalAsync(cancellationTokenSource.Token);
        var activationTask = Task.Run(() => AppActivationSignal.TrySignalAndWaitForAcknowledgement(
            signalName,
            acknowledgementSignalName,
            TimeSpan.FromSeconds(2)));

        Assert.True(await waitTask);

        signal.TryAcknowledge();

        Assert.True(await activationTask);
    }

    [Fact]
    public void TrySignalAndWaitForAcknowledgement_Returns_False_When_Primary_Instance_Does_Not_Acknowledge()
    {
        var signalName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");
        var acknowledgementSignalName = signalName + ".Ack";
        using var signal = new AppActivationSignal(signalName, acknowledgementSignalName);

        var activated = AppActivationSignal.TrySignalAndWaitForAcknowledgement(
            signalName,
            acknowledgementSignalName,
            TimeSpan.FromMilliseconds(100));

        Assert.False(activated);
    }
}
