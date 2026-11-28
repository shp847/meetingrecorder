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
        IReadOnlyList<SegmentAttributionOverride> overrides,
        IReadOnlyDictionary<string, string>? canonicalBySpeakerId = null)
    {
        var effectiveSegments = new List<TranscriptSegment>(segments.Count);
        var rows = new List<MeetingTranscriptSegmentRow>(segments.Count);
        var activeBySegment = overrides.Where(item => item.RevertedAtUtc is null && item.ArtifactRevision == artifactRevision)
            .GroupBy(item => item.SegmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            var current = segment.SegmentId is not null && activeBySegment.TryGetValue(segment.SegmentId, out var found) ? found : null;
            var attribution = SegmentAttributionOverrideResolver.Resolve(segment, artifactRevision, speakers,
                current is null ? [] : [current], canonicalBySpeakerId);
            effectiveSegments.Add(segment with { SpeakerLabel = attribution.DisplayLabel });
            rows.Add(new MeetingTranscriptSegmentRow(
                segment.Start.ToString(@"hh\:mm\:ss"),
                attribution.DisplayLabel,
                segment.Text,
                segment.SpeakerId,
                segment.SegmentId,
                string.IsNullOrWhiteSpace(segment.SourceSpeakerLabel) ? segment.SpeakerLabel : segment.SourceSpeakerLabel,
                attribution.State,
                attribution.OverrideId,
                segment.ManualSplitId,
                GetEffectiveSpeakerKey(segment, attribution, current, canonicalBySpeakerId),
                SourceSegmentId: segment.SourceSegmentId ?? segment.SegmentId,
                SelectionCorrectionId: GetSelectionCorrectionId(current),
                SourceTextOffset: segment.SourceTextOffset));
        }

        return new SegmentAttributionOverrideProjection(effectiveSegments, rows);
    }

    private static string? GetSelectionCorrectionId(SegmentAttributionOverride? current)
    {
        const string prefix = "selection-correction:";
        const string inheritedPrefix = "selection-inherited:";
        var reason = current?.Reason;
        if (reason?.StartsWith(prefix, StringComparison.Ordinal) == true) return reason[prefix.Length..];
        return reason?.StartsWith(inheritedPrefix, StringComparison.Ordinal) == true ? reason[inheritedPrefix.Length..] : null;
    }

    private static string? GetEffectiveSpeakerKey(
        TranscriptSegment segment,
        EffectiveSegmentAttribution attribution,
        SegmentAttributionOverride? current,
        IReadOnlyDictionary<string, string>? canonicalBySpeakerId)
    {
        if (attribution.State == EffectiveSegmentAttributionState.ReviewNeeded) return null;
        string? speakerId = segment.SpeakerId;
        if (attribution.State == EffectiveSegmentAttributionState.Override)
        {
            if (current is null) return null;
            if (current.TargetKind == SegmentAttributionOverrideTargetKind.DisplayName)
            {
                var selectionId = GetSelectionCorrectionId(current);
                if (selectionId is not null && current.Reason?.StartsWith("selection-correction:", StringComparison.Ordinal) == true)
                    return $"selection-name:{selectionId}:{current.TargetValue.Trim()}";
                return string.IsNullOrWhiteSpace(speakerId) ? null :
                    $"name:{speakerId}:{current.TargetValue.Trim()}";
            }
            speakerId = current.TargetValue;
        }
        if (string.IsNullOrWhiteSpace(speakerId)) return null;
        speakerId = speakerId.Trim();
        if (attribution.State == EffectiveSegmentAttributionState.Source && canonicalBySpeakerId is not null && canonicalBySpeakerId.TryGetValue(speakerId, out var canonical))
            speakerId = canonical;
        return $"speaker:{speakerId}";
    }
}
