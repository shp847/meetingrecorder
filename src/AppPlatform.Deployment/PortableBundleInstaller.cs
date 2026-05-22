using AppPlatform.Abstractions;
using System.Diagnostics;
using System.Text;

namespace AppPlatform.Deployment;

public sealed class PortableBundleInstaller
{
    private const string ManagedDataDirectoryName = "data";
    private static readonly string[] RequiredExecutablePayloadFiles =
    [
        "MeetingRecorder.App.exe",
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
    private readonly WindowsShortcutService _shortcutService;
    private readonly IDeploymentLogger _logger;
    private readonly InstallProvenanceStore _provenanceStore;

    public PortableBundleInstaller()
        : this(
            new InstallPathProcessManager(),
            new WindowsShortcutService(),
            NullDeploymentLogger.Instance,
            new InstallProvenanceStore())
    {
    }

    public PortableBundleInstaller(
        InstallPathProcessManager processManager,
        WindowsShortcutService shortcutService)
        : this(processManager, shortcutService, NullDeploymentLogger.Instance, new InstallProvenanceStore())
    {
    }

    public PortableBundleInstaller(
        InstallPathProcessManager processManager,
        WindowsShortcutService shortcutService,
        IDeploymentLogger logger)
        : this(processManager, shortcutService, logger, new InstallProvenanceStore())
    {
    }

    internal PortableBundleInstaller(
        InstallPathProcessManager processManager,
        WindowsShortcutService shortcutService,
        IDeploymentLogger logger,
        InstallProvenanceStore provenanceStore)
    {
        _processManager = processManager;
        _shortcutService = shortcutService;
        _logger = logger;
        _provenanceStore = provenanceStore;
    }

    public string GetDefaultInstallRoot(AppProductManifest manifest)
    {
        return manifest.ManagedInstallLayout.InstallRoot;
    }

    public async Task<InstallResult> InstallAsync(
        AppProductManifest manifest,
        InstallRequest request,
        CancellationToken cancellationToken)
    {
        var sourceBundleRoot = ResolveSourceBundleRoot(request.BundleRoot, manifest.ExecutableName);
        var resolvedInstallRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(request.InstallRoot)
                ? GetDefaultInstallRoot(manifest)
                : request.InstallRoot);
        var isUpdate = Directory.Exists(resolvedInstallRoot);
        var preservedPayloadFiles = ResolvePreservedPayloadFiles(
            request,
            sourceBundleRoot,
            resolvedInstallRoot,
            isUpdate);
        _logger.Info($"Validating bundle integrity for '{sourceBundleRoot}'.");
        BundleIntegrityValidator.ValidateBundle(sourceBundleRoot, preservedPayloadFiles);
        _logger.Info($"Installing bundle from '{sourceBundleRoot}' to '{resolvedInstallRoot}'.");
        var installParent = Directory.GetParent(resolvedInstallRoot)?.FullName
            ?? throw new InvalidOperationException("Install root must have a parent directory.");

        Directory.CreateDirectory(installParent);

        var backupRoot = CreateWorkspacePath(installParent, "APB");
        var stagingRoot = ResolveStagingRoot(
            sourceBundleRoot,
            installParent,
            preserveSourceBundleRoot: isUpdate);
        var stagingUsesSourceBundle = string.Equals(stagingRoot, sourceBundleRoot, StringComparison.OrdinalIgnoreCase);
        var movedBackup = false;
        var finalInstallMoved = false;
        var desktopShortcutCreated = false;
        var startMenuShortcutCreated = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stagingUsesSourceBundle)
            {
                _logger.Info($"Using already-extracted bundle as staging root '{stagingRoot}'.");
            }
            else
            {
                _logger.Info($"Copying bundle into staging root '{stagingRoot}'.");
                CopyDirectoryContents(
                    sourceBundleRoot,
                    stagingRoot,
                    overwriteExisting: true,
                    skippedRelativePaths: preservedPayloadFiles,
                    cancellationToken);
            }

