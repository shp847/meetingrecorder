namespace MeetingRecorder.Core.Domain;

public enum StageExecutionState
{
    NotStarted = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Skipped = 5,
}
