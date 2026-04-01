using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal enum ProcessingQueueRunState
{
    Idle = 0,
    Queued = 1,
    Processing = 2,
    Paused = 3,
}

internal enum ProcessingQueuePauseReason
{
    None = 0,
    LiveRecordingResponsiveMode = 1,
}

internal sealed record ProcessingQueueStatusSnapshot(
    ProcessingQueueRunState RunState,
    ProcessingQueuePauseReason PauseReason,
    int QueuedCount,
    int TotalRemainingCount,
    string? CurrentManifestPath,
    string? CurrentTitle,
    MeetingPlatform? CurrentPlatform,
    string? CurrentStageName,
    StageExecutionState? CurrentStageState,
    DateTimeOffset? CurrentStageUpdatedAtUtc,
    DateTimeOffset? CurrentItemStartedAtUtc,
    TimeSpan? CurrentItemEstimatedRemaining,
    TimeSpan? OverallEstimatedRemaining,
    DateTimeOffset LastUpdatedAtUtc);
