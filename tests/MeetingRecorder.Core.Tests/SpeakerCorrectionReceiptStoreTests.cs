using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerCorrectionReceiptStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppendOrReplaceAsync_Persists_One_Receipt_Per_Operation()
    {
        var store = new SpeakerCorrectionReceiptStore(Path.Combine(_root, "receipts.json"));
        await store.AppendOrReplaceAsync(Receipt("operation-1", SpeakerCorrectionLearningDisposition.Pending));
        await store.AppendOrReplaceAsync(Receipt("operation-1", SpeakerCorrectionLearningDisposition.Completed));

        var receipt = await store.FindAsync("operation-1");

        Assert.NotNull(receipt);
        Assert.Equal(SpeakerCorrectionLearningDisposition.Completed, receipt.LearningDisposition);
    }

    [Fact]
    public async Task FindAsync_Ignores_Malformed_Local_Receipt_File()
    {
        var path = Path.Combine(_root, "receipts.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "not json");

        Assert.Null(await new SpeakerCorrectionReceiptStore(path).FindAsync("operation-1"));
    }

    private static SpeakerCorrectionReceipt Receipt(string operationId, SpeakerCorrectionLearningDisposition disposition) => new(
        SpeakerCorrectionReceipt.CurrentSchemaVersion,
        operationId,
        "meeting",
        "revision",
        "speaker-1",
        SpeakerCorrectionAction.ConfirmName,
        "fingerprint",
        SpeakerNameSource.None,
        null,
        SpeakerNameSource.UserEdited,
        null,
        disposition,
        DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
