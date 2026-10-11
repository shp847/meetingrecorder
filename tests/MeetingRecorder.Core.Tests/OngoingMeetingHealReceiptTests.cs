using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class OngoingMeetingHealReceiptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Store_Persists_And_Loads_Current_Receipt_Atomically()
    {
        var path = Path.Combine(_root, "healing", "receipt.json");
        var store = new OngoingMeetingHealReceiptStore(path);
        var receipt = new OngoingMeetingHealReceipt(1, "first", "second", DateTimeOffset.UtcNow, "same-strong-identity", "archive");

        await store.SaveAsync(receipt);
        var restored = await store.TryLoadAsync();

        Assert.Equal(receipt, restored);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Store_Rejects_Corrupt_Or_Unknown_Receipt_Without_Throwing()
    {
        var path = Path.Combine(_root, "receipt.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new OngoingMeetingHealReceiptStore(path);

        Assert.Null(await store.TryLoadAsync());

        await File.WriteAllTextAsync(path, "{\"schemaVersion\":99}");
        Assert.Null(await store.TryLoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
