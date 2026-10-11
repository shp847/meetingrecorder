using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerRepairAuditEntry(
    string MeetingId,
    string ArtifactRevision,
    int UserEnteredNameCount,
    int ProfileAttributionCount,
    DateTimeOffset QueuedAtUtc,
    string State);

/// <summary>Bounded local audit of explicit speaker-label repair intent; never stores names, transcript text, or audio paths.</summary>
public sealed class SpeakerRepairAuditStore
{
    private const int MaximumEntries = 300;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SpeakerRepairAuditStore(string? path = null) =>
        _path = Path.GetFullPath(path ?? Path.Combine(AppDataPaths.GetAppRoot(), "speaker-corrections", "repair-audit.json"));

    public async Task RecordQueuedAsync(SpeakerRepairAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var entries = (await LoadAsync(cancellationToken))
            .Where(existing => !string.Equals(existing.MeetingId, entry.MeetingId, StringComparison.Ordinal) ||
                               !string.Equals(existing.ArtifactRevision, entry.ArtifactRevision, StringComparison.Ordinal))
            .Append(entry)
            .OrderBy(existing => existing.QueuedAtUtc)
            .TakeLast(MaximumEntries)
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<IReadOnlyList<SpeakerRepairAuditEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return Array.Empty<SpeakerRepairAuditEntry>();
        try
        {
            return JsonSerializer.Deserialize<SpeakerRepairAuditEntry[]>(await File.ReadAllTextAsync(_path, cancellationToken), JsonOptions) ?? [];
        }
        catch (JsonException) { return Array.Empty<SpeakerRepairAuditEntry>(); }
    }
}
