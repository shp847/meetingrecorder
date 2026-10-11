using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

public sealed record TranscriptSegment
{
    [JsonConstructor]
    public TranscriptSegment(
        TimeSpan Start,
        TimeSpan End,
        string? SpeakerId,
        string? SpeakerLabel,
        string Text,
        string? SegmentId = null)
    {
        this.Start = Start;
        this.End = End;
        this.SpeakerId = SpeakerId;
        this.SpeakerLabel = SpeakerLabel;
        this.Text = Text;
        this.SegmentId = SegmentId;
    }

    [JsonPropertyName("start")]
    public TimeSpan Start { get; init; }

    [JsonPropertyName("end")]
    public TimeSpan End { get; init; }

    [JsonPropertyName("speakerId")]
    public string? SpeakerId { get; init; }

    [JsonPropertyName("speakerLabel")]
    public string? SpeakerLabel { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; }

    [JsonPropertyName("segmentId")]
    public string? SegmentId { get; init; }

    [JsonIgnore]
    public string? SourceSpeakerLabel { get; init; }

    public TranscriptSegment(TimeSpan Start, TimeSpan End, string? SpeakerLabel, string Text)
        : this(Start, End, null, SpeakerLabel, Text)
    {
    }
}
