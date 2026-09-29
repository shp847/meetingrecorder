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

    public bool TryBegin(out CallbackIntent? intent)
    {
        if (_pending.Count == 0) { intent = null; return false; }
        intent = _pending.Dequeue();
        _pendingKeys.Remove(intent.Key);
        _activeEdges.Add($"{intent.CorrelationId}|{intent.Edge}");
        return true;
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
