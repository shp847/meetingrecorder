using MeetingRecorder.Core.Services;
using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class IdleCpuCapacityPolicyTests
{
    [Fact]
    public void Observe_ThreeIdleSamples_EntersAvailableState()
    {
        var state = IdleCpuCapacitySnapshot.Initial;
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        state = Observe(state, new(0, 0, 0), now);
        state = Observe(state, new(120, 100, 100), now.AddSeconds(30));
        state = Observe(state, new(240, 200, 200), now.AddSeconds(60));
        state = Observe(state, new(360, 300, 300), now.AddSeconds(90));

        Assert.Equal(IdleCpuCapacityState.Available, state.State);
        Assert.Equal(2, IdleCpuCapacityPolicy.GetNextLaunchCap(state, StagedBacklogWorkStage.Transcript));
    }

    [Fact]
    public void Observe_TwoHighUseSamples_BacksOffWithoutRaisingCap()
    {
        var state = IdleCpuCapacitySnapshot.Initial;
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        state = Observe(state, new(0, 0, 0), now);
        state = Observe(state, new(10, 100, 100), now.AddSeconds(30));
        state = Observe(state, new(20, 200, 200), now.AddSeconds(60));

        Assert.Equal(IdleCpuCapacityState.BackingOff, state.State);
        Assert.Equal(1, IdleCpuCapacityPolicy.GetNextLaunchCap(state, StagedBacklogWorkStage.Transcript));
    }

    [Fact]
    public void Observe_UnpluggedRecordingAndInvalidDelta_RemainConservative()
    {
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var unplugged = IdleCpuCapacityPolicy.Observe(IdleCpuCapacitySnapshot.Initial, new(0, 0, 0), now, false, false, true);
        var recording = IdleCpuCapacityPolicy.Observe(IdleCpuCapacitySnapshot.Initial, new(0, 0, 0), now, true, true, true);
        var first = Observe(IdleCpuCapacitySnapshot.Initial, new(100, 100, 100), now);
        var invalid = Observe(first, new(99, 100, 100), now.AddSeconds(30));

        Assert.Equal(IdleCpuCapacityState.Ineligible, unplugged.State);
        Assert.Equal(IdleCpuCapacityState.Ineligible, recording.State);
        Assert.Equal(IdleCpuCapacityState.Unknown, invalid.State);
        Assert.Equal(1, IdleCpuCapacityPolicy.GetNextLaunchCap(invalid, StagedBacklogWorkStage.Transcript));
    }

    [Fact]
    public void ResourceCapacityMonitor_ProbeFailureAndDisposal_RemainConservative()
    {
        var probe = new FakeCapacityProbe { ReturnSystemTimes = false, PluggedIn = true };
        using var monitor = new ResourceCapacityMonitor(
            isRecording: () => false,
            hasEligibleBacklog: () => true,
            probe,
            () => new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

        monitor.Sample();

        Assert.Equal(IdleCpuCapacityState.Unknown, monitor.Snapshot.State);
        Assert.Equal(1, IdleCpuCapacityPolicy.GetNextLaunchCap(monitor.Snapshot, StagedBacklogWorkStage.Transcript));
        Assert.Contains("sample failed", monitor.Snapshot.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static IdleCpuCapacitySnapshot Observe(IdleCpuCapacitySnapshot state, SystemCpuTimes times, DateTimeOffset now) =>
        IdleCpuCapacityPolicy.Observe(state, times, now, isPluggedIn: true, isRecording: false, hasEligibleBacklog: true);

    private sealed class FakeCapacityProbe : IResourceCapacityProbe
    {
        public bool ReturnSystemTimes { get; init; }

        public bool PluggedIn { get; init; }

        public bool TryGetSystemTimes(out SystemCpuTimes times)
        {
            times = default;
            return ReturnSystemTimes;
        }

        public bool TryGetAcPower(out bool isPluggedIn)
        {
            isPluggedIn = PluggedIn;
            return true;
        }
    }
}
