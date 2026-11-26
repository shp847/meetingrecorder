using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class CleanupRecommendationReadiness
{
    public static MeetingCleanupRecommendation Project(
        MeetingCleanupRecommendation recommendation, IReadOnlyDictionary<string, MeetingOutputRecord> records)
    {
        if (!recommendation.CanApplyAutomatically || recommendation.Action is not
            (MeetingCleanupAction.Archive or MeetingCleanupAction.Merge or MeetingCleanupAction.RegenerateTranscript or
             MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels))
            return recommendation;

        var stems = recommendation.Action == MeetingCleanupAction.Merge
            ? recommendation.RelatedStems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [recommendation.PrimaryStem];
        var meetings = stems.Select(stem => records.GetValueOrDefault(stem)).ToArray();
        var canRecover = meetings.Length > 0 && meetings.All(meeting => meeting is not null &&
            new[] { meeting.AudioPath, meeting.MarkdownPath, meeting.JsonPath, meeting.ReadyMarkerPath }
                .All(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
                    !CloudFileStorageOptimizer.IsCloudPlaceholderOrOffline(path)));
        if (canRecover && recommendation.Action is (MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels))
        {
            var meeting = meetings[0]!;
            canRecover = MeetingTranscriptDocumentReader.TryReadStructuredSegments(meeting.JsonPath, meeting.MarkdownPath, out var segments) &&
                segments.Count > 0;
        }
        return canRecover ? recommendation : recommendation with
        {
            CanApplyAutomatically = false,
            Description = recommendation.Action is MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels
                ? "Speaker labels need review because recovery files or readable transcript segments are unavailable."
                : "This fix needs review because published recovery files are unavailable.",
        };
    }

    public static MeetingCleanupRecommendation Project(MeetingCleanupRecommendation recommendation, MeetingOutputRecord meeting) =>
        Project(recommendation, new Dictionary<string, MeetingOutputRecord>(StringComparer.OrdinalIgnoreCase) { [meeting.Stem] = meeting });
}
