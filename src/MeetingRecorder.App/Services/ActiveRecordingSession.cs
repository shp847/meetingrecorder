using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.App.Services;

internal sealed class ActiveRecordingSession
{
    public required MeetingSessionManifest Manifest { get; set; }

    public required string ManifestPath { get; init; }

    public required ChunkedWaveRecorder LoopbackRecorder { get; init; }

    public ChunkedWaveRecorder? MicrophoneRecorder { get; set; }

    public DateTimeOffset? ActiveMicrophoneSegmentStartedAtUtc { get; set; }

    public List<MicrophoneCaptureSegment> CompletedMicrophoneCaptureSegments { get; init; } = [];

    public required bool AutoStarted { get; init; }

    public bool MeetingLifecycleManaged { get; set; }
}
