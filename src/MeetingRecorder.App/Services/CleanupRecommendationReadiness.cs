using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class CleanupRecommendationReadiness
{
    public static MeetingCleanupRecommendation Project(MeetingCleanupRecommendation recommendation, MeetingOutputRecord? meeting)
    {
        if (recommendation.Action is not (MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels))
            return recommendation;

        var canRecover = meeting is not null &&
            new[] { meeting.AudioPath, meeting.MarkdownPath, meeting.JsonPath, meeting.ReadyMarkerPath }
                .All(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                    !CloudFileStorageOptimizer.IsCloudPlaceholderOrOffline(path)) &&
            MeetingTranscriptDocumentReader.TryReadStructuredSegments(meeting.JsonPath, meeting.MarkdownPath, out var segments) &&
            segments.Count > 0;
        return canRecover ? recommendation : recommendation with
        {
            CanApplyAutomatically = false,
            Description = "Speaker labels need review because recovery files or readable transcript segments are unavailable.",
        };
    }
}
