namespace MeetingRecorder.Core.Services;

public sealed class AudioLevelHistory
{
    private readonly int _capacity;
    private readonly Queue<double> _samples;
    private readonly object _syncRoot = new();

    public AudioLevelHistory(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _samples = new Queue<double>(capacity);
    }

    public void AddSample(double level)
    {
        var clamped = Math.Clamp(level, 0d, 1d);

        lock (_syncRoot)
        {
            if (_samples.Count == _capacity)
            {
                _samples.Dequeue();
            }

            _samples.Enqueue(clamped);
        }
    }

    public double[] Snapshot(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be greater than zero.");
        }

        lock (_syncRoot)
        {
            var current = _samples.ToArray();
            if (current.Length >= sampleCount)
            {
                return current[^sampleCount..];
            }

            var snapshot = new double[sampleCount];
            Array.Copy(current, 0, snapshot, sampleCount - current.Length, current.Length);
            return snapshot;
        }
    }

    public bool HasRecentActivity(int sampleCount, double threshold)
    {
        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be greater than zero.");
        }

        var normalizedThreshold = Math.Clamp(threshold, 0d, 1d);
        var snapshot = Snapshot(sampleCount);
        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            if (snapshot[index] >= normalizedThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
