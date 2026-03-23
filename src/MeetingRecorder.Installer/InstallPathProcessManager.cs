namespace MeetingRecorder.Installer;

internal sealed class InstallPathProcessManager
{
    private readonly AppPlatform.Deployment.InstallPathProcessManager _inner;

    public InstallPathProcessManager()
        : this(new InstallPathProcessController())
    {
    }

    internal InstallPathProcessManager(IInstallPathProcessController processController)
    {
        _inner = new AppPlatform.Deployment.InstallPathProcessManager(new InstallPathProcessControllerAdapter(processController));
    }

    public async Task EnsureInstallPathReleasedAsync(string installRoot, CancellationToken cancellationToken)
    {
        await _inner.EnsureInstallPathReleasedAsync(installRoot, cancellationToken);
    }
}

internal interface IInstallPathProcessController
{
    bool TrySignalInstallerShutdown();

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class InstallPathProcessController : IInstallPathProcessController
{
    private readonly AppPlatform.Deployment.InstallPathProcessController _inner = new();

    public bool TrySignalInstallerShutdown()
    {
        return _inner.TrySignalInstallerShutdown();
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return _inner.DelayAsync(delay, cancellationToken);
    }

    public Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _inner.WaitForPrimaryInstanceReleaseAsync(timeout, cancellationToken);
    }
}

internal sealed class InstallPathProcessControllerAdapter : AppPlatform.Deployment.IInstallPathProcessController
{
    private readonly IInstallPathProcessController _inner;

    public InstallPathProcessControllerAdapter(IInstallPathProcessController inner)
    {
        _inner = inner;
    }

    public bool TrySignalInstallerShutdown()
    {
        return _inner.TrySignalInstallerShutdown();
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return _inner.DelayAsync(delay, cancellationToken);
    }

    public Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _inner.WaitForPrimaryInstanceReleaseAsync(timeout, cancellationToken);
    }
}
