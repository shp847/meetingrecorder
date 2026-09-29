using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public enum CallbackIntentOutcome { Accepted, Coalesced, DeclinedCycle, DeclinedOverload }

public sealed record CallbackIntent(string Key, string CorrelationId, string Edge, long Revision);

public sealed record CallbackIntentTraceEntry(long Sequence, string Key, string CorrelationId, string Edge, long Revision, CallbackIntentOutcome Outcome);

/// <summary>Small, metadata-only single-owner dispatcher contract. Callers drain
/// intents outside their callback stack; repeated work is coalesced by key.</summary>
public sealed class CallbackIntentDispatcher
{
    private const int MaximumQueueLength = 64;
    private const int MaximumTraceLength = 128;
    private readonly Queue<CallbackIntent> _pending = new();
    private readonly HashSet<string> _pendingKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeEdges = new(StringComparer.Ordinal);
    private readonly List<CallbackIntentTraceEntry> _trace = [];
    private long _sequence;

    public CallbackIntentOutcome Enqueue(CallbackIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.Key) || string.IsNullOrWhiteSpace(intent.CorrelationId) || string.IsNullOrWhiteSpace(intent.Edge) || intent.Revision < 0)
            throw new ArgumentException("Callback intents require normalized metadata.", nameof(intent));
        var edgeKey = $"{intent.CorrelationId}|{intent.Edge}";
        var outcome = _activeEdges.Contains(edgeKey) ? CallbackIntentOutcome.DeclinedCycle :
            _pendingKeys.Contains(intent.Key) ? CallbackIntentOutcome.Coalesced :
            _pending.Count >= MaximumQueueLength ? CallbackIntentOutcome.DeclinedOverload : CallbackIntentOutcome.Accepted;
        if (outcome == CallbackIntentOutcome.Accepted)
        {
            _pending.Enqueue(intent);
            _pendingKeys.Add(intent.Key);
        }
        AddTrace(intent, outcome);
        return outcome;
    }

    public bool TryBegin(string key, out CallbackIntent? intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_pending.Count == 0) { intent = null; return false; }
        var queued = _pending.ToArray();
        var index = Array.FindIndex(queued, candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (index < 0) { intent = null; return false; }
        _pending.Clear();
        for (var queuedIndex = 0; queuedIndex < queued.Length; queuedIndex++)
            if (queuedIndex != index) _pending.Enqueue(queued[queuedIndex]);
        intent = queued[index];
        _pendingKeys.Remove(intent.Key);
        _activeEdges.Add($"{intent.CorrelationId}|{intent.Edge}");
        return true;
    }

    public bool TryBegin(out CallbackIntent? intent)
    {
        if (_pending.Count == 0) { intent = null; return false; }
        return TryBegin(_pending.Peek().Key, out intent);
    }

    public void Complete(CallbackIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _activeEdges.Remove($"{intent.CorrelationId}|{intent.Edge}");
    }

    public IReadOnlyList<CallbackIntentTraceEntry> Snapshot() => _trace.ToArray();

    private void AddTrace(CallbackIntent intent, CallbackIntentOutcome outcome)
    {
        _trace.Add(new(++_sequence, intent.Key, intent.CorrelationId, intent.Edge, intent.Revision, outcome));
        if (_trace.Count > MaximumTraceLength) _trace.RemoveAt(0);
    }
}

public sealed class CallbackIntentTraceStore
{
    private readonly string _path;
    public CallbackIntentTraceStore(string path) => _path = Path.GetFullPath(path);

    public async Task SaveAsync(IReadOnlyList<CallbackIntentTraceEntry> trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Count > 128 || trace.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.CorrelationId) || string.IsNullOrWhiteSpace(entry.Edge) || entry.Revision < 0))
            throw new ArgumentException("Callback trace is invalid or exceeds its bounded capacity.", nameof(trace));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(trace), cancellationToken);
        File.Move(temporary, _path, true);
    }

    public async Task<IReadOnlyList<CallbackIntentTraceEntry>> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return Array.Empty<CallbackIntentTraceEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<CallbackIntentTraceEntry>>(await File.ReadAllTextAsync(_path, cancellationToken)) is { Count: <= 128 } trace
                ? trace : Array.Empty<CallbackIntentTraceEntry>();
        }
        catch (JsonException) { return Array.Empty<CallbackIntentTraceEntry>(); }
    }
}
