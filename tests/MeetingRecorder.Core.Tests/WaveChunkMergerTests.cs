using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class WaveChunkMergerTests
{
    [Fact]
    public async Task MergeAsync_Applies_Microphone_Segments_From_Their_Recorded_Start_Time()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");
        var format = new WaveFormat(16_000, 16, 1);

        CreateWave(loopbackChunk, format, amplitude: 1200, durationSeconds: 1.0);
        CreateWave(microphoneChunk, format, amplitude: 4800, durationSeconds: 0.4);

        var merger = new WaveChunkMerger();
        var sessionStartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        await merger.MergeAsync(
            [loopbackChunk],
            [
                new MicrophoneCaptureSegment(
                    sessionStartedAtUtc.AddMilliseconds(500),
                    sessionStartedAtUtc.AddMilliseconds(900),
                    [microphoneChunk]),
            ],
            sessionStartedAtUtc,
            sessionStartedAtUtc.AddSeconds(1),
            outputPath);

        var firstWindowAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.10, durationSeconds: 0.20);
        var secondWindowAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.60, durationSeconds: 0.20);

        Assert.True(secondWindowAmplitude > firstWindowAmplitude * 1.8d);
    }

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

    [Fact]
    public async Task MergeAsync_Reduces_Microphone_Bleed_When_Microphone_Mirrors_Loopback_At_Lower_Level()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");
        var format = new WaveFormat(16_000, 16, 1);

        CreateWave(loopbackChunk, format, amplitude: 6_000, durationSeconds: 0.4);
        CreateWave(microphoneChunk, format, amplitude: 3_000, durationSeconds: 0.4);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.05, durationSeconds: 0.20);
        var loopbackAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(loopbackChunk, startSeconds: 0.05, durationSeconds: 0.20);

        Assert.True(mergedAmplitude < loopbackAmplitude * 1.2d);
    }

    [Fact]
    public async Task MergeAsync_Suppresses_Delayed_Microphone_Bleed_When_The_Microphone_Is_A_Lagged_Copy_Of_Loopback()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");
        var format = new WaveFormat(16_000, 16, 1);

        CreateBurstWave(
            loopbackChunk,
            format,
            amplitude: 6_000,
            burstStartSeconds: 0.050,
            burstDurationSeconds: 0.080,
            totalDurationSeconds: 0.300);
        CreateBurstWave(
            microphoneChunk,
            format,
            amplitude: 4_800,
            burstStartSeconds: 0.090,
            burstDurationSeconds: 0.080,
            totalDurationSeconds: 0.300);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.145, durationSeconds: 0.020);
        var microphoneTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(microphoneChunk, startSeconds: 0.145, durationSeconds: 0.020);

        Assert.True(mergedTailAmplitude < microphoneTailAmplitude * 0.20d);
    }

    [Fact]
    public async Task MergeAsync_Suppresses_Long_Lag_Speaker_Bleed_With_Production_Shaped_Formats()
    {
        var root = CreateTempRoot();
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");

        CreatePatternWave(
            loopbackChunk,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2),
            amplitude: 0.40f,
            burstStartSeconds: 0.050,
            burstDurationSeconds: 0.060,
            burstSpacingSeconds: 0.120,
            burstCount: 6,
            totalDurationSeconds: 1.200);
        CreatePatternWave(
            microphoneChunk,
            new WaveFormat(16_000, 16, 1),
            amplitude: 6_000,
            burstStartSeconds: 0.310,
            burstDurationSeconds: 0.060,
            burstSpacingSeconds: 0.120,
            burstCount: 6,
            totalDurationSeconds: 1.200);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.790, durationSeconds: 0.040);
        var microphoneTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(microphoneChunk, startSeconds: 0.790, durationSeconds: 0.040);

        Assert.True(mergedTailAmplitude < microphoneTailAmplitude * 0.35d);
    }

    [Fact]
    public async Task MergeAsync_Suppresses_Very_Long_Lag_Speaker_Bleed_With_Production_Shaped_Formats()
    {
        var root = CreateTempRoot();
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");

        CreatePatternWave(
            loopbackChunk,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2),
            amplitude: 0.42f,
            burstStartSeconds: 0.050,
            burstDurationSeconds: 0.080,
            burstSpacingSeconds: 0.140,
            burstCount: 6,
            totalDurationSeconds: 1.500);
        CreatePatternWave(
            microphoneChunk,
            new WaveFormat(16_000, 16, 1),
            amplitude: 5_800,
            burstStartSeconds: 0.410,
            burstDurationSeconds: 0.080,
            burstSpacingSeconds: 0.140,
            burstCount: 6,
            totalDurationSeconds: 1.500);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 1.030, durationSeconds: 0.050);
        var microphoneTailAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(microphoneChunk, startSeconds: 1.030, durationSeconds: 0.050);

        Assert.True(mergedTailAmplitude < microphoneTailAmplitude * 0.35d);
    }

    [Fact]
    public async Task MergeAsync_Preserves_Production_Shaped_Microphone_Speech_When_It_Clearly_Dominates_The_Loopback()
    {
        var root = CreateTempRoot();
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");

        CreatePatternWave(
            loopbackChunk,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2),
            amplitude: 0.10f,
            burstStartSeconds: 0.120,
            burstDurationSeconds: 0.180,
            burstSpacingSeconds: 0.260,
            burstCount: 3,
            totalDurationSeconds: 1.000);
        CreatePatternWave(
            microphoneChunk,
            new WaveFormat(16_000, 16, 1),
            amplitude: 14_000,
            burstStartSeconds: 0.420,
            burstDurationSeconds: 0.240,
            burstSpacingSeconds: 0.300,
            burstCount: 1,
            totalDurationSeconds: 1.000);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedSpeechAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.460, durationSeconds: 0.100);
        var microphoneSpeechAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(microphoneChunk, startSeconds: 0.460, durationSeconds: 0.100);

        Assert.True(mergedSpeechAmplitude > microphoneSpeechAmplitude * 0.80d);
    }

    [Fact]
    public async Task MergeAsync_Preserves_Microphone_When_It_Dominates_The_Loopback()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var loopbackChunk = Path.Combine(root, "loopback.wav");
        var microphoneChunk = Path.Combine(root, "microphone.wav");
        var outputPath = Path.Combine(root, "merged.wav");
        var format = new WaveFormat(16_000, 16, 1);

        CreateWave(loopbackChunk, format, amplitude: 1_000, durationSeconds: 0.4);
        CreateWave(microphoneChunk, format, amplitude: 6_000, durationSeconds: 0.4);

        var merger = new WaveChunkMerger();
        await merger.MergeAsync([loopbackChunk], [microphoneChunk], outputPath);

        var mergedAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(outputPath, startSeconds: 0.05, durationSeconds: 0.20);
        var loopbackAmplitude = MeasureAverageAbsoluteAmplitudeWithAudioReader(loopbackChunk, startSeconds: 0.05, durationSeconds: 0.20);

        Assert.True(mergedAmplitude > loopbackAmplitude * 3d);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateWave(string path, WaveFormat format, short amplitude, double durationSeconds = 0.2)
    {
        using var writer = new WaveFileWriter(path, format);
        var frameCount = (int)(format.SampleRate * durationSeconds);
        var bytesPerSample = format.BitsPerSample / 8;
        var buffer = new byte[frameCount * format.Channels * bytesPerSample];

        for (var sampleIndex = 0; sampleIndex < frameCount * format.Channels; sampleIndex++)
        {
            BitConverter.TryWriteBytes(buffer.AsSpan(sampleIndex * bytesPerSample, bytesPerSample), amplitude);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void CreatePatternWave(
        string path,
        WaveFormat format,
        float amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount,
        double totalDurationSeconds)
    {
        using var writer = new WaveFileWriter(path, format);
        WritePattern(writer, format, amplitude, burstStartSeconds, burstDurationSeconds, burstSpacingSeconds, burstCount, totalDurationSeconds);
    }

    private static void CreatePatternWave(
        string path,
        WaveFormat format,
        short amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount,
        double totalDurationSeconds)
    {
        using var writer = new WaveFileWriter(path, format);
        WritePattern(writer, format, amplitude, burstStartSeconds, burstDurationSeconds, burstSpacingSeconds, burstCount, totalDurationSeconds);
    }

    private static void AddPatternToWave(
        string path,
        WaveFormat format,
        short amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount,
        double totalDurationSeconds)
    {
        float[] existingSamples;
        using (var existingReader = new AudioFileReader(path))
        {
            existingSamples = ReadAllSamples(existingReader);
        }

        using var writer = new WaveFileWriter(path, format);
        WritePattern(
            writer,
            format,
            amplitude,
            burstStartSeconds,
            burstDurationSeconds,
            burstSpacingSeconds,
            burstCount,
            totalDurationSeconds,
            existingSamples);
    }

    private static void CreateBurstWave(
        string path,
        WaveFormat format,
        short amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double totalDurationSeconds)
    {
        using var writer = new WaveFileWriter(path, format);
        var frameCount = (int)(format.SampleRate * totalDurationSeconds);
        var burstStartFrame = (int)(format.SampleRate * burstStartSeconds);
        var burstEndFrame = (int)(format.SampleRate * (burstStartSeconds + burstDurationSeconds));
        var bytesPerSample = format.BitsPerSample / 8;
        var buffer = new byte[frameCount * format.Channels * bytesPerSample];

        for (var frameIndex = burstStartFrame; frameIndex < Math.Min(frameCount, burstEndFrame); frameIndex++)
        {
            for (var channelIndex = 0; channelIndex < format.Channels; channelIndex++)
            {
                var sampleIndex = (frameIndex * format.Channels) + channelIndex;
                BitConverter.TryWriteBytes(buffer.AsSpan(sampleIndex * bytesPerSample, bytesPerSample), amplitude);
            }
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void WritePattern(
        WaveFileWriter writer,
        WaveFormat format,
        short amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount,
        double totalDurationSeconds,
        float[]? existingSamples = null)
    {
        var frameCount = (int)Math.Round(format.SampleRate * totalDurationSeconds);
        var samples = InitializeSamples(frameCount, format.Channels, existingSamples);
        ApplyPattern(samples, format.Channels, format.SampleRate, amplitude / 32768f, burstStartSeconds, burstDurationSeconds, burstSpacingSeconds, burstCount);
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static void WritePattern(
        WaveFileWriter writer,
        WaveFormat format,
        float amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount,
        double totalDurationSeconds,
        float[]? existingSamples = null)
    {
        var frameCount = (int)Math.Round(format.SampleRate * totalDurationSeconds);
        var samples = InitializeSamples(frameCount, format.Channels, existingSamples);
        ApplyPattern(samples, format.Channels, format.SampleRate, amplitude, burstStartSeconds, burstDurationSeconds, burstSpacingSeconds, burstCount);
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static float[] InitializeSamples(int frameCount, int channelCount, float[]? existingSamples)
    {
        var sampleCount = frameCount * channelCount;
        if (existingSamples is null)
        {
            return new float[sampleCount];
        }

        var samples = new float[sampleCount];
        Array.Copy(existingSamples, samples, Math.Min(existingSamples.Length, samples.Length));
        return samples;
    }

    private static void ApplyPattern(
        float[] samples,
        int channelCount,
        int sampleRate,
        float amplitude,
        double burstStartSeconds,
        double burstDurationSeconds,
        double burstSpacingSeconds,
        int burstCount)
    {
        for (var burstIndex = 0; burstIndex < burstCount; burstIndex++)
        {
            var startFrame = (int)Math.Round(sampleRate * (burstStartSeconds + (burstIndex * burstSpacingSeconds)));
            var endFrame = (int)Math.Round(sampleRate * (burstStartSeconds + burstDurationSeconds + (burstIndex * burstSpacingSeconds)));
            for (var frameIndex = Math.Max(0, startFrame); frameIndex < Math.Min(samples.Length / channelCount, endFrame); frameIndex++)
            {
                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    samples[(frameIndex * channelCount) + channelIndex] += amplitude;
                }
            }
        }
    }

    private static float[] ReadAllSamples(AudioFileReader reader)
    {
        var samples = new List<float>();
        var buffer = new float[4096];
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return samples.ToArray();
            }

            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    private static double MeasureAverageAbsoluteAmplitude(string path, WaveFormat format, double startSeconds, double durationSeconds)
    {
        using var reader = new WaveFileReader(path);
        Assert.Equal(format.SampleRate, reader.WaveFormat.SampleRate);
        Assert.Equal(format.Channels, reader.WaveFormat.Channels);
        Assert.Equal(format.BitsPerSample, reader.WaveFormat.BitsPerSample);

        var bytesPerSample = format.BitsPerSample / 8;
        var bytesPerFrame = bytesPerSample * format.Channels;
        var startFrame = (int)(format.SampleRate * startSeconds);
        var frameCount = (int)(format.SampleRate * durationSeconds);
        var buffer = new byte[frameCount * bytesPerFrame];

        reader.Position = startFrame * bytesPerFrame;
        var bytesRead = reader.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, bytesRead);

        var sum = 0d;
        var sampleCount = bytesRead / bytesPerSample;
        for (var offset = 0; offset < bytesRead; offset += bytesPerSample)
        {
            var sample = BitConverter.ToInt16(buffer, offset);
            sum += Math.Abs(sample);
        }

        return sampleCount == 0 ? 0d : sum / sampleCount;
    }

    private static double MeasureAverageAbsoluteAmplitudeWithAudioReader(string path, double startSeconds, double durationSeconds)
    {
        using var reader = new AudioFileReader(path);
        var skipSamples = (int)(reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * startSeconds);
        var takeSamples = (int)(reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * durationSeconds);
        var buffer = new float[4096];
        var sum = 0d;
        var sampleCount = 0;
        var skippedSamples = 0;

        while (skippedSamples < skipSamples)
        {
            var read = reader.Read(buffer, 0, Math.Min(buffer.Length, skipSamples - skippedSamples));
            if (read == 0)
            {
                return 0d;
            }

            skippedSamples += read;
        }

        while (sampleCount < takeSamples)
        {
            var read = reader.Read(buffer, 0, Math.Min(buffer.Length, takeSamples - sampleCount));
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                sum += Math.Abs(buffer[index]);
            }

            sampleCount += read;
        }

        return sampleCount == 0 ? 0d : sum / sampleCount;
    }
}
