using MeetingRecorder.Core.Configuration;
using System.Collections.ObjectModel;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Pure projection and editor-patch layer for outcome-level settings modes.
/// It never reads files, saves configuration, or inspects secret storage.
/// </summary>
public sealed class SettingsPresetService
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> RecordingMappings =
    [
        Values(
            (nameof(AppConfig.AutoDetectEnabled), true),
            (nameof(AppConfig.CalendarTitleFallbackEnabled), false),
            (nameof(AppConfig.MeetingAttendeeEnrichmentEnabled), true),
            (nameof(AppConfig.MeetingStopTimeoutSeconds), 30),
            (nameof(AppConfig.AutoDetectAudioPeakThreshold), 0.02d)),
        Values(
            (nameof(AppConfig.AutoDetectEnabled), false),
            (nameof(AppConfig.CalendarTitleFallbackEnabled), false),
            (nameof(AppConfig.MeetingAttendeeEnrichmentEnabled), false),
            (nameof(AppConfig.MeetingStopTimeoutSeconds), 30),
            (nameof(AppConfig.AutoDetectAudioPeakThreshold), 0.02d)),
    ];

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> ProcessingMappings =
    [
        Values(
            (nameof(AppConfig.BackgroundProcessingMode), BackgroundProcessingMode.Responsive),
            (nameof(AppConfig.BackgroundSpeakerLabelingMode), BackgroundSpeakerLabelingMode.Deferred),
            (nameof(AppConfig.InitialProcessingStrategy), InitialProcessingStrategy.ConfiguredStages)),
        Values(
            (nameof(AppConfig.BackgroundProcessingMode), BackgroundProcessingMode.Responsive),
            (nameof(AppConfig.BackgroundSpeakerLabelingMode), BackgroundSpeakerLabelingMode.Deferred),
            (nameof(AppConfig.InitialProcessingStrategy), InitialProcessingStrategy.TranscriptFirst)),
        Values(
            (nameof(AppConfig.BackgroundProcessingMode), BackgroundProcessingMode.MaximumThroughput),
            (nameof(AppConfig.BackgroundSpeakerLabelingMode), BackgroundSpeakerLabelingMode.Throttled),
            (nameof(AppConfig.InitialProcessingStrategy), InitialProcessingStrategy.ConfiguredStages)),
    ];

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> SummaryMappings =
    [
        Values(
            (nameof(AppConfig.SummaryGenerationMode), MeetingSummaryGenerationMode.Disabled),
            (nameof(AppConfig.SummaryProviderPreference), MeetingSummaryProviderPreference.LocalThenOpenAi)),
        Values(
            (nameof(AppConfig.SummaryGenerationMode), MeetingSummaryGenerationMode.Enabled),
            (nameof(AppConfig.SummaryProviderPreference), MeetingSummaryProviderPreference.LocalOnly)),
        Values(
            (nameof(AppConfig.SummaryGenerationMode), MeetingSummaryGenerationMode.Enabled),
            (nameof(AppConfig.SummaryProviderPreference), MeetingSummaryProviderPreference.LocalThenOpenAi)),
        Values(
            (nameof(AppConfig.SummaryGenerationMode), MeetingSummaryGenerationMode.Enabled),
            (nameof(AppConfig.SummaryProviderPreference), MeetingSummaryProviderPreference.OpenAiOnly)),
    ];

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> UpdateMappings =
    [
        Values(
            (nameof(AppConfig.UpdateCheckEnabled), true),
            (nameof(AppConfig.AutoInstallUpdatesEnabled), true)),
        Values(
            (nameof(AppConfig.UpdateCheckEnabled), true),
            (nameof(AppConfig.AutoInstallUpdatesEnabled), false)),
        Values(
            (nameof(AppConfig.UpdateCheckEnabled), false),
            (nameof(AppConfig.AutoInstallUpdatesEnabled), false)),
    ];

    private static readonly IReadOnlyDictionary<SettingsPresetCategory, IReadOnlyList<SettingsPresetOwnedField>> Ownership =
        new ReadOnlyDictionary<SettingsPresetCategory, IReadOnlyList<SettingsPresetOwnedField>>(
            new Dictionary<SettingsPresetCategory, IReadOnlyList<SettingsPresetOwnedField>>
            {
                [SettingsPresetCategory.Recording] =
                [
                    new(nameof(AppConfig.AutoDetectEnabled), "Automatic meeting detection"),
                    new(nameof(AppConfig.CalendarTitleFallbackEnabled), "Calendar title fallback"),
                    new(nameof(AppConfig.MeetingAttendeeEnrichmentEnabled), "Attendee enrichment"),
                    new(nameof(AppConfig.MeetingStopTimeoutSeconds), "Automatic stop timing"),
                    new(nameof(AppConfig.AutoDetectAudioPeakThreshold), "Detection sensitivity"),
                ],
                [SettingsPresetCategory.Processing] =
                [
                    new(nameof(AppConfig.BackgroundProcessingMode), "Background processing budget"),
                    new(nameof(AppConfig.BackgroundSpeakerLabelingMode), "Speaker-labeling timing"),
                    new(nameof(AppConfig.InitialProcessingStrategy), "Transcript-first publishing"),
                ],
                [SettingsPresetCategory.Summaries] =
                [
                    new(nameof(AppConfig.SummaryGenerationMode), "Summary enablement"),
                    new(nameof(AppConfig.SummaryProviderPreference), "Local or hosted summary routing"),
                ],
                [SettingsPresetCategory.Updates] =
                [
                    new(nameof(AppConfig.UpdateCheckEnabled), "Automatic update checks"),
                    new(nameof(AppConfig.AutoInstallUpdatesEnabled), "Idle update installation preference"),
                ],
            });

    public IReadOnlyDictionary<SettingsPresetCategory, IReadOnlyList<SettingsPresetOwnedField>> GetOwnershipMap()
    {
        return Ownership;
    }

    public SettingsPresetProjections Project(
        AppConfig config,
        SettingsPresetAvailability? availability = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var effectiveAvailability = availability ?? new SettingsPresetAvailability();
        return new SettingsPresetProjections(
            ProjectRecording(config),
            ProjectProcessing(config),
            ProjectSummaries(config, effectiveAvailability),
            ProjectUpdates(config));
    }

    public SettingsPresetApplyResult ApplyPreset(
        AppConfig editorConfig,
        SettingsPresetSelection selection,
        SettingsPresetAvailability? availability = null)
    {
        ArgumentNullException.ThrowIfNull(editorConfig);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.SelectedCategoryCount != 1)
        {
            throw new ArgumentException("Select exactly one settings preset category.", nameof(selection));
        }

        var effectiveAvailability = availability ?? new SettingsPresetAvailability();
        if (selection.Recording is { } recording)
        {
            return ApplyRecording(editorConfig, recording);
        }

        if (selection.Processing is { } processing)
        {
            return ApplyProcessing(editorConfig, processing);
        }

        if (selection.Summaries is { } summaries)
        {
            return ApplySummaries(editorConfig, summaries, effectiveAvailability);
        }

        return ApplyUpdates(editorConfig, selection.Updates!.Value);
    }

    private static SettingsPresetProjection<RecordingAssistanceMode> ProjectRecording(AppConfig config)
    {
        var mode = MatchesRecommendedRecording(config)
            ? RecordingAssistanceMode.Recommended
            : MatchesManualRecording(config)
                ? RecordingAssistanceMode.ManualOnly
                : RecordingAssistanceMode.Custom;

        return new SettingsPresetProjection<RecordingAssistanceMode>(
            mode,
            Ownership[SettingsPresetCategory.Recording],
            SettingsPresetSetupState.Ready,
            GetCustomReasons(
                mode == RecordingAssistanceMode.Custom,
                config,
                Ownership[SettingsPresetCategory.Recording],
                "Current value differs from the closest supported recording mode.",
                RecordingMappings),
            "Ready. Changes apply to the next recording after Save Changes.",
            new SettingsPresetControlSummary(
                "Recording assistance",
                [
                    "Automatic meeting detection",
                    "Local title and attendee assistance",
                    "Automatic stop timing",
                ],
                "Settings > Advanced",
                "Next recording"));
    }

    private static SettingsPresetProjection<ProcessingExperienceMode> ProjectProcessing(AppConfig config)
    {
        var mode = MatchesResponsiveProcessing(config)
            ? ProcessingExperienceMode.Responsive
            : MatchesTranscriptFirstProcessing(config)
                ? ProcessingExperienceMode.TranscriptFirst
                : MatchesFasterBacklogProcessing(config)
                    ? ProcessingExperienceMode.FasterBacklog
                    : ProcessingExperienceMode.Custom;

        return new SettingsPresetProjection<ProcessingExperienceMode>(
            mode,
            Ownership[SettingsPresetCategory.Processing],
            SettingsPresetSetupState.Ready,
            GetCustomReasons(
                mode == ProcessingExperienceMode.Custom,
                config,
                Ownership[SettingsPresetCategory.Processing],
                "Current value differs from the closest supported processing mode.",
                ProcessingMappings),
            "Ready. Changes apply to the next job after Save Changes.",
            new SettingsPresetControlSummary(
                "Processing experience",
                [
                    "Background processing budget",
                    "Transcript-first publishing",
                    "Speaker-labeling timing",
                ],
                "Settings > Advanced",
                "Next job"));
    }

    private static SettingsPresetProjection<SummaryExperienceMode> ProjectSummaries(
        AppConfig config,
        SettingsPresetAvailability availability)
    {
        var mode = MatchesSummaryOff(config)
            ? SummaryExperienceMode.Off
            : MatchesLocalOnlySummary(config)
                ? SummaryExperienceMode.LocalOnly
                : MatchesLocalWithHostedFallbackSummary(config)
                    ? SummaryExperienceMode.LocalWithHostedFallback
                    : MatchesHostedOnlySummary(config)
                        ? SummaryExperienceMode.HostedOnly
                        : SummaryExperienceMode.Custom;
        var needsProviderSetup =
            mode is SummaryExperienceMode.LocalWithHostedFallback or SummaryExperienceMode.HostedOnly &&
            !availability.IsHostedSummaryReady;

        return new SettingsPresetProjection<SummaryExperienceMode>(
            mode,
            Ownership[SettingsPresetCategory.Summaries],
            needsProviderSetup ? SettingsPresetSetupState.NeedsProviderSetup : SettingsPresetSetupState.Ready,
            GetCustomReasons(
                mode == SummaryExperienceMode.Custom,
                config,
                Ownership[SettingsPresetCategory.Summaries],
                "Current value differs from the closest supported summary mode.",
                SummaryMappings),
            needsProviderSetup
                ? "Needs provider setup. No transcript is sent until explicit hosted consent and credentials already exist."
                : "Ready. Changes apply to the next job after Save Changes.",
            new SettingsPresetControlSummary(
                "Meeting summaries",
                [
                    "Summary enablement",
                    "Local-only or hosted provider routing",
                ],
                "Settings > Advanced",
                "Next job"));
    }

    private static SettingsPresetProjection<UpdateExperienceMode> ProjectUpdates(AppConfig config)
    {
        var mode = MatchesAutomaticWhenIdleUpdates(config)
            ? UpdateExperienceMode.AutomaticWhenIdle
            : MatchesNotifyOnlyUpdates(config)
                ? UpdateExperienceMode.NotifyOnly
                : MatchesManualOnlyUpdates(config)
                    ? UpdateExperienceMode.ManualOnly
                    : UpdateExperienceMode.Custom;

        return new SettingsPresetProjection<UpdateExperienceMode>(
            mode,
            Ownership[SettingsPresetCategory.Updates],
            SettingsPresetSetupState.Ready,
            GetCustomReasons(
                mode == UpdateExperienceMode.Custom,
                config,
                Ownership[SettingsPresetCategory.Updates],
                "Current value differs from the closest supported update mode.",
                UpdateMappings),
            "Ready. Changes apply to future update checks after Save Changes.",
            new SettingsPresetControlSummary(
                "Update experience",
                [
                    "Automatic update checks",
                    "Idle update installation preference",
                ],
                "Settings > Updates",
                "Future update checks"));
    }

    private static SettingsPresetApplyResult ApplyRecording(AppConfig config, RecordingAssistanceMode mode)
    {
        var patched = mode switch
        {
            RecordingAssistanceMode.Recommended => config with
            {
                AutoDetectEnabled = true,
                CalendarTitleFallbackEnabled = false,
                MeetingAttendeeEnrichmentEnabled = true,
                MeetingStopTimeoutSeconds = 30,
                AutoDetectAudioPeakThreshold = 0.02d,
            },
            RecordingAssistanceMode.ManualOnly => config with
            {
                AutoDetectEnabled = false,
                CalendarTitleFallbackEnabled = false,
                MeetingAttendeeEnrichmentEnabled = false,
                MeetingStopTimeoutSeconds = 30,
                AutoDetectAudioPeakThreshold = 0.02d,
            },
            RecordingAssistanceMode.Custom => throw CreateCustomSelectionException(),
            _ => throw CreateUnknownSelectionException(nameof(mode)),
        };

        return Applied(config, patched, Ownership[SettingsPresetCategory.Recording], "Recording mode is pending Save Changes.");
    }

    private static SettingsPresetApplyResult ApplyProcessing(AppConfig config, ProcessingExperienceMode mode)
    {
        var patched = mode switch
        {
            ProcessingExperienceMode.Responsive => config with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            },
            ProcessingExperienceMode.TranscriptFirst => config with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                InitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
            },
            ProcessingExperienceMode.FasterBacklog => config with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.MaximumThroughput,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Throttled,
                InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            },
            ProcessingExperienceMode.Custom => throw CreateCustomSelectionException(),
            _ => throw CreateUnknownSelectionException(nameof(mode)),
        };

        return Applied(
            config,
            patched,
            Ownership[SettingsPresetCategory.Processing],
            "Processing mode is pending Save Changes. Faster Backlog uses only current safe worker settings; it does not enable GPU or promise extra capacity.");
    }

    private static SettingsPresetApplyResult ApplySummaries(
        AppConfig config,
        SummaryExperienceMode mode,
        SettingsPresetAvailability availability)
    {
        if (mode is SummaryExperienceMode.LocalWithHostedFallback or SummaryExperienceMode.HostedOnly &&
            !availability.IsHostedSummaryReady)
        {
            return new SettingsPresetApplyResult(
                config,
                false,
                "Needs provider setup. No transcript is sent and no provider preference changes until explicit hosted consent and credentials already exist.",
                Array.Empty<string>());
        }

        var patched = mode switch
        {
            SummaryExperienceMode.Off => config with
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
            },
            SummaryExperienceMode.LocalOnly => config with
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalOnly,
            },
            SummaryExperienceMode.LocalWithHostedFallback => config with
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
            },
            SummaryExperienceMode.HostedOnly => config with
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
            },
            SummaryExperienceMode.Custom => throw CreateCustomSelectionException(),
            _ => throw CreateUnknownSelectionException(nameof(mode)),
        };

        return Applied(config, patched, Ownership[SettingsPresetCategory.Summaries], "Summary mode is pending Save Changes.");
    }

    private static SettingsPresetApplyResult ApplyUpdates(AppConfig config, UpdateExperienceMode mode)
    {
        var patched = mode switch
        {
            UpdateExperienceMode.AutomaticWhenIdle => config with
            {
                UpdateCheckEnabled = true,
                AutoInstallUpdatesEnabled = true,
            },
            UpdateExperienceMode.NotifyOnly => config with
            {
                UpdateCheckEnabled = true,
                AutoInstallUpdatesEnabled = false,
            },
            UpdateExperienceMode.ManualOnly => config with
            {
                UpdateCheckEnabled = false,
                AutoInstallUpdatesEnabled = false,
            },
            UpdateExperienceMode.Custom => throw CreateCustomSelectionException(),
            _ => throw CreateUnknownSelectionException(nameof(mode)),
        };

        return Applied(config, patched, Ownership[SettingsPresetCategory.Updates], "Update mode is pending Save Changes. It never starts an immediate install.");
    }

    private static SettingsPresetApplyResult Applied(
        AppConfig original,
        AppConfig patched,
        IReadOnlyList<SettingsPresetOwnedField> ownedFields,
        string status)
    {
        var changedFields = ownedFields
            .Where(field => !Equals(
                typeof(AppConfig).GetProperty(field.FieldName)?.GetValue(original),
                typeof(AppConfig).GetProperty(field.FieldName)?.GetValue(patched)))
            .Select(field => field.FieldName)
            .ToArray();

        return new SettingsPresetApplyResult(patched, true, status, changedFields);
    }

    private static IReadOnlyList<SettingsPresetCustomReason> GetCustomReasons(
        bool isCustom,
        AppConfig config,
        IReadOnlyList<SettingsPresetOwnedField> ownedFields,
        string reason,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> mappings)
    {
        if (!isCustom)
        {
            return Array.Empty<SettingsPresetCustomReason>();
        }

        var values = ownedFields.ToDictionary(
            field => field.FieldName,
            field => typeof(AppConfig).GetProperty(field.FieldName)?.GetValue(config),
            StringComparer.Ordinal);
        var closestMapping = mappings
            .Select(mapping => new
            {
                Mapping = mapping,
                Differences = ownedFields
                    .Where(field => !Equals(values[field.FieldName], mapping[field.FieldName]))
                    .ToArray(),
            })
            .OrderBy(candidate => candidate.Differences.Length)
            .First();

        return closestMapping.Differences
            .Select(field => new SettingsPresetCustomReason(field.FieldName, reason))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> Values(params (string FieldName, object? Value)[] fields)
    {
        return new ReadOnlyDictionary<string, object?>(fields.ToDictionary(
            field => field.FieldName,
            field => field.Value,
            StringComparer.Ordinal));
    }

    private static bool MatchesRecommendedRecording(AppConfig config) =>
        config.AutoDetectEnabled &&
        !config.CalendarTitleFallbackEnabled &&
        config.MeetingAttendeeEnrichmentEnabled &&
        config.MeetingStopTimeoutSeconds == 30 &&
        config.AutoDetectAudioPeakThreshold == 0.02d;

    private static bool MatchesManualRecording(AppConfig config) =>
        !config.AutoDetectEnabled &&
        !config.CalendarTitleFallbackEnabled &&
        !config.MeetingAttendeeEnrichmentEnabled &&
        config.MeetingStopTimeoutSeconds == 30 &&
        config.AutoDetectAudioPeakThreshold == 0.02d;

    private static bool MatchesResponsiveProcessing(AppConfig config) =>
        config.BackgroundProcessingMode == BackgroundProcessingMode.Responsive &&
        config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
        config.InitialProcessingStrategy == InitialProcessingStrategy.ConfiguredStages;

    private static bool MatchesTranscriptFirstProcessing(AppConfig config) =>
        config.BackgroundProcessingMode == BackgroundProcessingMode.Responsive &&
        config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred &&
        config.InitialProcessingStrategy == InitialProcessingStrategy.TranscriptFirst;

    private static bool MatchesFasterBacklogProcessing(AppConfig config) =>
        config.BackgroundProcessingMode == BackgroundProcessingMode.MaximumThroughput &&
        config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Throttled &&
        config.InitialProcessingStrategy == InitialProcessingStrategy.ConfiguredStages;

    private static bool MatchesSummaryOff(AppConfig config) =>
        config.SummaryGenerationMode == MeetingSummaryGenerationMode.Disabled &&
        config.SummaryProviderPreference == MeetingSummaryProviderPreference.LocalThenOpenAi;

    private static bool MatchesLocalOnlySummary(AppConfig config) =>
        config.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
        config.SummaryProviderPreference == MeetingSummaryProviderPreference.LocalOnly;

    private static bool MatchesLocalWithHostedFallbackSummary(AppConfig config) =>
        config.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
        config.SummaryProviderPreference == MeetingSummaryProviderPreference.LocalThenOpenAi;

    private static bool MatchesHostedOnlySummary(AppConfig config) =>
        config.SummaryGenerationMode == MeetingSummaryGenerationMode.Enabled &&
        config.SummaryProviderPreference == MeetingSummaryProviderPreference.OpenAiOnly;

    private static bool MatchesAutomaticWhenIdleUpdates(AppConfig config) =>
        config.UpdateCheckEnabled && config.AutoInstallUpdatesEnabled;

    private static bool MatchesNotifyOnlyUpdates(AppConfig config) =>
        config.UpdateCheckEnabled && !config.AutoInstallUpdatesEnabled;

    private static bool MatchesManualOnlyUpdates(AppConfig config) =>
        !config.UpdateCheckEnabled && !config.AutoInstallUpdatesEnabled;

    private static ArgumentException CreateCustomSelectionException() =>
        new("Custom is inferred from exact field values and cannot be selected as a preset.");

    private static ArgumentOutOfRangeException CreateUnknownSelectionException(string name) =>
        new(name, "Unknown settings preset mode.");
}
