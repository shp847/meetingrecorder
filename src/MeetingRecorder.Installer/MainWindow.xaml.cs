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
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IReadOnlyList<string> _forwardedArguments;

    private GitHubReleaseBootstrapInfo? _latestRelease;
    private string _statusTitle = "Preparing the installer";
    private string _statusMessage = "One click is all most users should need. The installer will do the rest.";
    private string _releaseSummary = "Preparing installer package metadata.";
    private string _progressCaption = "Starting up...";
    private string _activityLog = "Installer started.";
    private string _manualSteps;
    private string _installRoot = AppDataPaths.GetManagedInstallRoot();
    private string _releasePageUrl = AppBranding.DefaultReleasePageUrl;
    private double _progressPercent;
    private bool _isIndeterminate = true;
    private bool _isBusy;
    private bool _installSucceeded;
    private string _statusTone = "Progress";
    private string _statusToneLabel = "Preparing";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _fileDownloader = new HttpFileDownloader();
        _forwardedArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _bootstrapper = new InstallerBootstrapper(
            new GitHubReleaseBootstrapService(new HttpAppUpdateFeedClient()),
            _fileDownloader);
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

    public string StatusTone
    {
        get => _statusTone;
        private set => SetProperty(ref _statusTone, value);
    }

    public string StatusToneLabel
    {
        get => _statusToneLabel;
        private set => SetProperty(ref _statusToneLabel, value);
    }

    public bool CanRetryInstall => !_isBusy && !_installSucceeded;

    public bool CanLaunchBackupInstaller => !_isBusy;

    public bool CanOpenInstallFolder => !_isBusy && Directory.Exists(_installRoot);

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
        SetStatusTone("Progress", "Installing");
        SetBusy(true);
        StatusTitle = "Preparing installer assets";
        StatusMessage = "The installer is locating a bundled local package or preparing the shared bootstrap path.";
        ProgressCaption = "Starting...";
        IsIndeterminate = true;

        try
        {
            var progress = new Progress<InstallerProgressInfo>(UpdateProgress);
            var result = await _bootstrapper.InstallLatestAsync(_forwardedArguments, progress, _lifetimeCancellation.Token);

            _latestRelease = result.ReleaseInfo;
            _installRoot = result.InstallRoot;
            _releasePageUrl = result.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl;
            ManualSteps = result.ManualSteps;

            _installSucceeded = true;
            SetStatusTone("Success", "Handoff complete");
            StatusTitle = "Command installer launched";
            StatusMessage =
                $"Continue in the command window that opened for {AppBranding.ProductName}. " +
                "This EXE now hands off to the shared command installer path, preferring bundled local package assets when they are present and otherwise using the GitHub bootstrap flow. " +
                $"App files install into '{_installRoot}', while writable config, logs, models, and work files stay under %LOCALAPPDATA%\\MeetingRecorder.";
            ReleaseSummary = BuildReleaseSummary(result.ReleaseInfo);
            ProgressPercent = 100;
            ProgressCaption = $"Diagnostic log: {result.DiagnosticLogPath}";
            IsIndeterminate = false;
            AppendLog($"Command bootstrap launched from '{result.BootstrapCommandPath}'.");
            AppendLog($"Diagnostic log: {result.DiagnosticLogPath}");
        }
        catch (OperationCanceledException)
        {
            SetStatusTone("Warning", "Cancelled");
            StatusTitle = "Installer cancelled";
            StatusMessage = "The installer was closed before it could finish.";
            ProgressCaption = "Cancelled.";
            IsIndeterminate = false;
            AppendLog("Installer cancelled.");
        }
        catch (Exception exception)
        {
            _installSucceeded = false;
            SetStatusTone("Danger", "Needs attention");
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

            if (progress.Title.Contains("Latest release found", StringComparison.OrdinalIgnoreCase) ||
                progress.Title.Contains("Using local installer assets", StringComparison.OrdinalIgnoreCase))
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
            SetStatusTone("Warning", "Fallback path");
            StatusTitle = "Launching the backup CMD installer";
            StatusMessage = "Preparing the fallback command installer.";
            ProgressCaption = "Preparing the fallback installer...";
            IsIndeterminate = true;
            AppendLog("Preparing backup installer assets.");

            var commandPath = await _bootstrapper.DownloadBackupInstallerAsync(_latestRelease, _lifetimeCancellation.Token);
            var workingDirectory = Path.GetDirectoryName(commandPath)
                ?? throw new InvalidOperationException("The backup installer path did not have a parent directory.");

            Process.Start(InstallerBootstrapper.BuildBootstrapHandoffStartInfo(
                commandPath,
                workingDirectory,
                BuildExecutableBootstrapArguments()));

            AppendLog("Backup CMD installer launched.");
            Close();
        }
        catch (Exception exception)
        {
            SetStatusTone("Danger", "Needs attention");
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
            return "Release metadata is still loading.";
        }

        var sizeText = releaseInfo.AppZipAsset.SizeBytes.HasValue
            ? $"{Math.Round(releaseInfo.AppZipAsset.SizeBytes.Value / BytesPerMegabyte, 1):0.0} MB"
            : "size unknown";
        var publishedText = releaseInfo.PublishedAtUtc.HasValue
            ? releaseInfo.PublishedAtUtc.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "publish time unavailable";
        return $"Version {releaseInfo.Version} | {sizeText} | published {publishedText}";
    }

    private void SetStatusTone(string tone, string label)
    {
        StatusTone = tone;
        StatusToneLabel = label;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        OnPropertyChanged(nameof(CanRetryInstall));
        OnPropertyChanged(nameof(CanLaunchBackupInstaller));
        OnPropertyChanged(nameof(CanOpenInstallFolder));
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

    private IReadOnlyList<string> BuildExecutableBootstrapArguments()
    {
        var arguments = new List<string>(_forwardedArguments);
        if (!arguments.Any(argument =>
                string.Equals(argument, "-InstallChannel", StringComparison.OrdinalIgnoreCase)))
        {
            arguments.Add("-InstallChannel");
            arguments.Add("ExecutableBootstrap");
        }

        return arguments;
    }
}
