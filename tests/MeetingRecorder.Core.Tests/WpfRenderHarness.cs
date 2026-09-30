using MeetingRecorder.App;
using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.IO;
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
        return CaptureShell(SyntheticShellState.SetupBlocked, 1280, 800, 1d);
    }

    public static WpfRenderEvidence CaptureProcessingShell()
    {
        return CaptureShell(SyntheticShellState.Processing, 1280, 800, 1d);
    }

    public static WpfRenderEvidence CaptureSelectionActiveShell()
    {
        return CaptureShell(SyntheticShellState.SelectionActive, 1280, 800, 1d);
    }

    public static WpfRenderEvidence CaptureCleanupRecommendationShell()
    {
        return CaptureShell(SyntheticShellState.CleanupRecommendation, 1280, 800, 1d);
    }

    public static WpfRenderEvidence CaptureEmptyHealthyShell()
    {
        return CaptureShell(SyntheticShellState.EmptyHealthy, 1280, 800, 1d);
    }

    public static WpfRenderEvidence CaptureShellAt125Dpi(SyntheticShellState state)
    {
        return CaptureShell(state, 1280, 800, 1.25d);
    }

    public static WpfRenderEvidence CaptureShell(
        SyntheticShellState state,
        int logicalWidth,
        int logicalHeight,
        double rasterScale)
    {
        WpfRenderEvidence? evidence = null;
        Exception? failure = null;
        using var finished = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                evidence = CaptureShellOnStaThread(state, logicalWidth, logicalHeight, rasterScale);
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

        if (!finished.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("WPF render harness did not finish within 15 seconds.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("WPF render harness failed.", failure);
        }

        return evidence ?? throw new InvalidOperationException("WPF render harness produced no evidence.");
    }

    private static WpfRenderEvidence CaptureShellOnStaThread(
        SyntheticShellState state,
        int logicalWidth,
        int logicalHeight,
        double rasterScale)
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("MEETINGRECORDER_WPF_HARNESS_EVIDENCE_ROOT");
        var rootDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(evidenceRoot)
                ? Path.Combine(Path.GetTempPath(), "MeetingRecorderWpfHarness")
                : Path.GetFullPath(evidenceRoot),
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"),
            Guid.NewGuid().ToString("N"));
        var documentsDirectory = Path.Combine(rootDirectory, "documents");
        Directory.CreateDirectory(documentsDirectory);
        var progressPath = Path.Combine(rootDirectory, "harness-progress.log");
        WriteProgress(progressPath, "root-created");

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(application.Dispatcher));
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/AppPlatform.Shell.Wpf;component/ShellTheme.xaml", UriKind.Relative),
        });

        using var profileScope = AppDataPaths.PushTestAppRoot(rootDirectory);
        var configPath = Path.Combine(rootDirectory, "config", "appsettings.json");
        var configStore = new AppConfigStore(configPath, documentsDirectory);
        var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
        WriteProgress(progressPath, "config-loaded");
        config = PrepareSyntheticShellState(configStore, config, state);
        WriteProgress(progressPath, "fixture-prepared");
        var liveConfig = new LiveAppConfig(configStore, config);
        var logger = new FileLogWriter(Path.Combine(rootDirectory, "logs", "wpf-harness.log"));
        var window = new MainWindow(liveConfig, logger)
        {
            Width = logicalWidth,
            Height = logicalHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
        };
        Window? settingsWindow = null;

        try
        {
            window.Show();
            WriteProgress(progressPath, "window-shown");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            WriteProgress(progressPath, "initial-rendered");

            if (window.ActualWidth != logicalWidth || window.ActualHeight != logicalHeight)
            {
                throw new InvalidOperationException(
                    $"Requested {logicalWidth}x{logicalHeight} viewport but WPF rendered " +
                    $"{window.ActualWidth:0}x{window.ActualHeight:0}.");
            }

            var homeAction = RequireElement<Button>(window, "HomePrimaryActionButton");
            var settingsAction = RequireElement<Button>(window, "HeaderSettingsButton");
            var tabControl = RequireElement<TabControl>(window, "MainTabControl");
            var shellStatusDetail = RequireElement<TextBlock>(window, "HeaderShellStatusDetailTextBlock");
            var stateToken = state switch
            {
                SyntheticShellState.SettingsRecording => "settings-recording",
                SyntheticShellState.Processing => "processing",
                SyntheticShellState.SelectionActive => "selection-active",
                SyntheticShellState.CleanupRecommendation => "cleanup-recommendation",
                SyntheticShellState.EmptyHealthy => "empty-healthy",
                _ => "setup-blocked",
            };
            if (state is SyntheticShellState.Processing or SyntheticShellState.SelectionActive or SyntheticShellState.CleanupRecommendation)
            {
                tabControl.SelectedItem = RequireElement<TabItem>(window, "MeetingsTabItem");
                WriteProgress(progressPath, "meetings-selected");
                RequireElement<Button>(window, "RefreshMeetingsButton").RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                WaitForMeetingRows(window);
                WriteProgress(progressPath, "meeting-rows-loaded");
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
                    WriteProgress(progressPath, "cleanup-recommendation-loaded");
                }
            }

            if (state == SyntheticShellState.SettingsRecording)
            {
                if (!settingsAction.Focus())
                {
                    throw new InvalidOperationException("Open Settings must accept focus before opening the Settings window.");
                }

                settingsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                settingsWindow = Application.Current.Windows
                    .Cast<Window>()
                    .Single(candidate => !ReferenceEquals(candidate, window));
                settingsWindow.UpdateLayout();
                settingsWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                WriteProgress(progressPath, "settings-opened");
            }

            var scaleToken = ((int)Math.Round(rasterScale * 100d)).ToString();
            var screenshotPath = Path.Combine(rootDirectory, $"{stateToken}-{logicalWidth}x{logicalHeight}-{scaleToken}.png");
            var automationTracePath = Path.Combine(rootDirectory, $"{stateToken}-automation-tree-{scaleToken}.txt");
            var keyboardTracePath = Path.Combine(rootDirectory, $"{stateToken}-keyboard-trace-{scaleToken}.txt");

            SaveScreenshot(settingsWindow ?? window, screenshotPath, rasterScale);
            var traceElements = state == SyntheticShellState.SettingsRecording
                ? new FrameworkElement[]
                {
                    RequireElement<Button>(settingsWindow!, "SettingsRecordingSectionButton"),
                    RequireElement<Button>(settingsWindow!, "SaveChangesButton"),
                    RequireElement<TextBlock>(settingsWindow!, "FooterStatusTextBlock"),
                }
                : state is SyntheticShellState.Processing or SyntheticShellState.SelectionActive or SyntheticShellState.CleanupRecommendation
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
            File.WriteAllLines(
                keyboardTracePath,
                settingsWindow is null
                    ? CreateKeyboardTrace(settingsAction, tabControl)
                    : CreateSettingsKeyboardTrace(settingsWindow, settingsAction));

            return new WpfRenderEvidence(rootDirectory, screenshotPath, automationTracePath, keyboardTracePath);
        }
        finally
        {
            settingsWindow?.Close();
            window.Close();
            if (!application.Dispatcher.HasShutdownStarted)
            {
                application.Shutdown();
            }

            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
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
        WaitForCondition(window.Dispatcher, () => meetings.Items.Count > 0, TimeSpan.FromSeconds(10));

        if (meetings.Items.Count == 0)
        {
            throw new InvalidOperationException("Synthetic processing manifest did not appear in the Meetings workspace.");
        }
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static void WriteProgress(string path, string stage)
    {
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {stage}{Environment.NewLine}");
    }

    private static void WaitForCleanupRecommendation(FrameworkElement window)
    {
        var banner = RequireElement<Border>(window, "MeetingCleanupReviewBannerBorder");
        WaitForCondition(window.Dispatcher, () => banner.Visibility == Visibility.Visible, TimeSpan.FromSeconds(10));

        if (banner.Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("Synthetic cleanup recommendation did not become visible.");
        }
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private static void WaitForCondition(
        Dispatcher dispatcher,
        Func<bool> condition,
        TimeSpan timeout)
    {
        if (condition())
        {
            return;
        }

        var deadline = DateTime.UtcNow.Add(timeout);
        var frame = new DispatcherFrame();
        using var signal = new System.Threading.Timer(
            _ => dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (condition() || DateTime.UtcNow >= deadline)
                    {
                        frame.Continue = false;
                    }
                }),
                DispatcherPriority.Send),
            null,
            TimeSpan.FromMilliseconds(25),
            TimeSpan.FromMilliseconds(25));
        Dispatcher.PushFrame(frame);
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
        if (!settingsAction.IsEnabled)
        {
            throw new InvalidOperationException("Open Settings must be enabled in the synthetic profile.");
        }

        if (!settingsAction.Focus())
        {
            throw new InvalidOperationException("Open Settings must accept keyboard focus.");
        }
        trace.Add("Focus: " + GetFocusedName());

        settingsAction.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        trace.Add("Tab: " + GetFocusedName());

        if (!tabControl.Focus())
        {
            throw new InvalidOperationException("Primary navigation must accept keyboard focus.");
        }
        trace.Add("Primary navigation: " + GetFocusedName());
        return trace;
    }

    private static IReadOnlyList<string> CreateSettingsKeyboardTrace(
        Window settingsWindow,
        Button settingsAction)
    {
        var sectionButton = RequireElement<Button>(settingsWindow, "SettingsRecordingSectionButton");
        if (!sectionButton.Focus())
        {
            throw new InvalidOperationException("Settings Recording section must accept keyboard focus.");
        }

        var trace = new List<string>
        {
            "Focus: " + GetFocusedName(),
        };
        sectionButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        trace.Add("Tab: " + GetFocusedName());

        var source = PresentationSource.FromVisual(settingsWindow)
            ?? throw new InvalidOperationException("Settings window has no presentation source for Escape validation.");
        settingsWindow.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
        settingsWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        if (settingsWindow.IsVisible)
        {
            throw new InvalidOperationException("Escape must close the Settings window.");
        }

        trace.Add("Escape: " + GetFocusedName());
        if (!string.Equals(GetFocusedName(), "Open Settings", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Escape must return focus to Open Settings.");
        }

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
        SettingsRecording,
    }
}
