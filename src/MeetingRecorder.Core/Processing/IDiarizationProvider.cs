using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Processing;

public interface IDiarizationProvider
{
    Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken);
}
