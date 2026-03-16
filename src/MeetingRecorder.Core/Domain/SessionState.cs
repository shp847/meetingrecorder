namespace MeetingRecorder.Core.Domain;

public enum SessionState
{
    Idle = 0,
    Recording = 1,
    Finalizing = 2,
    Queued = 3,
    Processing = 4,
    Published = 5,
    Failed = 6,
}
