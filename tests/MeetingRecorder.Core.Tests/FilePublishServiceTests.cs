using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class FilePublishServiceTests
{
    [Fact]
    public async Task PublishAudioAsync_Normalizes_Published_Wav_To_Speech_Optimized_Format()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source");
        var audioDir = Path.Combine(root, "audio");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(audioDir);

        var sourcePath = Path.Combine(sourceDir, "source.wav");
        CreateFloatWave(sourcePath, TimeSpan.FromSeconds(1), sampleRate: 48_000, channels: 2);

        var service = new FilePublishService();

        var publishedPath = await service.PublishAudioAsync(sourcePath, audioDir, "2026-03-16_004645_teams_test-call-3");

        Assert.True(File.Exists(publishedPath));
        Assert.Equal(".wav", Path.GetExtension(publishedPath));

        using var publishedReader = new WaveFileReader(publishedPath);
        Assert.Equal(WaveFormatEncoding.Pcm, publishedReader.WaveFormat.Encoding);
        Assert.Equal(16_000, publishedReader.WaveFormat.SampleRate);
        Assert.Equal(1, publishedReader.WaveFormat.Channels);
        Assert.Equal(16, publishedReader.WaveFormat.BitsPerSample);

        var sourceSize = new FileInfo(sourcePath).Length;
        var publishedSize = new FileInfo(publishedPath).Length;
        Assert.True(publishedSize < sourceSize, $"Expected published audio to be smaller than source. Source={sourceSize}, published={publishedSize}.");
    }

    [Fact]
    public async Task PublishAudioAsync_Returns_Existing_Path_When_Source_Already_Is_The_Destination()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        Directory.CreateDirectory(audioDir);
        var audioPath = Path.Combine(audioDir, "2026-03-16_004645_teams_test-call-3.wav");
        await File.WriteAllTextAsync(audioPath, "audio");

        var service = new FilePublishService();

        var publishedPath = await service.PublishAudioAsync(audioPath, audioDir, "2026-03-16_004645_teams_test-call-3");

        Assert.Equal(audioPath, publishedPath);
        Assert.True(File.Exists(audioPath));
    }

    private static void CreateFloatWave(string path, TimeSpan duration, int sampleRate, int channels)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
        var totalSamples = (int)(sampleRate * duration.TotalSeconds * channels);
        var samples = Enumerable.Repeat(0.1f, totalSamples).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
    }
}