            PrepareBundleForManagedInstall(stagingRoot);
            _logger.Info("Prepared staged bundle for managed install.");
            _logger.Info($"Validating staged bundle integrity under '{stagingRoot}'.");
            BundleIntegrityValidator.ValidateBundle(stagingRoot, preservedPayloadFiles);

            await _processManager.EnsureInstallPathReleasedAsync(resolvedInstallRoot, cancellationToken);

            if (isUpdate)
            {
                _logger.Info("Existing installation detected; promoting app files in place while preserving the current data directory.");
                ApplyStagedBundleToExistingInstall(
                    stagingRoot,
                    resolvedInstallRoot,
                    backupRoot,
                    preservedPayloadFiles,
                    cancellationToken);
                movedBackup = Directory.Exists(backupRoot) && Directory.EnumerateFileSystemEntries(backupRoot).Any();
                finalInstallMoved = true;
            }
            else
            {
                if (Directory.Exists(resolvedInstallRoot))
                {
                    _logger.Info($"Moving existing install '{resolvedInstallRoot}' to backup '{backupRoot}'.");
                    TryMoveExistingInstallToBackup(resolvedInstallRoot, backupRoot);
                    movedBackup = true;
                }

                _logger.Info($"Promoting staged bundle '{stagingRoot}' into '{resolvedInstallRoot}'.");
                Directory.Move(stagingRoot, resolvedInstallRoot);
                finalInstallMoved = true;
            }

            var executablePath = Path.Combine(resolvedInstallRoot, manifest.ExecutableName);
            var launcherPath = ResolveShortcutTargetPath(manifest, resolvedInstallRoot);
            var iconPath = Path.Combine(resolvedInstallRoot, "MeetingRecorder.ico");
            var taskbarTargetPath = File.Exists(executablePath)
                ? executablePath
                : launcherPath;
            var repairedPinnedTaskbarShortcuts = _shortcutService.RepairPinnedTaskbarShortcuts(
                manifest.ShortcutPolicy,
                taskbarTargetPath,
                resolvedInstallRoot,
                iconPath);
            foreach (var repairedPinnedTaskbarShortcut in repairedPinnedTaskbarShortcuts)
            {
                _logger.Info($"Repaired pinned taskbar shortcut '{repairedPinnedTaskbarShortcut}'.");
            }

            if (isUpdate && (request.CreateDesktopShortcut || request.CreateStartMenuShortcut))
            {
                var shortcutRepairResult = _shortcutService.RepairExistingShortcuts(
                    manifest.ShortcutPolicy,
                    launcherPath,
                    resolvedInstallRoot,
                    iconPath);
                foreach (var removedLegacyShortcutPath in shortcutRepairResult.RemovedLegacyPaths)
                {
                    _logger.Info($"Removed legacy shortcut artifact '{removedLegacyShortcutPath}'.");
                }

                foreach (var repairedShortcutPath in shortcutRepairResult.RepairedShortcutPaths)
                {
                    _logger.Info($"Repaired existing shortcut '{repairedShortcutPath}'.");
                }
            }
            else if (isUpdate)
            {
                _logger.Info("Skipping shortcut repair because this update did not request shell shortcut changes.");
            }

            if (request.CreateDesktopShortcut || request.CreateStartMenuShortcut)
            {
                var removedLegacyShortcutPaths = _shortcutService.RemoveLegacyShortcuts(
                    manifest.ShortcutPolicy,
                    removeDesktopShortcut: request.CreateDesktopShortcut,
                    removeStartMenuShortcut: request.CreateStartMenuShortcut);
                foreach (var removedLegacyShortcutPath in removedLegacyShortcutPaths)
                {
                    _logger.Info($"Removed legacy shortcut artifact '{removedLegacyShortcutPath}'.");
                }
            }

