namespace MeetingRecorder.Core.Services;

public sealed class AudioLevelHistory
{
    private readonly int _capacity;
    private readonly double[] _samples;
    private readonly object _syncRoot = new();
    private int _count;
    private int _nextWriteIndex;

    public AudioLevelHistory(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _samples = new double[capacity];
    }

    public void AddSample(double level)
    {
        var clamped = Math.Clamp(level, 0d, 1d);

        lock (_syncRoot)
        {
            _samples[_nextWriteIndex] = clamped;
            _nextWriteIndex = (_nextWriteIndex + 1) % _capacity;
            if (_count < _capacity)
            {
                _count++;
            }
        }
    }

    public double[] Snapshot(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be greater than zero.");
        }

        var snapshot = new double[sampleCount];
        CopySnapshot(snapshot);
        return snapshot;
    }

    public void CopySnapshot(double[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CopySnapshot(destination.AsSpan());
    }

    public void CopySnapshot(Span<double> destination)
    {
        if (destination.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination must not be empty.");
        }

        lock (_syncRoot)
        {
            destination.Clear();

            var valuesToCopy = Math.Min(_count, destination.Length);
            if (valuesToCopy == 0)
            {
                return;
            }

            var oldestIndex = _count == _capacity ? _nextWriteIndex : 0;
            var startOffset = _count > destination.Length ? _count - destination.Length : 0;
            var destinationOffset = destination.Length - valuesToCopy;
            for (var index = 0; index < valuesToCopy; index++)
            {
                var sourceIndex = (oldestIndex + startOffset + index) % _capacity;
                destination[destinationOffset + index] = _samples[sourceIndex];
            }
        }
    }

    public bool HasRecentActivity(int sampleCount, double threshold)
    {
        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be greater than zero.");
        }

        var normalizedThreshold = Math.Clamp(threshold, 0d, 1d);
        lock (_syncRoot)
        {
            var valuesToInspect = Math.Min(sampleCount, _count);
            for (var offset = 0; offset < valuesToInspect; offset++)
            {
                var sourceIndex = (_nextWriteIndex - 1 - offset + _capacity) % _capacity;
                if (_samples[sourceIndex] >= normalizedThreshold)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
