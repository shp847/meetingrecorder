using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class DeploymentPackagingTests
{
    [Fact]
    public void Deployment_Project_Declares_SystemIoPipelines_For_Self_Contained_Cli_Runtime_Compatibility()
    {
        var projectPath = GetPath("src", "AppPlatform.Deployment", "AppPlatform.Deployment.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("<PackageReference Include=\"System.IO.Pipelines\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_Cli_Reuses_Model_Provisioning_For_Install_And_Update_Flows()
    {
        var programPath = GetPath("src", "AppPlatform.Deployment.Cli", "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.Contains("ProvisionInstalledModelsAsync", program, StringComparison.Ordinal);
        Assert.Contains("InstallBundleAsync", program, StringComparison.Ordinal);
        Assert.Contains("InstallLatestAsync", program, StringComparison.Ordinal);
        Assert.Contains("ApplyUpdateAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_Cli_Supports_Short_Model_Provisioning_Aliases_For_Msi_Command_Length_Limits()
    {
        var programPath = GetPath("src", "AppPlatform.Deployment.Cli", "Program.cs");
        var program = File.ReadAllText(programPath);

        Assert.Contains("GetRequiredOption(options, \"--install-root\", \"-i\")", program, StringComparison.Ordinal);
        Assert.Contains("GetOptionalOption(options, \"--manifest-path\", \"-m\")", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredOption(options, \"--transcription-profile\", \"-t\")", program, StringComparison.Ordinal);
        Assert.Contains("GetRequiredOption(options, \"--speaker-labeling-profile\", \"-s\")", program, StringComparison.Ordinal);
        Assert.Contains("GetOptionalOption(options, \"--log-path\", \"-l\")", program, StringComparison.Ordinal);
    }

    private static string GetPath(params string[] parts)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine([repoRoot, .. parts]);
    }
}
