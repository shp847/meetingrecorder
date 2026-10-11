using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

internal sealed record BacklogAccelerationProfileOption(
    BacklogAccelerationProfile Value,
    string Label,
    string Detail);

/// <summary>
/// One explicit projection controls future staged-work admission. It never changes
/// active leases, worker priority, provider consent, or recording protection.
/// </summary>
internal static class BacklogAccelerationProfileResolver
{
    public static IReadOnlyList<BacklogAccelerationProfileOption> GetOptions() =>
    [
        new(BacklogAccelerationProfile.Normal, "Normal", "One staged worker; no scheduled or idle-capacity acceleration."),
        new(BacklogAccelerationProfile.TranscriptOnlyDrain, "Transcript-only drain", "Publish transcripts first; optional labels and summaries stay deferred."),
        new(BacklogAccelerationProfile.OvernightAcceleration, "Overnight acceleration", "Use the configured local overnight window for staged backlog work."),
        new(BacklogAccelerationProfile.IdleCapacityAcceleration, "Idle-capacity acceleration", "Use sustained local CPU capacity for daytime staged backlog work."),
        new(BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration, "Overnight + idle capacity", "Use both bounded acceleration policies; recording protection still wins."),
    ];

    public static AppConfig Apply(AppConfig config, BacklogAccelerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config with
        {
            BacklogAccelerationProfile = profile,
            BacklogAccelerationProfileMigrationVersion = 1,
            ProcessingScheduleMigrationApplied = true,
            InitialProcessingStrategy = profile == BacklogAccelerationProfile.TranscriptOnlyDrain
                ? InitialProcessingStrategy.TranscriptFirst
                : InitialProcessingStrategy.ConfiguredStages,
            OvernightInitialProcessingStrategy = profile == BacklogAccelerationProfile.TranscriptOnlyDrain
                ? InitialProcessingStrategy.TranscriptFirst
                : InitialProcessingStrategy.ConfiguredStages,
        };
    }

    public static bool IsOvernightEnabled(AppConfig config) =>
        config.BacklogAccelerationProfileMigrationVersion == 0 ||
        config.BacklogAccelerationProfile is BacklogAccelerationProfile.OvernightAcceleration or
            BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration;

    public static bool IsIdleCapacityEnabled(AppConfig config) =>
        config.BacklogAccelerationProfileMigrationVersion == 0 ||
        config.BacklogAccelerationProfile is BacklogAccelerationProfile.IdleCapacityAcceleration or
            BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration;

    public static string GetStatusText(AppConfig config) =>
        Normalize(config).Label;

    private static BacklogAccelerationProfileOption Normalize(AppConfig config) =>
        GetOptions().FirstOrDefault(option => option.Value == config.BacklogAccelerationProfile)
        ?? GetOptions()[0];
}
