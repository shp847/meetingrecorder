using AppPlatform.Abstractions;
using System.Diagnostics;
using System.IO.Compression;

namespace AppPlatform.Deployment;

public sealed class UpdatePackageInstaller
{
    private static readonly TimeSpan SourceProcessExitTimeout = TimeSpan.FromSeconds(15);

    private readonly PortableBundleInstaller _bundleInstaller;
    private readonly InstallPathProcessManager _processManager;
    private readonly IDeploymentLogger _logger;

    public UpdatePackageInstaller()
        : this(new PortableBundleInstaller(), new InstallPathProcessManager(), NullDeploymentLogger.Instance)
    {
    }

    public UpdatePackageInstaller(
        PortableBundleInstaller bundleInstaller,
        InstallPathProcessManager processManager)
        : this(bundleInstaller, processManager, NullDeploymentLogger.Instance)
    {
    }

    public UpdatePackageInstaller(
        PortableBundleInstaller bundleInstaller,
        InstallPathProcessManager processManager,
        IDeploymentLogger logger)
    {
        _bundleInstaller = bundleInstaller;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<UpdateResult> ApplyAsync(
        AppProductManifest manifest,
        UpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ZipPath))
        {
            throw new FileNotFoundException("The downloaded update package could not be found.", request.ZipPath);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), manifest.ProductId + "-update-" + Guid.NewGuid().ToString("N"));
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            _logger.Info($"Validating update package '{request.ZipPath}'.");
            ValidateUpdatePackage(request.ZipPath, request.ReleaseAssetSizeBytes);

            _logger.Info($"Applying update from '{request.ZipPath}' into '{request.InstallRoot}'.");
            await WaitForSourceProcessExitAsync(request.SourceProcessId, request.InstallRoot, cancellationToken);

            _logger.Info($"Using update workspace '{tempRoot}'.");
            Directory.CreateDirectory(extractPath);
            _logger.Info($"Extracting update package into '{extractPath}'.");
            try
            {
                ZipFile.ExtractToDirectory(request.ZipPath, extractPath, overwriteFiles: true);
            }
            catch (InvalidDataException exception)
            {
                throw CreateInvalidUpdatePackageException(request.ZipPath, exception);
            }

            var installResult = await _bundleInstaller.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: extractPath,
                    InstallRoot: request.InstallRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: request.LaunchAfterInstall,
                    ReleaseVersion: request.ReleaseVersion,
                    ReleasePublishedAtUtc: request.ReleasePublishedAtUtc,
                    ReleaseAssetSizeBytes: request.ReleaseAssetSizeBytes,
                    Channel: request.Channel),
                cancellationToken);

            return new UpdateResult(
                installResult.InstallRoot,
                installResult.ExecutablePath,
                installResult.ReleaseVersion,
                Succeeded: true);
        }
        finally
        {
            _logger.Info($"Cleaning up update workspace '{tempRoot}' and downloaded package '{request.ZipPath}'.");
            TryDeleteDirectory(tempRoot);
            TryDeleteFile(request.ZipPath);
        }
    }

    private static void ValidateUpdatePackage(string zipPath, long? expectedSizeBytes)
    {
        var actualSizeBytes = new FileInfo(zipPath).Length;
        if (expectedSizeBytes is > 0 && actualSizeBytes != expectedSizeBytes.Value)
        {
            throw CreateInvalidUpdatePackageException(
                zipPath,
                $"expected {expectedSizeBytes.Value} bytes but found {actualSizeBytes} bytes");
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            _ = archive.Entries.Count;
        }
        catch (InvalidDataException exception)
        {
            throw CreateInvalidUpdatePackageException(zipPath, exception);
        }
    }

    private static InvalidOperationException CreateInvalidUpdatePackageException(
        string zipPath,
        string detail)
    {
        return new InvalidOperationException(
            $"The downloaded update package '{zipPath}' is not a valid app update ZIP ({detail}). It may be the wrong release asset or an incomplete download. Download the Meeting Recorder app ZIP again.");
    }

    private static InvalidOperationException CreateInvalidUpdatePackageException(
        string zipPath,
        Exception exception)
    {
        return new InvalidOperationException(
            $"The downloaded update package '{zipPath}' is not a readable ZIP archive. It may be the wrong release asset or an incomplete download. Download the Meeting Recorder app ZIP again.",
            exception);
    }

    private async Task WaitForSourceProcessExitAsync(
        int processId,
        string installRoot,
        CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            _logger.Info("No source process ID was supplied; skipping source-process wait.");
            return;
        }

        _logger.Info($"Waiting for source process {processId} to exit before applying the update.");
        _processManager.TrySignalInstallerShutdown();

        if (await WaitForProcessExitAsync(processId, SourceProcessExitTimeout, cancellationToken))
        {
            _logger.Info($"Source process {processId} exited after the shutdown signal.");
            return;
        }

        _logger.Info(
            $"Source process {processId} did not exit within {SourceProcessExitTimeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} seconds. Escalating install-path release.");
        await _processManager.EnsureInstallPathReleasedAsync(installRoot, cancellationToken);

        if (await WaitForProcessExitAsync(processId, SourceProcessExitTimeout, cancellationToken))
        {
            _logger.Info($"Source process {processId} exited after the install-path release sequence.");
            return;
        }

        throw new TimeoutException(
            $"The running app process ({processId}) did not exit in time for the update. Close Meeting Recorder and try again.");
    }

    private async Task<bool> WaitForProcessExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // The source process has already exited.
            return true;
        }
        catch (ArgumentException)
        {
            // The source process has already exited.
            return true;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