            if (request.CreateDesktopShortcut)
            {
                var desktopPath = _shortcutService.GetDesktopShortcutPath(manifest.ShortcutPolicy);
                _logger.Info($"Creating desktop shortcut at '{desktopPath}'.");
                desktopShortcutCreated = _shortcutService.TryCreateShortcut(
                    desktopPath,
                    launcherPath,
                    resolvedInstallRoot,
                    iconPath).Success;
                _logger.Info(desktopShortcutCreated
                    ? "Desktop shortcut created."
                    : "Desktop shortcut creation failed.");
            }

            if (request.CreateStartMenuShortcut)
            {
                var startMenuPath = _shortcutService.GetStartMenuShortcutPath(manifest.ShortcutPolicy);
                _logger.Info($"Creating Start Menu shortcut at '{startMenuPath}'.");
                startMenuShortcutCreated = _shortcutService.TryCreateShortcut(
                    startMenuPath,
                    launcherPath,
                    resolvedInstallRoot,
                    iconPath).Success;
                _logger.Info(startMenuShortcutCreated
                    ? "Start Menu shortcut created."
                    : "Start Menu shortcut creation failed.");
            }

            var quarantinedLegacyInstallRoots = QuarantineLegacyInstallRoots(
                manifest.ManagedInstallLayout.LegacyInstallRoots,
                resolvedInstallRoot);
            foreach (var quarantinedLegacyInstallRoot in quarantinedLegacyInstallRoots)
            {
                _logger.Info($"Quarantined legacy install root to '{quarantinedLegacyInstallRoot}'.");
            }

