namespace MeetingRecorder.Core.Configuration;

public enum RecordingAssistanceMode
{
    Recommended = 0,
    ManualOnly = 1,
    Custom = 2,
}

public enum ProcessingExperienceMode
{
    Responsive = 0,
    TranscriptFirst = 1,
    FasterBacklog = 2,
    Custom = 3,
}

public enum SummaryExperienceMode
{
    Off = 0,
    LocalOnly = 1,
    LocalWithHostedFallback = 2,
    HostedOnly = 3,
    Custom = 4,
}

public enum UpdateExperienceMode
{
    AutomaticWhenIdle = 0,
    NotifyOnly = 1,
    ManualOnly = 2,
    Custom = 3,
}

public enum SettingsPresetCategory
{
    Recording = 0,
    Processing = 1,
    Summaries = 2,
    Updates = 3,
}

public enum SettingsPresetSetupState
{
    Ready = 0,
    NeedsProviderSetup = 1,
}

public sealed record SettingsPresetAvailability(
    bool HasHostedSummaryProviderConsent = false,
    bool HasHostedSummaryProviderCredentials = false)
{
    public bool IsHostedSummaryReady =>
        HasHostedSummaryProviderConsent && HasHostedSummaryProviderCredentials;
}

public sealed record SettingsPresetOwnedField(
    string FieldName,
    string Behavior);

public sealed record SettingsPresetCustomReason(
    string FieldName,
    string Reason);

public sealed record SettingsPresetControlSummary(
    string Title,
    IReadOnlyList<string> ControlledBehaviors,
    string AdvancedRoute,
    string AppliesWhen);

public sealed record SettingsPresetProjection<TMode>(
    TMode Mode,
    IReadOnlyList<SettingsPresetOwnedField> OwnedFields,
    SettingsPresetSetupState SetupState,
    IReadOnlyList<SettingsPresetCustomReason> CustomReasons,
    string Status,
    SettingsPresetControlSummary ControlSummary)
    where TMode : struct, Enum;

public sealed record SettingsPresetProjections(
    SettingsPresetProjection<RecordingAssistanceMode> Recording,
    SettingsPresetProjection<ProcessingExperienceMode> Processing,
    SettingsPresetProjection<SummaryExperienceMode> Summaries,
    SettingsPresetProjection<UpdateExperienceMode> Updates);

public sealed record SettingsPresetSelection
{
    public RecordingAssistanceMode? Recording { get; init; }

    public ProcessingExperienceMode? Processing { get; init; }

    public SummaryExperienceMode? Summaries { get; init; }

    public UpdateExperienceMode? Updates { get; init; }

    public int SelectedCategoryCount =>
        (Recording.HasValue ? 1 : 0) +
        (Processing.HasValue ? 1 : 0) +
        (Summaries.HasValue ? 1 : 0) +
        (Updates.HasValue ? 1 : 0);
}

public sealed record SettingsPresetApplyResult(
    AppConfig Config,
    bool Applied,
    string Status,
    IReadOnlyList<string> ChangedFields);
