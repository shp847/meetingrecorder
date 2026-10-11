using System.Globalization;
using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

internal enum OvernightAccelerationState
{
    Disabled = 0,
    OutsideWindow = 1,
    InvalidWindow = 2,
    AmbiguousLocalTime = 3,
    TranscriptOnly = 4,
    Accelerating = 5,
}

internal sealed record OvernightAccelerationDecision(
    OvernightAccelerationState State,
    TimeOnly? WindowStart,
    TimeOnly? WindowEnd)
{
    public bool IsWindowActive =>
        State is OvernightAccelerationState.TranscriptOnly or OvernightAccelerationState.Accelerating;

    public bool IsAccelerating => State == OvernightAccelerationState.Accelerating;

    public int GetMaximumWorkerCount(StagedBacklogWorkStage? stage) =>
        !IsAccelerating
            ? 1
            : stage switch
            {
                StagedBacklogWorkStage.Transcript => 3,
                StagedBacklogWorkStage.Diarization or StagedBacklogWorkStage.Summary => 1,
                _ => 1,
            };

    public string GetStatusText(StagedBacklogWorkStage? stage) => State switch
    {
        OvernightAccelerationState.Accelerating =>
            $"Overnight acceleration: {GetStageDisplayName(stage)} (up to {GetMaximumWorkerCount(stage)} worker{(GetMaximumWorkerCount(stage) == 1 ? string.Empty : "s")}).",
        OvernightAccelerationState.TranscriptOnly =>
            "Overnight transcript-first mode: speaker labels and summaries stay deferred.",
        OvernightAccelerationState.OutsideWindow => "Overnight acceleration is outside its configured window.",
        OvernightAccelerationState.InvalidWindow => "Overnight acceleration is paused because its time window is invalid.",
        OvernightAccelerationState.AmbiguousLocalTime => "Overnight acceleration is paused during an ambiguous local time.",
        _ => "Overnight acceleration is not configured.",
    };

    private static string GetStageDisplayName(StagedBacklogWorkStage? stage) => stage switch
    {
        StagedBacklogWorkStage.Transcript => "Transcripts",
        StagedBacklogWorkStage.Diarization => "Speaker labels",
        StagedBacklogWorkStage.Summary => "Summaries",
        _ => "Queued work",
    };
}

/// <summary>
/// Determines whether a local scheduled window may raise staged queue concurrency.
/// Invalid or ambiguous local time always keeps normal conservative processing.
/// </summary>
internal static class OvernightAccelerationPolicyResolver
{
    public static OvernightAccelerationDecision Resolve(AppConfig config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!IsScheduleConfigured(config))
        {
            return new OvernightAccelerationDecision(OvernightAccelerationState.Disabled, null, null);
        }

        if (!TryParseWindow(config, out var start, out var end))
        {
            return new OvernightAccelerationDecision(OvernightAccelerationState.InvalidWindow, null, null);
        }

        var localNow = now.ToLocalTime();
        var localDateTime = DateTime.SpecifyKind(localNow.DateTime, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsAmbiguousTime(localDateTime))
        {
            return new OvernightAccelerationDecision(OvernightAccelerationState.AmbiguousLocalTime, start, end);
        }

        var localTime = TimeOnly.FromDateTime(localDateTime);
        if (!IsInsideWindow(start, end, localTime))
        {
            return new OvernightAccelerationDecision(OvernightAccelerationState.OutsideWindow, start, end);
        }

        return GetOvernightStrategy(config) == InitialProcessingStrategy.TranscriptFirst
            ? new OvernightAccelerationDecision(OvernightAccelerationState.TranscriptOnly, start, end)
            : new OvernightAccelerationDecision(OvernightAccelerationState.Accelerating, start, end);
    }

    public static bool IsWindowActive(AppConfig config, TimeSpan localTime)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!IsScheduleConfigured(config) || !TryParseWindow(config, out var start, out var end))
        {
            return false;
        }

        if (localTime < TimeSpan.Zero || localTime >= TimeSpan.FromDays(1))
        {
            return false;
        }

        return IsInsideWindow(start, end, TimeOnly.FromTimeSpan(localTime));
    }

    private static bool IsScheduleConfigured(AppConfig config) =>
        BacklogAccelerationProfileResolver.IsOvernightEnabled(config) &&
        (config.ProcessingScheduleMigrationApplied ||
         config.ProcessingSpeedProfile == ProcessingSpeedProfile.OvernightDrain);

    private static InitialProcessingStrategy GetOvernightStrategy(AppConfig config)
    {
        if (!config.ProcessingScheduleMigrationApplied)
        {
            // The legacy named overnight profile now maps to staged acceleration.
            return config.ProcessingSpeedProfile == ProcessingSpeedProfile.OvernightDrain
                ? InitialProcessingStrategy.ConfiguredStages
                : config.InitialProcessingStrategy;
        }

        return config.OvernightInitialProcessingStrategy == InitialProcessingStrategy.TranscriptFirst
            ? InitialProcessingStrategy.TranscriptFirst
            : InitialProcessingStrategy.ConfiguredStages;
    }

    private static bool TryParseWindow(AppConfig config, out TimeOnly start, out TimeOnly end)
    {
        if (!TimeOnly.TryParseExact(config.OvernightDrainStartLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) ||
            !TimeOnly.TryParseExact(config.OvernightDrainEndLocal, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out end) ||
            start == end)
        {
            start = default;
            end = default;
            return false;
        }

        return true;
    }

    private static bool IsInsideWindow(TimeOnly start, TimeOnly end, TimeOnly localTime) =>
        start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
}
