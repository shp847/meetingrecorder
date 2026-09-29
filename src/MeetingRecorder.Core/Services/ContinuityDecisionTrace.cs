using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public enum ContinuityTraceEventKind
{
    Detection = 0,
    Identity = 1,
    Verdict = 2,
    GraceEntered = 3,
    GraceExited = 4,
    AutoStop = 5,
    Rollover = 6,
    Reclassify = 7,
    Recovery = 8,
    PublishHeal = 9,
}

public enum ContinuityTraceState
{
    Unknown = 0,
    SameMeeting = 1,
    DifferentMeeting = 2,
    Grace = 3,
    ManualReview = 4,
}

public sealed record ContinuityDecisionTraceEvent(
    int Sequence,
    DateTimeOffset OccurredAtUtc,
    ContinuityTraceEventKind Kind,
    ContinuityEvidenceTier EvidenceTier,
    string ReasonCode,
    ContinuityTraceState PriorState,
    ContinuityTraceState NewState,
    int DurationMilliseconds,
    int Count);

public sealed record ContinuityDecisionTraceSnapshot(
    int SchemaVersion,
    string OpaqueSessionId,
    string OpaqueDecisionId,
    string OpaqueCorrelationId,
    int DroppedEventCount,
    IReadOnlyList<ContinuityDecisionTraceEvent> Events);

public sealed class ContinuityDecisionTrace
{
    public const int CurrentSchemaVersion = 1;
    private static readonly Regex OpaqueIdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex ReasonCodePattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private readonly List<ContinuityDecisionTraceEvent> _events = [];
    private readonly int _capacity;
    private int _droppedEventCount;

    public ContinuityDecisionTrace(
        string opaqueSessionId,
        string opaqueDecisionId,
        string opaqueCorrelationId,
        int capacity = 64)
    {
        ValidateOpaqueId(opaqueSessionId, nameof(opaqueSessionId));
        ValidateOpaqueId(opaqueDecisionId, nameof(opaqueDecisionId));
        ValidateOpaqueId(opaqueCorrelationId, nameof(opaqueCorrelationId));
        if (capacity is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must remain bounded between 1 and 256 events.");
        }

        OpaqueSessionId = opaqueSessionId;
        OpaqueDecisionId = opaqueDecisionId;
        OpaqueCorrelationId = opaqueCorrelationId;
        _capacity = capacity;
    }

    public string OpaqueSessionId { get; }
    public string OpaqueDecisionId { get; }
    public string OpaqueCorrelationId { get; }

    public void Append(
        ContinuityTraceEventKind kind,
        ContinuityEvidenceTier evidenceTier,
        string reasonCode,
        ContinuityTraceState priorState,
        ContinuityTraceState newState,
        DateTimeOffset occurredAtUtc,
        int durationMilliseconds = 0,
        int count = 0)
    {
        if (!ReasonCodePattern.IsMatch(reasonCode) || durationMilliseconds < 0 || count < 0)
        {
            throw new ArgumentException("Trace events require a normalized reason code and non-negative bounded metrics.");
        }

        if (_events.Count == _capacity)
        {
            _events.RemoveAt(0);
            _droppedEventCount++;
        }

        _events.Add(new ContinuityDecisionTraceEvent(
            _events.Count == 0 ? _droppedEventCount + 1 : _events[^1].Sequence + 1,
            occurredAtUtc.ToUniversalTime(),
            kind,
            evidenceTier,
            reasonCode,
            priorState,
            newState,
            durationMilliseconds,
            count));
    }

    public ContinuityDecisionTraceSnapshot CreateSnapshot() => new(
        CurrentSchemaVersion,
        OpaqueSessionId,
        OpaqueDecisionId,
        OpaqueCorrelationId,
        _droppedEventCount,
        _events.ToArray());

    public static void ValidateSnapshot(ContinuityDecisionTraceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported continuity trace schema '{snapshot.SchemaVersion}'.");
        }

        ValidateOpaqueId(snapshot.OpaqueSessionId, nameof(snapshot.OpaqueSessionId));
        ValidateOpaqueId(snapshot.OpaqueDecisionId, nameof(snapshot.OpaqueDecisionId));
        ValidateOpaqueId(snapshot.OpaqueCorrelationId, nameof(snapshot.OpaqueCorrelationId));
        if (snapshot.DroppedEventCount < 0 || snapshot.Events.Count > 256)
        {
            throw new InvalidOperationException("Continuity trace bounds are invalid.");
        }

