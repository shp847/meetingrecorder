using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Services;
using System.Windows;

namespace MeetingRecorder.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configStore = new AppConfigStore(AppDataPaths.GetConfigPath());
        var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var liveConfig = new LiveAppConfig(configStore, config);
        var logger = new FileLogWriter(AppDataPaths.GetGlobalLogPath());
        var mainWindow = new MainWindow(liveConfig, logger);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
