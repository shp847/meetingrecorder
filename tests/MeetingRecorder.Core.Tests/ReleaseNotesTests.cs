using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class ReleaseNotesTests
{
    [Fact]
    public void ReleaseNotes_For_V0_3_Are_Present()
    {
        var repoRoot = GetRepoRoot();
        var releaseNotesPath = Path.Combine(repoRoot, "RELEASE_NOTES_v0.3.md");

        Assert.True(File.Exists(releaseNotesPath), $"Expected release notes at '{releaseNotesPath}'.");

        var contents = File.ReadAllText(releaseNotesPath);
        Assert.Contains("# Meeting Recorder v0.3", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseNotes_For_V0_3_Include_Meeting_Summaries()
    {
        var releaseNotesPath = Path.Combine(GetRepoRoot(), "RELEASE_NOTES_v0.3.md");
        var contents = File.ReadAllText(releaseNotesPath);

        Assert.Contains("meeting summaries", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ModelProxy", contents, StringComparison.Ordinal);
        Assert.Contains("OpenAI", contents, StringComparison.Ordinal);
        Assert.Contains("summary.snapshot.json", contents, StringComparison.Ordinal);
        Assert.Contains("meeting detail", contents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Temporary_Meeting_Summaries_Sprint_Plan_Is_Removed_After_Sprint_4()
    {
        var sprintPlanPath = Path.Combine(GetRepoRoot(), "MEETING_SUMMARIES_SPRINT_PLAN.md");

        Assert.False(File.Exists(sprintPlanPath), $"Temporary plan should be deleted after Sprint 4: {sprintPlanPath}");
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
