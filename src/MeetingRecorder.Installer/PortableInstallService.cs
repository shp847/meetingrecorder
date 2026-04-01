using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.IO;

namespace MeetingRecorder.Installer;

internal sealed class PortableInstallService
{
    private const string ManagedDataDirectoryName = "data";
    private const string PortableLauncherFileName = "Run-MeetingRecorder.cmd";
    private const string ExecutableFileName = "MeetingRecorder.App.exe";
    private static readonly string[] RequiredExecutablePayloadFiles =
    [
        ExecutableFileName,
        "AppPlatform.Deployment.Cli.exe",
        "MeetingRecorder.ProcessingWorker.exe",
        "MeetingRecorder.ProcessingWorker.dll",
        "MeetingRecorder.ProcessingWorker.deps.json",
        "MeetingRecorder.ProcessingWorker.runtimeconfig.json",
        "MeetingRecorder.Core.dll",
    ];
    private static readonly string[] PortableBundleMarkerFiles =
    [
        "portable.mode",
        "bundle-mode.txt",
    ];
    private readonly InstallPathProcessManager _processManager;

    public PortableInstallService()
        : this(new InstallPathProcessManager())
    {
    }

    internal PortableInstallService(InstallPathProcessManager processManager)
    {
        _processManager = processManager;
    }

    public string GetDefaultInstallRoot()
    {
        return AppDataPaths.GetManagedInstallRoot();
    }

