using System.Text.Json;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal enum CleanupWorkState
{
    Pending = 0,
    Queued = 1,
    Processing = 2,
    Completed = 3,
    ManualReview = 4,
    Failed = 5,
}

internal sealed record CleanupWorkLedgerEntry(
    string Fingerprint,
    CleanupWorkState State,
    DateTimeOffset UpdatedAtUtc,
    string? ManifestPath = null,
    string? Detail = null,
    MeetingCleanupAction? Action = null,
    IReadOnlyList<string>? AffectedStems = null,
    string? InputRevision = null);

internal sealed record CleanupWorkLedgerDocument(IReadOnlyList<CleanupWorkLedgerEntry> Entries);

/// <summary>
/// Persists cleanup work separately from recommendation discovery so queue acceptance is not mistaken for completion.
/// </summary>
internal sealed class MeetingCleanupWorkLedgerService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, CleanupWorkLedgerEntry>? _entries;

    public MeetingCleanupWorkLedgerService(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.GetManagedAppRoot(), "cache", "meeting-cleanup-work-ledger-v1.json");
    }

    public IReadOnlyList<CleanupWorkLedgerEntry> GetEntries()
    {
        lock (_gate)
        {
            return Load().Values.OrderBy(entry => entry.Fingerprint, StringComparer.Ordinal).ToArray();
        }
    }

    public IReadOnlySet<string> GetQueuedManifestPaths()
    {
        lock (_gate)
        {
            return Load().Values
                .Where(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing)
                .Select(entry => entry.ManifestPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    public bool IsEligibleForAutomaticApply(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        lock (_gate)
        {
            return !Load().TryGetValue(fingerprint.Trim(), out var entry) || entry.State == CleanupWorkState.Pending;
        }
    }

    public void Record(
        string fingerprint,
        CleanupWorkState state,
        string? manifestPath = null,
        string? detail = null,
        MeetingCleanupAction? action = null,
        IReadOnlyList<string>? affectedStems = null,
        string? inputRevision = null)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            var key = fingerprint.Trim();
            var previous = Load().GetValueOrDefault(key);
            _entries![key] = new CleanupWorkLedgerEntry(
                key,
                state,
                DateTimeOffset.UtcNow,
                manifestPath ?? previous?.ManifestPath,
                string.IsNullOrWhiteSpace(detail) ? previous?.Detail : detail.Trim(),
                action ?? previous?.Action,
                affectedStems ?? previous?.AffectedStems,
                string.IsNullOrWhiteSpace(inputRevision) ? previous?.InputRevision : inputRevision.Trim());
            Save();
        }
    }

    public void RecordContainmentHold(string fingerprint, MeetingCleanupAction action, string reason)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        lock (_gate)
        {
            var key = fingerprint.Trim();
            if (Load().TryGetValue(key, out var existing) &&
                existing.State == CleanupWorkState.ManualReview &&
                existing.Action == action &&
                string.Equals(existing.Detail, reason.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            _entries![key] = new CleanupWorkLedgerEntry(
                key,
                CleanupWorkState.ManualReview,
                DateTimeOffset.UtcNow,
                Action: action,
                Detail: reason.Trim());
            Save();
        }
    }

    public void RecordCompletionForManifest(string manifestPath, bool succeeded, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        lock (_gate)
        {
            var changed = false;
            foreach (var (fingerprint, entry) in Load().ToArray())
            {
                if (!string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal))
                {
                    continue;
                }

                _entries![fingerprint] = entry with
                {
                    State = succeeded ? CleanupWorkState.Completed : CleanupWorkState.Failed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Detail = string.IsNullOrWhiteSpace(detail) ? entry.Detail : detail.Trim(),
                };
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public void RecordProcessingForManifest(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        lock (_gate)
        {
            var changed = false;
            foreach (var (fingerprint, entry) in Load().ToArray())
            {
                if (!string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal) ||
                    entry.State != CleanupWorkState.Queued)
                {
                    continue;
                }

                _entries![fingerprint] = entry with
                {
                    State = CleanupWorkState.Processing,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public void MigrateLegacyEntries(IEnumerable<MeetingCleanupAutoApplyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_gate)
        {
            var changed = false;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Fingerprint) || Load().ContainsKey(entry.Fingerprint))
                {
                    continue;
                }

                var state = entry.LastFailureUtc.HasValue
                    ? CleanupWorkState.ManualReview
                    : CleanupWorkState.ManualReview;
                _entries![entry.Fingerprint] = new CleanupWorkLedgerEntry(
                    entry.Fingerprint,
                    state,
                    entry.LastFailureUtc ?? entry.LastQueuedSuccessUtc ?? DateTimeOffset.UtcNow,
                    Detail: entry.LastFailureUtc.HasValue ? entry.FailureMessage : "Legacy queued work needs reconciliation.");
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    private Dictionary<string, CleanupWorkLedgerEntry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            var document = File.Exists(_path)
                ? JsonSerializer.Deserialize<CleanupWorkLedgerDocument>(File.ReadAllText(_path), SerializerOptions)
                : null;
            _entries = (document?.Entries ?? Array.Empty<CleanupWorkLedgerEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Fingerprint))
                .ToDictionary(entry => entry.Fingerprint, StringComparer.Ordinal);
        }
        catch
        {
            _entries = new Dictionary<string, CleanupWorkLedgerEntry>(StringComparer.Ordinal);
        }

        return _entries;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Ledger path must include a parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new CleanupWorkLedgerDocument(_entries!.Values.OrderBy(entry => entry.Fingerprint, StringComparer.Ordinal).ToArray()), SerializerOptions));
        try
        {
            if (File.Exists(_path))
            {
                ReplaceWithRetry(tempPath);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void ReplaceWithRetry(string tempPath)
    {
        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(tempPath, _path, null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }
}
