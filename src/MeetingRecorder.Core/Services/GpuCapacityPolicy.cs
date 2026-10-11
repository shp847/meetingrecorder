namespace MeetingRecorder.Core.Services;

internal enum GpuCapacityState
{
    Unavailable = 0,
    Collecting = 1,
    Available = 2,
    BackingOff = 3,
}

/// <summary>
/// Opaque aggregate GPU observation. It intentionally contains no adapter,
/// engine, process, or application identity.
/// </summary>
internal readonly record struct GpuCapacitySample(bool IsAvailable, double? UtilizationPercent);

internal sealed record GpuCapacitySnapshot(
    GpuCapacityState State,
    DateTimeOffset? LastSampleAtUtc,
    int LowUseSampleCount,
    int HighUseSampleCount,
    DateTimeOffset? CooldownUntilUtc,
    double? CurrentUsePercent,
    string Reason)
{
    public static GpuCapacitySnapshot Initial { get; } = new(
        GpuCapacityState.Unavailable,
        null,
        0,
        0,
        null,
        null,
        "Optional GPU capacity is unavailable.");

    public bool IsAvailable => State == GpuCapacityState.Available;
}

/// <summary>
/// Pure fail-closed policy for an optional GPU capacity signal. A signal does
/// not establish provider or selected-adapter eligibility; callers must prove
/// those independently before treating availability as a launch permit.
/// </summary>
internal static class GpuCapacityPolicy
{
    public const double IdleEntryUsePercent = 35d;
    public const double HighUseExitPercent = 65d;
    public const int IdleEntrySampleCount = 3;
    public const int HighUseExitSampleCount = 2;
    public static readonly TimeSpan BackoffCooldown = TimeSpan.FromMinutes(2);

    public static GpuCapacitySnapshot Observe(
        GpuCapacitySnapshot previous,
        GpuCapacitySample sample,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(previous);

        if (previous.LastSampleAtUtc is { } lastSampleAtUtc && nowUtc <= lastSampleAtUtc)
        {
            return Unavailable(nowUtc, "Optional GPU sample clock did not advance.");
        }

        if (!sample.IsAvailable || sample.UtilizationPercent is not { } usePercent ||
            double.IsNaN(usePercent) || double.IsInfinity(usePercent) || usePercent is < 0d or > 100d)
        {
            return Unavailable(nowUtc, "Optional GPU capacity is unavailable.");
        }

        if (previous.CooldownUntilUtc is { } cooldownUntilUtc && nowUtc < cooldownUntilUtc)
        {
            return previous with
            {
                State = GpuCapacityState.BackingOff,
                LastSampleAtUtc = nowUtc,
                LowUseSampleCount = 0,
                HighUseSampleCount = 0,
                CurrentUsePercent = usePercent,
                Reason = "Optional GPU use is high; new GPU work remains paused.",
            };
        }

        var lowSamples = usePercent < IdleEntryUsePercent
            ? previous.LowUseSampleCount + 1
            : 0;
        var highSamples = usePercent > HighUseExitPercent
            ? previous.HighUseSampleCount + 1
            : 0;

        if (highSamples >= HighUseExitSampleCount)
        {
            return new GpuCapacitySnapshot(
                GpuCapacityState.BackingOff,
                nowUtc,
                0,
                highSamples,
                nowUtc + BackoffCooldown,
                usePercent,
                "Optional GPU use is high; new GPU work is paused.");
        }

        if (lowSamples >= IdleEntrySampleCount)
        {
            return new GpuCapacitySnapshot(
                GpuCapacityState.Available,
                nowUtc,
                lowSamples,
                0,
                null,
                usePercent,
                "Optional GPU capacity is available for one eligible staged worker.");
        }

        return new GpuCapacitySnapshot(
            GpuCapacityState.Collecting,
            nowUtc,
            lowSamples,
            highSamples,
            null,
            usePercent,
            "Collecting optional GPU capacity samples.");
    }

    public static bool CanLaunchOneGpuWorker(
        GpuCapacitySnapshot snapshot,
        bool isGpuCapableProvider,
        bool isProviderReady) =>
        snapshot.IsAvailable && isGpuCapableProvider && isProviderReady;

    private static GpuCapacitySnapshot Unavailable(DateTimeOffset nowUtc, string reason) =>
        new(GpuCapacityState.Unavailable, nowUtc, 0, 0, null, null, reason);
}
