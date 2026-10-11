namespace MeetingRecorder.Core.Services;

internal enum IdleCpuCapacityState
{
    Unknown = 0,
    Collecting = 1,
    Available = 2,
    BackingOff = 3,
    Ineligible = 4,
}

/// <summary>
/// Aggregate system CPU tick values from GetSystemTimes. Kernel time includes idle time.
/// </summary>
internal readonly record struct SystemCpuTimes(long IdleTicks, long KernelTicks, long UserTicks);

internal sealed record IdleCpuCapacitySnapshot(
    IdleCpuCapacityState State,
    SystemCpuTimes? PreviousTimes,
    DateTimeOffset? LastSampleAtUtc,
    int LowUseSampleCount,
    int HighUseSampleCount,
    DateTimeOffset? CooldownUntilUtc,
    double? CurrentUsePercent,
    string Reason)
{
    public static IdleCpuCapacitySnapshot Initial { get; } = new(
        IdleCpuCapacityState.Unknown,
        null,
        null,
        0,
        0,
        null,
        null,
        "CPU capacity has not been sampled.");

    public bool IsAvailable => State == IdleCpuCapacityState.Available;
}

/// <summary>
/// Pure, conservative policy for local aggregate CPU capacity. It has no process,
/// device, telemetry, or power-API access; callers supply already-redacted facts.
/// </summary>
internal static class IdleCpuCapacityPolicy
{
    public const double IdleEntryUsePercent = 55d;
    public const double HighUseExitPercent = 70d;
    public const int IdleEntrySampleCount = 3;
    public const int HighUseExitSampleCount = 2;
    public static readonly TimeSpan BackoffCooldown = TimeSpan.FromMinutes(2);

    public static IdleCpuCapacitySnapshot Observe(
        IdleCpuCapacitySnapshot previous,
        SystemCpuTimes currentTimes,
        DateTimeOffset nowUtc,
        bool isPluggedIn,
        bool isRecording,
        bool hasEligibleBacklog)
    {
        ArgumentNullException.ThrowIfNull(previous);

        if (!isPluggedIn)
        {
            return Ineligible(currentTimes, nowUtc, "Idle CPU acceleration requires verified AC power.");
        }

        if (isRecording)
        {
            return Ineligible(currentTimes, nowUtc, "Idle CPU acceleration is paused while recording.");
        }

        if (!hasEligibleBacklog)
        {
            return Ineligible(currentTimes, nowUtc, "Idle CPU acceleration has no eligible backlog.");
        }

        if (previous.LastSampleAtUtc is { } lastSampleAtUtc && nowUtc <= lastSampleAtUtc)
        {
            return Unknown(currentTimes, nowUtc, "CPU sample clock did not advance.");
        }

        if (previous.CooldownUntilUtc is { } cooldownUntilUtc && nowUtc < cooldownUntilUtc)
        {
            return previous with
            {
                State = IdleCpuCapacityState.BackingOff,
                PreviousTimes = currentTimes,
                LastSampleAtUtc = nowUtc,
                LowUseSampleCount = 0,
                HighUseSampleCount = 0,
                CurrentUsePercent = null,
                Reason = "CPU use is high; new accelerated work remains paused.",
            };
        }

        if (previous.PreviousTimes is not { } previousTimes)
        {
            return previous with
            {
                State = IdleCpuCapacityState.Collecting,
                PreviousTimes = currentTimes,
                LastSampleAtUtc = nowUtc,
                LowUseSampleCount = 0,
                HighUseSampleCount = 0,
                CooldownUntilUtc = null,
                CurrentUsePercent = null,
                Reason = "Collecting CPU capacity samples.",
            };
        }

        if (!TryGetUsePercent(previousTimes, currentTimes, out var usePercent))
        {
            return Unknown(currentTimes, nowUtc, "CPU sample delta was invalid.");
        }

        var lowSamples = usePercent < IdleEntryUsePercent
            ? previous.LowUseSampleCount + 1
            : 0;
        var highSamples = usePercent > HighUseExitPercent
            ? previous.HighUseSampleCount + 1
            : 0;

        if (highSamples >= HighUseExitSampleCount)
        {
            return new IdleCpuCapacitySnapshot(
                IdleCpuCapacityState.BackingOff,
                currentTimes,
                nowUtc,
                0,
                highSamples,
                nowUtc + BackoffCooldown,
                usePercent,
                "CPU use is high; new accelerated work is paused.");
        }

        if (lowSamples >= IdleEntrySampleCount)
        {
            return new IdleCpuCapacitySnapshot(
                IdleCpuCapacityState.Available,
                currentTimes,
                nowUtc,
                lowSamples,
                0,
                null,
                usePercent,
                "Idle CPU capacity is available for the next staged worker.");
        }

        return new IdleCpuCapacitySnapshot(
            IdleCpuCapacityState.Collecting,
            currentTimes,
            nowUtc,
            lowSamples,
            highSamples,
            null,
            usePercent,
            "Collecting CPU capacity samples.");
    }

    public static int GetNextLaunchCap(IdleCpuCapacitySnapshot snapshot, StagedBacklogWorkStage? stage) =>
        snapshot.IsAvailable
            ? stage switch
            {
                StagedBacklogWorkStage.Transcript => 2,
                StagedBacklogWorkStage.Diarization or StagedBacklogWorkStage.Summary => 1,
                _ => 1,
            }
            : 1;

    public static IdleCpuCapacitySnapshot MarkUnavailable(
        IdleCpuCapacitySnapshot previous,
        DateTimeOffset nowUtc,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return previous with
        {
            State = IdleCpuCapacityState.Unknown,
            LowUseSampleCount = 0,
            HighUseSampleCount = 0,
            CooldownUntilUtc = null,
            CurrentUsePercent = null,
            LastSampleAtUtc = nowUtc,
            Reason = reason,
        };
    }

    private static IdleCpuCapacitySnapshot Ineligible(SystemCpuTimes times, DateTimeOffset nowUtc, string reason) =>
        new(IdleCpuCapacityState.Ineligible, times, nowUtc, 0, 0, null, null, reason);

    private static IdleCpuCapacitySnapshot Unknown(SystemCpuTimes times, DateTimeOffset nowUtc, string reason) =>
        new(IdleCpuCapacityState.Unknown, times, nowUtc, 0, 0, null, null, reason);

    private static bool TryGetUsePercent(SystemCpuTimes previous, SystemCpuTimes current, out double usePercent)
    {
        var idleDelta = current.IdleTicks - previous.IdleTicks;
        var kernelDelta = current.KernelTicks - previous.KernelTicks;
        var userDelta = current.UserTicks - previous.UserTicks;
        var totalDelta = kernelDelta + userDelta;

        if (idleDelta < 0 || kernelDelta < 0 || userDelta < 0 || totalDelta <= 0 || idleDelta > totalDelta)
        {
            usePercent = 0;
            return false;
        }

        usePercent = (totalDelta - idleDelta) * 100d / totalDelta;
        return usePercent is >= 0d and <= 100d;
    }
}
