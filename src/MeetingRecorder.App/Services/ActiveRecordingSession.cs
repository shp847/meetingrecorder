using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.App.Services;

internal sealed class ActiveRecordingSession
{
    public required MeetingSessionManifest Manifest { get; set; }

    public required string ManifestPath { get; init; }

    public required ChunkedWaveRecorder LoopbackRecorder { get; set; }

    public required LoopbackCaptureSelection ActiveLoopbackSelection { get; set; }

    public required DateTimeOffset ActiveLoopbackSegmentStartedAtUtc { get; set; }

    public List<LoopbackCaptureSegment> CompletedLoopbackCaptureSegments { get; init; } = [];

    public List<CaptureTimelineEntry> CaptureTimelineEntries { get; init; } = [];

    public LoopbackCaptureSelection? PendingLoopbackSelection { get; set; }

    public int PendingLoopbackSelectionStableCount { get; set; }

    public int NextLoopbackSegmentSequence { get; set; } = 1;

    public ChunkedWaveRecorder? MicrophoneRecorder { get; set; }

    public MicrophoneCaptureSelection? ActiveMicrophoneSelection { get; set; }

    public DateTimeOffset? ActiveMicrophoneSegmentStartedAtUtc { get; set; }

    public List<MicrophoneCaptureSegment> CompletedMicrophoneCaptureSegments { get; init; } = [];

    public MicrophoneCaptureSelection? PendingMicrophoneSelection { get; set; }

    public int PendingMicrophoneSelectionStableCount { get; set; }

    public int NextMicrophoneSegmentSequence { get; set; } = 1;

    public required bool AutoStarted { get; init; }

    public bool MeetingLifecycleManaged { get; set; }
}
