using System.Collections.Concurrent;
using System.Text.Json;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum SpeakerCorrectionLearningDisposition
{
    NotRequested = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Local, bounded audit of speaker-name writes. It is intentionally separate
/// from published transcript artifacts and never writes transcript text/audio.
/// </summary>
public sealed record SpeakerCorrectionReceipt(
    int SchemaVersion,
    string OperationId,
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    SpeakerCorrectionAction Action,
    string PriorFingerprint,
    SpeakerNameSource PriorNameSource,
    string? PriorProfileId,
    SpeakerNameSource NextNameSource,
    string? NextProfileId,
    SpeakerCorrectionLearningDisposition LearningDisposition,
    DateTimeOffset RecordedAtUtc,
    string? PolicyVersion = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed class SpeakerCorrectionReceiptStore
{
    private const int MaximumReceipts = 200;
    private const int ReplaceAttemptCount = 4;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    public SpeakerCorrectionReceiptStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            AppDataPaths.GetAppRoot(), "speaker-corrections", "receipts.json"));
    }

    public async Task<SpeakerCorrectionReceipt?> FindAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return null;
        }

        return (await LoadAsync(cancellationToken))
            .LastOrDefault(receipt => string.Equals(receipt.OperationId, operationId.Trim(), StringComparison.Ordinal));
    }

    public async Task AppendOrReplaceAsync(SpeakerCorrectionReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SchemaVersion != SpeakerCorrectionReceipt.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(receipt.OperationId) ||
            string.IsNullOrWhiteSpace(receipt.MeetingStableIdentity) ||
            string.IsNullOrWhiteSpace(receipt.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(receipt.SpeakerId) ||
            string.IsNullOrWhiteSpace(receipt.PriorFingerprint))
        {
            throw new ArgumentException("Speaker correction receipt is incomplete.", nameof(receipt));
        }

        var writeLock = WriteLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            // Read after taking the path lock so parallel correction actions do
            // not overwrite each other's receipt. Windows scanners can briefly
            // hold the destination, so promotion is boundedly retried below.
            var receipts = (await LoadAsync(cancellationToken))
                .Where(existing => !string.Equals(existing.OperationId, receipt.OperationId, StringComparison.Ordinal))
                .Append(receipt)
                .OrderBy(entry => entry.RecordedAtUtc)
                .TakeLast(MaximumReceipts)
                .ToArray();
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Receipt store has no directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipts, JsonOptions), cancellationToken);
                await PromoteTemporaryFileAsync(temporaryPath, cancellationToken);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task PromoteTemporaryFileAsync(string temporaryPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, _path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < ReplaceAttemptCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<SpeakerCorrectionReceipt>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<SpeakerCorrectionReceipt>();
        }

        try
        {
            var receipts = JsonSerializer.Deserialize<IReadOnlyList<SpeakerCorrectionReceipt>>(
                await File.ReadAllTextAsync(_path, cancellationToken), JsonOptions);
            return receipts?
                .Where(receipt => receipt.SchemaVersion == SpeakerCorrectionReceipt.CurrentSchemaVersion)
                .Where(receipt => !string.IsNullOrWhiteSpace(receipt.OperationId))
                .ToArray() ?? Array.Empty<SpeakerCorrectionReceipt>();
        }
        catch (JsonException)
        {
            return Array.Empty<SpeakerCorrectionReceipt>();
        }
    }
}
