using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class UpdaterScriptTests
{
    [Fact]
    public void ApplyDownloadedUpdateScript_Delegates_Update_Apply_Work_To_AppPlatformDeploymentCli()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Apply-DownloadedUpdate.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected updater script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("AppPlatform.Deployment.Cli", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"apply-update\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("& $deploymentCliPath @cliArguments", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyDownloadedUpdateScript_No_Longer_Implements_Shell_Integration_Logic_Directly()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Apply-DownloadedUpdate.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected updater script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("NoDesktopShortcut = $true", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("NoStartMenuShortcut = $true", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("TryTaskbarPin = $true", scriptContents, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyDownloadedUpdateScript_Writes_Diagnostics_And_Pauses_On_Error()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Apply-DownloadedUpdate.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected updater script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("\"--log-path\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("\"--pause-on-error\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Diagnostic log", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Read-Host", scriptContents, StringComparison.Ordinal);
    }
}
