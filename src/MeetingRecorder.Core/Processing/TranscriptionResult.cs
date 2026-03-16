using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Processing;

public sealed record TranscriptionResult(
    IReadOnlyList<TranscriptSegment> Segments,
    string Language,
    string? Message);
