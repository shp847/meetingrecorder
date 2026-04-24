using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.Windows;
using System.Windows.Threading;

namespace MeetingRecorder.App;

public partial class App : Application
{
    private FileLogWriter? _logger;
    private bool _hasShownUnhandledExceptionMessage;
    private AppInstanceCoordinator? _instanceCoordinator;
    private AppActivationSignal? _activationSignal;
    private CancellationTokenSource? _activationMonitorCts;
    private Task? _activationMonitorTask;
    private InstallerShutdownSignal? _installerShutdownSignal;
    private CancellationTokenSource? _installerShutdownMonitorCts;
    private Task? _installerShutdownMonitorTask;
    private volatile bool _pendingActivationRequest;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceCoordinator = new AppInstanceCoordinator(AppInstanceCoordinator.DefaultInstanceName);
        var installerRelaunchCoordinator = new InstallerRelaunchCoordinator(
            () => InstallerRelaunchCoordinator.TryConsumeInstallerRelaunchMarker(),
            () => InstallerShutdownSignal.TrySignal(InstallerShutdownSignal.DefaultSignalName),
            timeout => _instanceCoordinator.WaitForPrimaryInstanceRelease(timeout),
            Thread.Sleep);
        var recoveredPrimaryInstanceFromInstallerRelaunch = false;
        if (!_instanceCoordinator.TryAcquirePrimaryInstance())
        {
            recoveredPrimaryInstanceFromInstallerRelaunch = installerRelaunchCoordinator.TryRecoverPrimaryInstance(
                primaryReleaseWait: TimeSpan.FromSeconds(20),
                postSignalDelay: TimeSpan.FromMilliseconds(500));
            if (!recoveredPrimaryInstanceFromInstallerRelaunch)
            {
                var activationRequested = AppActivationSignal.TrySignalAndWaitForAcknowledgement(
                    AppActivationSignal.DefaultSignalName,
                    AppActivationSignal.DefaultAcknowledgementSignalName,
                    TimeSpan.FromSeconds(2));
                if (activationRequested)
                {
                    Shutdown();
                    return;
                }

                var existingWindowActivator = new ExistingAppWindowActivator();
                var secondaryLaunchRecoveryCoordinator = new SecondaryLaunchRecoveryCoordinator(
                    trySignalAndWaitForAcknowledgement: timeout => AppActivationSignal.TrySignalAndWaitForAcknowledgement(
                        AppActivationSignal.DefaultSignalName,
                        AppActivationSignal.DefaultAcknowledgementSignalName,
                        timeout),
                    tryBringExistingWindowToFront: (currentProcessId, maxAttempts, retryDelay) =>
                        existingWindowActivator.TryBringExistingWindowToFront(currentProcessId, maxAttempts, retryDelay),
                    waitForPrimaryInstanceRelease: timeout => _instanceCoordinator.WaitForPrimaryInstanceRelease(timeout),
                    delay: Thread.Sleep);
                var recoveryResult = secondaryLaunchRecoveryCoordinator.Recover(
                    Environment.ProcessId,
                    initialActivationTimeout: TimeSpan.FromSeconds(2),
                    recoveryWindow: TimeSpan.FromSeconds(6),
                    retryDelay: TimeSpan.FromMilliseconds(250),
                    finalPrimaryReleaseWait: TimeSpan.FromSeconds(15));
                if (recoveryResult == SecondaryLaunchRecoveryResult.ActivatedExistingInstance)
                {
                    Shutdown();
                    return;
                }

                if (recoveryResult != SecondaryLaunchRecoveryResult.PrimaryInstanceReleased)
                {
                    MessageBox.Show(
                        $"{AppBranding.ProductName} is already running, but the existing instance did not respond. It may still be shutting down in the background. Wait a moment or end the lingering process in Task Manager, then try again.",
                        AppBranding.DisplayNameWithVersion,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    Shutdown();
                    return;
                }
            }
        }

        StartActivationMonitor();

        Exception? migrationFailure = null;
        var migratedLegacyData = false;
        try
        {
            migratedLegacyData = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall();
        }
        catch (Exception exception)
        {
            migrationFailure = exception;
        }

        _logger = new FileLogWriter(AppDataPaths.GetGlobalLogPath());
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;

        try
        {
            if (recoveredPrimaryInstanceFromInstallerRelaunch)
            {
                _logger.Log("Recovered the primary app instance after an installer relaunch request.");
            }

            if (migratedLegacyData)
            {
                _logger.Log("Migrated legacy portable Meeting Recorder data into the managed app data folder.");
            }

            if (migrationFailure is not null)
            {
                _logger.Log($"Legacy portable data migration failed: {migrationFailure}");
            }

            var configStore = new AppConfigStore(AppDataPaths.GetConfigPath());
            var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(configStore.LastLoadDiagnosticMessage))
            {
                _logger.Log(configStore.LastLoadDiagnosticMessage);
            }

            if (InstalledReleaseMetadataRepairService.TryRepairFromLegacyPortableInstall(configStore.ConfigPath))
            {
                config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
                _logger.Log("Refreshed installed release metadata from the legacy installer config so the Updates tab reflects the current package.");
            }

            if (InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(
                    configStore.ConfigPath,
                    AppBranding.Version,
                    Environment.ProcessPath,
                    AppContext.BaseDirectory))
            {
                _logger.Log("Repaired missing install provenance so the Updates tab can recover local installation metadata.");
            }

            var liveConfig = new LiveAppConfig(configStore, config);
            var mainWindow = new MainWindow(liveConfig, _logger);
            MainWindow = mainWindow;
            mainWindow.Show();
            if (_pendingActivationRequest)
            {
                _pendingActivationRequest = false;
                BringMainWindowToFront();
            }
            StartInstallerShutdownMonitor();
            _ = RunPostWindowStartupMaintenanceAsync(config);
        }
        catch (Exception exception)
        {
            _logger.Log($"Startup failure: {exception}");
            throw;
        }
    }

    private async Task RunPostWindowStartupMaintenanceAsync(AppConfig config)
    {
        await Task.Yield();

        TranscriptSidecarLayoutMigrationResult? transcriptSidecarMigrationResult = null;
        Exception? transcriptSidecarMigrationFailure = null;
        PublishedMeetingRepairResult? publishedMeetingRepairResult = null;
        Exception? publishedMeetingRepairFailure = null;

        try
        {
            transcriptSidecarMigrationResult = await Task.Run(
                () => TranscriptSidecarLayoutMigrationService.Migrate(config.TranscriptOutputDir));
        }
        catch (Exception exception)
        {
            transcriptSidecarMigrationFailure = exception;
        }

        if (transcriptSidecarMigrationResult is { MovedArtifactCount: > 0 } movedResult)
        {
            _logger?.Log(
                $"Moved {movedResult.MovedArtifactCount} legacy transcript sidecar artifact(s) into '{movedResult.SidecarDirectory}'.");
        }

        if (transcriptSidecarMigrationResult is { SkippedArtifactCount: > 0 } skippedResult)
        {
            _logger?.Log(
                $"Skipped {skippedResult.SkippedArtifactCount} transcript sidecar artifact migration(s) because the destination file already existed in '{skippedResult.SidecarDirectory}'.");
        }

        if (transcriptSidecarMigrationFailure is not null)
        {
            _logger?.Log($"Transcript sidecar migration failed: {transcriptSidecarMigrationFailure}");
        }

        try
        {
            publishedMeetingRepairResult = await Task.Run(
                () => PublishedMeetingRepairService.RepairKnownIssuesAsync(
                    config.AudioOutputDir,
                    config.TranscriptOutputDir,
                    AppDataPaths.GetAppRoot()));
        }
        catch (Exception exception)
        {
            publishedMeetingRepairFailure = exception;
        }

        if (publishedMeetingRepairResult is { AlreadyApplied: false } repairResult)
        {
            _logger?.Log(
                $"Published meeting repair completed: mergedSplitPairCount={repairResult.MergedSplitPairCount}; archivedArtifactCount={repairResult.ArchivedArtifactCount}; archiveDirectory='{repairResult.ArchiveDirectory}'.");
        }

        if (publishedMeetingRepairFailure is not null)
        {
            _logger?.Log($"Published meeting repair failed: {publishedMeetingRepairFailure}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_activationMonitorCts is not null && !_activationMonitorCts.IsCancellationRequested)
        {
            _activationMonitorCts.Cancel();
        }

        if (_installerShutdownMonitorCts is not null && !_installerShutdownMonitorCts.IsCancellationRequested)
        {
            _installerShutdownMonitorCts.Cancel();
        }

        ObserveCompletedMonitorTask(_activationMonitorTask);
        ObserveCompletedMonitorTask(_installerShutdownMonitorTask);

        _activationMonitorCts?.Dispose();
        if (_activationMonitorTask is null or { IsCompleted: true })
        {
            _activationSignal?.Dispose();
        }

        _installerShutdownMonitorCts?.Dispose();
        if (_installerShutdownMonitorTask is null or { IsCompleted: true })
        {
            _installerShutdownSignal?.Dispose();
        }
        _instanceCoordinator?.Dispose();

        base.OnExit(e);
    }

    private static void ObserveCompletedMonitorTask(Task? monitorTask)
    {
        if (monitorTask is not { IsCompleted: true })
        {
            return;
        }

        try
        {
            monitorTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StartInstallerShutdownMonitor()
    {
        _installerShutdownSignal = new InstallerShutdownSignal(InstallerShutdownSignal.DefaultSignalName);
        _installerShutdownMonitorCts = new CancellationTokenSource();
        _installerShutdownMonitorTask = MonitorInstallerShutdownAsync(_installerShutdownMonitorCts.Token);
    }

    private void StartActivationMonitor()
    {
        _activationSignal = new AppActivationSignal(
            AppActivationSignal.DefaultSignalName,
            AppActivationSignal.DefaultAcknowledgementSignalName);
        _activationMonitorCts = new CancellationTokenSource();
        _activationMonitorTask = MonitorActivationRequestsAsync(_activationMonitorCts.Token);
    }

    private async Task MonitorActivationRequestsAsync(CancellationToken cancellationToken)
    {
        var activationSignal = _activationSignal;
        if (activationSignal is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = await activationSignal.WaitForSignalAsync(cancellationToken);
            if (!signaled || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _logger?.Log("Received activation request from a second app launch.");
            activationSignal.TryAcknowledge();

            if (MainWindow is null)
            {
                _pendingActivationRequest = true;
                continue;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                BringMainWindowToFront();
                _pendingActivationRequest = false;
            });
        }
    }

    private async Task MonitorInstallerShutdownAsync(CancellationToken cancellationToken)
    {
        if (_installerShutdownSignal is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = await _installerShutdownSignal.WaitForSignalAsync(cancellationToken);
            if (!signaled || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _logger?.Log("Received installer shutdown request.");
            await Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow is null)
                {
                    Shutdown();
                    return;
                }

                if (MainWindow.IsVisible)
                {
                    if (MainWindow is MainWindow meetingRecorderWindow)
                    {
                        if (!meetingRecorderWindow.TryPrepareForInstallerShutdown())
                        {
                            _logger?.Log("Deferred installer shutdown request because recording or processing is still active.");
                            return;
                        }
                    }

                    MainWindow.Close();
                }
                else
                {
                    Shutdown();
                }
            });
        }
    }

    private void BringMainWindowToFront()
    {
        if (MainWindow is not Window window)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Log($"Dispatcher unhandled exception: {e.Exception}");
        if (!_hasShownUnhandledExceptionMessage)
        {
            _hasShownUnhandledExceptionMessage = true;
            MessageBox.Show(
                $"{AppBranding.ProductName} hit an unexpected UI error. Details were written to the log file.",
                AppBranding.DisplayNameWithVersion,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Log($"AppDomain unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    private void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Log($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}
