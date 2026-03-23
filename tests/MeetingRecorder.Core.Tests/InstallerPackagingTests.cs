using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class InstallerPackagingTests
{
    [Fact]
    public void WixPackage_Is_PerUser_And_Does_Not_Target_ProgramFiles()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.Contains("Scope=\"perUser\"", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Scope=\"perMachine\"", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramFiles64Folder", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixPackage_Uses_Documents_MeetingRecorder_Location_And_StartMenu_Shortcut()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.Contains("ProgramMenuFolder", packageContents, StringComparison.Ordinal);
        Assert.Contains("TARGETDIR", packageContents, StringComparison.Ordinal);
        Assert.Contains("PHYSICALUSERPROFILE", packageContents, StringComparison.Ordinal);
        Assert.Contains("Volatile Environment", packageContents, StringComparison.Ordinal);
        Assert.Contains("Name=\"USERPROFILE\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("<SetProperty Id=\"INSTALLFOLDER\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("[PHYSICALUSERPROFILE]\\Documents\\MeetingRecorder", packageContents, StringComparison.Ordinal);
        Assert.Contains("Name=\"MeetingRecorder\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("ProgramMenuFolder", packageContents, StringComparison.Ordinal);
        Assert.Contains("<Shortcut", packageContents, StringComparison.Ordinal);
        Assert.Contains("Target=\"[INSTALLFOLDER]MeetingRecorder.App.exe\"", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Target=\"[#MainExecutableFile]\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("ARPINSTALLLOCATION", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("<Property Id=\"ARPINSTALLLOCATION\" Value=\"[INSTALLFOLDER]\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("<SetProperty Id=\"ARPINSTALLLOCATION\"", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalProgramsFolder", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("PersonalFolder", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("UserProfileFolder", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixProject_Suppresses_Only_Intentional_PerUser_Validation_Warnings()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "MeetingRecorder.Setup.wixproj");

        Assert.True(File.Exists(projectPath), $"Expected WiX project file at '{projectPath}'.");

        var projectContents = File.ReadAllText(projectPath);

        Assert.Contains("SuppressIces", projectContents, StringComparison.Ordinal);
        Assert.Contains("ICE91", projectContents, StringComparison.Ordinal);
        Assert.Contains("ICE71", projectContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixPackage_Enables_Default_Msi_Logging_For_Troubleshooting()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.Contains("MsiLogging", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixPackage_Shows_Completion_Feedback_And_Offers_To_Launch_The_App()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.Contains("WixUI_InstallDir", packageContents, StringComparison.Ordinal);
        Assert.Contains("WIXUI_EXITDIALOGOPTIONALTEXT", packageContents, StringComparison.Ordinal);
        Assert.Contains("installed and ready to use", packageContents, StringComparison.Ordinal);
        Assert.Contains("WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT", packageContents, StringComparison.Ordinal);
        Assert.Contains("Launch Meeting Recorder", packageContents, StringComparison.Ordinal);
        Assert.Contains("<Property Id=\"WIXUI_EXITDIALOGOPTIONALCHECKBOX\" Value=\"1\" />", packageContents, StringComparison.Ordinal);
        Assert.Contains("Dialog=\"ExitDialog\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Event=\"DoAction\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Value=\"LaunchApplication\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("WixShellExecTarget", packageContents, StringComparison.Ordinal);
        Assert.Contains("<SetProperty Id=\"WixShellExecTarget\" Value=\"[INSTALLFOLDER]Launch-MeetingRecorder-AfterInstall.vbs\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("[INSTALLFOLDER]Launch-MeetingRecorder-AfterInstall.vbs", packageContents, StringComparison.Ordinal);
        Assert.DoesNotContain("<SetProperty Id=\"WixShellExecTarget\" Value=\"[INSTALLFOLDER]MeetingRecorder.App.exe\"", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixPackage_Allows_Refreshed_Same_Version_Reinstalls_To_Overwrite_Installed_Binaries()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.Contains("AllowSameVersionUpgrades=\"yes\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("<Property Id=\"REINSTALLMODE\" Value=\"amus\" />", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixPackage_Skips_The_License_Agreement_Dialog()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var packagePath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "Package.wxs");

        Assert.True(File.Exists(packagePath), $"Expected WiX package authoring at '{packagePath}'.");

        var packageContents = File.ReadAllText(packagePath);

        Assert.DoesNotContain("WixUI_Minimal", packageContents, StringComparison.Ordinal);
        Assert.Contains("WixUI_InstallDir", packageContents, StringComparison.Ordinal);
        Assert.Contains("InstallDirectory=\"INSTALLFOLDER\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Dialog=\"WelcomeDlg\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Value=\"VerifyReadyDlg\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Dialog=\"VerifyReadyDlg\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Value=\"WelcomeDlg\"", packageContents, StringComparison.Ordinal);
        Assert.Contains("Order=\"999\"", packageContents, StringComparison.Ordinal);
    }

    [Fact]
    public void WixProject_References_Ui_And_Util_Extensions_For_Finish_Dialog_And_Launch_Action()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var projectPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Setup", "MeetingRecorder.Setup.wixproj");

        Assert.True(File.Exists(projectPath), $"Expected WiX project file at '{projectPath}'.");

        var projectContents = File.ReadAllText(projectPath);

        Assert.Contains("WixToolset.UI.wixext", projectContents, StringComparison.Ordinal);
        Assert.Contains("WixToolset.Util.wixext", projectContents, StringComparison.Ordinal);
    }
}
