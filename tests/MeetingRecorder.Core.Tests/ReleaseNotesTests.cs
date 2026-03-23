using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class ReleaseNotesTests
{
    [Fact]
    public void ReleaseNotes_For_V0_3_Are_Present()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var releaseNotesPath = Path.Combine(repoRoot, "RELEASE_NOTES_v0.3.md");

        Assert.True(File.Exists(releaseNotesPath), $"Expected release notes at '{releaseNotesPath}'.");

        var contents = File.ReadAllText(releaseNotesPath);
        Assert.Contains("# Meeting Recorder v0.3", contents, StringComparison.Ordinal);
    }
}
