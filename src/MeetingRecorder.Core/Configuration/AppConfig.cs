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
    ClientProject = 4,
    Attendee = 5,
}

public enum InferenceAccelerationPreference
{
    Auto = 0,
    CpuOnly = 1,
}

public enum BackgroundProcessingMode
{
    Responsive = 0,
    Balanced = 1,
    FastestDrain = 2,
}

public enum BackgroundSpeakerLabelingMode
{
    Deferred = 0,
    Throttled = 1,
    Inline = 2,
}

public enum RushProcessingBehavior
{
    RunNextOnly = 0,
    RunNextIgnoreRecordingPause = 1,
}

public enum PreferredTeamsIntegrationMode
{
    Auto = 0,
    FallbackOnly = 1,
    ThirdPartyApi = 2,
    GraphCalendar = 3,
    GraphCalendarAndOnlineMeeting = 4,
}

public enum TeamsCapabilityStatus
{
    FallbackOnly = 0,
    ThirdPartyApiAvailableButControlOnly = 1,
    ThirdPartyApiUsable = 2,
    CalendarBacked = 3,
    CalendarAndOnlineMeeting = 4,
    BlockedByPolicyOrConsent = 5,
    SignedOut = 6,
}

public enum TeamsThirdPartyApiStatus
{
    Unavailable = 0,
    BlockedByTeamsPolicy = 1,
    ControlOnly = 2,
    ReadableStateAvailable = 3,
}

public enum TeamsPairingState
{
    Unknown = 0,
    NotPaired = 1,
    PairingAllowed = 2,
    Paired = 3,
    Blocked = 4,
}

public enum TeamsGraphCalendarStatus
{
    NotConfigured = 0,
    SignedOut = 1,
    BlockedByPolicyOrConsent = 2,
    Error = 3,
    Supported = 4,
}

public enum TeamsGraphOnlineMeetingStatus
{
    NotAttempted = 0,
    SignedOut = 1,
    BlockedByPolicyOrConsent = 2,
    Error = 3,
    NotAvailable = 4,
    Supported = 5,
}

public sealed record TeamsThirdPartyApiCapability
{
    public TeamsThirdPartyApiStatus Status { get; init; } = TeamsThirdPartyApiStatus.Unavailable;

    public bool ManageApiEnabled { get; init; }

    public TeamsPairingState PairingState { get; init; } = TeamsPairingState.Unknown;

    public bool SupportsReadableMeetingState { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}

public sealed record TeamsGraphCapability
{
    public TeamsGraphCalendarStatus CalendarStatus { get; init; } = TeamsGraphCalendarStatus.NotConfigured;

    public TeamsGraphOnlineMeetingStatus OnlineMeetingStatus { get; init; } = TeamsGraphOnlineMeetingStatus.NotAttempted;

    public bool CalendarSupported { get; init; }

    public bool OnlineMeetingSupported { get; init; }

    public string BlockReason { get; init; } = string.Empty;

    public bool PersistentAuthCacheEnabled { get; init; }

    public string AuthWarning { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}

public sealed record TeamsCapabilitySnapshot
{
    public TeamsCapabilityStatus Status { get; init; } = TeamsCapabilityStatus.FallbackOnly;

    public DateTimeOffset? LastProbeUtc { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool HeuristicBaselineReady { get; init; }

    public TeamsThirdPartyApiCapability ThirdPartyApi { get; init; } = new();

    public TeamsGraphCapability Graph { get; init; } = new();

    public bool ThirdPartyApiAvailable { get; init; }

    public bool ThirdPartyApiReadableStateSupported { get; init; }

    public bool GraphCalendarSupported { get; init; }

    public bool GraphOnlineMeetingSupported { get; init; }
}

public sealed record AppConfig
{
    public string AudioOutputDir { get; init; } = string.Empty;

    public string TranscriptOutputDir { get; init; } = string.Empty;

    public string WorkDir { get; init; } = string.Empty;

    public string ModelCacheDir { get; init; } = string.Empty;

    public string TranscriptionModelPath { get; init; } = string.Empty;

    public TranscriptionModelProfilePreference TranscriptionModelProfilePreference { get; init; } =
        TranscriptionModelProfilePreference.Standard;

    public string DiarizationAssetPath { get; init; } = string.Empty;

    public SpeakerLabelingModelProfilePreference SpeakerLabelingModelProfilePreference { get; init; } =
        SpeakerLabelingModelProfilePreference.Standard;

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

    public BackgroundProcessingMode BackgroundProcessingMode { get; init; } =
        BackgroundProcessingMode.Responsive;

    public BackgroundSpeakerLabelingMode BackgroundSpeakerLabelingMode { get; init; } =
        BackgroundSpeakerLabelingMode.Deferred;

    public RushProcessingRequest? RushProcessingRequest { get; init; }

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

    public PreferredTeamsIntegrationMode PreferredTeamsIntegrationMode { get; init; } =
        PreferredTeamsIntegrationMode.Auto;

    public string TeamsGraphTenantId { get; init; } = "organizations";

    public string TeamsGraphClientId { get; init; } = string.Empty;

    public TeamsCapabilitySnapshot TeamsCapabilitySnapshot { get; init; } = new();

    public MeetingsViewMode MeetingsViewMode { get; init; } = MeetingsViewMode.Grouped;

    public bool MeetingsGroupedViewMigrationApplied { get; init; }

    public MeetingsSortKey MeetingsSortKey { get; init; } = MeetingsSortKey.Started;

    public bool MeetingsSortDescending { get; init; } = true;

    public MeetingsGroupKey MeetingsGroupKey { get; init; } = MeetingsGroupKey.Week;

    public IReadOnlyList<DismissedMeetingRecommendation> DismissedMeetingRecommendations { get; init; } =
        Array.Empty<DismissedMeetingRecommendation>();
}

public sealed record RushProcessingRequest(
    string ManifestPath,
    RushProcessingBehavior Behavior,
    DateTimeOffset RequestedAtUtc);

public sealed record DismissedMeetingRecommendation(string Fingerprint, DateTimeOffset DismissedAtUtc);
