using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.Diagnostics;

namespace MeetingRecorder.Core.Tests;

public sealed class BackgroundProcessingPolicyTests
{
    [Fact]
    public void App_Config_Defaults_To_Responsive_Background_Processing_And_Deferred_Speaker_Labeling()
    {
        var config = new AppConfig();

        Assert.Equal(BackgroundProcessingMode.Responsive, config.BackgroundProcessingMode);
        Assert.Equal(BackgroundSpeakerLabelingMode.Deferred, config.BackgroundSpeakerLabelingMode);
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
    public void Fastest_Drain_Mode_Keeps_Processing_Inline_Without_Falling_Back_To_All_Cores()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.FastestDrain,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
        };

        Assert.False(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.Normal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(8, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 16));
        Assert.Equal(4, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 16));
        Assert.False(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
    }

    [Fact]
    public void Maximum_Throughput_Mode_Uses_Above_Normal_Priority_And_Capped_High_Budgets()
    {
        var config = new AppConfig
        {
            BackgroundProcessingMode = BackgroundProcessingMode.MaximumThroughput,
            BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
        };

        Assert.False(BackgroundProcessingPolicy.ShouldPauseNewBackgroundWork(config, isRecording: true));
        Assert.Equal(ProcessPriorityClass.AboveNormal, BackgroundProcessingPolicy.GetWorkerPriority(config));
        Assert.Equal(12, BackgroundProcessingPolicy.GetTranscriptionThreadCount(config, processorCount: 16));
        Assert.Equal(6, BackgroundProcessingPolicy.GetDiarizationThreadCount(config, processorCount: 16));
        Assert.False(BackgroundProcessingPolicy.ShouldSkipSpeakerLabelingInPrimaryPass(config));
    }
}
