using MeetingRecorder.App;
using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class ContinuityRepairReceiptNameTests
{
    [Fact]
    public async Task Receipt_Name_Is_Valid_And_Bounded_For_Realistic_Merge_Fingerprints()
    {
        var fingerprint = "Merge|meeting|continuity-repair-v2-split-pair|" + new string('a', 600) + "|title: / ";
        var name = MainWindow.BuildContinuityRepairReceiptFileName(fingerprint);
        Assert.EndsWith(".continuity-repair.json", name, StringComparison.Ordinal);
        Assert.DoesNotContain(name, character => Path.GetInvalidFileNameChars().Contains(character));
        Assert.True(name.Length < 100);
        Assert.Equal(name, MainWindow.BuildContinuityRepairReceiptFileName(fingerprint));
        Assert.NotEqual(name, MainWindow.BuildContinuityRepairReceiptFileName(fingerprint + "different"));

        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var receipt = Path.Combine(root, name);
            await File.WriteAllTextAsync(receipt, "{}");
            Assert.True(File.Exists(receipt));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
