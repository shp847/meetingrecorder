using MeetingRecorder.Core.Configuration;
using System.Diagnostics;

namespace MeetingRecorder.Core.Services;

public static class BackgroundProcessingPolicy
{
    public static bool ShouldPauseNewBackgroundWork(AppConfig config, bool isRecording)
    {
        return isRecording && config.BackgroundProcessingMode == BackgroundProcessingMode.Responsive;
    }

    public static ProcessPriorityClass GetWorkerPriority(AppConfig config)
    {
        return config.BackgroundProcessingMode switch
        {
            BackgroundProcessingMode.Responsive => ProcessPriorityClass.BelowNormal,
            BackgroundProcessingMode.Balanced => ProcessPriorityClass.BelowNormal,
            _ => ProcessPriorityClass.Normal,
        };
    }

    public static int GetTranscriptionThreadCount(AppConfig config, int processorCount)
    {
        var normalizedProcessorCount = Math.Max(1, processorCount);
        return config.BackgroundProcessingMode switch
        {
            BackgroundProcessingMode.Responsive => Math.Min(2, normalizedProcessorCount),
            BackgroundProcessingMode.Balanced => Math.Min(4, normalizedProcessorCount),
            _ => Math.Min(8, normalizedProcessorCount),
        };
    }

    public static int GetDiarizationThreadCount(AppConfig config, int processorCount)
    {
        var normalizedProcessorCount = Math.Max(1, processorCount);
        return config.BackgroundProcessingMode switch
        {
            BackgroundProcessingMode.Responsive => 1,
            BackgroundProcessingMode.Balanced => Math.Min(2, normalizedProcessorCount),
            _ => Math.Min(4, normalizedProcessorCount),
        };
    }

    public static bool ShouldSkipSpeakerLabelingInPrimaryPass(AppConfig config)
    {
        return config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred;
    }
}
