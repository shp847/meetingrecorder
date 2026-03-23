using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class PlatformProjectStructureTests
{
    [Fact]
    public void Solution_Includes_Neutral_AppPlatform_Projects()
    {
        var repoRoot = GetRepoRoot();
        var solutionPath = Path.Combine(repoRoot, "MeetingRecorder.sln");

        Assert.True(File.Exists(solutionPath), $"Expected solution file at '{solutionPath}'.");

        var solutionContents = File.ReadAllText(solutionPath);

        Assert.Contains("AppPlatform.Abstractions", solutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Configuration", solutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment", solutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Deployment.Cli", solutionContents, StringComparison.Ordinal);
        Assert.Contains("AppPlatform.Shell.Wpf", solutionContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Neutral_AppPlatform_Project_Files_Exist()
    {
        var repoRoot = GetRepoRoot();

        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "AppPlatform.Abstractions", "AppPlatform.Abstractions.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "AppPlatform.Configuration", "AppPlatform.Configuration.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "AppPlatform.Deployment", "AppPlatform.Deployment.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "AppPlatform.Deployment.Cli", "AppPlatform.Deployment.Cli.csproj")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "src", "AppPlatform.Shell.Wpf", "AppPlatform.Shell.Wpf.csproj")));
    }

    [Fact]
    public void MeetingRecorder_Projects_Consume_AppPlatform_Projects()
    {
        var repoRoot = GetRepoRoot();
        var appProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MeetingRecorder.App.csproj"));
        var coreProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.Core", "MeetingRecorder.Core.csproj"));
        var installerProject = File.ReadAllText(Path.Combine(repoRoot, "src", "MeetingRecorder.Installer", "MeetingRecorder.Installer.csproj"));

        Assert.Contains(@"..\AppPlatform.Abstractions\AppPlatform.Abstractions.csproj", appProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Shell.Wpf\AppPlatform.Shell.Wpf.csproj", appProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Configuration\AppPlatform.Configuration.csproj", coreProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Abstractions\AppPlatform.Abstractions.csproj", coreProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Deployment\AppPlatform.Deployment.csproj", installerProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Abstractions\AppPlatform.Abstractions.csproj", installerProject, StringComparison.Ordinal);
        Assert.Contains(@"..\AppPlatform.Shell.Wpf\AppPlatform.Shell.Wpf.csproj", installerProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_Includes_Binding_Design_Guidance_For_Future_Agents()
    {
        var repoRoot = GetRepoRoot();
        var designPath = Path.Combine(repoRoot, "DESIGN.md");
        var agentsPath = Path.Combine(repoRoot, "AGENTS.md");

        Assert.True(File.Exists(designPath), $"Expected design guidance at '{designPath}'.");
        Assert.True(File.Exists(agentsPath), $"Expected AGENTS instructions at '{agentsPath}'.");

        var designContents = File.ReadAllText(designPath);
        var agentsContents = File.ReadAllText(agentsPath);

        Assert.Contains("Design System Document", designContents, StringComparison.Ordinal);
        Assert.Contains("DESIGN.md", agentsContents, StringComparison.Ordinal);
        Assert.Contains("must follow", agentsContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("design", agentsContents, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
