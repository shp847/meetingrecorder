using MeetingRecorder.Installer;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallPathProcessManagerTests
{
    [Fact]
    public async Task EnsureInstallPathReleasedAsync_Retries_Until_The_Primary_Instance_Releases()
    {
        var targetRoot = @"C:\Users\Test\Documents\MeetingRecorder";
        var processController = new FakeInstallPathProcessController(
            signalResult: true,
            waitResults: [false, false, true]);
        var manager = new InstallPathProcessManager(processController);

        await manager.EnsureInstallPathReleasedAsync(targetRoot, CancellationToken.None);

        Assert.True(processController.SignalRequested);
        Assert.Equal(3, processController.SignalCallCount);
        Assert.Equal(3, processController.WaitCallCount);
    }

    [Fact]
    public async Task EnsureInstallPathReleasedAsync_Still_Signals_Shutdown_When_Path_Is_Not_In_Use()
    {
        var targetRoot = @"C:\Users\Test\Documents\MeetingRecorder";
        var processController = new FakeInstallPathProcessController(signalResult: false);
        var manager = new InstallPathProcessManager(processController);

        await manager.EnsureInstallPathReleasedAsync(targetRoot, CancellationToken.None);

        Assert.True(processController.SignalRequested);
        Assert.Equal(1, processController.SignalCallCount);
        Assert.Equal(0, processController.WaitCallCount);
    }

    [Fact]
    public async Task EnsureInstallPathReleasedAsync_Throws_When_The_Primary_Instance_Never_Releases()
    {
        var targetRoot = @"C:\Users\Test\Documents\MeetingRecorder";
        var processController = new FakeInstallPathProcessController(
            signalResult: true,
            waitResults: [false, false, false]);
        var manager = new InstallPathProcessManager(processController);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.EnsureInstallPathReleasedAsync(targetRoot, CancellationToken.None));

        Assert.Contains("still running", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, processController.SignalCallCount);
        Assert.Equal(3, processController.WaitCallCount);
    }

    private sealed class FakeInstallPathProcessController : IInstallPathProcessController
    {
        private readonly bool _signalResult;
        private readonly Queue<bool> _waitResults;

        public FakeInstallPathProcessController(bool signalResult, IEnumerable<bool>? waitResults = null)
        {
            _signalResult = signalResult;
            _waitResults = new Queue<bool>(waitResults ?? []);
        }

        public bool SignalRequested { get; private set; }

        public int SignalCallCount { get; private set; }

        public int WaitCallCount { get; private set; }

        public bool TrySignalInstallerShutdown()
        {
            SignalRequested = true;
            SignalCallCount++;
            return _signalResult;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WaitCallCount++;
            return Task.FromResult(_waitResults.Count > 0 && _waitResults.Dequeue());
        }
    }
}
