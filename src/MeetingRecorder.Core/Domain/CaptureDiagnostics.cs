using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureTimelineEventKind>))]
public enum CaptureTimelineEventKind
{
    Started = 0,
    Swapped = 1,
    Fallback = 2,
    NoChange = 3,
    SwapFailed = 4,
    Stopped = 5,
}

public sealed record LoopbackCaptureSegment(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    IReadOnlyList<string> ChunkPaths,
    string EndpointDeviceId,
    string EndpointName,
    string EndpointRole);

public sealed record CaptureTimelineEntry(
    DateTimeOffset OccurredAtUtc,
    CaptureTimelineEventKind Kind,
    string Summary,
    string? Detail);
