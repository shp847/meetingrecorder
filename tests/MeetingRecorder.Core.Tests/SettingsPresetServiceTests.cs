using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SettingsPresetServiceTests
{
    private readonly SettingsPresetService _service = new();

    [Fact]
    public void Project_Recognizes_All_Canonical_Modes()
    {
        var config = new AppConfig
        {
            AutoDetectEnabled = true,
            CalendarTitleFallbackEnabled = false,
            MeetingAttendeeEnrichmentEnabled = true,
            MeetingStopTimeoutSeconds = 30,
            AutoDetectAudioPeakThreshold = 0.02,
            BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
            InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            SummaryProviderPreference = MeetingSummaryProviderPreference.LocalOnly,
            UpdateCheckEnabled = true,
            AutoInstallUpdatesEnabled = false,
        };

        var projections = _service.Project(config);

        Assert.Equal(RecordingAssistanceMode.Recommended, projections.Recording.Mode);
        Assert.Equal(ProcessingExperienceMode.Responsive, projections.Processing.Mode);
        Assert.Equal(SummaryExperienceMode.LocalOnly, projections.Summaries.Mode);
        Assert.Equal(UpdateExperienceMode.NotifyOnly, projections.Updates.Mode);
        Assert.Empty(projections.Recording.CustomReasons);
    }

    [Fact]
    public void Project_Reports_Custom_Only_For_The_Category_With_A_Different_Owned_Field()
    {
        var config = RecommendedConfig() with
        {
            MeetingStopTimeoutSeconds = 45,
            MicCaptureEnabled = false,
            AudioOutputDir = "D:\\private-audio",
        };

        var projections = _service.Project(config);

        Assert.Equal(RecordingAssistanceMode.Custom, projections.Recording.Mode);
        Assert.Contains(projections.Recording.CustomReasons, reason => reason.FieldName == nameof(AppConfig.MeetingStopTimeoutSeconds));
        Assert.Equal(ProcessingExperienceMode.Responsive, projections.Processing.Mode);
        Assert.Equal(SummaryExperienceMode.Off, projections.Summaries.Mode);
        Assert.Equal(UpdateExperienceMode.NotifyOnly, projections.Updates.Mode);
        Assert.DoesNotContain(projections.Recording.CustomReasons, reason => reason.FieldName == nameof(AppConfig.MicCaptureEnabled));
    }

    [Fact]
    public void Project_Treats_Unknown_Enum_Values_As_Custom_Without_Throwing()
    {
        var config = RecommendedConfig() with
        {
            BackgroundProcessingMode = (BackgroundProcessingMode)999,
            SummaryProviderPreference = (MeetingSummaryProviderPreference)999,
        };

        var projections = _service.Project(config);

        Assert.Equal(ProcessingExperienceMode.Custom, projections.Processing.Mode);
        Assert.Equal(SummaryExperienceMode.Custom, projections.Summaries.Mode);
        Assert.NotEmpty(projections.Processing.CustomReasons);
        Assert.NotEmpty(projections.Summaries.CustomReasons);
    }

    [Fact]
    public void Project_Hosted_Summary_Modes_Require_Consent_And_Credentials()
    {
        var config = RecommendedConfig() with
        {
            SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
        };

        var blocked = _service.Project(config);
        var ready = _service.Project(config, new SettingsPresetAvailability(true, true));

        Assert.Equal(SummaryExperienceMode.LocalWithHostedFallback, blocked.Summaries.Mode);
        Assert.Equal(SettingsPresetSetupState.NeedsProviderSetup, blocked.Summaries.SetupState);
        Assert.Contains("no transcript is sent", blocked.Summaries.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SettingsPresetSetupState.Ready, ready.Summaries.SetupState);
    }

    [Fact]
    public void ApplyPreset_Changes_Only_The_Selected_Category_Owned_Fields()
    {
        var config = RecommendedConfig() with
        {
            MicCaptureEnabled = false,
            AudioOutputDir = "D:\\private-audio",
            SummaryOpenAiModel = "private-model",
            AutoInstallUpdatesEnabled = false,
            IncrementalWorkPlan = IncrementalWorkPlan.MissingAiSummaries,
        };

        var result = _service.ApplyPreset(
            config,
            new SettingsPresetSelection { Processing = ProcessingExperienceMode.FasterBacklog });

        Assert.True(result.Applied);
        var patched = result.Config;
        Assert.Equal(BackgroundProcessingMode.MaximumThroughput, patched.BackgroundProcessingMode);
        Assert.Equal(BackgroundSpeakerLabelingMode.Throttled, patched.BackgroundSpeakerLabelingMode);
        Assert.Equal(InitialProcessingStrategy.ConfiguredStages, patched.InitialProcessingStrategy);
        Assert.False(patched.MicCaptureEnabled);
        Assert.Equal("D:\\private-audio", patched.AudioOutputDir);
        Assert.Equal("private-model", patched.SummaryOpenAiModel);
        Assert.False(patched.AutoInstallUpdatesEnabled);
        Assert.Equal(IncrementalWorkPlan.MissingAiSummaries, patched.IncrementalWorkPlan);
        Assert.Equal(
            new[]
            {
                nameof(AppConfig.BackgroundProcessingMode),
                nameof(AppConfig.BackgroundSpeakerLabelingMode),
            },
            result.ChangedFields.OrderBy(field => field));
        Assert.Contains("does not enable GPU", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyPreset_Does_Not_Enable_Hosted_Summaries_Without_Provider_Setup()
    {
        var config = RecommendedConfig() with
        {
            SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled,
            SummaryProviderPreference = MeetingSummaryProviderPreference.LocalOnly,
            SummaryOpenAiModel = "private-model",
        };

        var result = _service.ApplyPreset(
            config,
            new SettingsPresetSelection { Summaries = SummaryExperienceMode.HostedOnly });

        Assert.False(result.Applied);
        Assert.Contains("Needs provider setup", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MeetingSummaryGenerationMode.Disabled, config.SummaryGenerationMode);
        Assert.Equal(MeetingSummaryProviderPreference.LocalOnly, config.SummaryProviderPreference);
        Assert.Equal("private-model", config.SummaryOpenAiModel);
    }

    [Fact]
    public void ApplyPreset_Enables_Hosted_Summaries_Only_When_Provider_Is_Ready()
    {
        var config = RecommendedConfig();

        var result = _service.ApplyPreset(
            config,
            new SettingsPresetSelection { Summaries = SummaryExperienceMode.HostedOnly },
            new SettingsPresetAvailability(true, true));

        Assert.True(result.Applied);
        Assert.Equal(MeetingSummaryGenerationMode.Enabled, result.Config.SummaryGenerationMode);
        Assert.Equal(MeetingSummaryProviderPreference.OpenAiOnly, result.Config.SummaryProviderPreference);
        Assert.Contains("pending Save Changes", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyPreset_Rejects_Custom_And_Multiple_Category_Selections()
    {
        var config = RecommendedConfig();

        Assert.Throws<ArgumentException>(() => _service.ApplyPreset(
            config,
            new SettingsPresetSelection { Recording = RecordingAssistanceMode.Custom }));
        Assert.Throws<ArgumentException>(() => _service.ApplyPreset(
            config,
            new SettingsPresetSelection
            {
                Recording = RecordingAssistanceMode.ManualOnly,
                Updates = UpdateExperienceMode.ManualOnly,
            }));
    }

    [Fact]
    public void OwnershipMap_Has_One_Owner_Per_Field_And_Excludes_Secrets_And_Microphone()
    {
        var fields = _service.GetOwnershipMap();

        var fieldNames = fields.Values.SelectMany(categoryFields => categoryFields).Select(field => field.FieldName).ToArray();
        Assert.Equal(fieldNames.Length, fieldNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(nameof(AppConfig.MicCaptureEnabled), fieldNames);
        Assert.DoesNotContain(nameof(AppConfig.SummaryOpenAiModel), fieldNames);
        Assert.DoesNotContain(nameof(AppConfig.AudioOutputDir), fieldNames);
    }

    [Fact]
    public void ApplyPreset_Uses_The_Current_Editor_Object_Without_Reloading_Or_Saving()
    {
        var editorConfig = RecommendedConfig() with { MeetingStopTimeoutSeconds = 75 };
        var reloadedConfig = RecommendedConfig() with { MeetingStopTimeoutSeconds = 15 };

        var result = _service.ApplyPreset(
            editorConfig,
            new SettingsPresetSelection { Updates = UpdateExperienceMode.ManualOnly });

        Assert.Equal(75, result.Config.MeetingStopTimeoutSeconds);
        Assert.Equal(15, reloadedConfig.MeetingStopTimeoutSeconds);
        Assert.False(result.Config.UpdateCheckEnabled);
        Assert.False(result.Config.AutoInstallUpdatesEnabled);
    }

    private static AppConfig RecommendedConfig() => new()
    {
        AutoDetectEnabled = true,
        CalendarTitleFallbackEnabled = false,
        MeetingAttendeeEnrichmentEnabled = true,
        MeetingStopTimeoutSeconds = 30,
        AutoDetectAudioPeakThreshold = 0.02,
        BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
        BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
        InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
        SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled,
        SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
        UpdateCheckEnabled = true,
        AutoInstallUpdatesEnabled = false,
    };
}
