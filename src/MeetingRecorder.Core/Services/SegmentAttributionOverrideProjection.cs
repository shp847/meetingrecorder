using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal sealed record SegmentAttributionOverrideProjection(
    IReadOnlyList<TranscriptSegment> EffectiveSegments,
    IReadOnlyList<MeetingTranscriptSegmentRow> Rows);

internal static class SegmentAttributionOverrideProjectionService
{
    public static SegmentAttributionOverrideProjection Project(
        IReadOnlyList<TranscriptSegment> segments,
        string artifactRevision,
        IReadOnlyDictionary<string, SpeakerIdentity> speakers,
        IReadOnlyList<SegmentAttributionOverride> overrides)
    {
        var effectiveSegments = new List<TranscriptSegment>(segments.Count);
        var rows = new List<MeetingTranscriptSegmentRow>(segments.Count);
        foreach (var segment in segments)
        {
            var attribution = SegmentAttributionOverrideResolver.Resolve(segment, artifactRevision, speakers, overrides);
            effectiveSegments.Add(segment with { SpeakerLabel = attribution.DisplayLabel });
            rows.Add(new MeetingTranscriptSegmentRow(
                segment.Start.ToString(@"hh\:mm\:ss"),
                attribution.DisplayLabel,
                segment.Text,
                segment.SpeakerId,
                segment.SegmentId,
                string.IsNullOrWhiteSpace(segment.SourceSpeakerLabel) ? segment.SpeakerLabel : segment.SourceSpeakerLabel,
                attribution.State));
        }

        return new SegmentAttributionOverrideProjection(effectiveSegments, rows);
    }
}
