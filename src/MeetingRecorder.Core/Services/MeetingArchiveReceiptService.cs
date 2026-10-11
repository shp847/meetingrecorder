using System.Security.Cryptography;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record MeetingArchiveReceiptArtifact(
    string ArtifactClass,
    string OriginalPath,
    string ArchivedFileName,
    long SizeBytes,
    string Sha256);

public sealed record MeetingArchiveReceipt(
    int SchemaVersion,
    string MeetingStem,
    string Category,
    DateTimeOffset ArchivedAtUtc,
    IReadOnlyList<MeetingArchiveReceiptArtifact> Artifacts);

/// <summary>Writes and restores only complete, user-requested published-artifact archives.</summary>
public sealed class MeetingArchiveReceiptService
{
    public const string ReceiptFileName = "archive-receipt.json";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<MeetingArchiveReceipt> ArchiveAsync(
        string meetingStem,
        string category,
        IEnumerable<(string ArtifactClass, string? SourcePath)> artifactSources,
        string archiveDirectory,
        CancellationToken cancellationToken = default)
    {
        var sources = artifactSources
            .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
            .Select(item => (item.ArtifactClass, SourcePath: Path.GetFullPath(item.SourcePath!)))
            .OrderBy(item => item.ArtifactClass, StringComparer.Ordinal)
            .ToArray();
        if (sources.Length == 0)
        {
            throw new InvalidOperationException("No published meeting artifacts are available to archive.");
        }

        var destinationDirectory = Path.Combine(archiveDirectory, category, meetingStem);
        Directory.CreateDirectory(destinationDirectory);
        var receiptPath = Path.Combine(destinationDirectory, ReceiptFileName);
        if (File.Exists(receiptPath))
        {
            throw new IOException("This archive already has a recovery receipt.");
        }

        var artifacts = new List<MeetingArchiveReceiptArtifact>(sources.Length);
        foreach (var source in sources)
        {
            var archivedFileName = Path.GetFileName(source.SourcePath);
            var destination = Path.Combine(destinationDirectory, archivedFileName);
            if (File.Exists(destination))
            {
                throw new IOException("Archive destination already contains a meeting artifact.");
            }

            artifacts.Add(new(
                source.ArtifactClass,
                source.SourcePath,
                archivedFileName,
                new FileInfo(source.SourcePath).Length,
                await ComputeSha256Async(source.SourcePath, cancellationToken)));
        }

        var moved = new List<(string Source, string Destination)>();
        try
        {
            foreach (var artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(destinationDirectory, artifact.ArchivedFileName);
                File.Move(artifact.OriginalPath, destination);
                moved.Add((artifact.OriginalPath, destination));
            }

            var receipt = new MeetingArchiveReceipt(SchemaVersion, meetingStem, category, DateTimeOffset.UtcNow, artifacts);
            await WriteReceiptAsync(receiptPath, receipt, cancellationToken);
            return receipt;
        }
        catch
        {
            RollBackMoves(moved, reverse: true);
            throw;
        }
    }

    public async Task<MeetingArchiveReceipt> RestoreAsync(string receiptPath, CancellationToken cancellationToken = default)
    {
        var receipt = await ReadReceiptAsync(receiptPath, cancellationToken);
        var archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(receiptPath))
            ?? throw new InvalidDataException("Archive receipt has no containing directory.");
        if (!IsManualRecoveryReceipt(receipt, archiveDirectory))
        {
            throw new InvalidDataException("Archive receipt is not valid for recovery.");
        }

