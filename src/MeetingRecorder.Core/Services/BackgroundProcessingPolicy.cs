using MeetingRecorder.Core.Configuration;
using System.Diagnostics;

namespace MeetingRecorder.Core.Services;

public static class BackgroundProcessingPolicy
{
    public static readonly TimeSpan DiarizationTimeout = TimeSpan.FromMinutes(45);

    public const int TranscriptOnlyDrainWorkerCount = 2;

    public static ProcessingSpeedProfile GetEffectiveSpeedProfile(AppConfig config)
    {
        if (config.ProcessingSpeedProfile == ProcessingSpeedProfile.OvernightDrain)
        {
            return IsOvernightDrainWindowActive(config, DateTimeOffset.Now.TimeOfDay)
                ? ProcessingSpeedProfile.TranscriptOnlyDrain
                : ProcessingSpeedProfile.Normal;
        }

        return config.ProcessingSpeedProfile;
    }

    public static bool IsTranscriptOnlyDrainActive(AppConfig config)
    {
        return GetEffectiveSpeedProfile(config) == ProcessingSpeedProfile.TranscriptOnlyDrain;
    }

    public static int GetMaxWorkerCount(AppConfig config)
    {
        return IsTranscriptOnlyDrainActive(config) ? TranscriptOnlyDrainWorkerCount : 1;
    }

    public static bool ShouldPauseNewBackgroundWork(AppConfig config, bool isRecording)
    {
        return isRecording && GetEffectiveBackgroundProcessingMode(config) == BackgroundProcessingMode.Responsive;
    }

    public static ProcessPriorityClass GetWorkerPriority(AppConfig config)
    {
        return GetEffectiveBackgroundProcessingMode(config) switch
        {
            BackgroundProcessingMode.Responsive => ProcessPriorityClass.BelowNormal,
            BackgroundProcessingMode.Balanced => ProcessPriorityClass.BelowNormal,
            BackgroundProcessingMode.MaximumThroughput => ProcessPriorityClass.AboveNormal,
            _ => ProcessPriorityClass.Normal,
        };
    }

    public static int GetTranscriptionThreadCount(AppConfig config, int processorCount)
    {
        var normalizedProcessorCount = Math.Max(1, processorCount);
        return GetEffectiveBackgroundProcessingMode(config) switch
        {
            BackgroundProcessingMode.Responsive => Math.Min(2, normalizedProcessorCount),
            BackgroundProcessingMode.Balanced => Math.Min(4, normalizedProcessorCount),
            BackgroundProcessingMode.MaximumThroughput => Math.Min(12, normalizedProcessorCount),
            _ => Math.Min(8, normalizedProcessorCount),
        };
    }

    public static int GetDiarizationThreadCount(AppConfig config, int processorCount)
    {
        var normalizedProcessorCount = Math.Max(1, processorCount);
        return GetEffectiveBackgroundProcessingMode(config) switch
        {
            BackgroundProcessingMode.Responsive => 1,
            BackgroundProcessingMode.Balanced => Math.Min(2, normalizedProcessorCount),
            BackgroundProcessingMode.MaximumThroughput => Math.Min(6, normalizedProcessorCount),
            _ => Math.Min(4, normalizedProcessorCount),
        };
    }

    public static bool ShouldSkipSpeakerLabelingInPrimaryPass(AppConfig config)
    {
        return config.BackgroundSpeakerLabelingMode == BackgroundSpeakerLabelingMode.Deferred ||
               IsTranscriptOnlyDrainActive(config);
    }

    public static bool ShouldSkipSummarizationInPrimaryPass(AppConfig config)
    {
        return IsTranscriptOnlyDrainActive(config);
    }

    private static BackgroundProcessingMode GetEffectiveBackgroundProcessingMode(AppConfig config)
    {
        return IsTranscriptOnlyDrainActive(config)
            ? BackgroundProcessingMode.MaximumThroughput
            : config.BackgroundProcessingMode;
    }

    private static bool IsOvernightDrainWindowActive(AppConfig config, TimeSpan localTime)
    {
        if (!TimeSpan.TryParse(config.OvernightDrainStartLocal, out var start) ||
            !TimeSpan.TryParse(config.OvernightDrainEndLocal, out var end))
        {
            start = TimeSpan.FromHours(22);
            end = TimeSpan.FromHours(6);
        }

        return start <= end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }
}