    public async Task<string> InstallAsync(
        string sourceBundleRoot,
        string installRoot,
        GitHubReleaseBootstrapInfo releaseInfo,
        IProgress<InstallerProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceBundleRoot) || !Directory.Exists(sourceBundleRoot))
        {
            throw new InvalidOperationException("The extracted Meeting Recorder bundle could not be found.");
        }

        var resolvedInstallRoot = Path.GetFullPath(installRoot);
        var installParent = Directory.GetParent(resolvedInstallRoot)?.FullName
            ?? throw new InvalidOperationException("Install root must have a parent directory.");

        Directory.CreateDirectory(installParent);

        var stagingRoot = CreateWorkspacePath(installParent, "MRI");
        var backupRoot = CreateWorkspacePath(installParent, "MRB");
        var isUpdate = Directory.Exists(resolvedInstallRoot);
        var movedBackup = false;
        var finalInstallMoved = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _processManager.EnsureInstallPathReleasedAsync(resolvedInstallRoot, cancellationToken);

            progress?.Report(new InstallerProgressInfo(
                "Preparing the installation",
                "Copying the app bundle into a safe staging folder.",
                82,
                isUpdate ? "Update install detected. Preserving your existing data next." : "Fresh install detected."));

            CopyDirectoryContents(sourceBundleRoot, stagingRoot, overwriteExisting: true, cancellationToken);
            PrepareBundleForManagedInstall(stagingRoot);

            if (isUpdate)
            {
                progress?.Report(new InstallerProgressInfo(
                    "Preserving your data",
                    "Keeping your config, transcripts, recordings, and downloaded models while refreshing the app files.",
                    88));

                ApplyStagedBundleToExistingInstall(
                    stagingRoot,
                    resolvedInstallRoot,
                    backupRoot,
                    cancellationToken);
                movedBackup = Directory.Exists(backupRoot) && Directory.EnumerateFileSystemEntries(backupRoot).Any();
                finalInstallMoved = true;
            }
            else
            {
                if (Directory.Exists(resolvedInstallRoot))
                {
                    TryMoveExistingInstallToBackup(resolvedInstallRoot, backupRoot);
                    movedBackup = true;
                }

                Directory.Move(stagingRoot, resolvedInstallRoot);
                finalInstallMoved = true;
            }

            progress?.Report(new InstallerProgressInfo(
                "Applying portable settings",
                "Writing install metadata and confirming launch-on-login settings.",
                94));

            var config = await EnsureInstalledConfigAsync(
                resolvedInstallRoot,
                releaseInfo,
                cancellationToken);

            EnsureInstalledExecutablePayload(sourceBundleRoot, resolvedInstallRoot);
            var launchPath = ResolveInstalledPostInstallLaunchPath(resolvedInstallRoot);

            progress?.Report(new InstallerProgressInfo(
                "Finishing up",
                "Enabling launch on login and cleaning up temporary files.",
                100));

            var launchOnLoginChanged = new AutoStartRegistrationService()
                .SyncRegistration(config.LaunchOnLoginEnabled, launchPath);

            TryDeleteDirectory(stagingRoot);
            CleanupBackupDirectory(backupRoot);

            return launchPath;
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);

            if (movedBackup)
            {
                if (isUpdate)
                {
                    TryDeleteReplaceableInstallEntries(resolvedInstallRoot);
                    RestoreBackupEntries(backupRoot, resolvedInstallRoot);
                }
                else
                {
                    if (finalInstallMoved && Directory.Exists(resolvedInstallRoot))
                    {
                        TryDeleteDirectory(resolvedInstallRoot);
                    }

                    if (!Directory.Exists(resolvedInstallRoot) && Directory.Exists(backupRoot))
                    {
                        Directory.Move(backupRoot, resolvedInstallRoot);
                    }
                }
            }

            throw;
        }
    }

    internal static void PrepareBundleForManagedInstall(string stagingRoot)
    {
        foreach (var markerFile in PortableBundleMarkerFiles)
        {
            var markerPath = Path.Combine(stagingRoot, markerFile);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    internal static string CreateWorkspacePath(string installParent, string prefix)
    {
        if (string.IsNullOrWhiteSpace(installParent))
        {
            throw new ArgumentException("Install parent is required.", nameof(installParent));
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Workspace prefix is required.", nameof(prefix));
        }

        return Path.Combine(
            installParent,
            prefix + "-" + Guid.NewGuid().ToString("N")[..12]);
    }

    internal static string GetInstalledConfigPath(string? localApplicationDataRootOverride = null)
    {
        return AppDataPaths.GetManagedConfigPath(localApplicationDataRootOverride);
    }

    internal static void CleanupBackupDirectory(string backupRoot)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        try
        {
            ResetDirectoryAttributes(backupRoot);
            Directory.Delete(backupRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only. A leftover backup folder should not fail the install.
        }
    }

    internal static void ApplyStagedBundleToExistingInstall(
        string stagingRoot,
        string installRoot,
        string backupRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);

        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(backupRoot);

        MoveReplaceableInstallEntriesToBackup(installRoot, backupRoot, cancellationToken);
        MoveStagedEntriesIntoInstallRoot(stagingRoot, installRoot, cancellationToken);
        MergeDirectoryWithoutOverwriting(
            Path.Combine(stagingRoot, ManagedDataDirectoryName),
            Path.Combine(installRoot, ManagedDataDirectoryName),
            cancellationToken);
    }

    internal static string ResolveInstalledLaunchPath(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var launcherPath = Path.Combine(installRoot, PortableLauncherFileName);
        if (File.Exists(launcherPath))
        {
            return launcherPath;
        }

        var executablePath = Path.Combine(installRoot, ExecutableFileName);
        if (File.Exists(executablePath))
        {
            return executablePath;
        }

        throw new InvalidOperationException(
            $"The installed Meeting Recorder launcher could not be found under '{installRoot}'.");
    }

    internal static string ResolveInstalledPostInstallLaunchPath(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var executablePath = Path.Combine(installRoot, ExecutableFileName);
        if (File.Exists(executablePath))
        {
            return executablePath;
        }

        throw new InvalidOperationException(
            $"Meeting Recorder install is missing '{ExecutableFileName}' under '{installRoot}'.");
    }

    internal static void EnsureInstalledExecutablePayload(string sourceBundleRoot, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBundleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        Directory.CreateDirectory(installRoot);

        foreach (var fileName in RequiredExecutablePayloadFiles)
        {
            var installedPath = Path.Combine(installRoot, fileName);
            if (File.Exists(installedPath))
            {
                continue;
            }

            var sourcePath = Path.Combine(sourceBundleRoot, fileName);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            File.Copy(sourcePath, installedPath, overwrite: true);
        }

        var missingRequiredFiles = RequiredExecutablePayloadFiles
            .Where(fileName => !File.Exists(Path.Combine(installRoot, fileName)))
            .ToArray();
        if (missingRequiredFiles.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Meeting Recorder install is missing required executable files after deployment: {string.Join(", ", missingRequiredFiles.Select(fileName => $"'{fileName}'"))}.");
    }

    private static void TryMoveExistingInstallToBackup(string installRoot, string backupRoot)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Move(installRoot, backupRoot);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromSeconds(attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromSeconds(attempt));
            }
        }

        try
        {
            Directory.Move(installRoot, backupRoot);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Meeting Recorder could not update files under '{installRoot}'. Close the app if it is still running, then try again. Underlying error: {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Meeting Recorder could not update files under '{installRoot}'. A security tool or file lock may be blocking the install. Close the app if it is still running, then try again. Underlying error: {exception.Message}",
                exception);
        }
    }

    private static void CopyDirectoryContents(
        string sourcePath,
        string destinationPath,
        bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException("Destination file must have a parent directory."));
            File.Copy(file, destinationFile, overwriteExisting);
        }
    }

    private static void MergeDirectoryWithoutOverwriting(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            if (File.Exists(destinationFile))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException("Destination file must have a parent directory."));
            File.Copy(file, destinationFile, overwrite: false);
        }
    }

    private static async Task<AppConfig> EnsureInstalledConfigAsync(
        string installRoot,
        GitHubReleaseBootstrapInfo releaseInfo,
        CancellationToken cancellationToken)
    {
        var configPath = GetInstalledConfigPath();
        var store = new AppConfigStore(configPath);
        var current = await store.LoadOrCreateAsync(cancellationToken);
        var updated = current with
        {
            LaunchOnLoginEnabled = current.LaunchOnLoginEnabled,
            InstalledReleaseVersion = releaseInfo.Version,
            InstalledReleasePublishedAtUtc = releaseInfo.PublishedAtUtc,
            InstalledReleaseAssetSizeBytes = releaseInfo.AppZipAsset.SizeBytes,
            PendingUpdateZipPath = string.Empty,
            PendingUpdateVersion = string.Empty,
            PendingUpdatePublishedAtUtc = null,
            PendingUpdateAssetSizeBytes = null,
            UpdateFeedUrl = string.IsNullOrWhiteSpace(current.UpdateFeedUrl)
                ? AppBranding.DefaultUpdateFeedUrl
                : current.UpdateFeedUrl,
        };

        return await store.SaveAsync(updated, cancellationToken);
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

    private static void MoveReplaceableInstallEntriesToBackup(
        string installRoot,
        string backupRoot,
        CancellationToken cancellationToken)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsPreservedTopLevelEntry(entryPath))
            {
                continue;
            }

            var entryName = Path.GetFileName(entryPath);
            var backupPath = Path.Combine(backupRoot, entryName);
            MoveFileSystemEntry(entryPath, backupPath);
        }
    }

    private static void MoveStagedEntriesIntoInstallRoot(
        string stagingRoot,
        string installRoot,
        CancellationToken cancellationToken)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(stagingRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsPreservedTopLevelEntry(entryPath))
            {
                continue;
            }

            var entryName = Path.GetFileName(entryPath);
            var destinationPath = Path.Combine(installRoot, entryName);
            MoveFileSystemEntry(entryPath, destinationPath);
        }
    }

    private static void RestoreBackupEntries(string backupRoot, string installRoot)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(backupRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var entryName = Path.GetFileName(entryPath);
            var destinationPath = Path.Combine(installRoot, entryName);
            MoveFileSystemEntry(entryPath, destinationPath);
        }
    }

    private static void TryDeleteReplaceableInstallEntries(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return;
        }

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsPreservedTopLevelEntry(entryPath))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entryPath))
                {
                    Directory.Delete(entryPath, recursive: true);
                }
                else if (File.Exists(entryPath))
                {
                    File.Delete(entryPath);
                }
            }
            catch
            {
                // Best effort rollback cleanup only.
            }
        }
    }

    private static bool IsPreservedTopLevelEntry(string entryPath)
    {
        var entryName = Path.GetFileName(entryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(entryName, ManagedDataDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveFileSystemEntry(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        File.Move(sourcePath, destinationPath);
    }

    private static void ResetDirectoryAttributes(string rootPath)
    {
        foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        File.SetAttributes(rootPath, FileAttributes.Normal);
    }
}
