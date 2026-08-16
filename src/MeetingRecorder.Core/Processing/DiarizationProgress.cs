namespace MeetingRecorder.Core.Processing;

/// <summary>Safe, content-free progress emitted by local speaker labeling.</summary>
public sealed record DiarizationProgress(
    string Phase,
    string? ExecutionProvider,
    int Attempt,
    int? AttemptCount,
    TimeSpan? InputDuration,
    long? InputBytes,
    long? WorkingSetBytes,
    string? Detail,
    DateTimeOffset UpdatedAtUtc);

public interface IDiarizationProgressSource
{
    event Action<DiarizationProgress>? ProgressChanged;
}
