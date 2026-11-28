using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed record MeetingCatalogPaths(string AudioDirectory, string TranscriptDirectory, string WorkDirectory)
{
    public bool Matches(MeetingCatalogPaths other) =>
        string.Equals(AudioDirectory, other.AudioDirectory, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TranscriptDirectory, other.TranscriptDirectory, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(WorkDirectory, other.WorkDirectory, StringComparison.OrdinalIgnoreCase);
}

internal sealed record MeetingCatalogSnapshot(
    IReadOnlyList<MeetingOutputRecord> Records,
    IReadOnlyList<(string ReceiptPath, MeetingArchiveReceipt Receipt)> ArchiveReceipts,
    IReadOnlyList<AutomaticRecoverySnapshot> AutomaticRecovery,
    DateTimeOffset CapturedAtUtc);

/// <summary>Owns one reusable, cancelable background scan for the current meeting folders.</summary>
internal sealed class MeetingCatalogPreloader<T>(Func<MeetingCatalogPaths, CancellationToken, Task<T>> load) : IDisposable
{
    private readonly object _gate = new();
    private MeetingCatalogPaths? _paths;
    private CancellationTokenSource? _cancellation;
    private Task<T>? _task;
    private bool _disposed;

    public bool Start(MeetingCatalogPaths paths, CancellationToken lifetime)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paths?.Matches(paths) == true && _task is { IsCanceled: false, IsFaulted: false }) return false;
            Clear();
            _paths = paths;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            try { _task = load(paths, _cancellation.Token); }
            catch (Exception exception) { _task = Task.FromException<T>(exception); }
            _ = _task.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }

    public Task<T>? TryGet(MeetingCatalogPaths paths)
    {
        lock (_gate) return !_disposed && _paths?.Matches(paths) == true ? _task : null;
    }

    public void Invalidate()
    {
        lock (_gate) Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }
    }

    private void Clear()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _task = null;
        _paths = null;
    }
}
