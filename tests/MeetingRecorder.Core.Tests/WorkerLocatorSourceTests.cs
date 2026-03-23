namespace MeetingRecorder.Core.Tests;

public sealed class WorkerLocatorSourceTests
{
    [Fact]
    public void Resolve_Uses_Installed_Process_Path_Root_For_Worker_Discovery()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "Services", "WorkerLocator.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains(
            "EnumerateCandidates(Environment.ProcessPath, AppContext.BaseDirectory)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var installedRoot = ResolveInstalledAppRoot(processPath, appContextBaseDirectory);",
            source,
            StringComparison.Ordinal);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }
}
