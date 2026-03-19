using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.Windows;
using System.Windows.Threading;

namespace MeetingRecorder.App;

public partial class App : Application
{
    private FileLogWriter? _logger;
    private bool _hasShownUnhandledExceptionMessage;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            var liveConfig = new LiveAppConfig(configStore, config);
            var mainWindow = new MainWindow(liveConfig, _logger);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            _logger.Log($"Startup failure: {exception}");
            throw;
        }
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
