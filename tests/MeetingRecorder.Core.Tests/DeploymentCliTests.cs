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

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
