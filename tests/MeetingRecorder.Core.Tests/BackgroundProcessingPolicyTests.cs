using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.Diagnostics;

namespace MeetingRecorder.Core.Tests;

public sealed class BackgroundProcessingPolicyTests
{
    [Theory]
    [InlineData("22:00", "06:00", "23:00", true)]
    [InlineData("22:00", "06:00", "05:59", true)]
    [InlineData("22:00", "06:00", "12:00", false)]
    public void IsOvernightDrainWindowActive_Uses_Configured_Local_Window(
        string start,
        string end,
        string localTime,
        bool expected)
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            OvernightDrainStartLocal = start,
            OvernightDrainEndLocal = end,
        };

        Assert.Equal(expected, BackgroundProcessingPolicy.IsOvernightDrainWindowActive(config, TimeSpan.Parse(localTime)));
    }

    [Fact]
    public void App_Config_Defaults_To_Responsive_Background_Processing_And_Deferred_Speaker_Labeling()
    {
        var config = new AppConfig();

        Assert.Equal(BackgroundProcessingMode.Responsive, config.BackgroundProcessingMode);
        Assert.Equal(BackgroundSpeakerLabelingMode.Deferred, config.BackgroundSpeakerLabelingMode);
        Assert.Equal(InitialProcessingStrategy.ConfiguredStages, config.InitialProcessingStrategy);
        Assert.Equal(
            IncrementalWorkPlan.QueuedRecordings |
            IncrementalWorkPlan.DeferredSpeakerLabels |
            IncrementalWorkPlan.SafeCleanup,
            config.IncrementalWorkPlan);
    }

    [Fact]
    public void Responsive_Mode_Pauses_New_Background_Work_And_Uses_Conservative_Budgets()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
        };

        Assert.True(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.BelowNormal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(2, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 12));
        Assert.Equal(1, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 12));
        Assert.True(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
    }

    [Fact]
    public void Fastest_Drain_Mode_Keeps_Processing_Inline_Without_Falling_Back_To_All_Cores_Or_Normal_Priority()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.FastestDrain,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
        };

        Assert.False(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.BelowNormal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(8, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 16));
        Assert.Equal(4, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 16));
        Assert.False(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
    }

    [Fact]
    public void Maximum_Throughput_Mode_Uses_Low_Process_Priority_And_Capped_High_Budgets()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.MaximumThroughput,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
        };

        Assert.False(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.BelowNormal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(12, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 16));
        Assert.Equal(6, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 16));
        Assert.False(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
        Assert.Equal(2, BackgroundProcessingPolicy.GetMaxWorkerCount(config));
    }

    [Fact]
    public void Migrated_Overnight_Acceleration_Uses_Configured_Stages_Only_Inside_Window()
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            OvernightInitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "06:00",
        };

        Assert.Equal(
            InitialProcessingStrategy.ConfiguredStages,
            BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy(config, TimeSpan.Parse("12:00")));
        Assert.Equal(
            InitialProcessingStrategy.ConfiguredStages,
            BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy(config, TimeSpan.Parse("23:00")));
    }

    [Fact]
    public void Legacy_Overnight_Profile_Uses_Configured_Stages_In_Its_Window()
    {
        var legacy = new AppConfig
        {
            ProcessingScheduleMigrationApplied = false,
            ProcessingSpeedProfile = ProcessingSpeedProfile.OvernightDrain,
            BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "06:00",
        };

        Assert.Equal(
            InitialProcessingStrategy.ConfiguredStages,
            BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy(legacy, TimeSpan.Parse("23:00")));
    }

    [Theory]
    [InlineData("not-a-time", "also-not-a-time", "23:00", false)]
    [InlineData("not-a-time", "also-not-a-time", "12:00", false)]
    [InlineData("22:00", "06:00", "22:00", true)]
    [InlineData("22:00", "06:00", "06:00", false)]
    public void IsOvernightDrainWindowActive_Uses_Defaults_For_Invalid_Clock_Text_And_Has_Exclusive_End(
        string start,
        string end,
        string localTime,
        bool expected)
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            OvernightDrainStartLocal = start,
            OvernightDrainEndLocal = end,
        };

        Assert.Equal(expected, BackgroundProcessingPolicy.IsOvernightDrainWindowActive(config, TimeSpan.Parse(localTime)));
    }

    [Fact]
    public void Overnight_Acceleration_Uses_Stage_Specific_Conservative_Caps()
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            OvernightInitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "06:00",
        };

        var decision = OvernightAccelerationPolicyResolver.Resolve(
            config,
            AtLocal(2026, 9, 28, 23, 0));

        Assert.True(decision.IsAccelerating);
        Assert.Equal(3, decision.GetMaximumWorkerCount(StagedBacklogWorkStage.Transcript));
        Assert.Equal(1, decision.GetMaximumWorkerCount(StagedBacklogWorkStage.Diarization));
        Assert.Equal(1, decision.GetMaximumWorkerCount(StagedBacklogWorkStage.Summary));
        Assert.Contains("Transcripts", decision.GetStatusText(StagedBacklogWorkStage.Transcript), StringComparison.Ordinal);
    }

    [Fact]
    public void Overnight_Acceleration_Uses_Conservative_Fallbacks_For_Invalid_And_Ambiguous_Local_Time()
    {
        var invalidWindow = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "22:00",
        };
        var invalidDecision = OvernightAccelerationPolicyResolver.Resolve(invalidWindow, DateTimeOffset.Now);

        Assert.Equal(OvernightAccelerationState.InvalidWindow, invalidDecision.State);
        Assert.False(invalidDecision.IsAccelerating);

        var ambiguousLocalTime = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);
        Assert.True(TimeZoneInfo.Local.IsAmbiguousTime(ambiguousLocalTime));
        var ambiguousOffset = TimeZoneInfo.Local.GetAmbiguousTimeOffsets(ambiguousLocalTime)[0];
        var ambiguousDecision = OvernightAccelerationPolicyResolver.Resolve(
            invalidWindow with { OvernightDrainStartLocal = "00:00", OvernightDrainEndLocal = "06:00" },
            new DateTimeOffset(ambiguousLocalTime, ambiguousOffset));

        Assert.Equal(OvernightAccelerationState.AmbiguousLocalTime, ambiguousDecision.State);
        Assert.False(ambiguousDecision.IsAccelerating);
    }

    [Fact]
    public void Transcript_Only_Remains_Explicit_And_Does_Not_Raise_Overnight_Caps()
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            OvernightInitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "06:00",
        };

        var decision = OvernightAccelerationPolicyResolver.Resolve(
            config,
            AtLocal(2026, 9, 28, 23, 0));

        Assert.Equal(OvernightAccelerationState.TranscriptOnly, decision.State);
        Assert.False(decision.IsAccelerating);
        Assert.Equal(1, decision.GetMaximumWorkerCount(StagedBacklogWorkStage.Transcript));
    }

    [Fact]
    public void Transcript_Only_Drain_Uses_Maximum_Budgets_And_Skips_Optional_Enrichment()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            ProcessingSpeedProfile = ProcessingSpeedProfile.TranscriptOnlyDrain,
        };

        Assert.False(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.BelowNormal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(12, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 16));
        Assert.Equal(6, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 16));
        Assert.True(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
        Assert.True(BackgroundProcessingPolicy.ShouldSkipSummarizationInPrimaryPass(config));
        Assert.Equal(2, BackgroundProcessingPolicy.GetMaxWorkerCount(config));
    }

    private static DateTimeOffset AtLocal(int year, int month, int day, int hour, int minute)
    {
        var localDateTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }
}
