namespace MeetingRecorder.Core.Configuration;

public sealed record AppConfig
{
    public string AudioOutputDir { get; init; } = string.Empty;

    public string TranscriptOutputDir { get; init; } = string.Empty;

    public string WorkDir { get; init; } = string.Empty;

    public string ModelCacheDir { get; init; } = string.Empty;

    public string TranscriptionModelPath { get; init; } = string.Empty;

    public string DiarizationAssetPath { get; init; } = string.Empty;

    public bool MicCaptureEnabled { get; init; } = true;

    public bool LaunchOnLoginEnabled { get; init; }

    public bool AutoDetectEnabled { get; init; } = true;

    public bool CalendarTitleFallbackEnabled { get; init; }

    public bool UpdateCheckEnabled { get; init; } = true;

    public bool AutoInstallUpdatesEnabled { get; init; } = true;

    public string UpdateFeedUrl { get; init; } = string.Empty;

    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    public string InstalledReleaseVersion { get; init; } = string.Empty;

    public DateTimeOffset? InstalledReleasePublishedAtUtc { get; init; }

    public long? InstalledReleaseAssetSizeBytes { get; init; }

    public string PendingUpdateZipPath { get; init; } = string.Empty;

    public string PendingUpdateVersion { get; init; } = string.Empty;

    public DateTimeOffset? PendingUpdatePublishedAtUtc { get; init; }

    public long? PendingUpdateAssetSizeBytes { get; init; }

    public double AutoDetectAudioPeakThreshold { get; init; } = 0.02d;

    public int MeetingStopTimeoutSeconds { get; init; } = 30;
}
