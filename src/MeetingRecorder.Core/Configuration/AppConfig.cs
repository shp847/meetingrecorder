namespace MeetingRecorder.Core.Configuration;

public enum MeetingsViewMode
{
    Table = 0,
    Grouped = 1,
}

public enum MeetingsSortKey
{
    Started = 0,
    Title = 1,
    Duration = 2,
    Platform = 3,
}

public enum MeetingsGroupKey
{
    Week = 0,
    Month = 1,
    Platform = 2,
    Status = 3,
}

public enum InferenceAccelerationPreference
{
    Auto = 0,
    CpuOnly = 1,
}

public sealed record AppConfig
{
    public string AudioOutputDir { get; init; } = string.Empty;

    public string TranscriptOutputDir { get; init; } = string.Empty;

    public string WorkDir { get; init; } = string.Empty;

    public string ModelCacheDir { get; init; } = string.Empty;

    public string TranscriptionModelPath { get; init; } = string.Empty;

    public string DiarizationAssetPath { get; init; } = string.Empty;

    public InferenceAccelerationPreference DiarizationAccelerationPreference { get; init; } =
        InferenceAccelerationPreference.Auto;

    public bool MicCaptureEnabled { get; init; } = true;

    public bool LaunchOnLoginEnabled { get; init; }

    public bool AutoDetectEnabled { get; init; }

    public bool AutoDetectSecurityPromptMigrationApplied { get; init; }

    public bool CalendarTitleFallbackEnabled { get; init; }

    public bool MeetingAttendeeEnrichmentEnabled { get; init; } = true;

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

    public MeetingsViewMode MeetingsViewMode { get; init; } = MeetingsViewMode.Grouped;

    public bool MeetingsGroupedViewMigrationApplied { get; init; }

    public MeetingsSortKey MeetingsSortKey { get; init; } = MeetingsSortKey.Started;

    public bool MeetingsSortDescending { get; init; } = true;

    public MeetingsGroupKey MeetingsGroupKey { get; init; } = MeetingsGroupKey.Week;

    public IReadOnlyList<DismissedMeetingRecommendation> DismissedMeetingRecommendations { get; init; } =
        Array.Empty<DismissedMeetingRecommendation>();
}

public sealed record DismissedMeetingRecommendation(string Fingerprint, DateTimeOffset DismissedAtUtc);
