using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.App.Services;

internal sealed record AudioSourceSessionSnapshot(
    int ProcessId,
    string ProcessName,
    double PeakLevel,
    bool IsActive,
    bool IsSystemSounds,
    bool IsCurrentProcess,
    string? DisplayName,
    string? SessionIdentifier);

internal sealed record AudioSourceAttributionMatch(
    DetectedAudioSource Source,
    int? ProcessId,
    string? MatchedProcessName,
    double SessionPeakLevel);

internal sealed record AudioSourceAttributionSnapshot(
    string? DeviceName,
    double EndpointPeakLevel,
    bool IsEndpointActive,
    string? StatusDetail,
    IReadOnlyList<AudioSourceSessionSnapshot> Sessions,
    AudioSourceAttributionMatch? Match)
{
    public double PeakLevel => EndpointPeakLevel;

    public bool IsActive => IsEndpointActive;
}
