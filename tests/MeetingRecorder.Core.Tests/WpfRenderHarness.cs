using MeetingRecorder.App;
using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
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
        return CaptureShell(SyntheticShellState.SetupBlocked, rasterScale: 1d);
    }

    public static WpfRenderEvidence CaptureProcessingShell()
    {
        return CaptureShell(SyntheticShellState.Processing, rasterScale: 1d);
    }

    public static WpfRenderEvidence CaptureSelectionActiveShell()
    {
        return CaptureShell(SyntheticShellState.SelectionActive, rasterScale: 1d);
    }

    public static WpfRenderEvidence CaptureCleanupRecommendationShell()
    {
        return CaptureShell(SyntheticShellState.CleanupRecommendation, rasterScale: 1d);
    }

    public static WpfRenderEvidence CaptureEmptyHealthyShell()
    {
        return CaptureShell(SyntheticShellState.EmptyHealthy, rasterScale: 1d);
    }

    public static WpfRenderEvidence CaptureShellAt125Dpi(SyntheticShellState state)
    {
        return CaptureShell(state, rasterScale: 1.25d);
    }

    private static WpfRenderEvidence CaptureShell(SyntheticShellState state, double rasterScale)
    {
        WpfRenderEvidence? evidence = null;
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                evidence = CaptureShellOnStaThread(state, rasterScale);
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

    private static WpfRenderEvidence CaptureShellOnStaThread(SyntheticShellState state, double rasterScale)
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
        config = PrepareSyntheticShellState(configStore, config, state);
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

        window.Width = 1280d / rasterScale;
        window.Height = 800d / rasterScale;

        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            var homeAction = RequireElement<Button>(window, "HomePrimaryActionButton");
            var settingsAction = RequireElement<Button>(window, "HeaderSettingsButton");
            var tabControl = RequireElement<TabControl>(window, "MainTabControl");
            var shellStatusDetail = RequireElement<TextBlock>(window, "HeaderShellStatusDetailTextBlock");
            var stateToken = state switch
            {
                SyntheticShellState.Processing => "processing",
                SyntheticShellState.SelectionActive => "selection-active",
                SyntheticShellState.CleanupRecommendation => "cleanup-recommendation",
                SyntheticShellState.EmptyHealthy => "empty-healthy",
                _ => "setup-blocked",
            };
            if (state is SyntheticShellState.Processing or SyntheticShellState.SelectionActive or SyntheticShellState.CleanupRecommendation)
            {
                tabControl.SelectedItem = RequireElement<TabItem>(window, "MeetingsTabItem");
                WaitForMeetingRows(window);
                if (state == SyntheticShellState.SelectionActive)
                {
                    var meetings = RequireElement<ListView>(window, "MeetingsDataGrid");
                    meetings.SelectedItem = meetings.Items[0];
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                }

                if (state == SyntheticShellState.CleanupRecommendation)
                {
                    WaitForCleanupRecommendation(window);
                }
            }

            var scaleToken = rasterScale == 1.25d ? "125" : "100";
            var screenshotPath = Path.Combine(rootDirectory, $"{stateToken}-1280x800-{scaleToken}.png");
            var automationTracePath = Path.Combine(rootDirectory, $"{stateToken}-automation-tree-{scaleToken}.txt");
            var keyboardTracePath = Path.Combine(rootDirectory, $"{stateToken}-keyboard-trace-{scaleToken}.txt");

            SaveScreenshot(window, screenshotPath, rasterScale);
            var traceElements = state is SyntheticShellState.Processing or SyntheticShellState.SelectionActive or SyntheticShellState.CleanupRecommendation
                ? new FrameworkElement[]
                {
                    homeAction,
                    tabControl,
                    shellStatusDetail,
                    RequireElement<ListView>(window, "MeetingsDataGrid"),
                    RequireElement<Border>(window, "MeetingsProcessingStatusBorder"),
                    RequireElement<TextBlock>(window, "SelectedMeetingInspectorTitleTextBlock"),
                    RequireElement<Border>(window, "MeetingCleanupReviewBannerBorder"),
                    RequireElement<TextBlock>(window, "MeetingCleanupReviewBannerTextBlock"),
                    RequireElement<TextBlock>(window, "DashboardModelReadinessTextBlock"),
                }
                : new FrameworkElement[]
                {
                    homeAction,
                    tabControl,
                    shellStatusDetail,
                    RequireElement<TextBlock>(window, "DashboardModelReadinessTextBlock"),
                };
            File.WriteAllLines(automationTracePath, CreateAutomationTrace(traceElements));
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

    private static AppConfig PrepareSyntheticShellState(
        AppConfigStore configStore,
        AppConfig config,
        SyntheticShellState state)
    {
        if (state == SyntheticShellState.EmptyHealthy)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.TranscriptionModelPath)!);
            using var model = File.Create(config.TranscriptionModelPath);
            model.SetLength(WhisperModelService.MinimumExpectedModelBytes + 1);
            return config;
        }

        if (state is not (SyntheticShellState.Processing or SyntheticShellState.SelectionActive or SyntheticShellState.CleanupRecommendation))
        {
            return config;
        }

        var processingConfig = config with
        {
            MeetingsViewPreset = state == SyntheticShellState.CleanupRecommendation
                ? MeetingsViewPreset.NeedsAttention
                : MeetingsViewPreset.Processing,
            MeetingsViewPresetInitialized = true,
        };
        var pathBuilder = new ArtifactPathBuilder();
        var sessionId = "synthetic-processing";
        var sessionRoot = pathBuilder.BuildSessionRoot(processingConfig.WorkDir, sessionId);
        Directory.CreateDirectory(sessionRoot);
        var startedAtUtc = new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
        var manifest = new MeetingSessionManifest
        {
            SessionId = sessionId,
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Synthetic processing review",
            StartedAtUtc = startedAtUtc,
            State = SessionState.Processing,
            DetectionEvidence = Array.Empty<DetectionSignal>(),
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            TranscriptionStatus = new ProcessingStageStatus(
                "transcription",
                StageExecutionState.Running,
                startedAtUtc,
                "Synthetic fixture: transcribing locally."),
            DiarizationStatus = new ProcessingStageStatus(
                "diarization",
                StageExecutionState.NotStarted,
                startedAtUtc,
                null),
            PublishStatus = new ProcessingStageStatus(
                "publish",
                StageExecutionState.NotStarted,
                startedAtUtc,
                null),
        };
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        new SessionManifestStore(pathBuilder).SaveAsync(manifest, manifestPath).GetAwaiter().GetResult();
        if (state == SyntheticShellState.CleanupRecommendation)
        {
            var artifactPath = Path.Combine(
                processingConfig.AudioOutputDir,
                pathBuilder.BuildFileStem(MeetingPlatform.Teams, startedAtUtc.AddHours(-1), "Teams") + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            File.WriteAllBytes(artifactPath, Array.Empty<byte>());
        }
        configStore.SaveAsync(processingConfig).GetAwaiter().GetResult();
        return processingConfig;
    }

    private static void WaitForMeetingRows(FrameworkElement window)
    {
        var meetings = RequireElement<ListView>(window, "MeetingsDataGrid");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (meetings.Items.Count == 0 && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(25),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        Assert.True(meetings.Items.Count > 0, "Synthetic processing manifest must appear in the Meetings workspace.");
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static void WaitForCleanupRecommendation(FrameworkElement window)
    {
        var banner = RequireElement<Border>(window, "MeetingCleanupReviewBannerBorder");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (banner.Visibility != Visibility.Visible && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(25),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        Assert.Equal(Visibility.Visible, banner.Visibility);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static T RequireElement<T>(FrameworkElement window, string name)
        where T : FrameworkElement
    {
        return window.FindName(name) as T
            ?? throw new InvalidOperationException($"Could not find WPF element '{name}'.");
    }

    private static void SaveScreenshot(FrameworkElement element, string path, double rasterScale)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * rasterScale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * rasterScale));
        var dpi = 96d * rasterScale;
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
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

    public enum SyntheticShellState
    {
        SetupBlocked,
        Processing,
        SelectionActive,
        CleanupRecommendation,
        EmptyHealthy,
    }
}
