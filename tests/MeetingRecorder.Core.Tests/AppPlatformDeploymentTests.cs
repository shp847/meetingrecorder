using AppPlatform.Deployment;
using System.Reflection;
using AppPlatform.Abstractions;

namespace MeetingRecorder.Core.Tests;

public sealed class AppPlatformDeploymentTests
{
    [Fact]
    public void PlatformShortcutService_Writes_Lnk_Shortcut_File()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var shortcutPath = Path.Combine(root, "Meeting Recorder.lnk");
            var targetPath = Path.Combine(root, "Run-MeetingRecorder.cmd");
            var workingDirectory = Path.Combine(root, "app");
            var iconPath = Path.Combine(root, "MeetingRecorder.ico");

            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(targetPath, "@echo off");
            File.WriteAllText(iconPath, "icon");

            var service = new WindowsShortcutService();

            var result = service.TryCreateShortcut(shortcutPath, targetPath, workingDirectory, iconPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Null(result.ErrorMessage);
            Assert.True(File.Exists(shortcutPath));
            Assert.EndsWith(".lnk", shortcutPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformInstallPathManager_Retries_Until_The_Primary_Instance_Releases()
    {
        var targetRoot = @"C:\Users\Test\Documents\MeetingRecorder";
        var processController = new FakeInstallPathProcessController(
            signalResult: true,
            waitResults: [false, false, true]);
        var manager = new InstallPathProcessManager(processController);

        await manager.EnsureInstallPathReleasedAsync(targetRoot, CancellationToken.None);

        Assert.True(processController.SignalRequested);
        Assert.Equal(3, processController.SignalCallCount);
        Assert.Equal(3, processController.WaitCallCount);
    }

    [Fact]
    public void PlatformInstallPathController_Avoids_Process_Inspection_And_Control_APIs()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var controllerPath = Path.Combine(repoRoot, "src", "AppPlatform.Deployment", "InstallPathProcessManager.cs");

        Assert.True(File.Exists(controllerPath), $"Expected deployment controller source at '{controllerPath}'.");

        var source = File.ReadAllText(controllerPath);

        Assert.DoesNotContain("MainModule", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.GetProcesses", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindowHandle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseMainWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformShortcutService_Removes_Legacy_Lnk_Artifacts_And_Nested_StartMenu_Folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var desktopRoot = Path.Combine(root, "Desktop");
        var programsRoot = Path.Combine(root, "Programs");
        var legacyFolder = Path.Combine(programsRoot, "Meeting Recorder");
        Directory.CreateDirectory(desktopRoot);
        Directory.CreateDirectory(programsRoot);
        Directory.CreateDirectory(legacyFolder);

        try
        {
            var desktopLegacyShortcut = Path.Combine(desktopRoot, "Meeting Recorder.lnk");
            var desktopLegacyCmdShortcut = Path.Combine(desktopRoot, "Meeting Recorder.cmd");
            var startMenuLegacyShortcut = Path.Combine(programsRoot, "Meeting Recorder.lnk");
            var startMenuLegacyCmdShortcut = Path.Combine(programsRoot, "Meeting Recorder.cmd");
            var nestedLegacyShortcut = Path.Combine(legacyFolder, "Meeting Recorder.lnk");
            File.WriteAllText(desktopLegacyShortcut, "desktop");
            File.WriteAllText(desktopLegacyCmdShortcut, "desktop-cmd");
            File.WriteAllText(startMenuLegacyShortcut, "start-menu");
            File.WriteAllText(startMenuLegacyCmdShortcut, "start-menu-cmd");
            File.WriteAllText(nestedLegacyShortcut, "nested");

            var service = new WindowsShortcutService();

            var removedPaths = service.RemoveLegacyShortcuts(
                new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"),
                desktopRoot,
                programsRoot);

            Assert.Contains(desktopLegacyShortcut, removedPaths);
            Assert.Contains(desktopLegacyCmdShortcut, removedPaths);
            Assert.Contains(startMenuLegacyShortcut, removedPaths);
            Assert.Contains(startMenuLegacyCmdShortcut, removedPaths);
            Assert.Contains(legacyFolder, removedPaths);
            Assert.False(File.Exists(desktopLegacyShortcut));
            Assert.False(File.Exists(desktopLegacyCmdShortcut));
            Assert.False(File.Exists(startMenuLegacyShortcut));
            Assert.False(File.Exists(startMenuLegacyCmdShortcut));
            Assert.False(Directory.Exists(legacyFolder));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public void PlatformShortcutService_Repairs_Existing_Shortcuts_When_Legacy_Artifacts_Are_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var desktopRoot = Path.Combine(root, "Desktop");
        var programsRoot = Path.Combine(root, "Programs");
        var legacyFolder = Path.Combine(programsRoot, "Meeting Recorder");
        var installRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        Directory.CreateDirectory(desktopRoot);
        Directory.CreateDirectory(programsRoot);
        Directory.CreateDirectory(legacyFolder);
        Directory.CreateDirectory(installRoot);

        try
        {
            var desktopShortcutPath = Path.Combine(desktopRoot, "Meeting Recorder.lnk");
            var legacyNestedShortcutPath = Path.Combine(legacyFolder, "Meeting Recorder.lnk");
            var launcherPath = Path.Combine(installRoot, "Run-MeetingRecorder.cmd");
            var iconPath = Path.Combine(installRoot, "MeetingRecorder.ico");
            File.WriteAllText(desktopShortcutPath, "stale-desktop");
            File.WriteAllText(legacyNestedShortcutPath, "stale-nested");
            File.WriteAllText(launcherPath, "@echo off");
            File.WriteAllText(iconPath, "icon");

            var service = new WindowsShortcutService();

            var result = service.RepairExistingShortcuts(
                new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"),
                launcherPath,
                installRoot,
                iconPath,
                desktopRoot,
                programsRoot);

            Assert.Contains(desktopShortcutPath, result.RepairedShortcutPaths);
            Assert.Contains(Path.Combine(programsRoot, "Meeting Recorder.lnk"), result.RepairedShortcutPaths);
            Assert.Contains(legacyFolder, result.RemovedLegacyPaths);
            Assert.True(File.Exists(desktopShortcutPath));
            Assert.True(File.Exists(Path.Combine(programsRoot, "Meeting Recorder.lnk")));
            Assert.False(Directory.Exists(legacyFolder));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformPortableBundleInstaller_Quarantines_Legacy_Install_Roots_When_Deploying_To_Canonical_Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(root, "bundle");
        var canonicalInstallRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        var dataRoot = Path.Combine(root, "LocalAppData", "MeetingRecorder");
        var legacyInstallRoot = Path.Combine(root, "LocalAppData", "Programs", "Meeting Recorder");
        Directory.CreateDirectory(bundleRoot);
        Directory.CreateDirectory(legacyInstallRoot);
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.App.exe"), "app-exe");
        File.WriteAllText(Path.Combine(bundleRoot, "Run-MeetingRecorder.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.product.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "AppPlatform.Deployment.Cli.exe"), "cli");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.exe"), "worker");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.dll"), "worker-dll");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.deps.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.Core.dll"), "core");
        File.WriteAllText(
            Path.Combine(bundleRoot, "bundle-integrity.json"),
            """
            {
              "formatVersion": 1,
              "requiredFiles": [
                { "relativePath": "MeetingRecorder.App.exe", "lengthBytes": 7, "sha256": "83656a19d94db9fdacd5feab3e847869ff93d70bf5989f5212e6a719a24c4e25" },
                { "relativePath": "AppPlatform.Deployment.Cli.exe", "lengthBytes": 3, "sha256": "99bb88401742848e032fd6f51709415fb6be169a72d2e5d7fc44289255160d3c" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.exe", "lengthBytes": 6, "sha256": "87eba76e7f3164534045ba922e7770fb58bbd14ad732bbf5ba6f11cc56989e6e" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.dll", "lengthBytes": 10, "sha256": "4c58c2d87ddefa51ea622fa9db6a15d03f664fb3b0d9bc6c44aca741144d4aeb" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.deps.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.runtimeconfig.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.Core.dll", "lengthBytes": 4, "sha256": "0d45f5fd462b8c70bffb10021ac1bcff3f58f29b1faf7568595095427d42812c" },
                { "relativePath": "MeetingRecorder.product.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "Run-MeetingRecorder.cmd", "lengthBytes": 9, "sha256": "abb30b0a70e39de39ce0790c6c157fd04bcfb998705ec1672fe8070ff2d34573" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(legacyInstallRoot, "legacy.txt"), "legacy-install");

        try
        {
            var installer = new PortableBundleInstaller(
                new InstallPathProcessManager(new FakeInstallPathProcessController(signalResult: false)),
                new WindowsShortcutService(),
                NullDeploymentLogger.Instance);
            var manifest = new AppProductManifest(
                ProductId: "meeting-recorder",
                ProductName: "Meeting Recorder",
                DisplayName: "Meeting Recorder",
                ExecutableName: "MeetingRecorder.App.exe",
                PortableLauncherFileName: "Run-MeetingRecorder.cmd",
                InstallerExecutableName: "MeetingRecorderInstaller.exe",
                InstallerMsiName: "MeetingRecorderInstaller.msi",
                PortableArchivePrefix: "MeetingRecorder",
                UpdateFeedUrl: "https://example.com/releases/latest",
                ReleasePageUrl: "https://example.com/releases",
                GitHubRepositoryOwner: "example",
                GitHubRepositoryName: "meeting-recorder",
                ManagedInstallLayout: new ManagedInstallLayout(
                    InstallRoot: canonicalInstallRoot,
                    DataRoot: dataRoot,
                    ConfigPath: Path.Combine(dataRoot, "config", "appsettings.json"),
                    PreservedDataDirectories: ["config", "logs"],
                    MergeWithoutOverwriteDirectories: ["models"],
                    LegacyInstallRoots: [legacyInstallRoot]),
                ReleaseChannelPolicy: new AppReleaseChannelPolicy(true, true, true, true, true),
                ShortcutPolicy: new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"));

            await installer.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: bundleRoot,
                    InstallRoot: canonicalInstallRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: false,
                    Channel: InstallChannel.CommandBootstrap,
                    ReleaseVersion: null,
                    ReleasePublishedAtUtc: null,
                    ReleaseAssetSizeBytes: null),
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(canonicalInstallRoot, "MeetingRecorder.App.exe")));
            Assert.Equal("app-exe", File.ReadAllText(Path.Combine(canonicalInstallRoot, "MeetingRecorder.App.exe")));
            Assert.False(Directory.Exists(legacyInstallRoot));

            var quarantinedLegacyRoots = Directory.GetDirectories(
                Path.GetDirectoryName(legacyInstallRoot)!,
                "Meeting Recorder.legacy-*",
                SearchOption.TopDirectoryOnly);

            Assert.Single(quarantinedLegacyRoots);
            Assert.True(File.Exists(Path.Combine(quarantinedLegacyRoots[0], "legacy.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformPortableBundleInstaller_Quarantines_The_Legacy_Documents_Spaced_Install_Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(root, "bundle");
        var canonicalInstallRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        var spacedLegacyInstallRoot = Path.Combine(root, "Documents", "Meeting Recorder");
        var dataRoot = Path.Combine(root, "LocalAppData", "MeetingRecorder");
        Directory.CreateDirectory(bundleRoot);
        Directory.CreateDirectory(spacedLegacyInstallRoot);
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.App.exe"), "app-exe");
        File.WriteAllText(Path.Combine(bundleRoot, "Run-MeetingRecorder.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.product.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "AppPlatform.Deployment.Cli.exe"), "cli");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.exe"), "worker");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.dll"), "worker-dll");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.deps.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.Core.dll"), "core");
        File.WriteAllText(
            Path.Combine(bundleRoot, "bundle-integrity.json"),
            """
            {
              "formatVersion": 1,
              "requiredFiles": [
                { "relativePath": "MeetingRecorder.App.exe", "lengthBytes": 7, "sha256": "83656a19d94db9fdacd5feab3e847869ff93d70bf5989f5212e6a719a24c4e25" },
                { "relativePath": "AppPlatform.Deployment.Cli.exe", "lengthBytes": 3, "sha256": "99bb88401742848e032fd6f51709415fb6be169a72d2e5d7fc44289255160d3c" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.exe", "lengthBytes": 6, "sha256": "87eba76e7f3164534045ba922e7770fb58bbd14ad732bbf5ba6f11cc56989e6e" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.dll", "lengthBytes": 10, "sha256": "4c58c2d87ddefa51ea622fa9db6a15d03f664fb3b0d9bc6c44aca741144d4aeb" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.deps.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.runtimeconfig.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.Core.dll", "lengthBytes": 4, "sha256": "0d45f5fd462b8c70bffb10021ac1bcff3f58f29b1faf7568595095427d42812c" },
                { "relativePath": "MeetingRecorder.product.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "Run-MeetingRecorder.cmd", "lengthBytes": 9, "sha256": "abb30b0a70e39de39ce0790c6c157fd04bcfb998705ec1672fe8070ff2d34573" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(spacedLegacyInstallRoot, "legacy.txt"), "legacy-install");

        try
        {
            var installer = new PortableBundleInstaller(
                new InstallPathProcessManager(new FakeInstallPathProcessController(signalResult: false)),
                new WindowsShortcutService(),
                NullDeploymentLogger.Instance);
            var manifest = new AppProductManifest(
                ProductId: "meeting-recorder",
                ProductName: "Meeting Recorder",
                DisplayName: "Meeting Recorder",
                ExecutableName: "MeetingRecorder.App.exe",
                PortableLauncherFileName: "Run-MeetingRecorder.cmd",
                InstallerExecutableName: "MeetingRecorderInstaller.exe",
                InstallerMsiName: "MeetingRecorderInstaller.msi",
                PortableArchivePrefix: "MeetingRecorder",
                UpdateFeedUrl: "https://example.com/releases/latest",
                ReleasePageUrl: "https://example.com/releases",
                GitHubRepositoryOwner: "example",
                GitHubRepositoryName: "meeting-recorder",
                ManagedInstallLayout: new ManagedInstallLayout(
                    InstallRoot: canonicalInstallRoot,
                    DataRoot: dataRoot,
                    ConfigPath: Path.Combine(dataRoot, "config", "appsettings.json"),
                    PreservedDataDirectories: ["config", "logs"],
                    MergeWithoutOverwriteDirectories: ["models"],
                    LegacyInstallRoots: [spacedLegacyInstallRoot]),
                ReleaseChannelPolicy: new AppReleaseChannelPolicy(true, true, true, true, true),
                ShortcutPolicy: new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"));

            await installer.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: bundleRoot,
                    InstallRoot: canonicalInstallRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: false,
                    Channel: InstallChannel.CommandBootstrap,
                    ReleaseVersion: null,
                    ReleasePublishedAtUtc: null,
                    ReleaseAssetSizeBytes: null),
                CancellationToken.None);

            Assert.False(Directory.Exists(spacedLegacyInstallRoot));

            var quarantinedLegacyRoots = Directory.GetDirectories(
                Path.GetDirectoryName(spacedLegacyInstallRoot)!,
                "Meeting Recorder.legacy-*",
                SearchOption.TopDirectoryOnly);

            Assert.Single(quarantinedLegacyRoots);
            Assert.True(File.Exists(Path.Combine(quarantinedLegacyRoots[0], "legacy.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformPortableBundleInstaller_Rejects_Bundles_Without_An_Integrity_Manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(root, "bundle");
        var installRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        var dataRoot = Path.Combine(root, "LocalAppData", "MeetingRecorder");
        Directory.CreateDirectory(bundleRoot);
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.App.exe"), "app-exe");

        try
        {
            var installer = new PortableBundleInstaller(
                new InstallPathProcessManager(new FakeInstallPathProcessController(signalResult: false)),
                new WindowsShortcutService(),
                NullDeploymentLogger.Instance);
            var manifest = new AppProductManifest(
                ProductId: "meeting-recorder",
                ProductName: "Meeting Recorder",
                DisplayName: "Meeting Recorder",
                ExecutableName: "MeetingRecorder.App.exe",
                PortableLauncherFileName: "Run-MeetingRecorder.cmd",
                InstallerExecutableName: "MeetingRecorderInstaller.exe",
                InstallerMsiName: "MeetingRecorderInstaller.msi",
                PortableArchivePrefix: "MeetingRecorder",
                UpdateFeedUrl: "https://example.com/releases/latest",
                ReleasePageUrl: "https://example.com/releases",
                GitHubRepositoryOwner: "example",
                GitHubRepositoryName: "meeting-recorder",
                ManagedInstallLayout: new ManagedInstallLayout(
                    InstallRoot: installRoot,
                    DataRoot: dataRoot,
                    ConfigPath: Path.Combine(dataRoot, "config", "appsettings.json"),
                    PreservedDataDirectories: ["config", "logs"],
                    MergeWithoutOverwriteDirectories: ["models"],
                    LegacyInstallRoots: []),
                ReleaseChannelPolicy: new AppReleaseChannelPolicy(true, true, true, true, true),
                ShortcutPolicy: new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: bundleRoot,
                    InstallRoot: installRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: false,
                    Channel: InstallChannel.CommandBootstrap,
                    ReleaseVersion: "0.3",
                    ReleasePublishedAtUtc: null,
                    ReleaseAssetSizeBytes: null),
                CancellationToken.None));

            Assert.Contains("bundle-integrity.json", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformPortableBundleInstaller_Writes_Install_Provenance_For_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(root, "bundle");
        var installRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        var dataRoot = Path.Combine(root, "LocalAppData", "MeetingRecorder");
        Directory.CreateDirectory(bundleRoot);
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.App.exe"), "app-exe");
        File.WriteAllText(Path.Combine(bundleRoot, "AppPlatform.Deployment.Cli.exe"), "cli");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.exe"), "worker");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.dll"), "worker-dll");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.deps.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.Core.dll"), "core");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.product.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "Run-MeetingRecorder.cmd"), "@echo off");
        File.WriteAllText(
            Path.Combine(bundleRoot, "bundle-integrity.json"),
            """
            {
              "formatVersion": 1,
              "requiredFiles": [
                { "relativePath": "MeetingRecorder.App.exe", "lengthBytes": 7, "sha256": "83656a19d94db9fdacd5feab3e847869ff93d70bf5989f5212e6a719a24c4e25" },
                { "relativePath": "AppPlatform.Deployment.Cli.exe", "lengthBytes": 3, "sha256": "99bb88401742848e032fd6f51709415fb6be169a72d2e5d7fc44289255160d3c" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.exe", "lengthBytes": 6, "sha256": "87eba76e7f3164534045ba922e7770fb58bbd14ad732bbf5ba6f11cc56989e6e" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.dll", "lengthBytes": 10, "sha256": "4c58c2d87ddefa51ea622fa9db6a15d03f664fb3b0d9bc6c44aca741144d4aeb" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.deps.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.runtimeconfig.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.Core.dll", "lengthBytes": 4, "sha256": "0d45f5fd462b8c70bffb10021ac1bcff3f58f29b1faf7568595095427d42812c" },
                { "relativePath": "MeetingRecorder.product.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "Run-MeetingRecorder.cmd", "lengthBytes": 9, "sha256": "abb30b0a70e39de39ce0790c6c157fd04bcfb998705ec1672fe8070ff2d34573" }
              ]
            }
            """);

        try
        {
            var installer = new PortableBundleInstaller(
                new InstallPathProcessManager(new FakeInstallPathProcessController(signalResult: false)),
                new WindowsShortcutService(),
                NullDeploymentLogger.Instance);
            var manifest = new AppProductManifest(
                ProductId: "meeting-recorder",
                ProductName: "Meeting Recorder",
                DisplayName: "Meeting Recorder",
                ExecutableName: "MeetingRecorder.App.exe",
                PortableLauncherFileName: "Run-MeetingRecorder.cmd",
                InstallerExecutableName: "MeetingRecorderInstaller.exe",
                InstallerMsiName: "MeetingRecorderInstaller.msi",
                PortableArchivePrefix: "MeetingRecorder",
                UpdateFeedUrl: "https://example.com/releases/latest",
                ReleasePageUrl: "https://example.com/releases",
                GitHubRepositoryOwner: "example",
                GitHubRepositoryName: "meeting-recorder",
                ManagedInstallLayout: new ManagedInstallLayout(
                    InstallRoot: installRoot,
                    DataRoot: dataRoot,
                    ConfigPath: Path.Combine(dataRoot, "config", "appsettings.json"),
                    PreservedDataDirectories: ["config", "logs"],
                    MergeWithoutOverwriteDirectories: ["models"],
                    LegacyInstallRoots: []),
                ReleaseChannelPolicy: new AppReleaseChannelPolicy(true, true, true, true, true),
                ShortcutPolicy: new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"));

            await installer.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: bundleRoot,
                    InstallRoot: installRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: false,
                    Channel: InstallChannel.CommandBootstrap,
                    ReleaseVersion: "0.3",
                    ReleasePublishedAtUtc: DateTimeOffset.Parse("2026-03-22T14:22:00Z"),
                    ReleaseAssetSizeBytes: 72000000),
                CancellationToken.None);

            var provenancePath = Path.Combine(dataRoot, "install-provenance.json");
            Assert.True(File.Exists(provenancePath));
            var provenanceContents = File.ReadAllText(provenancePath);
            Assert.Contains("CommandBootstrap", provenanceContents, StringComparison.Ordinal);
            Assert.Contains("\"initialVersion\": \"0.3\"", provenanceContents, StringComparison.Ordinal);
            Assert.Contains("\"lastInstalledVersion\": \"0.3\"", provenanceContents, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    [Fact]
    public async Task PlatformPortableBundleInstaller_Updates_In_Place_When_Data_File_Is_Locked()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppPlatformDeploymentTests", Guid.NewGuid().ToString("N"));
        var bundleRoot = Path.Combine(root, "bundle");
        var installRoot = Path.Combine(root, "Documents", "MeetingRecorder");
        var dataRoot = Path.Combine(root, "LocalAppData", "MeetingRecorder");
        Directory.CreateDirectory(bundleRoot);
        Directory.CreateDirectory(installRoot);

        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.App.exe"), "new-app");
        File.WriteAllText(Path.Combine(bundleRoot, "AppPlatform.Deployment.Cli.exe"), "cli");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.exe"), "worker");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.dll"), "worker-dll");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.deps.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.Core.dll"), "core");
        File.WriteAllText(Path.Combine(bundleRoot, "MeetingRecorder.product.json"), "{ }");
        File.WriteAllText(Path.Combine(bundleRoot, "Run-MeetingRecorder.cmd"), "@echo off");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "data", "models"));
        File.WriteAllText(Path.Combine(bundleRoot, "data", "models", "model.bin"), "model");
        File.WriteAllText(
            Path.Combine(bundleRoot, "bundle-integrity.json"),
            """
            {
              "formatVersion": 1,
              "requiredFiles": [
                { "relativePath": "MeetingRecorder.App.exe", "lengthBytes": 7, "sha256": "84e693276868f8699a9400ea321b5e385967b64dc195292d2ebb9c50cace6ff7" },
                { "relativePath": "AppPlatform.Deployment.Cli.exe", "lengthBytes": 3, "sha256": "99bb88401742848e032fd6f51709415fb6be169a72d2e5d7fc44289255160d3c" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.exe", "lengthBytes": 6, "sha256": "87eba76e7f3164534045ba922e7770fb58bbd14ad732bbf5ba6f11cc56989e6e" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.dll", "lengthBytes": 10, "sha256": "4c58c2d87ddefa51ea622fa9db6a15d03f664fb3b0d9bc6c44aca741144d4aeb" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.deps.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.ProcessingWorker.runtimeconfig.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "MeetingRecorder.Core.dll", "lengthBytes": 4, "sha256": "0d45f5fd462b8c70bffb10021ac1bcff3f58f29b1faf7568595095427d42812c" },
                { "relativePath": "MeetingRecorder.product.json", "lengthBytes": 3, "sha256": "257c1be96ae69f4b01c2c69bdb6d78605f59175819fb007d0bf245bf48444c4a" },
                { "relativePath": "Run-MeetingRecorder.cmd", "lengthBytes": 9, "sha256": "abb30b0a70e39de39ce0790c6c157fd04bcfb998705ec1672fe8070ff2d34573" }
              ]
            }
            """);

        File.WriteAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe"), "old-app");
        File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "old-launcher");
        Directory.CreateDirectory(Path.Combine(installRoot, "runtimes"));
        File.WriteAllText(Path.Combine(installRoot, "runtimes", "old.dll"), "old-runtime");
        var lockedDataDirectory = Path.Combine(installRoot, "data", "logs");
        Directory.CreateDirectory(lockedDataDirectory);
        var lockedDataPath = Path.Combine(lockedDataDirectory, "app.log");
        File.WriteAllText(lockedDataPath, "locked-log");

        try
        {
            using var lockedDataStream = new FileStream(
                lockedDataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            var installer = new PortableBundleInstaller(
                new InstallPathProcessManager(new FakeInstallPathProcessController(signalResult: false)),
                new WindowsShortcutService(),
                NullDeploymentLogger.Instance);
            var manifest = new AppProductManifest(
                ProductId: "meeting-recorder",
                ProductName: "Meeting Recorder",
                DisplayName: "Meeting Recorder",
                ExecutableName: "MeetingRecorder.App.exe",
                PortableLauncherFileName: "Run-MeetingRecorder.cmd",
                InstallerExecutableName: "MeetingRecorderInstaller.exe",
                InstallerMsiName: "MeetingRecorderInstaller.msi",
                PortableArchivePrefix: "MeetingRecorder",
                UpdateFeedUrl: "https://example.com/releases/latest",
                ReleasePageUrl: "https://example.com/releases",
                GitHubRepositoryOwner: "example",
                GitHubRepositoryName: "meeting-recorder",
                ManagedInstallLayout: new ManagedInstallLayout(
                    InstallRoot: installRoot,
                    DataRoot: dataRoot,
                    ConfigPath: Path.Combine(dataRoot, "config", "appsettings.json"),
                    PreservedDataDirectories: ["config", "logs"],
                    MergeWithoutOverwriteDirectories: ["models"],
                    LegacyInstallRoots: []),
                ReleaseChannelPolicy: new AppReleaseChannelPolicy(true, true, true, true, true),
                ShortcutPolicy: new ShellShortcutPolicy(
                    "Meeting Recorder",
                    "Meeting Recorder.lnk",
                    "Meeting Recorder.lnk",
                    "MeetingRecorder"));

            await installer.InstallAsync(
                manifest,
                new InstallRequest(
                    BundleRoot: bundleRoot,
                    InstallRoot: installRoot,
                    CreateDesktopShortcut: false,
                    CreateStartMenuShortcut: false,
                    LaunchAfterInstall: false,
                    Channel: InstallChannel.ExecutableBootstrap,
                    ReleaseVersion: "0.3",
                    ReleasePublishedAtUtc: null,
                    ReleaseAssetSizeBytes: null),
                CancellationToken.None);

            Assert.Equal("new-app", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe")));
            Assert.True(File.Exists(lockedDataPath));
            Assert.True(File.Exists(Path.Combine(installRoot, "data", "models", "model.bin")));
            Assert.True(File.Exists(Path.Combine(installRoot, "runtimes", "old.dll")) == false);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private sealed class FakeInstallPathProcessController : IInstallPathProcessController
    {
        private readonly bool _signalResult;
        private readonly Queue<bool> _waitResults;

        public FakeInstallPathProcessController(bool signalResult, IEnumerable<bool>? waitResults = null)
        {
            _signalResult = signalResult;
            _waitResults = new Queue<bool>(waitResults ?? []);
        }

        public bool SignalRequested { get; private set; }

        public int SignalCallCount { get; private set; }

        public int WaitCallCount { get; private set; }

        public bool TrySignalInstallerShutdown()
        {
            SignalRequested = true;
            SignalCallCount++;
            return _signalResult;
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> WaitForPrimaryInstanceReleaseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WaitCallCount++;
            return Task.FromResult(_waitResults.Count > 0 && _waitResults.Dequeue());
        }
    }
}
