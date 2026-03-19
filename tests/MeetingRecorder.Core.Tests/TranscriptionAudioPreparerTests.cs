using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class TranscriptionAudioPreparerTests
{
    [Fact]
    public async Task PrepareAsync_Converts_Float_Stereo_Audio_To_Whisper_Compatible_Pcm_Mono()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "input.wav");
        var outputPath = Path.Combine(root, "prepared.wav");
        CreateFloatStereoWave(sourcePath, TimeSpan.FromMilliseconds(500));

        var preparer = new TranscriptionAudioPreparer();

        await preparer.PrepareAsync(sourcePath, outputPath);

        using var reader = new WaveFileReader(outputPath);
        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(16_000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
    }

    private static void CreateFloatStereoWave(string path, TimeSpan duration)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        var totalSamples = (int)(48_000 * duration.TotalSeconds * 2);
        var samples = Enumerable.Repeat(0.25f, totalSamples).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
    }
}
