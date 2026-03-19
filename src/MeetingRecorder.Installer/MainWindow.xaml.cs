using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MeetingRecorder.Installer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double BytesPerMegabyte = 1024d * 1024d;
    private readonly HttpFileDownloader _fileDownloader;
    private readonly InstallerBootstrapper _bootstrapper;
    private readonly WindowsShortcutService _shortcutService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private GitHubReleaseBootstrapInfo? _latestRelease;
    private string _statusTitle = "Preparing the installer";
    private string _statusMessage = "One click is all most users should need. The installer will do the rest.";
    private string _releaseSummary = "Waiting to contact GitHub for the latest release.";
    private string _progressCaption = "Starting up...";
    private string _activityLog = "Installer started.";
    private string _manualSteps;
    private string _installRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MeetingRecorder");
    private string _releasePageUrl = AppBranding.DefaultReleasePageUrl;
    private double _progressPercent;
    private bool _isIndeterminate = true;
    private bool _isBusy;
    private bool _installSucceeded;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _fileDownloader = new HttpFileDownloader();
        _shortcutService = new WindowsShortcutService();
        _bootstrapper = new InstallerBootstrapper(
            new GitHubReleaseBootstrapService(new HttpAppUpdateFeedClient()),
            _fileDownloader,
            new PortableInstallService());
        _manualSteps = _bootstrapper.BuildManualSteps(null);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        UpdateButtonStates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ReleaseSummary
    {
        get => _releaseSummary;
        private set => SetProperty(ref _releaseSummary, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public string ProgressCaption
    {
        get => _progressCaption;
        private set => SetProperty(ref _progressCaption, value);
    }

    public string ActivityLog
    {
        get => _activityLog;
        private set => SetProperty(ref _activityLog, value);
    }

    public string ManualSteps
    {
        get => _manualSteps;
        private set => SetProperty(ref _manualSteps, value);
    }

    public bool CanRetryInstall => !_isBusy && !_installSucceeded;

    public bool CanLaunchBackupInstaller => !_isBusy && !_installSucceeded;

    public bool CanOpenInstallFolder => !_isBusy && Directory.Exists(_installRoot);

    public bool CanCreateDesktopShortcut => !_isBusy && Directory.Exists(_installRoot);

    public bool CanCreateStartMenuShortcut => !_isBusy && Directory.Exists(_installRoot);

    public bool CanOpenReleasePage => !_isBusy;

    public bool CanCopyManualSteps => true;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await StartInstallAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _lifetimeCancellation.Cancel();
        _fileDownloader.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async Task StartInstallAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _installSucceeded = false;
        SetBusy(true);
        StatusTitle = "Checking GitHub for the latest release";
        StatusMessage = "The installer is contacting GitHub and preparing the portable install.";
        ProgressCaption = "Starting...";
        IsIndeterminate = true;

        try
        {
            var progress = new Progress<InstallerProgressInfo>(UpdateProgress);
            var result = await _bootstrapper.InstallLatestAsync(progress, _lifetimeCancellation.Token);

            _latestRelease = result.ReleaseInfo;
            _installRoot = result.InstallRoot;
            _releasePageUrl = result.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl;
            ManualSteps = result.ManualSteps;

            _installSucceeded = true;
            StatusTitle = "Installed successfully";
            StatusMessage =
                $"{AppBranding.ProductName} was installed into {_installRoot} and launched directly. " +
                "If you want shortcuts, use the explicit shortcut buttons below so the main install path stays friendlier to corporate endpoint controls.";
            ReleaseSummary = BuildReleaseSummary(result.ReleaseInfo);
            ProgressPercent = 100;
            ProgressCaption = "Done. The app is ready to use.";
            IsIndeterminate = false;
            AppendLog("Installer finished successfully.");
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "Installer cancelled";
            StatusMessage = "The installer was closed before it could finish.";
            ProgressCaption = "Cancelled.";
            IsIndeterminate = false;
            AppendLog("Installer cancelled.");
        }
        catch (Exception exception)
        {
            _installSucceeded = false;
            StatusTitle = "The primary installer hit a problem";
            StatusMessage = exception.Message;
            ProgressCaption = "You can retry, use the backup CMD installer, or follow the manual steps.";
            IsIndeterminate = false;
            _releasePageUrl = _bootstrapper.GetReleasePageUrl(_latestRelease);
            ManualSteps = _bootstrapper.BuildManualSteps(_latestRelease);
            AppendLog(exception.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateProgress(InstallerProgressInfo progress)
    {
        StatusTitle = progress.Title;
        StatusMessage = progress.Message;

        if (progress.Percent.HasValue)
        {
            IsIndeterminate = false;
            ProgressPercent = Math.Max(0, Math.Min(100, progress.Percent.Value));
        }
        else
        {
            IsIndeterminate = true;
        }

        if (!string.IsNullOrWhiteSpace(progress.Detail))
        {
            ProgressCaption = progress.Detail;

            if (progress.Title.Contains("Latest release found", StringComparison.OrdinalIgnoreCase))
            {
                ReleaseSummary = progress.Detail;
            }
        }
        else
        {
            ProgressCaption = progress.Message;
        }

        if (!progress.Title.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog($"{progress.Title}: {progress.Message}");
        }
    }

    private async void RetryInstallButton_Click(object sender, RoutedEventArgs e)
    {
        ActivityLog = "Retrying the primary installer...";
        await StartInstallAsync();
    }

    private async void BackupInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            StatusTitle = "Launching the backup CMD installer";
            StatusMessage = "Downloading the fallback command installer from GitHub.";
            ProgressCaption = "Preparing the fallback installer...";
            IsIndeterminate = true;
            AppendLog("Downloading backup installer assets.");

            var commandPath = await _bootstrapper.DownloadBackupInstallerAsync(_latestRelease, _lifetimeCancellation.Token);
            var workingDirectory = Path.GetDirectoryName(commandPath)
                ?? throw new InvalidOperationException("The backup installer path did not have a parent directory.");

            Process.Start(new ProcessStartInfo
            {
                FileName = commandPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });

            AppendLog("Backup CMD installer launched.");
            Close();
        }
        catch (Exception exception)
        {
            StatusTitle = "Backup installer failed";
            StatusMessage = exception.Message;
            ProgressCaption = "Use the manual steps if the backup path is blocked too.";
            IsIndeterminate = false;
            AppendLog(exception.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenInstallFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_installRoot))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _installRoot,
            UseShellExecute = true,
        });
    }

    private void CreateDesktopShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        TryCreateShortcut(
            _shortcutService.GetDesktopShortcutPath(),
            "Desktop");
    }

    private void CreateStartMenuShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        TryCreateShortcut(
            _shortcutService.GetStartMenuShortcutPath(),
            "Start menu");
    }

    private void OpenReleasePageButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _releasePageUrl,
            UseShellExecute = true,
        });
    }

    private void CopyManualStepsButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ManualSteps);
        AppendLog("Manual install steps copied to the clipboard.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var timestampedLine = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        ActivityLog = string.IsNullOrWhiteSpace(ActivityLog)
            ? timestampedLine
            : ActivityLog + Environment.NewLine + timestampedLine;
    }

    private string BuildReleaseSummary(GitHubReleaseBootstrapInfo? releaseInfo)
    {
        if (releaseInfo is null)
        {
            return "Release metadata was not available.";
        }

        var sizeText = releaseInfo.AppZipAsset.SizeBytes.HasValue
            ? $"{Math.Round(releaseInfo.AppZipAsset.SizeBytes.Value / BytesPerMegabyte, 1):0.0} MB"
            : "size unknown";
        var publishedText = releaseInfo.PublishedAtUtc.HasValue
            ? releaseInfo.PublishedAtUtc.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "publish time unavailable";
        return $"Latest GitHub release: {releaseInfo.Version} | {sizeText} | published {publishedText}";
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateButtonStates();
    }

    private void TryCreateShortcut(string shortcutPath, string surfaceName)
    {
        if (!Directory.Exists(_installRoot))
        {
            return;
        }

        var launcherPath = ResolveShortcutTargetPath();
        var iconPath = Path.Combine(_installRoot, "MeetingRecorder.ico");
        var result = _shortcutService.TryCreateShortcut(shortcutPath, launcherPath, _installRoot, iconPath);
        if (result.Success)
        {
            StatusTitle = $"{surfaceName} shortcut created";
            StatusMessage = $"A {surfaceName.ToLowerInvariant()} shortcut was added for {AppBranding.ProductName}.";
            AppendLog($"Created {surfaceName.ToLowerInvariant()} shortcut at '{shortcutPath}'.");
        }
        else
        {
            StatusTitle = $"{surfaceName} shortcut was blocked";
            StatusMessage =
                $"The install succeeded, but Windows blocked {surfaceName.ToLowerInvariant()} shortcut creation. " +
                "You can still launch from the app folder or pin the app manually.";
            AppendLog(
                $"Unable to create {surfaceName.ToLowerInvariant()} shortcut at '{shortcutPath}': {result.ErrorMessage}");
        }

        UpdateButtonStates();
    }

    private string ResolveShortcutTargetPath()
    {
        var launcherPath = Path.Combine(_installRoot, "Run-MeetingRecorder.cmd");
        if (File.Exists(launcherPath))
        {
            return launcherPath;
        }

        return Path.Combine(_installRoot, "MeetingRecorder.App.exe");
    }

    private void UpdateButtonStates()
    {
        OnPropertyChanged(nameof(CanRetryInstall));
        OnPropertyChanged(nameof(CanLaunchBackupInstaller));
        OnPropertyChanged(nameof(CanOpenInstallFolder));
        OnPropertyChanged(nameof(CanCreateDesktopShortcut));
        OnPropertyChanged(nameof(CanCreateStartMenuShortcut));
        OnPropertyChanged(nameof(CanOpenReleasePage));
        OnPropertyChanged(nameof(CanCopyManualSteps));
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
