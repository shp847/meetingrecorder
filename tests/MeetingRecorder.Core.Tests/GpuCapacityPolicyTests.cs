using MeetingRecorder.Core.Services;
using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class GpuCapacityPolicyTests
{
    [Fact]
    public void Observe_RequiresThreeLowOpaqueSamplesBeforeAvailable()
    {
        var start = DateTimeOffset.Parse("2026-09-28T12:00:00Z");
        var snapshot = GpuCapacitySnapshot.Initial;

        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 34d), start);
        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 34d), start.AddSeconds(30));
        Assert.Equal(GpuCapacityState.Collecting, snapshot.State);

        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 34d), start.AddMinutes(1));

        Assert.Equal(GpuCapacityState.Available, snapshot.State);
    }

    [Fact]
    public void Observe_BacksOffAfterTwoHighSamplesAndPreservesCooldown()
    {
        var start = DateTimeOffset.Parse("2026-09-28T12:00:00Z");
        var snapshot = GpuCapacitySnapshot.Initial;

        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 66d), start);
        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 66d), start.AddSeconds(30));

        Assert.Equal(GpuCapacityState.BackingOff, snapshot.State);
        Assert.Equal(start.AddSeconds(30).Add(GpuCapacityPolicy.BackoffCooldown), snapshot.CooldownUntilUtc);

        snapshot = GpuCapacityPolicy.Observe(snapshot, new GpuCapacitySample(true, 1d), start.AddMinutes(1));

        Assert.Equal(GpuCapacityState.BackingOff, snapshot.State);
    }

    [Theory]
    [InlineData(false, 10d)]
    [InlineData(true, -1d)]
    [InlineData(true, 101d)]
    public void Observe_UnavailableOrInvalidSampleFailsClosed(bool available, double utilization)
    {
        var snapshot = GpuCapacityPolicy.Observe(
            GpuCapacitySnapshot.Initial,
            new GpuCapacitySample(available, utilization),
            DateTimeOffset.Parse("2026-09-28T12:00:00Z"));

        Assert.Equal(GpuCapacityState.Unavailable, snapshot.State);
        Assert.False(snapshot.IsAvailable);
    }

    [Fact]
    public void CanLaunchOneGpuWorker_RequiresCapacityAndProvenProviderReadiness()
    {
        var available = GpuCapacitySnapshot.Initial with { State = GpuCapacityState.Available };

        Assert.True(GpuCapacityPolicy.CanLaunchOneGpuWorker(available, true, true));
        Assert.False(GpuCapacityPolicy.CanLaunchOneGpuWorker(available, false, true));
        Assert.False(GpuCapacityPolicy.CanLaunchOneGpuWorker(available, true, false));
        Assert.False(GpuCapacityPolicy.CanLaunchOneGpuWorker(GpuCapacitySnapshot.Initial, true, true));
    }

    [Fact]
    public void Monitor_UsesOpaqueProbeOnlyWhileBacklogNeedsDecision()
    {
        var now = DateTimeOffset.Parse("2026-09-28T12:00:00Z");
        var probe = new SequencedGpuCapacityProbe(34d, 34d, 34d);
        using var monitor = new GpuCapacityMonitor(
            hasEligibleBacklog: () => true,
            probe,
            utcNow: () => now);

        monitor.Sample();
        now = now.AddSeconds(30);
        monitor.Sample();
        now = now.AddSeconds(30);
        monitor.Sample();

        Assert.Equal(GpuCapacityState.Available, monitor.Snapshot.State);
        Assert.Equal(3, probe.CallCount);
    }

    [Fact]
    public void Monitor_SkipsProbeAndFailsClosedWithoutEligibleBacklog()
    {
        var probe = new SequencedGpuCapacityProbe(1d);
        using var monitor = new GpuCapacityMonitor(
            hasEligibleBacklog: () => false,
            probe,
            utcNow: () => DateTimeOffset.Parse("2026-09-28T12:00:00Z"));

        monitor.Sample();

        Assert.Equal(GpuCapacityState.Unavailable, monitor.Snapshot.State);
        Assert.Equal(0, probe.CallCount);
    }

    private sealed class SequencedGpuCapacityProbe : IGpuCapacityProbe
    {
        private readonly Queue<double> _samples;

        public SequencedGpuCapacityProbe(params double[] samples) => _samples = new Queue<double>(samples);

        public int CallCount { get; private set; }

        public bool TryGetAggregateUtilization(out double utilizationPercent)
        {
            CallCount++;
            if (_samples.Count == 0)
            {
                utilizationPercent = 0;
                return false;
            }

            utilizationPercent = _samples.Dequeue();
            return true;
        }

        public void Dispose()
        {
        }
    }
}
