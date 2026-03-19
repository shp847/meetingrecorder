using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MeetingRecorder.Installer;

public partial class App : Application
{
    private bool _hasShownUnexpectedError;

    public App()
    {
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnexpectedException("dispatcher", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        HandleUnexpectedException("app-domain", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnexpectedException("task-scheduler", e.Exception);
        e.SetObserved();
    }

    private void HandleUnexpectedException(string source, Exception? exception)
    {
        try
        {
            var logPath = WriteUnexpectedErrorLog(source, exception);
            if (_hasShownUnexpectedError)
            {
                return;
            }

            _hasShownUnexpectedError = true;
            MessageBox.Show(
                "The installer hit an unexpected error.\n\n" +
                "Try the backup CMD installer or the manual install steps.\n\n" +
                $"Details were written to:\n{logPath}",
                "Meeting Recorder Installer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Avoid escalating into Windows Error Reporting if logging or UI feedback fails too.
        }
    }

    private static string WriteUnexpectedErrorLog(string source, Exception? exception)
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderInstaller");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "installer-error.log");
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Exception: {exception}");
        builder.AppendLine(new string('-', 80));
        File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
        return logPath;
    }
}
