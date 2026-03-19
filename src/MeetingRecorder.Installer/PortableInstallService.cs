using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.IO;

namespace MeetingRecorder.Installer;

internal sealed class PortableInstallService
{
    private static readonly string[] PreservedDataDirectories =
    [
        "config",
        "logs",
        "audio",
        "transcripts",
        "work",
    ];

    public string GetDefaultInstallRoot()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, "MeetingRecorder");
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

        var stagingRoot = Path.Combine(installParent, "MeetingRecorder-install-" + Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(installParent, "MeetingRecorder-backup-" + Guid.NewGuid().ToString("N"));
        var isUpdate = Directory.Exists(resolvedInstallRoot);
        var movedBackup = false;
        var finalInstallMoved = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallerProgressInfo(
                "Preparing the installation",
                "Copying the app bundle into a safe staging folder.",
                82,
                isUpdate ? "Update install detected. Preserving your existing data next." : "Fresh install detected."));

            CopyDirectoryContents(sourceBundleRoot, stagingRoot, overwriteExisting: true, cancellationToken);

            if (isUpdate)
            {
                progress?.Report(new InstallerProgressInfo(
                    "Preserving your data",
                    "Keeping your config, transcripts, recordings, and downloaded models.",
                    88));

                var existingDataRoot = Path.Combine(resolvedInstallRoot, "data");
                var stagingDataRoot = Path.Combine(stagingRoot, "data");

                foreach (var preservedDirectory in PreservedDataDirectories)
                {
                    CopyDirectoryContents(
                        Path.Combine(existingDataRoot, preservedDirectory),
                        Path.Combine(stagingDataRoot, preservedDirectory),
                        overwriteExisting: true,
                        cancellationToken);
                }

                MergeDirectoryWithoutOverwriting(
                    Path.Combine(existingDataRoot, "models"),
                    Path.Combine(stagingDataRoot, "models"),
                    cancellationToken);
            }

            if (Directory.Exists(resolvedInstallRoot))
            {
                TryMoveExistingInstallToBackup(resolvedInstallRoot, backupRoot);
                movedBackup = true;
            }

            Directory.Move(stagingRoot, resolvedInstallRoot);
            finalInstallMoved = true;

            progress?.Report(new InstallerProgressInfo(
                "Applying portable settings",
                "Writing install metadata and confirming launch-on-login settings.",
                94));

            var config = await EnsureInstalledConfigAsync(
                resolvedInstallRoot,
                releaseInfo,
                cancellationToken);

            var executablePath = Path.Combine(resolvedInstallRoot, "MeetingRecorder.App.exe");

            progress?.Report(new InstallerProgressInfo(
                "Finishing up",
                "Enabling launch on login and cleaning up temporary files.",
                100));

            var launchOnLoginChanged = new AutoStartRegistrationService()
                .SyncRegistration(config.LaunchOnLoginEnabled, executablePath);

            if (movedBackup && Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }

            return executablePath;
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);

            if (movedBackup)
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

            throw;
        }
    }

    private static void TryMoveExistingInstallToBackup(string installRoot, string backupRoot)
    {
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
        var configPath = Path.Combine(installRoot, "data", "config", "appsettings.json");
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
}
