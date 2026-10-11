using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record PastRematchReceipt(
    int SchemaVersion,
    string OperationId,
    string MeetingStableIdentity,
    string ArtifactRevision,
    PastRematchOutcome Outcome,
    string Reason,
    DateTimeOffset RecordedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Bounded local receipt ledger for explicit historic name-rematch operations.</summary>
public sealed class PastRematchReceiptStore
{
    private const int MaximumReceipts = 300;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public PastRematchReceiptStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(AppDataPaths.GetAppRoot(), "speaker-corrections", "past-rematches.json"));
    }

    public async Task<PastRematchReceipt?> FindAsync(string operationId, string meetingStableIdentity, CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).LastOrDefault(receipt =>
            string.Equals(receipt.OperationId, operationId, StringComparison.Ordinal) &&
            string.Equals(receipt.MeetingStableIdentity, meetingStableIdentity, StringComparison.Ordinal));

    public async Task AppendOrReplaceAsync(PastRematchReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SchemaVersion != PastRematchReceipt.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(receipt.OperationId) ||
            string.IsNullOrWhiteSpace(receipt.MeetingStableIdentity) ||
            string.IsNullOrWhiteSpace(receipt.ArtifactRevision))
        {
            throw new ArgumentException("Past rematch receipt is incomplete.", nameof(receipt));
        }

        var records = (await LoadAsync(cancellationToken))
            .Where(existing => !string.Equals(existing.OperationId, receipt.OperationId, StringComparison.Ordinal) ||
                               !string.Equals(existing.MeetingStableIdentity, receipt.MeetingStableIdentity, StringComparison.Ordinal))
            .Append(receipt)
            .OrderBy(entry => entry.RecordedAtUtc)
            .TakeLast(MaximumReceipts)
            .ToArray();
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Rematch receipt store has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(records, JsonOptions), cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task<IReadOnlyList<PastRematchReceipt>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return Array.Empty<PastRematchReceipt>();
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<PastRematchReceipt>>(
                await File.ReadAllTextAsync(_path, cancellationToken), JsonOptions)?
                .Where(receipt => receipt.SchemaVersion == PastRematchReceipt.CurrentSchemaVersion)
                .ToArray() ?? Array.Empty<PastRematchReceipt>();
        }
        catch (JsonException)
        {
            return Array.Empty<PastRematchReceipt>();
        }
    }
}
