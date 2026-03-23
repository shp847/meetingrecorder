using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingOutputStatusResolverTests
{
    [Fact]
    public void ResolveDisplayStatus_Returns_Published_When_Audio_And_Markdown_Are_Present_Without_A_Ready_Marker()
    {
        var record = new MeetingOutputRecord(
            "2026-03-19_235528_teams_wang-stein",
            "Wang, Stein",
            new DateTimeOffset(2026, 03, 19, 23, 55, 28, TimeSpan.Zero),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(11),
            AudioPath: "audio.wav",
            MarkdownPath: "transcript.md",
            JsonPath: null,
            ReadyMarkerPath: null,
            ManifestPath: null,
            ManifestState: null,
            Attendees: Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: null);

        var status = MeetingOutputStatusResolver.ResolveDisplayStatus(record);

        Assert.Equal(SessionState.Published.ToString(), status);
    }

    [Fact]
    public void ResolveDisplayStatus_Returns_TranscriptFilesPresent_When_Only_Transcript_Artifacts_Are_Present()
    {
        var record = new MeetingOutputRecord(
            "2026-03-19_235528_teams_wang-stein",
            "Wang, Stein",
            new DateTimeOffset(2026, 03, 19, 23, 55, 28, TimeSpan.Zero),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(11),
            AudioPath: null,
            MarkdownPath: "transcript.md",
            JsonPath: null,
            ReadyMarkerPath: null,
            ManifestPath: null,
            ManifestState: null,
            Attendees: Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: null);

        var status = MeetingOutputStatusResolver.ResolveDisplayStatus(record);

        Assert.Equal("Transcript files present", status);
    }
}
