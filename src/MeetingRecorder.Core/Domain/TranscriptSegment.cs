namespace MeetingRecorder.Core.Domain;

public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string? SpeakerLabel,
    string Text);
