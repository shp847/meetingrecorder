using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Tests;

public sealed class WindowMeetingDetectorTests
{
    [Fact]
    public void IsBetterCandidate_Prefers_GoogleMeet_Browser_Candidate_Over_Generic_Teams_Tie()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var googleMeetCandidate = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            true,
            true,
            1d,
            "Meet - mna-sfqg-htr",
            new[]
            {
                new DetectionSignal("window-title", "Meet - mna-sfqg-htr", 0.85d, timestamp),
                new DetectionSignal("browser-window", "Meet - mna-sfqg-htr", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.391", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var teamsCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "Microsoft Teams",
            new[]
            {
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.391", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");

        var result = WindowMeetingDetector.IsBetterCandidate(googleMeetCandidate, teamsCandidate);

        Assert.True(result);
    }
}
