using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace MeetingRecorder.Core.Services;

public sealed record AutomaticRecoveryArtifact(string OriginalPath, string BackupFile, long Length, string Sha256);
public sealed record AutomaticRecoveryReceipt(
    int Version, string Fingerprint, string Action, IReadOnlyList<string> MeetingStems,
    DateTimeOffset CreatedAtUtc, string State, IReadOnlyList<AutomaticRecoveryArtifact> Artifacts);
public sealed record AutomaticRecoverySnapshot(string DirectoryPath, AutomaticRecoveryReceipt Receipt);
public sealed record AutomaticRecoveryResult(IReadOnlyList<string> RestoredOriginals, IReadOnlyList<string> ConflictCopies);

/// <summary>Creates verified local copies before automatic work can alter published artifacts.</summary>
public sealed class AutomaticMeetingRecoveryService
{
    private const string ReceiptFile = "recovery-receipt.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetRoot(string archiveRoot) => Path.Combine(archiveRoot, "Automatic Recovery");

    public static string? ComputeInputRevision(IEnumerable<string> paths, string readinessKey)
    {
        try
        {
            var normalized = paths.Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (normalized.Length == 0) return null;
            var source = new StringBuilder(readinessKey);
            foreach (var path in normalized)
            {
                var info = new FileInfo(path);
                source.Append('\n').Append(path).Append('|');
                if (!info.Exists) source.Append("missing");
                else source.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<AutomaticRecoverySnapshot> PrepareAsync(
        string archiveRoot, string fingerprint, string action, IReadOnlyList<string> meetingStems,
        IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var sources = sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sources.Length == 0 || sources.Any(path => !File.Exists(path)))
            throw new IOException("Automatic recovery requires all selected artifacts to be available.");

        var root = Path.GetFullPath(GetRoot(archiveRoot));
        Directory.CreateDirectory(root);
        var driveRoot = Path.GetPathRoot(root);
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            var available = new DriveInfo(driveRoot).AvailableFreeSpace;
            var required = sources.Sum(path => new FileInfo(path).Length);
            const long reserveBytes = 512L * 1024 * 1024;
            if (available < reserveBytes || required > available - reserveBytes)
                throw new IOException("There is not enough free space for a verified recovery copy.");
        }
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var artifacts = new List<AutomaticRecoveryArtifact>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupName = $"{artifacts.Count:D4}.backup";
            var backupPath = Path.Combine(directory, backupName);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, cancellationToken);
            var backupHash = await HashAsync(backupPath, cancellationToken);
            var length = new FileInfo(backupPath).Length;
            if (new FileInfo(source).Length != length ||
                !string.Equals(await HashAsync(source, cancellationToken), backupHash, StringComparison.Ordinal))
                throw new IOException("A meeting artifact changed while its recovery copy was being made.");
            artifacts.Add(new AutomaticRecoveryArtifact(source, backupName, length, backupHash));
        }

