using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Processing;

public sealed record DiarizationResult(
    IReadOnlyList<TranscriptSegment> Segments,
    bool AppliedSpeakerLabels,
    string? Message);
