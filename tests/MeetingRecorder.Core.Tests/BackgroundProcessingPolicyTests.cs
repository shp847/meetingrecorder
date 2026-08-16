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
    public void Migrated_Overnight_Strategy_Uses_Transcript_First_Only_Inside_Window()
    {
        var config = new AppConfig
        {
            ProcessingScheduleMigrationApplied = true,
            InitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            OvernightInitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
            OvernightDrainStartLocal = "22:00",
            OvernightDrainEndLocal = "06:00",
        };

        Assert.Equal(
            InitialProcessingStrategy.ConfiguredStages,
            BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy(config, TimeSpan.Parse("12:00")));
        Assert.Equal(
            InitialProcessingStrategy.TranscriptFirst,
            BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy(config, TimeSpan.Parse("23:00")));
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
}