        var expectedSequence = snapshot.DroppedEventCount + 1;
        foreach (var traceEvent in snapshot.Events)
        {
            if (traceEvent.Sequence != expectedSequence++ ||
                !ReasonCodePattern.IsMatch(traceEvent.ReasonCode) ||
                traceEvent.DurationMilliseconds < 0 ||
                traceEvent.Count < 0)
            {
                throw new InvalidOperationException("Continuity trace event is invalid or non-monotonic.");
            }
        }
    }

    private static void ValidateOpaqueId(string value, string name)
    {
        if (!OpaqueIdPattern.IsMatch(value))
        {
            throw new ArgumentException("Continuity trace identifiers must be opaque lowercase tokens.", name);
        }
    }
}

public enum ContinuityTraceLoadStatus
{
    Missing = 0,
    Loaded = 1,
    Unsupported = 2,
    Corrupt = 3,
}

public sealed record ContinuityTraceLoadResult(
    ContinuityTraceLoadStatus Status,
    ContinuityDecisionTraceSnapshot? Snapshot);

public sealed class ContinuityDecisionTraceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string _sidecarPath;

    public ContinuityDecisionTraceStore(string sidecarPath)
    {
        if (string.IsNullOrWhiteSpace(sidecarPath))
        {
            throw new ArgumentException("Trace sidecar path is required.", nameof(sidecarPath));
        }

        _sidecarPath = Path.GetFullPath(sidecarPath);
    }

    public async Task SaveAsync(ContinuityDecisionTraceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ContinuityDecisionTrace.ValidateSnapshot(snapshot);
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        AssertMetadataOnlyJson(json);

        var directory = Path.GetDirectoryName(_sidecarPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _sidecarPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _sidecarPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ContinuityTraceLoadResult> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_sidecarPath))
        {
            return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Missing, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_sidecarPath, cancellationToken).ConfigureAwait(false);
            AssertMetadataOnlyJson(json);
            var snapshot = JsonSerializer.Deserialize<ContinuityDecisionTraceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Corrupt, null);
            }

            if (snapshot.SchemaVersion != ContinuityDecisionTrace.CurrentSchemaVersion)
            {
                return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Unsupported, null);
            }

            ContinuityDecisionTrace.ValidateSnapshot(snapshot);
            return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Loaded, snapshot);
        }
        catch (JsonException)
        {
            return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Corrupt, null);
        }
        catch (InvalidOperationException)
        {
            return new ContinuityTraceLoadResult(ContinuityTraceLoadStatus.Corrupt, null);
        }
    }

    private static void AssertMetadataOnlyJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var permitted = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion", "opaqueSessionId", "opaqueDecisionId", "opaqueCorrelationId", "droppedEventCount", "events",
            "sequence", "occurredAtUtc", "kind", "evidenceTier", "reasonCode", "priorState", "newState", "durationMilliseconds", "count",
        };
        ValidateProperties(document.RootElement, permitted);
    }

    private static void ValidateProperties(JsonElement element, ISet<string> permitted)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!permitted.Contains(property.Name))
                {
                    throw new InvalidOperationException("Continuity trace sidecar contains a disallowed property.");
                }

                ValidateProperties(property.Value, permitted);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateProperties(item, permitted);
            }
        }
    }
}

public sealed record ContinuityReplayRun(
    ContinuityReplayDecisionResult Decision,
    string TraceDigest,
    int TraceEventCount,
    int DroppedEventCount);

public sealed class ContinuityReplayRunner
{
    private readonly ContinuityReplayDecisionContract _contract = new();

    public ContinuityReplayRun Run(ContinuityReplaySnapshot snapshot, ContinuityDecisionTraceSnapshot trace)
    {
        ContinuityDecisionTrace.ValidateSnapshot(trace);
        var decision = _contract.Evaluate(snapshot);
        var canonical = string.Join("|", trace.Events.Select(item => $"{item.Sequence}:{(int)item.Kind}:{(int)item.EvidenceTier}:{item.ReasonCode}:{(int)item.PriorState}:{(int)item.NewState}:{item.DurationMilliseconds}:{item.Count}"));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ContinuityReplayRun(decision, digest, trace.Events.Count, trace.DroppedEventCount);
    }
}

public static class ContinuityTraceExplanationFormatter
{
    public static string Format(ContinuityReplayDecisionResult result) => result.ReasonCode switch
    {
        "manual-stop-authority" => "A user stop keeps this session out of automatic continuation.",
        "contradictory-strong-identity" => "Conflicting strong identity evidence keeps meetings separate.",
        "stable-identity-continuity" => "Compatible strong identity supports continuity; it does not merge published meetings.",
        "bounded-ambiguous-grace" => "Ambiguous evidence is retained only in a bounded continuity grace period.",
        _ => "Insufficient identity evidence requires manual review.",
    };
}
