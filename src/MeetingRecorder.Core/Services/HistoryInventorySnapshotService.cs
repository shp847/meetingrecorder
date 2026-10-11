using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record HistoryInventoryEntry(
    string MeetingId,
    bool HasAudio,
    bool HasManifest,
    bool HasReadyMarker,
    bool HasTranscriptJson,
    bool HasTranscriptMarkdown,
    bool HasRecoverableSource,
    string? AudioSha256);

public sealed record HistoryInventorySnapshot(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<HistoryInventoryEntry> Entries);

/// <summary>Creates metadata-only recovery evidence without reading transcript text or persisting local paths.</summary>
public sealed class HistoryInventorySnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly MeetingOutputCatalogService _catalog;

    public HistoryInventorySnapshotService(MeetingOutputCatalogService catalog) => _catalog = catalog;

    public HistoryInventorySnapshot Create(string audioOutputDir, string transcriptOutputDir, string? workDir)
    {
        var entries = _catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .Select(record => new HistoryInventoryEntry(
                HashText(record.Stem),
                File.Exists(record.AudioPath),
                File.Exists(record.ManifestPath),
                File.Exists(record.ReadyMarkerPath),
                File.Exists(record.JsonPath),
                File.Exists(record.MarkdownPath),
                HasRecoverableSource(record),
                File.Exists(record.AudioPath) ? HashFile(record.AudioPath!) : null))
            .OrderBy(entry => entry.MeetingId, StringComparer.Ordinal)
            .ToArray();

        return new HistoryInventorySnapshot(1, DateTimeOffset.UtcNow, entries);
    }

    public async Task WriteAtomicAsync(HistoryInventorySnapshot snapshot, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath)));
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool HasRecoverableSource(MeetingOutputRecord record) => File.Exists(record.AudioPath) || File.Exists(record.ManifestPath);
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
