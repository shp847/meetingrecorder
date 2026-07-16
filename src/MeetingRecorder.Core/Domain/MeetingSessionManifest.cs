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

    public IReadOnlyList<LoopbackCaptureSegment> LoopbackCaptureSegments { get; init; } = Array.Empty<LoopbackCaptureSegment>();

    public IReadOnlyList<string> MicrophoneChunkPaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<MicrophoneCaptureSegment> MicrophoneCaptureSegments { get; init; } = Array.Empty<MicrophoneCaptureSegment>();

    public IReadOnlyList<CaptureTimelineEntry> CaptureTimeline { get; init; } = Array.Empty<CaptureTimelineEntry>();

    public string? MergedAudioPath { get; init; }

    public ImportedSourceAudioInfo? ImportedSourceAudio { get; init; }

    public string? ProjectName { get; init; }

    public IReadOnlyList<string> KeyAttendees { get; init; } = Array.Empty<string>();

    public DetectedAudioSource? DetectedAudioSource { get; init; }

    public IReadOnlyList<MeetingAttendee> Attendees { get; init; } = Array.Empty<MeetingAttendee>();

    public MeetingProcessingOverrides? ProcessingOverrides { get; init; }

    public MeetingProcessingMetadata? ProcessingMetadata { get; init; }

    public ProcessingStageStatus TranscriptionStatus { get; init; } =
        new("transcription", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public ProcessingStageStatus DiarizationStatus { get; init; } =
        new("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public ProcessingStageStatus SummarizationStatus { get; init; } =
        new("summarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public MeetingSummary? Summary { get; init; }

    public ProcessingStageStatus PublishStatus { get; init; } =
        new("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null);

    public string? ErrorSummary { get; init; }
}

public enum ExternalAudioImportMethod
{
    WatchedFolder = 0,
    FilePicker = 1,
    DragDrop = 2,
}

public sealed record ImportedSourceAudioInfo
{
    public ImportedSourceAudioInfo()
    {
    }

    public ImportedSourceAudioInfo(
        string originalPath,
        long sourceSizeBytes,
        DateTimeOffset sourceLastWriteUtc)
        : this(
            originalPath,
            sourceSizeBytes,
            sourceLastWriteUtc,
            Path.GetFileName(originalPath),
            ExternalAudioImportMethod.WatchedFolder,
            probedDuration: null,
            sourceRetained: true)
    {
    }

    public ImportedSourceAudioInfo(
        string originalPath,
        long sourceSizeBytes,
        DateTimeOffset sourceLastWriteUtc,
        string? sourceDisplayName,
        ExternalAudioImportMethod importMethod,
        TimeSpan? probedDuration,
        bool sourceRetained)
    {
        OriginalPath = originalPath;
        SourceSizeBytes = sourceSizeBytes;
        SourceLastWriteUtc = sourceLastWriteUtc;
        SourceDisplayName = sourceDisplayName ?? string.Empty;
        ImportMethod = importMethod;
        ProbedDuration = probedDuration;
        SourceRetained = sourceRetained;
    }

    public string OriginalPath { get; init; } = string.Empty;

    public long SourceSizeBytes { get; init; }

    public DateTimeOffset SourceLastWriteUtc { get; init; }

    public string SourceDisplayName { get; init; } = string.Empty;

    public ExternalAudioImportMethod ImportMethod { get; init; } = ExternalAudioImportMethod.WatchedFolder;

    public TimeSpan? ProbedDuration { get; init; }

    public bool SourceRetained { get; init; } = true;
}

public sealed record MicrophoneCaptureSegment(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    IReadOnlyList<string> ChunkPaths);
