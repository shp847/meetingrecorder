using MeetingRecorder.App;
using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MeetingRecorder.Core.Tests;

internal sealed record WpfRenderEvidence(
    string RootDirectory,
    string ScreenshotPath,
    string AutomationTracePath,
    string KeyboardTracePath);

/// <summary>
/// Renders the real main shell in a dedicated STA thread. It does not start
/// the application entry point, acquire the single-instance mutex, open audio
/// devices, or use the installed profile.
/// </summary>
internal static class WpfRenderHarness
{
    public static WpfRenderEvidence CaptureHomeShell()
    {
        WpfRenderEvidence? evidence = null;
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                evidence = CaptureHomeShellOnStaThread();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finished.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!finished.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("WPF render harness did not finish within 30 seconds.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("WPF render harness failed.", failure);
        }

        return evidence ?? throw new InvalidOperationException("WPF render harness produced no evidence.");
    }

    private static WpfRenderEvidence CaptureHomeShellOnStaThread()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderWpfHarness",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"),
            Guid.NewGuid().ToString("N"));
        var documentsDirectory = Path.Combine(rootDirectory, "documents");
        Directory.CreateDirectory(documentsDirectory);

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/AppPlatform.Shell.Wpf;component/ShellTheme.xaml", UriKind.Relative),
        });

        using var profileScope = AppDataPaths.PushTestAppRoot(rootDirectory);
        var configPath = Path.Combine(rootDirectory, "config", "appsettings.json");
        var configStore = new AppConfigStore(configPath, documentsDirectory);
        var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        var liveConfig = new LiveAppConfig(configStore, config);
        var logger = new FileLogWriter(Path.Combine(rootDirectory, "logs", "wpf-harness.log"));
        var window = new MainWindow(liveConfig, logger)
        {
            Width = 1280,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            var homeAction = RequireElement<Button>(window, "HomePrimaryActionButton");
            var settingsAction = RequireElement<Button>(window, "HeaderSettingsButton");
            var tabControl = RequireElement<TabControl>(window, "MainTabControl");
            var shellStatusDetail = RequireElement<TextBlock>(window, "HeaderShellStatusDetailTextBlock");
            var screenshotPath = Path.Combine(rootDirectory, "home-1280x800-100.png");
            var automationTracePath = Path.Combine(rootDirectory, "home-automation-tree.txt");
            var keyboardTracePath = Path.Combine(rootDirectory, "home-keyboard-trace.txt");

            SaveScreenshot(window, screenshotPath);
            File.WriteAllLines(automationTracePath, CreateAutomationTrace(homeAction, tabControl, shellStatusDetail));
            File.WriteAllLines(keyboardTracePath, CreateKeyboardTrace(settingsAction, tabControl));

            return new WpfRenderEvidence(rootDirectory, screenshotPath, automationTracePath, keyboardTracePath);
        }
        finally
        {
            window.Close();
            if (!application.Dispatcher.HasShutdownStarted)
            {
                application.Shutdown();
            }
        }
    }

    private static T RequireElement<T>(FrameworkElement window, string name)
        where T : FrameworkElement
    {
        return window.FindName(name) as T
            ?? throw new InvalidOperationException($"Could not find WPF element '{name}'.");
    }

    private static void SaveScreenshot(FrameworkElement element, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IReadOnlyList<string> CreateAutomationTrace(params FrameworkElement[] elements)
    {
        return elements.Select(element =>
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(element)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
            var name = AutomationProperties.GetName(element);
            return string.Join(" | ", new[]
            {
                element.Name,
                peer?.GetAutomationControlType().ToString() ?? "None",
                string.IsNullOrWhiteSpace(name) ? peer?.GetName() ?? string.Empty : name,
                peer?.IsKeyboardFocusable().ToString() ?? "False",
            });
        }).ToArray();
    }

    private static IReadOnlyList<string> CreateKeyboardTrace(Button settingsAction, TabControl tabControl)
    {
        var trace = new List<string>();
        Assert.True(settingsAction.IsEnabled, "Open Settings must be enabled in the synthetic profile.");
        Assert.True(settingsAction.Focus(), "Open Settings must accept keyboard focus.");
        trace.Add("Focus: " + GetFocusedName());

        settingsAction.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        trace.Add("Tab: " + GetFocusedName());

        Assert.True(tabControl.Focus(), "Primary navigation must accept keyboard focus.");
        trace.Add("Primary navigation: " + GetFocusedName());
        return trace;
    }

    private static string GetFocusedName()
    {
        return Keyboard.FocusedElement is FrameworkElement element
            ? string.IsNullOrWhiteSpace(AutomationProperties.GetName(element))
                ? element.Name
                : AutomationProperties.GetName(element)
            : "<none>";
    }
}
