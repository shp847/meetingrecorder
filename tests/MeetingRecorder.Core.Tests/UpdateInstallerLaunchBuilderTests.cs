using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class UpdateInstallerLaunchBuilderTests
{
    [Fact]
    public void ResolveInstalledAppRoot_Prefers_ProcessPath_Directory_Over_AppContext_BaseDirectory()
    {
        var installedRoot = UpdateInstallerLaunchBuilder.ResolveInstalledAppRoot(
            @"C:\Users\test\Documents\MeetingRecorder\MeetingRecorder.App.exe",
            @"C:\Users\test\AppData\Local\Temp\.net\MeetingRecorder.App\abc123\");

        Assert.Equal(@"C:\Users\test\Documents\MeetingRecorder", installedRoot);
    }

    [Fact]
    public void Build_Uses_Deployment_Cli_For_Updater_Process()
    {
        var result = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.2",
            "0.2",
            "https://example.com/update.zip",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-03-20T03:15:00Z"),
            74231265,
            false,
            true,
            false,
            "Update available.");

        var startInfo = UpdateInstallerLaunchBuilder.Build(
            @"C:\app\AppPlatform.Deployment.Cli.exe",
            @"C:\temp\MeetingRecorder.zip",
            @"C:\app",
            1234,
            result);

        Assert.Equal(@"C:\app\AppPlatform.Deployment.Cli.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Normal, startInfo.WindowStyle);
        Assert.Equal(@"C:\app", startInfo.WorkingDirectory.TrimEnd('\\'));
        Assert.Contains("apply-update", startInfo.ArgumentList);
        Assert.Contains("--zip-path", startInfo.ArgumentList);
        Assert.Contains(@"C:\temp\MeetingRecorder.zip", startInfo.ArgumentList);
        Assert.Contains("--install-root", startInfo.ArgumentList);
        Assert.Contains(@"C:\app", startInfo.ArgumentList);
        Assert.Contains("--source-process-id", startInfo.ArgumentList);
        Assert.Contains("1234", startInfo.ArgumentList);
        Assert.Contains("--release-version", startInfo.ArgumentList);
        Assert.Contains("0.2", startInfo.ArgumentList);
        Assert.Contains("--pause-on-error", startInfo.ArgumentList);
        Assert.Contains("--log-path", startInfo.ArgumentList);
        Assert.DoesNotContain("-Command", startInfo.ArgumentList);
        Assert.DoesNotContain("-File", startInfo.ArgumentList);
    }
}
