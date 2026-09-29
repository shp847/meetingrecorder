using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingRecommendationPresentationTests
{
    [Fact]
    public void Inspector_And_Detail_Use_The_Same_Resolved_Primary_Recommendation()
    {
        var recommendation = new MeetingPrimaryRecommendation(
            MeetingPrimaryRecommendationKind.RecoverTranscript,
            "Review transcript recovery",
            "A failed transcript can be recovered from available source audio.",
            MeetingRecommendationSeverity.High,
            MeetingRecommendationActionTarget.MeetingDetails,
            IsActionEligible: true,
            BlockReason: null,
            SnapshotFingerprint: "stable-fingerprint",
            RecommendationVersion: MeetingRecommendationResolver.CurrentRecommendationVersion,
            SnapshotVersion: 2,
            EvaluatedAtUtc: new DateTimeOffset(2026, 9, 27, 15, 0, 0, TimeSpan.Zero),
            IsDismissed: false);
        var meeting = new MeetingOutputRecord(
            "meeting-1",
            "Client sync",
            new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeSpan.Zero),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(30),
            @"C:\Meetings\client-sync.wav",
            @"C:\Meetings\client-sync.md",
            @"C:\Meetings\client-sync.json",
            null,
            null,
            SessionState.Failed,
            Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: "model.bin");
        var transcript = new MeetingTranscriptReaderResult(false, "Transcript unavailable.", Array.Empty<MeetingTranscriptSegmentRow>());

        var inspector = MainWindowInteractionLogic.BuildMeetingInspectorState(
            meeting,
            Array.Empty<MeetingCleanupRecommendation>(),
            primaryRecommendation: recommendation);
        var detail = MainWindowInteractionLogic.BuildMeetingDetailWindowState(
            meeting,
            Array.Empty<MeetingCleanupRecommendation>(),
            transcript,
            canOpenAudio: true,
            canOpenTranscript: false,
            canRegenerateTranscript: true,
            canAddSpeakerLabels: false,
            canProcessAsap: false,
            isSelectedMeetingAsap: false,
            primaryRecommendation: recommendation);

        Assert.Equal([recommendation.Label], inspector.RecommendationBadges);
        Assert.Equal(inspector.RecommendationBadges, detail.RecommendationBadges);
    }
}