        var receipt = new AutomaticRecoveryReceipt(1, fingerprint, action, meetingStems.ToArray(), DateTimeOffset.UtcNow, "Prepared", artifacts);
        await WriteReceiptAsync(directory, receipt, cancellationToken);
        return new AutomaticRecoverySnapshot(directory, receipt);
    }

    public async Task<bool> AreOriginalsUnchangedAsync(AutomaticRecoverySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ValidateReceipt(snapshot);
        foreach (var item in snapshot.Receipt.Artifacts)
        {
            if (!await MatchesAsync(item.OriginalPath, item, cancellationToken) ||
                !await MatchesAsync(Path.Combine(snapshot.DirectoryPath, item.BackupFile), item, cancellationToken)) return false;
        }
        return true;
    }

    public async Task<bool> CanRestoreAsync(AutomaticRecoverySnapshot snapshot, IEnumerable<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        ValidateReceipt(snapshot);
        var roots = allowedRoots.Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();
        foreach (var item in snapshot.Receipt.Artifacts)
            if (!roots.Any(root => item.OriginalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) ||
                !await MatchesAsync(Path.Combine(snapshot.DirectoryPath, item.BackupFile), item, cancellationToken)) return false;
        return true;
    }

    public async Task<bool> AreCopiesVerifiedAsync(
        AutomaticRecoverySnapshot snapshot, IReadOnlyDictionary<string, string> copies, CancellationToken cancellationToken = default)
    {
        ValidateReceipt(snapshot);
        foreach (var item in snapshot.Receipt.Artifacts)
        {
            if (!copies.TryGetValue(item.OriginalPath, out var copy) ||
                !await MatchesAsync(copy, item, cancellationToken) ||
                !await MatchesAsync(Path.Combine(snapshot.DirectoryPath, item.BackupFile), item, cancellationToken)) return false;
        }
        return true;
    }

    public async Task<AutomaticRecoverySnapshot> SetStateAsync(
        AutomaticRecoverySnapshot snapshot, string state, CancellationToken cancellationToken = default)
    {
        ValidateReceipt(snapshot);
        if (state is not ("Prepared" or "Dispatched" or "Completed" or "Restored" or "Review"))
            throw new ArgumentOutOfRangeException(nameof(state));
        var updated = snapshot.Receipt with { State = state };
        await WriteReceiptAsync(snapshot.DirectoryPath, updated, cancellationToken);
        return snapshot with { Receipt = updated };
    }

    public async Task<AutomaticRecoveryResult> RestoreAsync(
        AutomaticRecoverySnapshot snapshot, IEnumerable<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        if (!await CanRestoreAsync(snapshot, allowedRoots, cancellationToken))
            throw new InvalidDataException("Recovery copy or original location cannot be verified.");

        var restored = new List<string>();
        var conflicts = new List<string>();
        for (var index = 0; index < snapshot.Receipt.Artifacts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = snapshot.Receipt.Artifacts[index];
            if (await MatchesAsync(item.OriginalPath, item, cancellationToken)) continue;
            var destination = item.OriginalPath;
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                destination = Path.Combine(snapshot.DirectoryPath, "Recovered", $"{index:D4}-{Path.GetFileName(item.OriginalPath)}");
                conflicts.Add(destination);
            }
            else
            {
                restored.Add(destination);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                if (!await MatchesAsync(destination, item, cancellationToken))
                    throw new IOException("A recovery destination already contains different data.");
                continue;
            }
            var temp = destination + ".recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = new FileStream(Path.Combine(snapshot.DirectoryPath, item.BackupFile), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await input.CopyToAsync(output, cancellationToken);
                if (!await MatchesAsync(temp, item, cancellationToken)) throw new IOException("Recovery copy did not verify.");
                File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return new AutomaticRecoveryResult(restored, conflicts);
    }

    public async Task<IReadOnlyList<AutomaticRecoverySnapshot>> FindAsync(string archiveRoot, CancellationToken cancellationToken = default)
    {
        var root = GetRoot(archiveRoot);
        if (!Directory.Exists(root)) return [];
        var snapshots = new List<AutomaticRecoverySnapshot>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, ReceiptFile);
            if (!File.Exists(path)) continue;
            try
            {
                var receipt = JsonSerializer.Deserialize<AutomaticRecoveryReceipt>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
                if (receipt is null) continue;
                var snapshot = new AutomaticRecoverySnapshot(directory, receipt);
                ValidateReceipt(snapshot);
                snapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                // An incomplete or malformed copy cannot authorize mutation or recovery.
            }
        }
        return snapshots.OrderByDescending(snapshot => snapshot.Receipt.CreatedAtUtc).ToArray();
    }

    private static void ValidateReceipt(AutomaticRecoverySnapshot snapshot)
    {
        if (snapshot.Receipt.Version != 1 || string.IsNullOrWhiteSpace(snapshot.Receipt.Fingerprint) || snapshot.Receipt.MeetingStems is null ||
            snapshot.Receipt.Artifacts is null || snapshot.Receipt.Artifacts.Count == 0 || snapshot.Receipt.Artifacts.Any(item =>
                item is null || string.IsNullOrWhiteSpace(item.OriginalPath) ||
                !Path.IsPathFullyQualified(item.OriginalPath) ||
                !string.Equals(Path.GetFullPath(item.OriginalPath), item.OriginalPath, StringComparison.OrdinalIgnoreCase) || item.Length < 0 ||
                string.IsNullOrWhiteSpace(item.Sha256) || string.IsNullOrWhiteSpace(item.BackupFile) ||
                !string.Equals(item.BackupFile, Path.GetFileName(item.BackupFile), StringComparison.Ordinal) ||
                item.BackupFile.Length <= 7 || !item.BackupFile.EndsWith(".backup", StringComparison.Ordinal) ||
                !item.BackupFile[..^7].All(char.IsAsciiDigit)) ||
            snapshot.Receipt.Artifacts.Select(item => item.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Receipt.Artifacts.Count ||
            snapshot.Receipt.Artifacts.Select(item => item.BackupFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Receipt.Artifacts.Count)
            throw new InvalidDataException("Recovery receipt is invalid.");
    }

    private static async Task<bool> MatchesAsync(string path, AutomaticRecoveryArtifact artifact, CancellationToken cancellationToken) =>
        File.Exists(path) && new FileInfo(path).Length == artifact.Length &&
        string.Equals(await HashAsync(path, cancellationToken), artifact.Sha256, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task WriteReceiptAsync(string directory, AutomaticRecoveryReceipt receipt, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ReceiptFile);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
