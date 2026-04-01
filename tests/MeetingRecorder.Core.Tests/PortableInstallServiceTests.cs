using MeetingRecorder.Core.Services;
using MeetingRecorder.Installer;

namespace MeetingRecorder.Core.Tests;

public sealed class PortableInstallServiceTests : IDisposable
{
    private readonly string _root;

    public PortableInstallServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void PrepareBundleForManagedInstall_Removes_Portable_Markers()
    {
        var stagingRoot = CreateDirectory("staging");
        File.WriteAllText(Path.Combine(stagingRoot, "portable.mode"), string.Empty);
        File.WriteAllText(Path.Combine(stagingRoot, "bundle-mode.txt"), "portable");

        PortableInstallService.PrepareBundleForManagedInstall(stagingRoot);

        Assert.False(File.Exists(Path.Combine(stagingRoot, "portable.mode")));
        Assert.False(File.Exists(Path.Combine(stagingRoot, "bundle-mode.txt")));
    }

    [Fact]
    public void GetInstalledConfigPath_Uses_Managed_App_Data_Root()
    {
        var localApplicationDataRoot = CreateDirectory("localappdata");

        var configPath = PortableInstallService.GetInstalledConfigPath(localApplicationDataRoot);

        Assert.Equal(
            Path.Combine(localApplicationDataRoot, "MeetingRecorder", "config", "appsettings.json"),
            configPath);
    }

