using System.Runtime.InteropServices;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal interface IResourceCapacityProbe
{
    bool TryGetSystemTimes(out SystemCpuTimes times);

    bool TryGetAcPower(out bool isPluggedIn);
}

/// <summary>
/// Bounded local aggregate capacity sampler. It intentionally exposes no process,
/// device, user-activity, or telemetry data.
/// </summary>
internal sealed class ResourceCapacityMonitor : IDisposable
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private readonly object _syncRoot = new();
    private readonly IResourceCapacityProbe _probe;
    private readonly Func<bool> _isRecording;
    private readonly Func<bool> _hasEligibleBacklog;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Timer _timer;
    private IdleCpuCapacitySnapshot _snapshot = IdleCpuCapacitySnapshot.Initial;
    private bool _disposed;

    public ResourceCapacityMonitor(
        Func<bool> isRecording,
        Func<bool> hasEligibleBacklog,
        IResourceCapacityProbe? probe = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _isRecording = isRecording ?? throw new ArgumentNullException(nameof(isRecording));
        _hasEligibleBacklog = hasEligibleBacklog ?? throw new ArgumentNullException(nameof(hasEligibleBacklog));
        _probe = probe ?? new WindowsResourceCapacityProbe();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _timer = new Timer(_ => Sample(), null, SampleInterval, SampleInterval);
    }

    public IdleCpuCapacitySnapshot Snapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _snapshot;
            }
        }
    }

    internal void Sample()
    {
        var nowUtc = _utcNow();
        try
        {
            if (!_hasEligibleBacklog())
            {
                lock (_syncRoot)
                {
                    _snapshot = IdleCpuCapacityPolicy.MarkUnavailable(
                        _snapshot,
                        nowUtc,
                        "CPU capacity sampling is idle because no staged backlog needs a decision.");
                    return;
                }
            }

            if (!_probe.TryGetSystemTimes(out var times))
            {
                lock (_syncRoot)
                {
                    _snapshot = IdleCpuCapacityPolicy.MarkUnavailable(
                        _snapshot,
                        nowUtc,
                        "CPU capacity is unavailable because the local system sample failed.");
                    return;
                }
            }

            var isPluggedIn = _probe.TryGetAcPower(out var pluggedIn) && pluggedIn;
            lock (_syncRoot)
            {
                _snapshot = IdleCpuCapacityPolicy.Observe(
                    _snapshot,
                    times,
                    nowUtc,
                    isPluggedIn,
                    _isRecording(),
                    hasEligibleBacklog: true);
            }
        }
        catch
        {
            lock (_syncRoot)
            {
                _snapshot = IdleCpuCapacityPolicy.MarkUnavailable(
                    _snapshot,
                    nowUtc,
                    "CPU capacity is unavailable because local sampling failed.");
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }
}

internal sealed class WindowsResourceCapacityProbe : IResourceCapacityProbe
{
    public bool TryGetSystemTimes(out SystemCpuTimes times)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            times = default;
            return false;
        }

        times = new SystemCpuTimes(ToLong(idle), ToLong(kernel), ToLong(user));
        return true;
    }

    public bool TryGetAcPower(out bool isPluggedIn)
    {
        if (!GetSystemPowerStatus(out var powerStatus))
        {
            isPluggedIn = false;
            return false;
        }

        isPluggedIn = powerStatus.AcLineStatus == 1;
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    private static long ToLong(FileTime fileTime) => ((long)fileTime.HighDateTime << 32) | fileTime.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
