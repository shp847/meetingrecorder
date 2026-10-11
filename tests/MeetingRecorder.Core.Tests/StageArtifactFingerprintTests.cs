using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class StageArtifactFingerprintTests
{
    [Fact]
    public async Task IsCurrent_Requires_The_Exact_Observed_Artifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "first.artifact");
        var second = Path.Combine(root, "second.artifact");
        await File.WriteAllTextAsync(first, "initial first");
        await File.WriteAllTextAsync(second, "initial second");

        var fingerprint = StageArtifactFingerprint.Capture(first, second);

        Assert.True(fingerprint.IsCurrent());
        await File.WriteAllTextAsync(second, "changed second");
        Assert.False(fingerprint.IsCurrent());
    }

    [Fact]
    public async Task Capture_Rejects_A_Missing_Artifact_Instead_Of_Claiming_Currentness()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var present = Path.Combine(root, "present.artifact");
        await File.WriteAllTextAsync(present, "present");

        Assert.Throws<FileNotFoundException>(() => StageArtifactFingerprint.Capture(present, Path.Combine(root, "missing.artifact")));
    }
}
