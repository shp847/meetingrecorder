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

    [Fact]
    public async Task SaveAsync_RoundTrips_Attendees_And_Processing_Metadata()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "work");
        var store = new SessionManifestStore(new ArtifactPathBuilder());

        var manifest = await store.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Attendee Roundtrip",
            Array.Empty<DetectionSignal>());

        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var expected = manifest with
        {
            Attendees =
            [
                new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar]),
                new MeetingAttendee("John Doe", [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar]),
            ],
            ProcessingOverrides = new MeetingProcessingOverrides(
                @"C:\models\ggml-small.bin",
                "ggml-small.bin"),
            ProcessingMetadata = new MeetingProcessingMetadata(
                "ggml-medium.bin",
                true),
        };

        await store.SaveAsync(expected, manifestPath);
        var saved = await store.LoadAsync(manifestPath);

        Assert.Collection(saved.Attendees,
            attendee =>
            {
                Assert.Equal("Jane Smith", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
            },
            attendee =>
            {
                Assert.Equal("John Doe", attendee.Name);
                Assert.Equal(
                    [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                    attendee.Sources);
            });
        Assert.Equal(@"C:\models\ggml-small.bin", saved.ProcessingOverrides?.TranscriptionModelPath);
        Assert.Equal("ggml-small.bin", saved.ProcessingOverrides?.TranscriptionModelFileName);
        Assert.Equal("ggml-medium.bin", saved.ProcessingMetadata?.TranscriptionModelFileName);
        Assert.True(saved.ProcessingMetadata?.HasSpeakerLabels);
    }
}
