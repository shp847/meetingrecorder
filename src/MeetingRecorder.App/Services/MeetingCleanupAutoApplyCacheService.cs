using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.App.Services;

internal sealed class MeetingCleanupAutoApplyCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _cachePath;
    private readonly object _gate = new();
    private Dictionary<string, MeetingCleanupAutoApplyEntry>? _entriesByFingerprint;

    public MeetingCleanupAutoApplyCacheService(string? cachePath = null)
    {
        _cachePath = string.IsNullOrWhiteSpace(cachePath)
            ? Path.Combine(AppDataPaths.GetManagedAppRoot(), "cache", "meeting-cleanup-auto-apply-v2.json")
            : Path.GetFullPath(cachePath);
    }

    public bool ShouldSkipAutomaticApply(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        lock (_gate)
        {
            return LoadEntriesByFingerprint().ContainsKey(fingerprint.Trim());
        }
    }

    public void RecordSuccess(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            var entriesByFingerprint = LoadEntriesByFingerprint();
            if (!entriesByFingerprint.Remove(fingerprint.Trim()))
            {
                return;
            }

            Save(entriesByFingerprint.Values);
        }
    }

    public void RecordQueuedSuccess(string fingerprint, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            var entriesByFingerprint = LoadEntriesByFingerprint();
            entriesByFingerprint[fingerprint.Trim()] = new MeetingCleanupAutoApplyEntry(
                fingerprint.Trim(),
                LastFailureUtc: null,
                FailureMessage: string.Empty,
                LastQueuedSuccessUtc: nowUtc);
            Save(entriesByFingerprint.Values);
        }
    }

    public void RecordQueuedSuccesses(IEnumerable<string> fingerprints, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);

        var normalizedFingerprints = fingerprints
            .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
            .Select(fingerprint => fingerprint.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedFingerprints.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            var entriesByFingerprint = LoadEntriesByFingerprint();
            var addedEntry = false;
            foreach (var fingerprint in normalizedFingerprints)
            {
                if (entriesByFingerprint.ContainsKey(fingerprint))
                {
                    continue;
                }

                entriesByFingerprint[fingerprint] = new MeetingCleanupAutoApplyEntry(
                    fingerprint,
                    LastFailureUtc: null,
                    FailureMessage: string.Empty,
                    LastQueuedSuccessUtc: nowUtc);
                addedEntry = true;
            }

            if (addedEntry)
            {
                Save(entriesByFingerprint.Values);
            }
        }
    }

    public void RecordFailure(string fingerprint, DateTimeOffset nowUtc, string? failureMessage)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            var entriesByFingerprint = LoadEntriesByFingerprint();
            entriesByFingerprint[fingerprint.Trim()] = new MeetingCleanupAutoApplyEntry(
                fingerprint.Trim(),
                nowUtc,
                string.IsNullOrWhiteSpace(failureMessage) ? string.Empty : failureMessage.Trim(),
                LastQueuedSuccessUtc: null);
            Save(entriesByFingerprint.Values);
        }
    }

    public IReadOnlyList<MeetingCleanupAutoApplyEntry> GetEntries()
    {
        lock (_gate)
        {
            return LoadEntriesByFingerprint().Values.ToArray();
        }
    }

    private Dictionary<string, MeetingCleanupAutoApplyEntry> LoadEntriesByFingerprint()
    {
        if (_entriesByFingerprint is not null)
        {
            return _entriesByFingerprint;
        }

        if (!File.Exists(_cachePath))
        {
            _entriesByFingerprint = new Dictionary<string, MeetingCleanupAutoApplyEntry>(StringComparer.Ordinal);
            return _entriesByFingerprint;
        }

        try
        {
            var contents = File.ReadAllText(_cachePath).Trim();
            if (string.IsNullOrWhiteSpace(contents))
            {
                _entriesByFingerprint = new Dictionary<string, MeetingCleanupAutoApplyEntry>(StringComparer.Ordinal);
                return _entriesByFingerprint;
            }

            var document = JsonSerializer.Deserialize<MeetingCleanupAutoApplyCacheDocument>(contents, SerializerOptions);
            _entriesByFingerprint = (document?.Entries ?? Array.Empty<MeetingCleanupAutoApplyEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Fingerprint))
                .ToDictionary(entry => entry.Fingerprint, StringComparer.Ordinal);
            return _entriesByFingerprint;
        }
        catch
        {
            _entriesByFingerprint = new Dictionary<string, MeetingCleanupAutoApplyEntry>(StringComparer.Ordinal);
            return _entriesByFingerprint;
        }
    }

    private void Save(IEnumerable<MeetingCleanupAutoApplyEntry> entries)
    {
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new InvalidOperationException("Cache path must include a parent directory.");
        Directory.CreateDirectory(directory);

        var orderedEntries = entries
            .OrderBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        var document = new MeetingCleanupAutoApplyCacheDocument(orderedEntries);
        var tempPath = _cachePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, SerializerOptions));

        if (File.Exists(_cachePath))
        {
            File.Replace(tempPath, _cachePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, _cachePath);
    }
}

internal sealed record MeetingCleanupAutoApplyCacheDocument(
    IReadOnlyList<MeetingCleanupAutoApplyEntry> Entries);

internal sealed record MeetingCleanupAutoApplyEntry(
    string Fingerprint,
    DateTimeOffset? LastFailureUtc,
    string FailureMessage,
    DateTimeOffset? LastQueuedSuccessUtc = null);