            EnsureInstalledExecutablePayload(sourceBundleRoot, resolvedInstallRoot, preservedPayloadFiles);
            _logger.Info($"Validating installed bundle integrity under '{resolvedInstallRoot}'.");
            try
            {
                BundleIntegrityValidator.ValidateBundle(resolvedInstallRoot, preservedPayloadFiles);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"The installed bundle under '{resolvedInstallRoot}' is incomplete after promotion. A security tool may have quarantined one or more executable files. Underlying error: {exception.Message}",
                    exception);
            }

            PersistInstallProvenance(manifest, request, isUpdate);
            _logger.Info($"Persisted install provenance at '{_provenanceStore.GetPath(manifest.ManagedInstallLayout.DataRoot)}'.");

            if (request.LaunchAfterInstall)
            {
                _logger.Info($"Launching installed app from '{launcherPath}'.");
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    WorkingDirectory = resolvedInstallRoot,
                    UseShellExecute = true,
                });
            }

            TryDeleteDirectory(stagingRoot);
            CleanupBackupDirectory(backupRoot);
            _logger.Info("Cleaned up installer backup directory.");

            return new InstallResult(
                InstallRoot: resolvedInstallRoot,
                ExecutablePath: executablePath,
                ReleaseVersion: request.ReleaseVersion,
                DesktopShortcutCreated: desktopShortcutCreated,
                StartMenuShortcutCreated: startMenuShortcutCreated);
        }
        catch
        {
            _logger.Error("Bundle install failed. Attempting rollback.");
            TryDeleteDirectory(stagingRoot);

            if (movedBackup)
            {
                if (isUpdate)
                {
                    TryDeleteReplaceableInstallEntries(resolvedInstallRoot, preservedPayloadFiles);
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

            _logger.Info("Rollback completed.");
            throw;
        }
    }

    public static string ResolveSourceBundleRoot(string bundleRoot, string executableName)
    {
        var resolvedBundleRoot = Path.GetFullPath(bundleRoot);
        var nestedBundleRoot = Path.Combine(resolvedBundleRoot, "MeetingRecorder");
        if (File.Exists(Path.Combine(nestedBundleRoot, BundleIntegrityValidator.ManifestFileName)))
        {
            return nestedBundleRoot;
        }

        if (File.Exists(Path.Combine(resolvedBundleRoot, BundleIntegrityValidator.ManifestFileName)))
        {
            return resolvedBundleRoot;
        }

        if (File.Exists(Path.Combine(nestedBundleRoot, executableName)))
        {
            return nestedBundleRoot;
        }

        if (File.Exists(Path.Combine(resolvedBundleRoot, executableName)))
        {
            return resolvedBundleRoot;
        }

        throw new InvalidOperationException("The portable application bundle could not be found.");
    }

    private static IReadOnlySet<string> ResolvePreservedPayloadFiles(
        InstallRequest request,
        string sourceBundleRoot,
        string installRoot,
        bool isUpdate)
    {
        if (!isUpdate)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var requestedPreservedPayloadFiles = (request.PreservedPayloadFiles ?? [])
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToArray();
        if (requestedPreservedPayloadFiles.Length > 0)
        {
            EnsureStableAppHostLayoutsSupportAutoUpdate(
                sourceBundleRoot,
                installRoot,
                requestedPreservedPayloadFiles);
            return new HashSet<string>(
                requestedPreservedPayloadFiles,
                StringComparer.OrdinalIgnoreCase);
        }

        if (request.Channel != InstallChannel.AutoUpdate)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var stableExecutableFiles = EnsureStableAppHostLayoutsSupportAutoUpdate(
                sourceBundleRoot,
                installRoot,
                requestedPreservedPayloadFiles: null)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new HashSet<string>(
            stableExecutableFiles,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string[] EnsureStableAppHostLayoutsSupportAutoUpdate(
        string sourceBundleRoot,
        string installRoot,
        IReadOnlyList<string>? requestedPreservedPayloadFiles)
    {
        var sourceLayout = BundleLayoutInfoStore.TryLoad(sourceBundleRoot);
        var installedLayout = BundleLayoutInfoStore.TryLoad(installRoot);
        if (sourceLayout?.SupportsStableAppHostUpdates != true ||
            installedLayout?.SupportsStableAppHostUpdates != true)
        {
            throw CreateInstallerResetRequiredException();
        }

        var stableExecutableFiles = requestedPreservedPayloadFiles is { Count: > 0 }
            ? requestedPreservedPayloadFiles.ToArray()
            : sourceLayout.StableExecutableFiles;
        if (stableExecutableFiles.Length == 0 ||
            stableExecutableFiles.Any(fileName => !File.Exists(Path.Combine(installRoot, fileName))))
        {
            throw CreateInstallerResetRequiredException();
        }

        return stableExecutableFiles;
    }

    private static InvalidOperationException CreateInstallerResetRequiredException()
    {
        return new InvalidOperationException(
            "This installation uses the legacy single-file update layout or is missing stable apphost executables. Install the latest Meeting Recorder MSI or bootstrapper once to complete a one-time installer reset before using in-app updates again.");
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

        return Path.Combine(installParent, prefix + "-" + Guid.NewGuid().ToString("N")[..12]);
    }

    internal static string CreateStagingWorkspacePath(string installParent)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolvedInstallParent = Path.GetFullPath(installParent);
        var tempDrive = Path.GetPathRoot(tempRoot);
        var installDrive = Path.GetPathRoot(resolvedInstallParent);
        var stagingParent = string.Equals(tempDrive, installDrive, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(tempRoot, "MeetingRecorderInstaller")
            : resolvedInstallParent;

        Directory.CreateDirectory(stagingParent);
        return CreateWorkspacePath(stagingParent, "MeetingRecorderUpdate");
    }

    internal static string ResolveStagingRoot(string sourceBundleRoot, string installParent)
    {
        return ResolveStagingRoot(
            sourceBundleRoot,
            installParent,
            preserveSourceBundleRoot: false);
    }

    private static string ResolveStagingRoot(
        string sourceBundleRoot,
        string installParent,
        bool preserveSourceBundleRoot)
    {
        var resolvedSourceBundleRoot = Path.GetFullPath(sourceBundleRoot);
        var resolvedInstallParent = Path.GetFullPath(installParent);
        if (preserveSourceBundleRoot)
        {
            return CreateStagingWorkspacePath(resolvedInstallParent);
        }

        var sourceDrive = Path.GetPathRoot(resolvedSourceBundleRoot);
        var installDrive = Path.GetPathRoot(resolvedInstallParent);
        if (string.Equals(sourceDrive, installDrive, StringComparison.OrdinalIgnoreCase) &&
            IsPathUnderDirectory(resolvedSourceBundleRoot, Path.GetFullPath(Path.GetTempPath())))
        {
            return resolvedSourceBundleRoot;
        }

        return CreateStagingWorkspacePath(resolvedInstallParent);
    }

    private static bool IsPathUnderDirectory(string candidatePath, string parentDirectory)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedParent = Path.GetFullPath(parentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    internal static void EnsureInstalledExecutablePayload(
        string sourceBundleRoot,
        string installRoot)
    {
        EnsureInstalledExecutablePayload(
            sourceBundleRoot,
            installRoot,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    internal static void EnsureInstalledExecutablePayload(
        string sourceBundleRoot,
        string installRoot,
        IReadOnlySet<string> preservedPayloadFiles)
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

            if (preservedPayloadFiles.Contains(fileName))
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
            $"The installed bundle is missing required executable files after deployment: {string.Join(", ", missingRequiredFiles.Select(fileName => $"'{fileName}'"))}.");
    }

    internal static IReadOnlyList<string> QuarantineLegacyInstallRoots(
        IReadOnlyList<string> legacyInstallRoots,
        string currentInstallRoot)
    {
        var quarantinedRoots = new List<string>();
        var normalizedCurrentInstallRoot = Path.GetFullPath(currentInstallRoot);

        foreach (var legacyInstallRoot in legacyInstallRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(legacyInstallRoot, normalizedCurrentInstallRoot, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(legacyInstallRoot))
            {
                continue;
            }

            var quarantineRoot = CreateLegacyInstallBackupPath(legacyInstallRoot);
            TryMoveExistingInstallToBackup(legacyInstallRoot, quarantineRoot);
            quarantinedRoots.Add(quarantineRoot);
        }

        return quarantinedRoots;
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
            // Best effort cleanup only.
        }
    }

    private static string ResolveShortcutTargetPath(AppProductManifest manifest, string installRoot)
    {
        var launcherPath = Path.Combine(installRoot, manifest.PortableLauncherFileName);
        if (File.Exists(launcherPath))
        {
            return launcherPath;
        }

        return Path.Combine(installRoot, manifest.ExecutableName);
    }

    private static string CreateLegacyInstallBackupPath(string legacyInstallRoot)
    {
        var parentDirectory = Path.GetDirectoryName(legacyInstallRoot)
            ?? throw new InvalidOperationException("Legacy install root must have a parent directory.");
        var directoryName = Path.GetFileName(legacyInstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(parentDirectory, $"{directoryName}.legacy-{timestamp}");
    }

    private static void ApplyStagedBundleToExistingInstall(
        string stagingRoot,
        string installRoot,
        string backupRoot,
        IReadOnlySet<string> preservedPayloadFiles,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(backupRoot);

        MoveReplaceableInstallEntriesToBackup(installRoot, backupRoot, preservedPayloadFiles, cancellationToken);
        MoveStagedEntriesIntoInstallRoot(stagingRoot, installRoot, preservedPayloadFiles, cancellationToken);
        MergeDirectoryWithoutOverwriting(
            Path.Combine(stagingRoot, ManagedDataDirectoryName),
            Path.Combine(installRoot, ManagedDataDirectoryName),
            cancellationToken);
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
                $"Could not update files under '{installRoot}'. Close the app if it is still running, then try again. Underlying error: {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Could not update files under '{installRoot}'. A security tool or file lock may be blocking the install. Underlying error: {exception.Message}",
                exception);
        }
    }

    private static void CopyDirectoryContents(
        string sourcePath,
        string destinationPath,
        bool overwriteExisting,
        IReadOnlySet<string> skippedRelativePaths,
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
            if (skippedRelativePaths.Contains(relativePath))
            {
                continue;
            }

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

    private static void MoveReplaceableInstallEntriesToBackup(
        string installRoot,
        string backupRoot,
        IReadOnlySet<string> preservedPayloadFiles,
        CancellationToken cancellationToken)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(installRoot, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsPreservedTopLevelEntry(entryPath, preservedPayloadFiles))
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
        IReadOnlySet<string> preservedPayloadFiles,
        CancellationToken cancellationToken)
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(stagingRoot, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsPreservedTopLevelEntry(entryPath, preservedPayloadFiles))
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

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(backupRoot, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            var entryName = Path.GetFileName(entryPath);
            var destinationPath = Path.Combine(installRoot, entryName);
            MoveFileSystemEntry(entryPath, destinationPath);
        }
    }

    private static void TryDeleteReplaceableInstallEntries(
        string installRoot,
        IReadOnlySet<string>? preservedPayloadFiles = null)
    {
        if (!Directory.Exists(installRoot))
        {
            return;
        }

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsPreservedTopLevelEntry(entryPath, preservedPayloadFiles))
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

    private static bool IsPreservedTopLevelEntry(
        string entryPath,
        IReadOnlySet<string>? preservedPayloadFiles = null)
    {
        var entryName = Path.GetFileName(entryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(entryName, ManagedDataDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            preservedPayloadFiles?.Contains(entryName) == true;
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

    private void PersistInstallProvenance(
        AppProductManifest manifest,
        InstallRequest request,
        bool isUpdate)
    {
        var existingProvenance = _provenanceStore.TryLoad(manifest.ManagedInstallLayout.DataRoot);
        var installedAtUtc = DateTimeOffset.UtcNow;
        var resolvedChannel = request.Channel is InstallChannel.Unknown
            ? InstallChannel.DirectCli
            : request.Channel;
        var resolvedVersion = string.IsNullOrWhiteSpace(request.ReleaseVersion)
            ? string.Empty
            : request.ReleaseVersion;

        var updatedProvenance = existingProvenance is null
            ? CreateInitialInstallProvenance(
                isUpdate,
                resolvedChannel,
                resolvedVersion,
                installedAtUtc,
                request.ReleasePublishedAtUtc,
                request.ReleaseAssetSizeBytes)
            : existingProvenance with
            {
                LastUpdateChannel = resolvedChannel,
                LastInstalledVersion = string.IsNullOrWhiteSpace(resolvedVersion)
                    ? existingProvenance.LastInstalledVersion
                    : resolvedVersion,
                LastInstalledAtUtc = installedAtUtc,
                LastReleasePublishedAtUtc = request.ReleasePublishedAtUtc ?? existingProvenance.LastReleasePublishedAtUtc,
                LastReleaseAssetSizeBytes = request.ReleaseAssetSizeBytes is > 0
                    ? request.ReleaseAssetSizeBytes
                    : existingProvenance.LastReleaseAssetSizeBytes,
            };

        _provenanceStore.Save(manifest.ManagedInstallLayout.DataRoot, updatedProvenance);
    }

    private static InstallProvenance CreateInitialInstallProvenance(
        bool isUpdate,
        InstallChannel channel,
        string releaseVersion,
        DateTimeOffset installedAtUtc,
        DateTimeOffset? releasePublishedAtUtc,
        long? releaseAssetSizeBytes)
    {
        if (isUpdate)
        {
            return new InstallProvenance(
                InitialChannel: InstallChannel.Unknown,
                LastUpdateChannel: channel,
                InitialVersion: string.Empty,
                LastInstalledVersion: releaseVersion,
                LastInstalledAtUtc: installedAtUtc,
                LastReleasePublishedAtUtc: releasePublishedAtUtc,
                LastReleaseAssetSizeBytes: releaseAssetSizeBytes);
        }

        return new InstallProvenance(
            InitialChannel: channel,
            LastUpdateChannel: channel,
            InitialVersion: releaseVersion,
            LastInstalledVersion: releaseVersion,
            LastInstalledAtUtc: installedAtUtc,
            LastReleasePublishedAtUtc: releasePublishedAtUtc,
            LastReleaseAssetSizeBytes: releaseAssetSizeBytes);
    }
}
