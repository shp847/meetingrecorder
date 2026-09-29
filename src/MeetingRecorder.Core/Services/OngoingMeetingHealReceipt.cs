using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record OngoingMeetingHealReceipt(
    int SchemaVersion,
    string PredecessorSessionId,
    string SuccessorSessionId,
    DateTimeOffset HealedAtUtc,
    string ReasonCode,
    string ArchiveDirectory)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed class OngoingMeetingHealReceiptStore
{
    private readonly string _path;
    public OngoingMeetingHealReceiptStore(string path) => _path = Path.GetFullPath(path);

    public async Task SaveAsync(OngoingMeetingHealReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SchemaVersion != OngoingMeetingHealReceipt.CurrentSchemaVersion || string.IsNullOrWhiteSpace(receipt.ReasonCode))
            throw new ArgumentException("Invalid heal receipt.", nameof(receipt));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(receipt), cancellationToken);
        File.Move(temp, _path, true);
    }

    public async Task<OngoingMeetingHealReceipt?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<OngoingMeetingHealReceipt>(await File.ReadAllTextAsync(_path, cancellationToken));
            return receipt?.SchemaVersion == OngoingMeetingHealReceipt.CurrentSchemaVersion ? receipt : null;
        }
        catch (JsonException) { return null; }
    }
}
