using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class WavInputInspectorTests
{
    [Fact]
    public async Task Inspect_OversizedPcmDataHeader_UsesPhysicalDurationAndCreatesTemporaryRepair()
    {
        var root = CreateRoot();
        var sourcePath = Path.Combine(root, "source.wav");
        var normalizedPath = Path.Combine(root, "normalized.wav");
        CreatePcmWave(sourcePath, TimeSpan.FromSeconds(2));
        var dataSizeOffset = FindDataSizeOffset(sourcePath);
        await using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = dataSizeOffset;
            await stream.WriteAsync(BitConverter.GetBytes(uint.MaxValue));
        }

        var inspector = new WavInputInspector();
        var inspection = inspector.Inspect(sourcePath);

        Assert.Equal(WavInputDisposition.NormalizeTemporaryCopy, inspection.Disposition);
        Assert.InRange(inspection.EffectiveDuration, TimeSpan.FromSeconds(1.99), TimeSpan.FromSeconds(2.01));
        Assert.NotNull(inspection.DiagnosticMessage);

        var repaired = await inspector.CreateNormalizedTemporaryCopyAsync(sourcePath, normalizedPath, inspection);

        Assert.Equal(normalizedPath, repaired);
        using var reader = new WaveFileReader(repaired);
        Assert.InRange(reader.TotalTime, TimeSpan.FromSeconds(1.99), TimeSpan.FromSeconds(2.01));
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        source.Position = dataSizeOffset;
        var sourceHeader = new byte[4];
        _ = source.Read(sourceHeader, 0, sourceHeader.Length);
        Assert.Equal(uint.MaxValue, BitConverter.ToUInt32(sourceHeader));
    }

    [Fact]
    public void Inspect_UnalignedPhysicalPcmPayload_RequiresManualReview()
    {
        var root = CreateRoot();
        var sourcePath = Path.Combine(root, "unaligned.wav");
        CreatePcmWave(sourcePath, TimeSpan.FromSeconds(1));
        using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(stream.Length - 1);
        }

        var exception = Assert.Throws<WavInputManualReviewException>(() => new WavInputInspector().Inspect(sourcePath));

        Assert.Contains("manual review", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreatePcmWave(string path, TimeSpan duration)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1));
        writer.Write(new byte[(int)(duration.TotalSeconds * 16_000 * 2)], 0, (int)(duration.TotalSeconds * 16_000 * 2));
    }

    private static long FindDataSizeOffset(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var index = 0; index <= bytes.Length - 8; index++)
        {
            if (bytes[index] == (byte)'d' &&
                bytes[index + 1] == (byte)'a' &&
                bytes[index + 2] == (byte)'t' &&
                bytes[index + 3] == (byte)'a')
            {
                return index + 4;
            }
        }

        throw new InvalidOperationException("The test WAV did not contain a data chunk.");
    }
}
