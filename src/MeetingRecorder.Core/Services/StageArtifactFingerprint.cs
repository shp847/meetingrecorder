using System.Security.Cryptography;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Ephemeral local currentness proof for a requested enrichment pass. Paths and
/// hashes stay in-process: neither is shown to users, logged, or persisted as
/// a replacement for the durable staged-work revision token.
/// </summary>
internal sealed class StageArtifactFingerprint
{
    private readonly IReadOnlyList<Entry> _entries;

    private StageArtifactFingerprint(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
    }

    public static StageArtifactFingerprint Capture(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Length == 0 || paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Stage artifact identity was incomplete.");
        }

        return new StageArtifactFingerprint(paths.Select(CaptureEntry).ToArray());
    }

    public bool IsCurrent() => _entries.All(entry =>
    {
        try
        {
            return File.Exists(entry.Path) &&
                   string.Equals(ComputeContentHash(entry.Path), entry.ContentHash, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    });

    private static Entry CaptureEntry(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A current stage artifact was unavailable.");
        }

        return new Entry(path, ComputeContentHash(path));
    }

    private static string ComputeContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record Entry(string Path, string ContentHash);
}
