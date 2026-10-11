using System.Runtime.InteropServices;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal interface IGpuCapacityProbe : IDisposable
{
    bool TryGetAggregateUtilization(out double utilizationPercent);
}

/// <summary>
/// Bounded local GPU sampler. It retains only the largest numeric engine value
/// from a sample; adapter, engine, and process identities are never materialized.
/// </summary>
internal sealed class GpuCapacityMonitor : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly IGpuCapacityProbe _probe;
    private readonly Func<bool> _hasEligibleBacklog;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Timer _timer;
    private GpuCapacitySnapshot _snapshot = GpuCapacitySnapshot.Initial;
    private bool _disposed;

    public GpuCapacityMonitor(
        Func<bool> hasEligibleBacklog,
        IGpuCapacityProbe? probe = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _hasEligibleBacklog = hasEligibleBacklog ?? throw new ArgumentNullException(nameof(hasEligibleBacklog));
        _probe = probe ?? new WindowsGpuCapacityProbe();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _timer = new Timer(_ => Sample(), null, ResourceCapacityMonitor.SampleInterval, ResourceCapacityMonitor.SampleInterval);
    }

    public GpuCapacitySnapshot Snapshot
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
            var sample = _hasEligibleBacklog() && _probe.TryGetAggregateUtilization(out var utilization)
                ? new GpuCapacitySample(true, utilization)
                : new GpuCapacitySample(false, null);
            lock (_syncRoot)
            {
                _snapshot = GpuCapacityPolicy.Observe(_snapshot, sample, nowUtc);
            }
        }
        catch
        {
            lock (_syncRoot)
            {
                _snapshot = GpuCapacityPolicy.Observe(
                    _snapshot,
                    new GpuCapacitySample(false, null),
                    nowUtc);
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
        _probe.Dispose();
    }
}

/// <summary>
/// Uses the Windows GPU Engine counter only as an opaque aggregate safety signal.
/// Wildcard instance names remain unmanaged and are never read, logged, or stored.
/// The highest valid numeric value wins so any busy engine prevents acceleration.
/// </summary>
internal sealed class WindowsGpuCapacityProbe : IGpuCapacityProbe
{
    private const uint ErrorSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const string GpuEngineCounterPath = "\\GPU Engine(*)\\Utilization Percentage";

    private IntPtr _query;
    private IntPtr _counter;
    private bool _firstCollection = true;
    private bool _disposed;

    public bool TryGetAggregateUtilization(out double utilizationPercent)
    {
        utilizationPercent = 0;
        if (_disposed || !EnsureQuery())
        {
            return false;
        }

        if (PdhCollectQueryData(_query) != ErrorSuccess)
        {
            return false;
        }

        if (_firstCollection)
        {
            _firstCollection = false;
            return false;
        }

        uint bufferBytes = 0;
        uint itemCount = 0;
        var status = PdhGetFormattedCounterArrayW(
            _counter,
            PdhFmtDouble,
            ref bufferBytes,
            ref itemCount,
            IntPtr.Zero);
        if (status != PdhMoreData || bufferBytes == 0 || itemCount == 0 || bufferBytes > int.MaxValue)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)bufferBytes);
        try
        {
            status = PdhGetFormattedCounterArrayW(
                _counter,
                PdhFmtDouble,
                ref bufferBytes,
                ref itemCount,
                buffer);
            if (status != ErrorSuccess || itemCount == 0)
            {
                return false;
            }

            var itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
            var highestUtilization = double.MinValue;
            for (var index = 0u; index < itemCount; index++)
            {
                var itemPointer = IntPtr.Add(buffer, checked((int)index * itemSize));
                var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(itemPointer);
                if (item.Value.Status != ErrorSuccess ||
                    double.IsNaN(item.Value.DoubleValue) ||
                    double.IsInfinity(item.Value.DoubleValue) ||
                    item.Value.DoubleValue is < 0d or > 100d)
                {
                    return false;
                }

                highestUtilization = Math.Max(highestUtilization, item.Value.DoubleValue);
            }

            if (highestUtilization == double.MinValue)
            {
                return false;
            }

            utilizationPercent = highestUtilization;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }
    }

    private bool EnsureQuery()
    {
        if (_query != IntPtr.Zero && _counter != IntPtr.Zero)
        {
            return true;
        }

        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != ErrorSuccess)
        {
            _query = IntPtr.Zero;
            return false;
        }

        if (PdhAddEnglishCounterW(_query, GpuEngineCounterPath, IntPtr.Zero, out _counter) == ErrorSuccess)
        {
            return true;
        }

        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
        return false;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public IntPtr Name;
        public PdhFormattedCounterValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }
}
