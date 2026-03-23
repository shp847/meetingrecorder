using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerScriptTests
{
    [Fact]
    public void InstallMeetingRecorderScript_Delegates_Bundle_Install_Work_To_AppPlatformDeploymentCli()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("AppPlatform.Deployment.Cli", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"install-bundle\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Resolve-DeploymentCliPath", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMeetingRecorderScript_No_Longer_Owns_Managed_Config_Metadata_Writes()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("function Get-ManagedConfigPath", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("installedReleasePublishedAtUtc", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingUpdateZipPath", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingUpdateVersion", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMeetingRecorderScript_No_Longer_Implements_Its_Own_Install_Root_Swap_Retry_Loop()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("function Move-ExistingInstallToBackup", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Sleep -Seconds $attempt", scriptContents, StringComparison.Ordinal);
        Assert.Contains("& $deploymentCliPath @cliArguments", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"--pause-on-error\"", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallMeetingRecorderScript_Avoids_WScript_Com_Shortcut_Creation()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("WScript.Shell", scriptContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallMeetingRecorderScript_Avoids_Executable_Path_Process_Inspection()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-MeetingRecorder.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("Win32_Process", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("$process.ExecutablePath", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallLatestFromGitHubScript_Downloads_A_Bundle_And_Delegates_Install_Work_To_AppPlatformDeploymentCli()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-LatestFromGitHub.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected bootstrap script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("AppPlatform.Deployment.Cli", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Resolve-DeploymentCliPathFromExtractedBundle", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Expand-Archive", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"install-bundle\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("$ProgressPreference = \"SilentlyContinue\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"--log-path\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"--pause-on-error\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Diagnostic log", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Read-Host", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallLatestFromGitHubScript_Persists_The_Bootstrap_Channel_When_Delegating_To_The_Cli()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-LatestFromGitHub.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected bootstrap script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("[string]$InstallChannel", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"--install-channel\"", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallLatestFromGitHubScript_Can_Install_From_A_Local_Package_Zip_When_Present()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-LatestFromGitHub.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected bootstrap script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("[string]$PackageZipPath", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Resolve-LocalPackageZipPath", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Expand-Archive -Path $resolvedPackageZipPath", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"Using local package zip", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallLatestFromGitHubCommandWrapper_Uses_RuntimeSafe_ErrorLevel_Handling_In_The_LocalScript_Path()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Install-LatestFromGitHub.cmd");

        Assert.True(File.Exists(scriptPath), $"Expected bootstrap command wrapper at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("setlocal EnableDelayedExpansion", scriptContents, StringComparison.Ordinal);
        Assert.Contains("set \"EXIT_CODE=!errorlevel!\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("if not \"!EXIT_CODE!\"==\"0\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("exit /b !EXIT_CODE!", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableLauncherScript_Pauses_With_A_Clear_Message_When_AppHost_Is_Missing()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Run-MeetingRecorder.cmd");

        Assert.True(File.Exists(scriptPath), $"Expected launcher script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("pause", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("start \"\" dotnet", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MeetingRecorder.App.dll", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableLauncherScript_Waits_Briefly_For_The_AppHost_Before_Failing()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Run-MeetingRecorder.cmd");

        Assert.True(File.Exists(scriptPath), $"Expected launcher script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("set /a WAIT_SECONDS_REMAINING=", scriptContents, StringComparison.Ordinal);
        Assert.Contains(":waitForAppHost", scriptContents, StringComparison.Ordinal);
        Assert.Contains("timeout /t 1 /nobreak >nul", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.App.exe is still missing", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerRelaunchLauncher_Sets_The_Installer_Relaunch_Environment_Variable_Before_Starting_The_App()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Launch-MeetingRecorder-AfterInstall.vbs");

        Assert.True(File.Exists(scriptPath), $"Expected MSI relaunch wrapper at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("installer-relaunch.flag", scriptContents, StringComparison.Ordinal);
        Assert.Contains("%LOCALAPPDATA%", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("shell.Run", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckDependenciesScript_Fails_Clearly_When_The_Wpf_AppHost_Is_Missing()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Check-Dependencies.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected dependency script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.ProcessingWorker.dll", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Meeting Recorder cannot start because MeetingRecorder.App.exe is missing", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("launches will fall back to MeetingRecorder.App.dll through dotnet", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishPortableScript_Merges_Worker_Runtime_Contents_Without_Nesting_A_Second_Runtimes_Directory()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected publish script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("Join-Path $workerTemp \"runtimes\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Join-Path $finalPath \"runtimes\"", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Destination (Join-Path $finalPath $_.Name)", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishPortableScript_Emits_A_Bundle_Integrity_Manifest_And_Copies_The_Product_Manifest()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected publish script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("bundle-integrity.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.ProcessingWorker.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.product.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("$productManifestPath", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -Path $productManifestPath", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishPortableScript_Emits_Release_Source_Metadata_For_Traceable_Releases()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected publish script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("release-source.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("git rev-parse HEAD", scriptContents, StringComparison.Ordinal);
        Assert.Contains("git status --short", scriptContents, StringComparison.Ordinal);
        Assert.Contains("builtAtUtc", scriptContents, StringComparison.Ordinal);
        Assert.Contains("gitCommit", scriptContents, StringComparison.Ordinal);
        Assert.Contains("isWorktreeDirty", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-SourceStatePathspec", scriptContents, StringComparison.Ordinal);
        Assert.Contains(".artifacts", scriptContents, StringComparison.Ordinal);
        Assert.Contains("bin", scriptContents, StringComparison.Ordinal);
        Assert.Contains("obj", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishPortableScript_Publishes_The_Wpf_App_As_A_Single_File_SelfContained_Bundle()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1");
        var appProjectPath = Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MeetingRecorder.App.csproj");

        Assert.True(File.Exists(scriptPath), $"Expected publish script at '{scriptPath}'.");
        Assert.True(File.Exists(appProjectPath), $"Expected app project at '{appProjectPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);
        var appProjectContents = File.ReadAllText(appProjectPath);

        Assert.Contains("MeetingRecorder.App\\MeetingRecorder.App.csproj", scriptContents, StringComparison.Ordinal);
        Assert.Contains("<PublishSingleFile", appProjectContents, StringComparison.Ordinal);
        Assert.Contains("<IncludeAllContentForSelfExtract", appProjectContents, StringComparison.Ordinal);
        Assert.Contains("<IncludeNativeLibrariesForSelfExtract", appProjectContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Harvests_PerUser_Msi_Components_With_Hkcu_KeyPaths()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("Software\\Meeting Recorder\\Installer\\Components", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyPath=\"yes\" />' -f $fileId", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Harvests_RemoveFolder_Cleanup_For_UserProfile_Directories()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("RemoveFolder", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Software\\Meeting Recorder\\Installer\\Directories", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Uses_A_Separate_Msi_Build_Output_Directory_From_The_Generated_Wix_Source()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("$msiBuildOutputPath = Join-Path $packagePath \"msi-build\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("-OutputDirectory $msiBuildOutputPath", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("-OutputDirectory $msiTemp", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Explicitly_Creates_The_Zip_Staging_Directory_Before_Copying_Bootstrap_Files()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Stamps_The_Msi_WordCount_For_NoElevation_PerUser_Installs()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("function Set-MsiPerUserNoElevationSummaryInfo", scriptContents, StringComparison.Ordinal);
        Assert.Contains("WixToolset.Dtf.WindowsInstaller.dll", scriptContents, StringComparison.Ordinal);
        Assert.Contains("$summaryInfo.WordCount = 10", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Set-MsiPerUserNoElevationSummaryInfo -MsiPath $installerMsiBuiltPath", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadReleaseAssetsScript_Derives_UserAgent_Version_From_DirectoryBuildProps()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release upload script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("Directory.Build.props", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("MeetingRecorderInstallerUpload/0.2", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadReleaseAssetsScript_Suppresses_WebRequest_Progress_And_Queues_Parallel_Upload_Jobs()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release upload script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("$ProgressPreference = \"SilentlyContinue\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Start-GitHubReleaseAssetUploadJob", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Wait-GitHubReleaseAssetUploadJobs", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Start-Job", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadReleaseAssetsScript_Polls_And_Prints_Coarse_Upload_Progress_Snapshots()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release upload script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("Write-GitHubReleaseAssetUploadProgressSnapshot", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-GitHubReleaseAssetUploadProgressState", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Wait-Job -Job $jobsToWaitOn -Any -Timeout 2", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Upload progress:", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadReleaseAssetsScript_Refuses_To_Upload_Assets_That_Do_Not_Match_A_Clean_Current_Repo_State()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release upload script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("release-source.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Assert-ReleaseAssetsMatchCurrentRepoState", scriptContents, StringComparison.Ordinal);
        Assert.Contains("git status --short", scriptContents, StringComparison.Ordinal);
        Assert.Contains("git rev-parse HEAD", scriptContents, StringComparison.Ordinal);
        Assert.Contains("must be rebuilt from the current clean repo state", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScripts_Ignore_Generated_Build_Output_When_Validating_Source_State()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var uploadScriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.ps1");
        var buildScriptPath = Path.Combine(repoRoot, "scripts", "Build-Release.ps1");

        Assert.True(File.Exists(uploadScriptPath), $"Expected release upload script at '{uploadScriptPath}'.");
        Assert.True(File.Exists(buildScriptPath), $"Expected release build script at '{buildScriptPath}'.");

        var uploadScriptContents = File.ReadAllText(uploadScriptPath);
        var buildScriptContents = File.ReadAllText(buildScriptPath);

        Assert.Contains(".artifacts", uploadScriptContents, StringComparison.Ordinal);
        Assert.Contains("bin", uploadScriptContents, StringComparison.Ordinal);
        Assert.Contains("obj", uploadScriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-SourceStatePathspec", uploadScriptContents, StringComparison.Ordinal);

        Assert.Contains(".artifacts", buildScriptContents, StringComparison.Ordinal);
        Assert.Contains("bin", buildScriptContents, StringComparison.Ordinal);
        Assert.Contains("obj", buildScriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-SourceStatePathspec", buildScriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadReleaseAssetsCommandWrapper_Loads_Local_Token_Bootstrap_And_Normalizes_Cmd_WorkingDirectory()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Upload-ReleaseAssets.cmd");

        Assert.True(File.Exists(scriptPath), $"Expected release upload wrapper at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("set \"SCRIPT_DIR=%~dp0\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("pushd \"%SCRIPT_DIR%\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Upload-ReleaseAssets.local.cmd", scriptContents, StringComparison.Ordinal);
        Assert.Contains("call \"%LOCAL_ENV_SCRIPT%\"", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void GitIgnore_Excludes_Local_ReleaseUpload_Token_Bootstrap()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");

        Assert.True(File.Exists(gitIgnorePath), $"Expected gitignore at '{gitIgnorePath}'.");

        var gitIgnoreContents = File.ReadAllText(gitIgnorePath);

        Assert.Contains("scripts/Upload-ReleaseAssets.local.cmd", gitIgnoreContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerScript_Does_Not_Package_Local_ReleaseUpload_Token_Bootstrap()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected installer build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("Upload-ReleaseAssets.local.cmd", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload-ReleaseAssets.cmd", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReleaseScript_Validates_Release_Source_Metadata_Before_GitHub_Upload()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Build-Release.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release build script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("release-source.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Assert-ReleaseAssetsMatchCurrentRepoState", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Sync-ReleaseAssetsToGitHubLatestRelease", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployLocalScript_Uses_The_Published_Bundle_And_Verifies_Deployed_Output()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Deploy-Local.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected local deploy script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains(".artifacts\\publish\\win-x64\\MeetingRecorder", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Publish-Portable.ps1", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"install-bundle\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.product.json", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Meeting Recorder.lnk", scriptContents, StringComparison.Ordinal);
        Assert.Contains("CreateShortcut($ShortcutPath)", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("MeetingRecorder.App.dll", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployLocalScript_Detects_Unexpected_Running_App_Instances_Outside_The_Canonical_Install()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Deploy-Local.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected local deploy script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("Unexpected running app instance", scriptContents, StringComparison.Ordinal);
        Assert.Contains("canonical install", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name \"MeetingRecorder.App\"", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseSmokeTestScript_Launches_The_Built_Bundle_And_Installed_Msi_Copy_And_Watches_For_Crash_Events()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Smoke-Test-Release.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected release smoke test script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("MeetingRecorder.App.exe", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-WinEvent", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Windows Error Reporting", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Application Error", scriptContents, StringComparison.Ordinal);
        Assert.Contains(".NET Runtime", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Wait-Process", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorderInstaller.msi", scriptContents, StringComparison.Ordinal);
        Assert.Contains("msiexec.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
    }
}