        foreach (var artifact in receipt.Artifacts)
        {
            var archivedPath = Path.Combine(archiveDirectory, artifact.ArchivedFileName);
            if (!File.Exists(archivedPath) ||
                new FileInfo(archivedPath).Length != artifact.SizeBytes ||
                !string.Equals(await ComputeSha256Async(archivedPath, cancellationToken), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Archive artifact drift prevents safe recovery.");
            }

            if (File.Exists(artifact.OriginalPath) || Directory.Exists(artifact.OriginalPath))
            {
                throw new IOException("Recovery destination already contains a meeting artifact.");
            }
        }

        var moved = new List<(string Source, string Destination)>();
        try
        {
            foreach (var artifact in receipt.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var archivedPath = Path.Combine(archiveDirectory, artifact.ArchivedFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(artifact.OriginalPath)!);
                File.Move(archivedPath, artifact.OriginalPath);
                moved.Add((archivedPath, artifact.OriginalPath));
            }

            return receipt;
        }
        catch
        {
            RollBackMoves(moved, reverse: false);
            throw;
        }
    }

    public async Task<MeetingArchiveReceipt> ReadReceiptAsync(string receiptPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(receiptPath);
        return await JsonSerializer.DeserializeAsync<MeetingArchiveReceipt>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Archive receipt is empty or malformed.");
    }

    public async Task<IReadOnlyList<(string ReceiptPath, MeetingArchiveReceipt Receipt)>> FindReceiptsAsync(
        string archiveRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(archiveRoot)) return Array.Empty<(string, MeetingArchiveReceipt)>();

        var receipts = new List<(string ReceiptPath, MeetingArchiveReceipt Receipt)>();
        foreach (var receiptPath in Directory.EnumerateFiles(archiveRoot, ReceiptFileName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var receipt = await ReadReceiptAsync(receiptPath, cancellationToken);
                var receiptDirectory = Path.GetDirectoryName(Path.GetFullPath(receiptPath));
                if (receiptDirectory is not null &&
                    IsManualRecoveryReceipt(receipt, receiptDirectory) &&
                    await HasCompleteArchivedArtifactsAsync(receipt, receiptDirectory, cancellationToken))
                {
                    receipts.Add((receiptPath, receipt));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                // A malformed local receipt is intentionally not discoverable as recoverable.
            }
        }

        return receipts
            .OrderByDescending(item => item.Receipt.ArchivedAtUtc)
            .ThenBy(item => item.Receipt.MeetingStem, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteReceiptAsync(string receiptPath, MeetingArchiveReceipt receipt, CancellationToken cancellationToken)
    {
        var temporaryPath = receiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, receipt, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, receiptPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void RollBackMoves(IEnumerable<(string Source, string Destination)> moves, bool reverse)
    {
        foreach (var move in moves.Reverse())
        {
            var source = reverse ? move.Destination : move.Source;
            var destination = reverse ? move.Source : move.Destination;
            if (!File.Exists(source) || File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool IsManualRecoveryReceipt(MeetingArchiveReceipt receipt, string archiveDirectory)
    {
        if (receipt.SchemaVersion != SchemaVersion ||
            !string.Equals(receipt.Category, "manual-archive", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(receipt.MeetingStem) ||
            receipt.Artifacts.Count == 0)
        {
            return false;
        }

        return receipt.Artifacts.All(artifact =>
            !string.IsNullOrWhiteSpace(artifact.ArtifactClass) &&
            Path.IsPathFullyQualified(artifact.OriginalPath) &&
            !string.IsNullOrWhiteSpace(artifact.ArchivedFileName) &&
            string.Equals(artifact.ArchivedFileName, Path.GetFileName(artifact.ArchivedFileName), StringComparison.Ordinal) &&
            !Path.IsPathRooted(artifact.ArchivedFileName) &&
            artifact.SizeBytes >= 0 &&
            !string.IsNullOrWhiteSpace(artifact.Sha256) &&
            Path.GetFullPath(Path.Combine(archiveDirectory, artifact.ArchivedFileName))
                .StartsWith(Path.GetFullPath(archiveDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> HasCompleteArchivedArtifactsAsync(
        MeetingArchiveReceipt receipt,
        string archiveDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in receipt.Artifacts)
        {
            var archivedPath = Path.Combine(archiveDirectory, artifact.ArchivedFileName);
            if (!File.Exists(archivedPath) ||
                new FileInfo(archivedPath).Length != artifact.SizeBytes ||
                !string.Equals(await ComputeSha256Async(archivedPath, cancellationToken), artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
