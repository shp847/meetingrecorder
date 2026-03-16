namespace MeetingRecorder.Core.Domain;

public sealed record ProcessingStageStatus(
    string StageName,
    StageExecutionState State,
    DateTimeOffset UpdatedAtUtc,
    string? Message);
