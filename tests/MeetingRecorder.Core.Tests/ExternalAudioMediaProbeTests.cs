using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioMediaProbeTests
{
    [Fact]
    public async Task ProbeAsync_Uses_The_Transcription_Preparation_Adapter_For_A_Ready_Wav()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteSilentWaveFileAsync(sourcePath, TimeSpan.FromSeconds(2));
        var request = CreateRequest(sourcePath, "ready");

        var result = await new ExternalAudioMediaProbe().ProbeAsync(request);

        Assert.Equal(ExternalAudioMediaProbeStatus.Ready, result.Status);
        Assert.True(result.NormalizationFeasible);
        Assert.InRange(
            result.Duration!.Value,
            TimeSpan.FromSeconds(1.975),
            TimeSpan.FromSeconds(2.025));
        Assert.Equal(16_000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.NotNull(result.StorageRequirements);
        Assert.DoesNotContain(sourcePath, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_Uses_A_Stable_Cache_Per_Opaque_Observation()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");
        var adapter = new CountingPreparationProbe(TimeSpan.FromSeconds(2));
        var probe = new ExternalAudioMediaProbe(adapter);
        var request = CreateRequest(sourcePath, "cache-key");

        var first = await probe.ProbeAsync(request);
        var second = await probe.ProbeAsync(request);

        Assert.Equal(ExternalAudioMediaProbeStatus.Ready, first.Status);
        Assert.Equal(first, second);
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Changing_Before_It_Uses_The_Decoder_When_Observation_Differs()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");
        var info = new FileInfo(sourcePath);
        var adapter = new CountingPreparationProbe(TimeSpan.FromSeconds(2));
        var request = new ExternalAudioMediaProbeRequest(
            sourcePath,
            "changed",
            info.Length + 1,
            new DateTimeOffset(info.LastWriteTimeUtc));

        var result = await new ExternalAudioMediaProbe(adapter).ProbeAsync(request);

        Assert.Equal(ExternalAudioMediaProbeStatus.Changing, result.Status);
        Assert.Equal(0, adapter.CallCount);
        Assert.DoesNotContain(sourcePath, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Waiting_For_A_Locked_Source_Without_Decoding()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");
        var adapter = new CountingPreparationProbe(TimeSpan.FromSeconds(2));

        using (new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await new ExternalAudioMediaProbe(adapter).ProbeAsync(CreateRequest(sourcePath, "locked"));

            Assert.Equal(ExternalAudioMediaProbeStatus.WaitingForSettle, result.Status);
        }

        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task ProbeAsync_Returns_TooShort_For_A_Decodable_Subsecond_Source()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");

        var result = await new ExternalAudioMediaProbe(new CountingPreparationProbe(TimeSpan.FromMilliseconds(500)))
            .ProbeAsync(CreateRequest(sourcePath, "short"));

        Assert.Equal(ExternalAudioMediaProbeStatus.TooShort, result.Status);
        Assert.False(result.NormalizationFeasible);
    }

    [Fact]
    public async Task ProbeAsync_Rejects_Unsupported_Extensions_Before_It_Uses_The_Decoder()
    {
        var sourcePath = CreatePath("notes.txt");
        await WriteTextFileAsync(sourcePath, "synthetic");
        var adapter = new CountingPreparationProbe(TimeSpan.FromSeconds(2));

        var result = await new ExternalAudioMediaProbe(adapter).ProbeAsync(CreateRequest(sourcePath, "extension"));

        Assert.Equal(ExternalAudioMediaProbeStatus.UnsupportedExtension, result.Status);
        Assert.Equal(0, adapter.CallCount);
    }

    [Theory]
    [InlineData("memo.wav")]
    [InlineData("memo.mp3")]
    [InlineData("memo.m4a")]
    [InlineData("memo.aac")]
    [InlineData("memo.mp4")]
    public async Task ProbeAsync_Accepts_Each_Public_Container_Only_When_The_Preparation_Adapter_Accepts_It(string fileName)
    {
        var sourcePath = CreatePath(fileName);
        await WriteTextFileAsync(sourcePath, "synthetic");
        var adapter = new CountingPreparationProbe(TimeSpan.FromSeconds(2));

        var result = await new ExternalAudioMediaProbe(adapter).ProbeAsync(CreateRequest(sourcePath, fileName));

        Assert.Equal(ExternalAudioMediaProbeStatus.Ready, result.Status);
        Assert.Equal(1, adapter.CallCount);
        Assert.Equal("test-decoder", result.DecoderVersion);
    }

    [Fact]
    public async Task ProbeAsync_Returns_Changing_When_Source_Mutates_During_Preparation()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");

        var result = await new ExternalAudioMediaProbe(new MutatingPreparationProbe())
            .ProbeAsync(CreateRequest(sourcePath, "mutating"));

        Assert.Equal(ExternalAudioMediaProbeStatus.Changing, result.Status);
        Assert.DoesNotContain(sourcePath, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_Propagates_User_Cancellation_Without_Caching_A_Result()
    {
        var sourcePath = CreatePath("memo.wav");
        await WriteTextFileAsync(sourcePath, "synthetic");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExternalAudioMediaProbe(new CountingPreparationProbe(TimeSpan.FromSeconds(2)))
                .ProbeAsync(CreateRequest(sourcePath, "cancelled"), cancellation.Token));
    }

    private static string CreatePath(string fileName) => Path.Combine(
        Path.GetTempPath(),
        "MeetingRecorderTests",
        Guid.NewGuid().ToString("N"),
        fileName);

    private static ExternalAudioMediaProbeRequest CreateRequest(string sourcePath, string observationKey)
    {
        var info = new FileInfo(sourcePath);
        return new ExternalAudioMediaProbeRequest(
            sourcePath,
            observationKey,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc));
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        var buffer = new byte[(int)(format.AverageBytesPerSecond * duration.TotalSeconds)];
        using var writer = new WaveFileWriter(path, format);
        writer.Write(buffer, 0, buffer.Length);
        writer.Flush();
        return Task.CompletedTask;
    }

    private static Task WriteTextFileAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, text);
    }

    private sealed class CountingPreparationProbe : IExternalAudioPreparationProbe
    {
        private readonly TimeSpan _duration;

        public CountingPreparationProbe(TimeSpan duration)
        {
            _duration = duration;
        }

        public int CallCount { get; private set; }

        public Task<ExternalAudioDecodedProbe> ProbeAsync(
            string sourcePath,
            string temporaryPreparedAudioPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ExternalAudioDecodedProbe(
                _duration,
                16_000,
                1,
                "test-decoder"));
        }
    }

    private sealed class MutatingPreparationProbe : IExternalAudioPreparationProbe
    {
        public Task<ExternalAudioDecodedProbe> ProbeAsync(
            string sourcePath,
            string temporaryPreparedAudioPath,
            CancellationToken cancellationToken = default)
        {
            File.AppendAllText(sourcePath, "x");
            return Task.FromResult(new ExternalAudioDecodedProbe(
                TimeSpan.FromSeconds(2),
                16_000,
                1,
                "test-decoder"));
        }
    }
}
