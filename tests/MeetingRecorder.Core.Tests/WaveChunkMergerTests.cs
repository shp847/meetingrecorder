using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class WaveChunkMergerTests
{
    [Fact]
    public async Task MergeAsync_Throws_When_Sequential_Wave_Chunks_Do_Not_Share_A_Format()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstChunk = Path.Combine(root, "chunk-1.wav");
        var secondChunk = Path.Combine(root, "chunk-2.wav");
        CreateWave(firstChunk, new WaveFormat(16_000, 16, 1), amplitude: 1000);
        CreateWave(secondChunk, new WaveFormat(48_000, 16, 2), amplitude: 1000);

        var merger = new WaveChunkMerger();
        var outputPath = Path.Combine(root, "merged.wav");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            merger.MergeAsync([firstChunk, secondChunk], outputPath));

        Assert.Contains("share the same format", exception.Message, StringComparison.Ordinal);
    }

    private static void CreateWave(string path, WaveFormat format, short amplitude)
    {
        using var writer = new WaveFileWriter(path, format);
        var frameCount = format.SampleRate / 5;
        var bytesPerSample = format.BitsPerSample / 8;
        var buffer = new byte[frameCount * format.Channels * bytesPerSample];

        for (var sampleIndex = 0; sampleIndex < frameCount * format.Channels; sampleIndex++)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(sampleIndex * bytesPerSample, bytesPerSample), amplitude);
        }

        writer.Write(buffer, 0, buffer.Length);
    }
}
