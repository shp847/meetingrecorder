using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SessionManifestStoreTests
{
    [Fact]
    public async Task CreateAsync_Persists_A_Queued_Manifest_With_Default_Statuses()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Architecture Review",
            new[]
            {
                new DetectionSignal("window-title", "Architecture Review | Microsoft Teams", 0.9, DateTimeOffset.UtcNow),
            });

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var saved = await store.LoadAsync(manifestPath);

        Assert.Equal(SessionState.Queued, manifest.State);
        Assert.Equal("Architecture Review", saved.DetectedTitle);
        Assert.Equal(StageExecutionState.NotStarted, saved.TranscriptionStatus.State);
        Assert.Single(saved.DetectionEvidence);
    }
}
