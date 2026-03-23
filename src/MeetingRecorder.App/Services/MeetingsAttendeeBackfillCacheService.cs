using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.App.Services;

internal sealed class MeetingsAttendeeBackfillCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static readonly TimeSpan NoMatchLifetime = TimeSpan.FromDays(7);

    private readonly string _cachePath;
    private readonly object _gate = new();
    private Dictionary<string, MeetingsAttendeeBackfillCacheEntry>? _entriesByStem;

    public MeetingsAttendeeBackfillCacheService(string? cachePath = null)
    {
        _cachePath = string.IsNullOrWhiteSpace(cachePath)
            ? Path.Combine(AppDataPaths.GetManagedAppRoot(), "cache", "meetings-attendee-backfill-v1.json")
            : Path.GetFullPath(cachePath);
    }

    public bool ShouldSkipAutomaticBackfill(MeetingOutputRecord record, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var entriesByStem = LoadEntriesByStem();
            if (!entriesByStem.TryGetValue(record.Stem, out var entry))
            {
                return false;
            }

            var fingerprint = BuildFingerprint(record);
            if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                entriesByStem.Remove(record.Stem);
                Save(entriesByStem.Values);
                return false;
            }

            if (nowUtc - entry.LastNoMatchUtc > NoMatchLifetime)
            {
                entriesByStem.Remove(record.Stem);
                Save(entriesByStem.Values);
                return false;
            }

            return true;
        }
    }

    public void RecordNoMatch(MeetingOutputRecord record, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var entriesByStem = LoadEntriesByStem();
            entriesByStem[record.Stem] = new MeetingsAttendeeBackfillCacheEntry(
                record.Stem,
                BuildFingerprint(record),
                nowUtc);
            Save(entriesByStem.Values);
        }
    }

    public void Clear(MeetingOutputRecord record)
    {
        lock (_gate)
        {
            var entriesByStem = LoadEntriesByStem();
            if (!entriesByStem.Remove(record.Stem))
            {
                return;
            }

            Save(entriesByStem.Values);
        }
    }

    private Dictionary<string, MeetingsAttendeeBackfillCacheEntry> LoadEntriesByStem()
    {
        if (_entriesByStem is not null)
        {
            return _entriesByStem;
        }

        if (!File.Exists(_cachePath))
        {
            _entriesByStem = new Dictionary<string, MeetingsAttendeeBackfillCacheEntry>(StringComparer.OrdinalIgnoreCase);
            return _entriesByStem;
        }

        try
        {
            var contents = File.ReadAllText(_cachePath).Trim();
            if (string.IsNullOrWhiteSpace(contents))
            {
                _entriesByStem = new Dictionary<string, MeetingsAttendeeBackfillCacheEntry>(StringComparer.OrdinalIgnoreCase);
                return _entriesByStem;
            }

            var document = JsonSerializer.Deserialize<MeetingsAttendeeBackfillCacheDocument>(contents, SerializerOptions);
            _entriesByStem = (document?.Entries ?? Array.Empty<MeetingsAttendeeBackfillCacheEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Stem) && !string.IsNullOrWhiteSpace(entry.Fingerprint))
                .ToDictionary(entry => entry.Stem, StringComparer.OrdinalIgnoreCase);
            return _entriesByStem;
        }
        catch
        {
            _entriesByStem = new Dictionary<string, MeetingsAttendeeBackfillCacheEntry>(StringComparer.OrdinalIgnoreCase);
            return _entriesByStem;
        }
    }

    private void Save(IEnumerable<MeetingsAttendeeBackfillCacheEntry> entries)
    {
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new InvalidOperationException("Cache path must include a parent directory.");
        Directory.CreateDirectory(directory);

        var orderedEntries = entries
            .OrderBy(entry => entry.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var document = new MeetingsAttendeeBackfillCacheDocument(orderedEntries);
        var tempPath = _cachePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(document, SerializerOptions));

        if (File.Exists(_cachePath))
        {
            File.Replace(tempPath, _cachePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, _cachePath);
    }

    private static string BuildFingerprint(MeetingOutputRecord record)
    {
        var endValue = record.Duration is { } duration && duration > TimeSpan.Zero
            ? record.StartedAtUtc.Add(duration).UtcDateTime.ToString("O")
            : string.Empty;
        var manifestWriteUtc = TryGetLastWriteTimeUtc(record.ManifestPath);
        var jsonWriteUtc = TryGetLastWriteTimeUtc(record.JsonPath);

        return string.Join(
            "|",
            record.Platform,
            record.StartedAtUtc.UtcDateTime.ToString("O"),
            endValue,
            manifestWriteUtc,
            jsonWriteUtc);
    }

    private static string TryGetLastWriteTimeUtc(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? File.GetLastWriteTimeUtc(path).ToString("O")
            : string.Empty;
    }
}

internal sealed record MeetingsAttendeeBackfillCacheDocument(
    IReadOnlyList<MeetingsAttendeeBackfillCacheEntry> Entries);

internal sealed record MeetingsAttendeeBackfillCacheEntry(
    string Stem,
    string Fingerprint,
    DateTimeOffset LastNoMatchUtc);
