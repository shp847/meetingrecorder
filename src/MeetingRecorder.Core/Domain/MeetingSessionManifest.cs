namespace MeetingRecorder.Core.Domain;

public sealed record MeetingSessionManifest
{
    public string SessionId { get; init; } = string.Empty;

    public MeetingPlatform Platform { get; init; }

    public string DetectedTitle { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public SessionState State { get; init; }

    public IReadOnlyList<DetectionSignal> DetectionEvidence { get; init; } = Array.Empty<DetectionSignal>();

    public IReadOnlyList<string> RawChunkPaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MicrophoneChunkPaths { get; init; } = Array.Empty<string>();

    public string? MergedAudioPath { get; init; }

    public ProcessingStageStatus TranscriptionStatus { get; init; } =
        new("transcription", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public ProcessingStageStatus DiarizationStatus { get; init; } =
        new("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public ProcessingStageStatus PublishStatus { get; init; } =
        new("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public string? ErrorSummary { get; init; }
}
