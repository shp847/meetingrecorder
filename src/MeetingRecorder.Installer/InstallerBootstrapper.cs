using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Installer;

internal sealed class InstallerBootstrapper
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    private readonly GitHubReleaseBootstrapService _bootstrapService;
    private readonly HttpFileDownloader _fileDownloader;
    private readonly IInstallerProcessLauncher _processLauncher;
    private readonly string? _bootstrapAssetRootOverride;

    public InstallerBootstrapper(
        GitHubReleaseBootstrapService bootstrapService,
        HttpFileDownloader fileDownloader)
        : this(
            bootstrapService,
            fileDownloader,
            new InstallerProcessLauncher(),
            bootstrapAssetRootOverride: null)
    {
    }

    internal InstallerBootstrapper(
        GitHubReleaseBootstrapService bootstrapService,
        HttpFileDownloader fileDownloader,
        IInstallerProcessLauncher processLauncher)
        : this(
            bootstrapService,
            fileDownloader,
            processLauncher,
            bootstrapAssetRootOverride: null)
    {
    }

    internal InstallerBootstrapper(
        GitHubReleaseBootstrapService bootstrapService,
        HttpFileDownloader fileDownloader,
        IInstallerProcessLauncher processLauncher,
        string? bootstrapAssetRootOverride)
    {
        _bootstrapService = bootstrapService;
        _fileDownloader = fileDownloader;
        _processLauncher = processLauncher;
        _bootstrapAssetRootOverride = bootstrapAssetRootOverride;
    }

    public Task<InstallerSessionResult> InstallLatestAsync(
        IProgress<InstallerProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        return InstallLatestAsync(
            Array.Empty<string>(),
            progress,
            cancellationToken);
    }

    public async Task<InstallerSessionResult> InstallLatestAsync(
        IReadOnlyList<string> forwardedArguments,
        IProgress<InstallerProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var installRoot = AppDataPaths.GetManagedInstallRoot();
        var localAssetSet = TryResolveLocalBootstrapAssetSet(_bootstrapAssetRootOverride ?? AppContext.BaseDirectory);
        if (localAssetSet is not null)
        {
            progress?.Report(new InstallerProgressInfo(
                "Using local installer assets",
                $"Installing the colocated {localAssetSet.ReleaseInfo.Version} package instead of querying GitHub.",
                18,
                BuildReleaseSummary(localAssetSet.ReleaseInfo)));

            var localDiagnosticLogPath = CreateInstallerLogPath("bootstrap-handoff");
            var localBootstrapArguments = BuildBootstrapForwardedArguments(
                forwardedArguments,
                packageZipPath: localAssetSet.PackageZipPath,
                releaseInfo: localAssetSet.ReleaseInfo);
            WriteBootstrapHandoffLog(localDiagnosticLogPath, localAssetSet.ReleaseInfo, localAssetSet.AssetSet, localBootstrapArguments);

            progress?.Report(new InstallerProgressInfo(
                "Launching the command bootstrapper",
                "Continue in the command window that opens next. This EXE launcher is handing off to the local packaged installer assets.",
                100,
                $"Diagnostic log: {localDiagnosticLogPath}"));

            _processLauncher.Launch(BuildBootstrapHandoffStartInfo(
                localAssetSet.AssetSet.CommandPath,
                localAssetSet.AssetSet.WorkingDirectory,
                localBootstrapArguments));

            return new InstallerSessionResult(
                InstallRoot: installRoot,
                BootstrapCommandPath: localAssetSet.AssetSet.CommandPath,
                DiagnosticLogPath: localDiagnosticLogPath,
                ReleaseInfo: localAssetSet.ReleaseInfo,
                ManualSteps: ManualInstallGuideBuilder.Build(localAssetSet.ReleaseInfo, installRoot),
                ReleasePageUrl: localAssetSet.ReleaseInfo.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl);
        }

        progress?.Report(new InstallerProgressInfo(
            "Checking GitHub for the latest release",
            "Reading the current release metadata.",
            8));

        var release = await _bootstrapService.GetLatestReleaseAsync(
            AppBranding.DefaultUpdateFeedUrl,
            cancellationToken);
        var manualSteps = ManualInstallGuideBuilder.Build(release, installRoot);

        progress?.Report(new InstallerProgressInfo(
            "Latest release found",
            $"Preparing a thin handoff for {release.Version}.",
            18,
            BuildReleaseSummary(release)));

        var assetSet = await DownloadBootstrapAssetSetAsync(release, progress, cancellationToken);
        var diagnosticLogPath = CreateInstallerLogPath("bootstrap-handoff");
        var bootstrapArguments = BuildBootstrapForwardedArguments(forwardedArguments);
        WriteBootstrapHandoffLog(diagnosticLogPath, release, assetSet, bootstrapArguments);

        progress?.Report(new InstallerProgressInfo(
            "Launching the command bootstrapper",
            "Continue in the command window that opens next. This EXE launcher now steps aside after the handoff.",
            100,
            $"Diagnostic log: {diagnosticLogPath}"));

        _processLauncher.Launch(BuildBootstrapHandoffStartInfo(
            assetSet.CommandPath,
            assetSet.WorkingDirectory,
            bootstrapArguments));

        return new InstallerSessionResult(
            InstallRoot: installRoot,
            BootstrapCommandPath: assetSet.CommandPath,
            DiagnosticLogPath: diagnosticLogPath,
            ReleaseInfo: release,
            ManualSteps: manualSteps,
            ReleasePageUrl: release.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl);
    }

    public async Task<string> DownloadBackupInstallerAsync(
        GitHubReleaseBootstrapInfo? releaseInfo,
        CancellationToken cancellationToken)
    {
        var localAssetSet = TryResolveLocalBootstrapAssetSet(_bootstrapAssetRootOverride ?? AppContext.BaseDirectory);
        if (localAssetSet is not null)
        {
            return localAssetSet.AssetSet.CommandPath;
        }

        var assetSet = await DownloadBootstrapAssetSetAsync(releaseInfo, progress: null, cancellationToken);
        return assetSet.CommandPath;
    }

    public string BuildManualSteps(GitHubReleaseBootstrapInfo? releaseInfo)
    {
        return ManualInstallGuideBuilder.Build(
            releaseInfo,
            AppDataPaths.GetManagedInstallRoot());
    }

    public string GetReleasePageUrl(GitHubReleaseBootstrapInfo? releaseInfo)
    {
        return releaseInfo?.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl;
    }

    internal static ProcessStartInfo BuildBootstrapHandoffStartInfo(
        string commandPath,
        string workingDirectory,
        IReadOnlyList<string> forwardedArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        return new ProcessStartInfo
        {
            FileName = commandPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Arguments = string.Join(" ", forwardedArguments.Select(QuoteCommandArgument)),
        };
    }

    internal static ProcessStartInfo BuildLaunchStartInfo(string executablePath, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };
    }

    private async Task<BootstrapAssetSet> DownloadBootstrapAssetSetAsync(
        GitHubReleaseBootstrapInfo? releaseInfo,
        IProgress<InstallerProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "MeetingRecorderBootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var commandUrl = releaseInfo?.BackupCommandAsset?.DownloadUrl ?? BuildStableReleaseAssetUrl("Install-LatestFromGitHub.cmd");
        var powerShellUrl = releaseInfo?.BackupPowerShellAsset?.DownloadUrl ?? BuildStableReleaseAssetUrl("Install-LatestFromGitHub.ps1");
        var commandPath = Path.Combine(tempRoot, "Install-LatestFromGitHub.cmd");
        var powerShellPath = Path.Combine(tempRoot, "Install-LatestFromGitHub.ps1");

        progress?.Report(new InstallerProgressInfo(
            "Downloading the command bootstrapper",
            "Fetching the CMD and PowerShell fallback assets that own the real install flow.",
            34));

        await _fileDownloader.DownloadFileAsync(commandUrl, commandPath, (_, _, _) => { }, cancellationToken);

        progress?.Report(new InstallerProgressInfo(
            "Downloading the command bootstrapper",
            "Fetching the PowerShell implementation behind the CMD launcher.",
            62));

        await _fileDownloader.DownloadFileAsync(powerShellUrl, powerShellPath, (_, _, _) => { }, cancellationToken);

        return new BootstrapAssetSet(commandPath, powerShellPath, tempRoot);
    }

    private static string BuildStableReleaseAssetUrl(string assetName)
    {
        return $"https://github.com/{AppBranding.GitHubRepositoryOwner}/{AppBranding.GitHubRepositoryName}/releases/latest/download/{assetName}";
    }

    private static IReadOnlyList<string> BuildBootstrapForwardedArguments(
        IReadOnlyList<string> forwardedArguments,
        string? packageZipPath = null,
        GitHubReleaseBootstrapInfo? releaseInfo = null)
    {
        var effectiveArguments = new List<string>(forwardedArguments);
        if (!effectiveArguments.Any(argument =>
                string.Equals(argument, "-InstallChannel", StringComparison.OrdinalIgnoreCase)))
        {
            effectiveArguments.Add("-InstallChannel");
            effectiveArguments.Add("ExecutableBootstrap");
        }

        if (!string.IsNullOrWhiteSpace(packageZipPath))
        {
            effectiveArguments.Add("-PackageZipPath");
            effectiveArguments.Add(packageZipPath);
        }

        if (releaseInfo is not null)
        {
            if (!string.IsNullOrWhiteSpace(releaseInfo.Version))
            {
                effectiveArguments.Add("-ReleaseVersion");
                effectiveArguments.Add(releaseInfo.Version);
            }

            if (releaseInfo.PublishedAtUtc.HasValue)
            {
                effectiveArguments.Add("-ReleasePublishedAtUtc");
                effectiveArguments.Add(releaseInfo.PublishedAtUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (releaseInfo.AppZipAsset.SizeBytes.HasValue && releaseInfo.AppZipAsset.SizeBytes.Value > 0)
            {
                effectiveArguments.Add("-ReleaseAssetSizeBytes");
                effectiveArguments.Add(releaseInfo.AppZipAsset.SizeBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return effectiveArguments;
    }

    private static string CreateInstallerLogPath(string operationName)
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderInstaller");
        Directory.CreateDirectory(logDirectory);
        return Path.Combine(
            logDirectory,
            $"{operationName}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
    }

    private static void WriteBootstrapHandoffLog(
        string logPath,
        GitHubReleaseBootstrapInfo release,
        BootstrapAssetSet assetSet,
        IReadOnlyList<string> bootstrapArguments)
    {
        var lines = new List<string>
        {
            $"{DateTimeOffset.Now:O} Thin EXE launcher preparing command-bootstrap handoff.",
            $"{DateTimeOffset.Now:O} Release version: {release.Version}",
            $"{DateTimeOffset.Now:O} Command bootstrap path: {assetSet.CommandPath}",
            $"{DateTimeOffset.Now:O} PowerShell bootstrap path: {assetSet.PowerShellPath}",
            $"{DateTimeOffset.Now:O} Working directory: {assetSet.WorkingDirectory}",
            $"{DateTimeOffset.Now:O} Forwarded arguments: {string.Join(" ", bootstrapArguments.Select(QuoteCommandArgument))}",
        };

        if (!string.IsNullOrWhiteSpace(assetSet.PackageZipPath))
        {
            lines.Add($"{DateTimeOffset.Now:O} Local package zip path: {assetSet.PackageZipPath}");
        }

        File.WriteAllLines(logPath, lines, Encoding.UTF8);
    }

    private static string BuildReleaseSummary(GitHubReleaseBootstrapInfo release)
    {
        var sizeText = release.AppZipAsset.SizeBytes.HasValue
            ? $"{Math.Round(release.AppZipAsset.SizeBytes.Value / BytesPerMegabyte, 1):0.0} MB"
            : "size unknown";
        var publishedText = release.PublishedAtUtc.HasValue
            ? release.PublishedAtUtc.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "publish time unavailable";
        return $"Version {release.Version} | {sizeText} | published {publishedText}";
    }

    private static string QuoteCommandArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return "\"" + argument.Replace("\"", "\"\"") + "\"";
    }

    internal static LocalBootstrapAssetSet? TryResolveLocalBootstrapAssetSet(string bootstrapAssetRoot)
    {
        if (string.IsNullOrWhiteSpace(bootstrapAssetRoot) || !Directory.Exists(bootstrapAssetRoot))
        {
            return null;
        }

        var commandPath = Path.Combine(bootstrapAssetRoot, "Install-LatestFromGitHub.cmd");
        var powerShellPath = Path.Combine(bootstrapAssetRoot, "Install-LatestFromGitHub.ps1");
        if (!File.Exists(commandPath) || !File.Exists(powerShellPath))
        {
            return null;
        }

        var packageZipPath = Directory.EnumerateFiles(bootstrapAssetRoot, "MeetingRecorder-*.zip", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(static file => file.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(static file => file.LastWriteTimeUtc)
            .Select(static file => file.FullName)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(packageZipPath))
        {
            return null;
        }

        var packageZipFile = new FileInfo(packageZipPath);
        var releaseVersionLabel = ResolveLocalPackageVersionLabel(packageZipFile.Name);
        var publishedAtUtc = packageZipFile.LastWriteTimeUtc == DateTime.MinValue
            ? (DateTimeOffset?)null
            : new DateTimeOffset(packageZipFile.LastWriteTimeUtc, TimeSpan.Zero);
        var releaseInfo = new GitHubReleaseBootstrapInfo(
            releaseVersionLabel,
            AppBranding.DefaultReleasePageUrl,
            publishedAtUtc,
            new GitHubReleaseAssetInfo("MeetingRecorderInstaller.exe", string.Empty, null, null),
            new GitHubReleaseAssetInfo(packageZipFile.Name, packageZipFile.FullName, packageZipFile.Length, publishedAtUtc),
            new GitHubReleaseAssetInfo(Path.GetFileName(commandPath), commandPath, new FileInfo(commandPath).Length, null),
            new GitHubReleaseAssetInfo(Path.GetFileName(powerShellPath), powerShellPath, new FileInfo(powerShellPath).Length, null));

        return new LocalBootstrapAssetSet(
            new BootstrapAssetSet(commandPath, powerShellPath, bootstrapAssetRoot, packageZipFile.FullName),
            packageZipFile.FullName,
            releaseInfo);
    }

    private static string ResolveLocalPackageVersionLabel(string packageFileName)
    {
        var match = Regex.Match(
            packageFileName ?? string.Empty,
            @"(?<version>\d+\.\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["version"].Value : "local";
    }
}

internal sealed record BootstrapAssetSet(
    string CommandPath,
    string PowerShellPath,
    string WorkingDirectory,
    string? PackageZipPath = null);

internal sealed record LocalBootstrapAssetSet(
    BootstrapAssetSet AssetSet,
    string PackageZipPath,
    GitHubReleaseBootstrapInfo ReleaseInfo);

internal interface IInstallerProcessLauncher
{
    void Launch(ProcessStartInfo startInfo);
}

internal sealed class InstallerProcessLauncher : IInstallerProcessLauncher
{
    public void Launch(ProcessStartInfo startInfo)
    {
        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("The command bootstrapper could not be started.");
        }
    }
}
