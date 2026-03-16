using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Processing;

public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        CancellationToken cancellationToken);
}
