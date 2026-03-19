using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class AudioLevelGraphTests
{
    [Fact]
    public void MeasurePeakLevel_Returns_Normalized_Peak_For_IeeeFloat_Stereo_Audio()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var buffer = new byte[sizeof(float) * 4];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, sizeof(float)), 0.25f);
        BitConverter.TryWriteBytes(buffer.AsSpan(sizeof(float), sizeof(float)), -0.50f);
        BitConverter.TryWriteBytes(buffer.AsSpan(sizeof(float) * 2, sizeof(float)), 0.10f);
        BitConverter.TryWriteBytes(buffer.AsSpan(sizeof(float) * 3, sizeof(float)), -0.20f);

        var peak = AudioLevelMeter.MeasurePeakLevel(buffer, buffer.Length, format);

        Assert.Equal(0.5d, peak, 3);
    }

    [Fact]
    public void Snapshot_Pads_With_Zeroes_And_Keeps_Most_Recent_Samples()
    {
        var history = new AudioLevelHistory(capacity: 3);
        history.AddSample(0.2d);
        history.AddSample(0.4d);
        history.AddSample(0.6d);
        history.AddSample(0.8d);

        var snapshot = history.Snapshot(5);

        Assert.Equal(new[] { 0d, 0d, 0.4d, 0.6d, 0.8d }, snapshot);
    }

    [Fact]
    public void HasRecentActivity_Returns_True_When_A_Recent_Sample_Exceeds_Threshold()
    {
        var history = new AudioLevelHistory(capacity: 5);
        history.AddSample(0.001d);
        history.AddSample(0.015d);
        history.AddSample(0.031d);

        var hasRecentActivity = history.HasRecentActivity(sampleCount: 4, threshold: 0.02d);

        Assert.True(hasRecentActivity);
    }
}
