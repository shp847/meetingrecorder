using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class DeploymentCliTests
{
    [Fact]
    public async Task DeploymentCli_Allows_Short_Alias_Options_For_PrintLayout()
    {
        var repoRoot = GetRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Product", "MeetingRecorder.product.json");
        Assert.True(File.Exists(manifestPath), $"Expected product manifest at '{manifestPath}'.");

        var cliAssembly = Assembly.Load("AppPlatform.Deployment.Cli");
        var programType = cliAssembly.GetType("AppPlatform.Deployment.Cli.Program", throwOnError: true)!;
        var mainMethod = programType.GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(string[])],
            modifiers: null);

        Assert.NotNull(mainMethod);

        var result = mainMethod!.Invoke(null, [new[] { "print-layout", "-m", manifestPath }]);
        Assert.NotNull(result);

        var exitCode = await (Task<int>)result!;

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void DeploymentCli_ProvisionModels_Repairs_Missing_Install_Provenance_For_Fresh_Msi_Installs()
    {
        var repoRoot = GetRepoRoot();
        var programPath = Path.Combine(repoRoot, "src", "AppPlatform.Deployment.Cli", "Program.cs");

        Assert.True(File.Exists(programPath), $"Expected deployment CLI source at '{programPath}'.");

        var source = File.ReadAllText(programPath);
        var methodStart = source.IndexOf("private static async Task<int> ProvisionModelsAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static int RepairShortcuts", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("InstalledProvenanceRepairService.TryRepairMissingInstallProvenance(", methodBlock);
        Assert.Contains("Repaired missing install provenance during MSI post-install provisioning.", methodBlock);
    }

    [Fact]
    public void DeploymentCli_Prefers_Direct_Executable_Relaunch_Over_Shell_Launching_The_Cmd_Wrapper()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeploymentCliTests", Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "MeetingRecorder");
        Directory.CreateDirectory(installRoot);

        try
        {
            File.WriteAllText(Path.Combine(installRoot, "MeetingRecorder.App.exe"), "apphost");
            File.WriteAllText(Path.Combine(installRoot, "Run-MeetingRecorder.cmd"), "@echo off");

            var cliAssembly = Assembly.Load("AppPlatform.Deployment.Cli");
            var abstractionsAssembly = Assembly.Load("AppPlatform.Abstractions");
            var programType = cliAssembly.GetType("AppPlatform.Deployment.Cli.Program", throwOnError: true)!;
            var helperMethod = programType.GetMethod(
                "BuildInstalledAppLaunchStartInfo",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(helperMethod);

            var manifestType = abstractionsAssembly.GetType("AppPlatform.Abstractions.AppProductManifest", throwOnError: true)!;
            var layoutType = abstractionsAssembly.GetType("AppPlatform.Abstractions.ManagedInstallLayout", throwOnError: true)!;
            var releasePolicyType = abstractionsAssembly.GetType("AppPlatform.Abstractions.AppReleaseChannelPolicy", throwOnError: true)!;
            var shortcutPolicyType = abstractionsAssembly.GetType("AppPlatform.Abstractions.ShellShortcutPolicy", throwOnError: true)!;

            var layout = Activator.CreateInstance(
                layoutType,
                installRoot,
                Path.Combine(root, "data"),
                Path.Combine(root, "data", "config", "appsettings.json"),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
            var releasePolicy = Activator.CreateInstance(releasePolicyType, true, true, true, false, true);
            var shortcutPolicy = Activator.CreateInstance(
                shortcutPolicyType,
                "Meeting Recorder",
                "Meeting Recorder.lnk",
                "Meeting Recorder.lnk",
                "MeetingRecorder");
            var manifest = Activator.CreateInstance(
                manifestType,
                "meeting-recorder",
                "Meeting Recorder",
                "Meeting Recorder",
                "MeetingRecorder.App.exe",
                "Run-MeetingRecorder.cmd",
                string.Empty,
                "MeetingRecorderInstaller.msi",
                "MeetingRecorder",
                "https://example.com/releases/latest",
                "https://example.com/releases",
                "example",
                "meeting-recorder",
                layout,
                releasePolicy,
                shortcutPolicy);

            var startInfo = (System.Diagnostics.ProcessStartInfo)helperMethod!.Invoke(null, [manifest!, installRoot])!;

            Assert.Equal(Path.Combine(installRoot, "MeetingRecorder.App.exe"), startInfo.FileName);
            Assert.Equal(installRoot, startInfo.WorkingDirectory);
            Assert.False(startInfo.UseShellExecute);
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

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
