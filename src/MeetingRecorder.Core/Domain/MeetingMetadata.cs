using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<MeetingAttendeeSource>))]
public enum MeetingAttendeeSource
{
    Unknown = 0,
    OutlookCalendar = 1,
    TeamsLiveRoster = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<AudioSourceMatchKind>))]
public enum AudioSourceMatchKind
{
    EndpointFallback = 0,
    Process = 1,
    Window = 2,
    BrowserWindow = 3,
    BrowserTab = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter<AudioSourceConfidence>))]
public enum AudioSourceConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public sealed record MeetingAttendee(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sources")] IReadOnlyList<MeetingAttendeeSource> Sources);

public sealed record DetectedAudioSource(
    [property: JsonPropertyName("appName")] string AppName,
    [property: JsonPropertyName("windowTitle")] string? WindowTitle,
    [property: JsonPropertyName("browserTabTitle")] string? BrowserTabTitle,
    [property: JsonPropertyName("matchKind")] AudioSourceMatchKind MatchKind,
    [property: JsonPropertyName("confidence")] AudioSourceConfidence Confidence,
    [property: JsonPropertyName("observedAtUtc")] DateTimeOffset ObservedAtUtc);

public sealed record MeetingProcessingOverrides(
    string? TranscriptionModelPath,
    string? TranscriptionModelFileName);

public enum DiarizationAttributionMode
{
    SegmentOverlap = 0,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiarizationExecutionProvider>))]
public enum DiarizationExecutionProvider
{
    Cpu = 0,
    Directml = 1,
}

public sealed record SpeakerIdentity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("isUserEdited")] bool IsUserEdited);

public sealed record SpeakerTurn(
    [property: JsonPropertyName("speakerId")] string SpeakerId,
    [property: JsonPropertyName("start")] TimeSpan Start,
    [property: JsonPropertyName("end")] TimeSpan End);

public sealed record DiarizationMetadata
{
    [JsonConstructor]
    public DiarizationMetadata(
        string provider,
        string segmentationModelFileName,
        string embeddingModelFileName,
        string bundleVersion,
        DiarizationAttributionMode attributionMode,
        DiarizationExecutionProvider executionProvider,
        bool gpuAccelerationRequested,
        bool gpuAccelerationAvailable,
        string? diagnosticMessage)
    {
        Provider = provider;
        SegmentationModelFileName = segmentationModelFileName;
        EmbeddingModelFileName = embeddingModelFileName;
        BundleVersion = bundleVersion;
        AttributionMode = attributionMode;
        ExecutionProvider = executionProvider;
        GpuAccelerationRequested = gpuAccelerationRequested;
        GpuAccelerationAvailable = gpuAccelerationAvailable;
        DiagnosticMessage = diagnosticMessage;
    }

    [JsonPropertyName("provider")]
    public string Provider { get; init; }

    [JsonPropertyName("segmentationModelFileName")]
    public string SegmentationModelFileName { get; init; }

    [JsonPropertyName("embeddingModelFileName")]
    public string EmbeddingModelFileName { get; init; }

    [JsonPropertyName("bundleVersion")]
    public string BundleVersion { get; init; }

    [JsonPropertyName("attributionMode")]
    public DiarizationAttributionMode AttributionMode { get; init; }

    [JsonPropertyName("executionProvider")]
    public DiarizationExecutionProvider ExecutionProvider { get; init; }

    [JsonPropertyName("gpuAccelerationRequested")]
    public bool GpuAccelerationRequested { get; init; }

    [JsonPropertyName("gpuAccelerationAvailable")]
    public bool GpuAccelerationAvailable { get; init; }

    [JsonPropertyName("diagnosticMessage")]
    public string? DiagnosticMessage { get; init; }
}

public sealed record MeetingProcessingMetadata(
    string? TranscriptionModelFileName,
    bool HasSpeakerLabels,
    IReadOnlyList<SpeakerIdentity>? Speakers = null,
    IReadOnlyList<SpeakerTurn>? SpeakerTurns = null,
    DiarizationMetadata? DiarizationMetadata = null);
