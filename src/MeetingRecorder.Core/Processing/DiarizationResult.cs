using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Processing;

public sealed record DiarizationResult(
    IReadOnlyList<TranscriptSegment> Segments,
    bool AppliedSpeakerLabels,
    string? Message,
    IReadOnlyList<SpeakerIdentity>? Speakers,
    IReadOnlyList<SpeakerTurn>? SpeakerTurns,
    DiarizationMetadata? Metadata,
    IReadOnlyList<SpeakerVoiceSample>? SpeakerVoiceSamples = null)
{
    public DiarizationResult(
        IReadOnlyList<TranscriptSegment> Segments,
        bool AppliedSpeakerLabels,
        string? Message)
        : this(
            Segments,
            AppliedSpeakerLabels,
            Message,
            null,
            null,
            null,
            null)
    {
    }
}
