using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;

namespace MeetingRecorder.ProcessingWorker;

/// <summary>
/// Keeps transcript publication independent from optional speaker labeling.
/// </summary>
internal sealed class DisabledDiarizationProvider : IDiarizationProvider
{
    public static DisabledDiarizationProvider Instance { get; } = new();

    private DisabledDiarizationProvider()
    {
    }

    public Task<DiarizationResult> ApplySpeakerLabelsAsync(
        string audioPath,
        IReadOnlyList<TranscriptSegment> transcriptSegments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiarizationResult(
            transcriptSegments,
            false,
            "Speaker labeling is disabled by local configuration."));
    }
}
