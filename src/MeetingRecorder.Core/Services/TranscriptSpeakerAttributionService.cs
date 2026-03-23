using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class TranscriptSpeakerAttributionService
{
    public IReadOnlyList<TranscriptSegment> ApplySpeakerTurns(
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyDictionary<string, string> speakerDisplayNames)
    {
        if (transcriptSegments.Count == 0 || speakerTurns.Count == 0)
        {
            return transcriptSegments;
        }

        return transcriptSegments
            .Select(segment => ApplySpeakerTurn(segment, speakerTurns, speakerDisplayNames))
            .ToArray();
    }

    public IReadOnlyList<SpeakerIdentity> BuildSpeakerCatalog(IReadOnlyList<SpeakerTurn> speakerTurns)
    {
        if (speakerTurns.Count == 0)
        {
            return Array.Empty<SpeakerIdentity>();
        }

        return speakerTurns
            .Select(turn => turn.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static speakerId => speakerId, StringComparer.Ordinal)
            .Select((speakerId, index) => new SpeakerIdentity(speakerId, $"Speaker {index + 1}", false))
            .ToArray();
    }

    private static TranscriptSegment ApplySpeakerTurn(
        TranscriptSegment segment,
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyDictionary<string, string> speakerDisplayNames)
    {
        var bestTurn = speakerTurns
            .Select(turn => new
            {
                Turn = turn,
                Overlap = CalculateOverlap(segment.Start, segment.End, turn.Start, turn.End),
                MidpointDistance = CalculateMidpointDistance(segment.Start, segment.End, turn.Start, turn.End),
            })
            .Where(result => result.Overlap > TimeSpan.Zero)
            .OrderByDescending(result => result.Overlap)
            .ThenBy(result => result.MidpointDistance)
            .Select(result => result.Turn)
            .FirstOrDefault();

        if (bestTurn is null)
        {
            return segment;
        }

        speakerDisplayNames.TryGetValue(bestTurn.SpeakerId, out var displayName);
        return segment with
        {
            SpeakerId = bestTurn.SpeakerId,
            SpeakerLabel = string.IsNullOrWhiteSpace(displayName)
                ? segment.SpeakerLabel
                : displayName,
        };
    }

    private static TimeSpan CalculateOverlap(
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        TimeSpan turnStart,
        TimeSpan turnEnd)
    {
        var overlapStart = segmentStart > turnStart ? segmentStart : turnStart;
        var overlapEnd = segmentEnd < turnEnd ? segmentEnd : turnEnd;
        return overlapEnd > overlapStart
            ? overlapEnd - overlapStart
            : TimeSpan.Zero;
    }

    private static TimeSpan CalculateMidpointDistance(
        TimeSpan segmentStart,
        TimeSpan segmentEnd,
        TimeSpan turnStart,
        TimeSpan turnEnd)
    {
        var segmentMidpoint = segmentStart + TimeSpan.FromTicks((segmentEnd - segmentStart).Ticks / 2);
        var turnMidpoint = turnStart + TimeSpan.FromTicks((turnEnd - turnStart).Ticks / 2);
        var difference = segmentMidpoint - turnMidpoint;
        return difference.Duration();
    }
}
