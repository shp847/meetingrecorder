using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

public sealed record TranscriptSegment(
    [property: JsonPropertyName("start")] TimeSpan Start,
    [property: JsonPropertyName("end")] TimeSpan End,
    [property: JsonPropertyName("speakerId")] string? SpeakerId,
    [property: JsonPropertyName("speakerLabel")] string? SpeakerLabel,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("segmentId")] string? SegmentId = null)
{
    public TranscriptSegment(TimeSpan Start, TimeSpan End, string? SpeakerLabel, string Text)
        : this(Start, End, null, SpeakerLabel, Text)
    {
    }
}