    [Fact]
    public void GetDefaultInstallRoot_Uses_UserProfileDocuments_Instead_Of_Redirected_MyDocuments()
    {
        var service = new PortableInstallService();

        var installRoot = service.GetDefaultInstallRoot();

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "MeetingRecorder"),
            installRoot);
    }

    [Fact]
    public void CreateWorkspacePath_Uses_Short_Sibling_Directory_Name()
    {
        var installParent = CreateDirectory("documents");

        var workspacePath = PortableInstallService.CreateWorkspacePath(installParent, "MRI");

        Assert.Equal(installParent, Directory.GetParent(workspacePath)!.FullName);
        Assert.Matches(@"^MRI-[0-9a-f]{12}$", Path.GetFileName(workspacePath));
        Assert.True(
            Path.GetFileName(workspacePath).Length
            < "MeetingRecorder-install-12345678901234567890123456789012".Length);
    }

    [Fact]
    public void CleanupBackupDirectory_Does_Not_Throw_When_Backup_Contains_ReadOnly_Files()
    {
        var backupRoot = CreateDirectory("backup");
        var nestedLogsDirectory = Path.Combine(backupRoot, "data", "work", "session", "logs");
        Directory.CreateDirectory(nestedLogsDirectory);
        var readOnlyFile = Path.Combine(nestedLogsDirectory, "app.log");
        File.WriteAllText(readOnlyFile, "installer backup");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

        var exception = Record.Exception(() => PortableInstallService.CleanupBackupDirectory(backupRoot));

        Assert.Null(exception);
    }

    [Fact]
    public void ApplyStagedBundleToExistingInstall_Replaces_App_Files_Without_Moving_Locked_Data_Files()
    {
        var installRoot = CreateDirectory("install");
        var backupRoot = CreateDirectory("backup");
        var stagingRoot = CreateDirectory("staging");

        File.WriteAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe"), "old-app");
        File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "old-launcher");
        Directory.CreateDirectory(Path.Combine(installRoot, "runtimes"));
        File.WriteAllText(Path.Combine(installRoot, "runtimes", "old.dll"), "old-runtime");

        var lockedDataDirectory = Path.Combine(installRoot, "data", "logs");
        Directory.CreateDirectory(lockedDataDirectory);
        var lockedDataPath = Path.Combine(lockedDataDirectory, "app.log");
        File.WriteAllText(lockedDataPath, "locked-log");

        File.WriteAllText(Path.Combine(stagingRoot, "MeetingRecorder.App.exe"), "new-app");
        File.WriteAllText(Path.Combine(stagingRoot, "Run-MeetingRecorder.cmd"), "new-launcher");
        Directory.CreateDirectory(Path.Combine(stagingRoot, "runtimes"));
        File.WriteAllText(Path.Combine(stagingRoot, "runtimes", "new.dll"), "new-runtime");
        Directory.CreateDirectory(Path.Combine(stagingRoot, "data", "models"));
        File.WriteAllText(Path.Combine(stagingRoot, "data", "models", "model.bin"), "model");

        using var lockedDataStream = new FileStream(
            lockedDataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        PortableInstallService.ApplyStagedBundleToExistingInstall(
            stagingRoot,
            installRoot,
            backupRoot,
            CancellationToken.None);

        Assert.Equal("new-app", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe")));
        Assert.Equal("new-launcher", File.ReadAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtimes", "new.dll")));
        Assert.True(File.Exists(lockedDataPath));
        Assert.True(File.Exists(Path.Combine(installRoot, "data", "models", "model.bin")));
        Assert.True(File.Exists(Path.Combine(backupRoot, "MeetingRecorder.App.exe")));
    }

    [Fact]
    public void ResolveInstalledLaunchPath_Prefers_Portable_Launcher_When_Present()
    {
        var installRoot = CreateDirectory("install");
        var launcherPath = Path.Combine(installRoot, "Run-MeetingRecorder.cmd");
        File.WriteAllText(launcherPath, "@echo off");
        File.WriteAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe"), "apphost");

        var resolvedPath = PortableInstallService.ResolveInstalledLaunchPath(installRoot);

        Assert.Equal(launcherPath, resolvedPath);
    }

    [Fact]
    public void ResolveInstalledLaunchPath_Falls_Back_To_AppHost_When_Launcher_Is_Missing()
    {
        var installRoot = CreateDirectory("install");
        var executablePath = Path.Combine(installRoot, "MeetingRecorder.App.exe");
        File.WriteAllText(executablePath, "apphost");

        var resolvedPath = PortableInstallService.ResolveInstalledLaunchPath(installRoot);

        Assert.Equal(executablePath, resolvedPath);
    }

    [Fact]
    public void ResolveInstalledPostInstallLaunchPath_Prefers_AppHost_When_Present()
    {
        var installRoot = CreateDirectory("install");
        var executablePath = Path.Combine(installRoot, "MeetingRecorder.App.exe");
        File.WriteAllText(executablePath, "apphost");
        File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "@echo off");

        var resolvedPath = PortableInstallService.ResolveInstalledPostInstallLaunchPath(installRoot);

        Assert.Equal(executablePath, resolvedPath);
    }

    [Fact]
    public void EnsureInstalledExecutablePayload_Restores_Missing_Worker_Payload_Files_From_Source_Bundle()
    {
        var sourceBundleRoot = CreateDirectory("source");
        var installRoot = CreateDirectory("install");

        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.App.exe"), "apphost");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "AppPlatform.Deployment.Cli.exe"), "cli");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.ProcessingWorker.exe"), "worker");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.ProcessingWorker.dll"), "worker-dll");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.ProcessingWorker.deps.json"), "{ }");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json"), "{ }");
        File.WriteAllText(Path.Combine(sourceBundleRoot, "MeetingRecorder.Core.dll"), "core");
        File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "@echo off");

        PortableInstallService.EnsureInstalledExecutablePayload(sourceBundleRoot, installRoot);

        Assert.Equal("apphost", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe")));
        Assert.Equal("cli", File.ReadAllText(Path.Combine(installRoot, "AppPlatform.Deployment.Cli.exe")));
        Assert.Equal("worker", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.ProcessingWorker.exe")));
        Assert.Equal("worker-dll", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.ProcessingWorker.dll")));
        Assert.Equal("{ }", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.ProcessingWorker.deps.json")));
        Assert.Equal("{ }", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.ProcessingWorker.runtimeconfig.json")));
        Assert.Equal("core", File.ReadAllText(Path.Combine(installRoot, "MeetingRecorder.Core.dll")));
    }

    [Fact]
    public void ResolveInstalledPostInstallLaunchPath_Throws_When_AppHost_Is_Missing()
    {
        var installRoot = CreateDirectory("install");
        File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "@echo off");

        var exception = Assert.Throws<InvalidOperationException>(
            () => PortableInstallService.ResolveInstalledPostInstallLaunchPath(installRoot));

        Assert.Contains("MeetingRecorder.App.exe", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
