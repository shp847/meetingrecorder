namespace MeetingRecorder.Core.Configuration;

public sealed record AppConfig
{
    public string AudioOutputDir { get; init; } = string.Empty;

    public string TranscriptOutputDir { get; init; } = string.Empty;

    public string WorkDir { get; init; } = string.Empty;

    public string ModelCacheDir { get; init; } = string.Empty;

    public string TranscriptionModelPath { get; init; } = string.Empty;

    public string DiarizationAssetPath { get; init; } = string.Empty;

    public bool MicCaptureEnabled { get; init; }

    public bool AutoDetectEnabled { get; init; } = true;

    public double AutoDetectAudioPeakThreshold { get; init; } = 0.02d;

    public int MeetingStopTimeoutSeconds { get; init; } = 30;
}
