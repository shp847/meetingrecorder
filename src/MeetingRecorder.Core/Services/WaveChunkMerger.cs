using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingRecorder.Core.Services;

public sealed class WaveChunkMerger
{
    public Task<string> MergeAsync(
        IReadOnlyList<string> chunkPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return MergeWaveChunksAsync(chunkPaths, outputPath, cancellationToken);
    }

    public async Task<string> MergeAsync(
        IReadOnlyList<string> loopbackChunkPaths,
        IReadOnlyList<string> microphoneChunkPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (microphoneChunkPaths.Count == 0)
        {
            return await MergeWaveChunksAsync(loopbackChunkPaths, outputPath, cancellationToken);
        }

        if (loopbackChunkPaths.Count == 0)
        {
            return await MergeWaveChunksAsync(microphoneChunkPaths, outputPath, cancellationToken);
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must have a directory.");
        Directory.CreateDirectory(outputDirectory);

        var outputFileName = Path.GetFileNameWithoutExtension(outputPath);
        var loopbackMergedPath = Path.Combine(outputDirectory, $"{outputFileName}.loopback.tmp.wav");
        var microphoneMergedPath = Path.Combine(outputDirectory, $"{outputFileName}.microphone.tmp.wav");

        try
        {
            await MergeWaveChunksAsync(loopbackChunkPaths, loopbackMergedPath, cancellationToken);
            await MergeWaveChunksAsync(microphoneChunkPaths, microphoneMergedPath, cancellationToken);
            MixWaveFiles(loopbackMergedPath, microphoneMergedPath, outputPath, cancellationToken);
            return outputPath;
        }
        finally
        {
            TryDelete(loopbackMergedPath);
            TryDelete(microphoneMergedPath);
        }
    }

    private static Task<string> MergeWaveChunksAsync(
        IReadOnlyList<string> chunkPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (chunkPaths.Count == 0)
        {
            throw new InvalidOperationException("At least one wave chunk is required.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Output path must have a directory."));
        cancellationToken.ThrowIfCancellationRequested();

        using var firstReader = new WaveFileReader(chunkPaths[0]);
        using var writer = new WaveFileWriter(outputPath, firstReader.WaveFormat);
        CopyReader(firstReader, writer, cancellationToken);

        for (var index = 1; index < chunkPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var nextReader = new WaveFileReader(chunkPaths[index]);
            if (!nextReader.WaveFormat.Equals(firstReader.WaveFormat))
            {
                throw new InvalidOperationException("Wave chunks must share the same format to be merged.");
            }

            CopyReader(nextReader, writer, cancellationToken);
        }

        return Task.FromResult(outputPath);
    }

    private static void MixWaveFiles(
        string loopbackPath,
        string microphonePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var loopbackReader = new AudioFileReader(loopbackPath);
        using var microphoneReader = new AudioFileReader(microphonePath);

        var targetFormat = loopbackReader.WaveFormat;
        ISampleProvider loopbackProvider = loopbackReader;
        ISampleProvider microphoneProvider = microphoneReader;

        loopbackProvider = MatchWaveFormat(loopbackProvider, targetFormat.SampleRate, targetFormat.Channels);
        microphoneProvider = MatchWaveFormat(microphoneProvider, targetFormat.SampleRate, targetFormat.Channels);

        var mixer = new MixingSampleProvider(targetFormat);
        mixer.AddMixerInput(loopbackProvider);
        mixer.AddMixerInput(microphoneProvider);

        using var writer = new WaveFileWriter(outputPath, targetFormat);
        var buffer = new float[8192];
        int samplesRead;
        while ((samplesRead = mixer.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteSamples(buffer, 0, samplesRead);
        }
    }

    private static void CopyReader(WaveFileReader reader, WaveFileWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        int bytesRead;
        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(buffer, 0, bytesRead);
        }
    }

    private static ISampleProvider MatchWaveFormat(ISampleProvider provider, int sampleRate, int channels)
    {
        ISampleProvider current = provider;

        if (current.WaveFormat.SampleRate != sampleRate)
        {
            current = new WdlResamplingSampleProvider(current, sampleRate);
        }

        if (current.WaveFormat.Channels == channels)
        {
            return current;
        }

        if (current.WaveFormat.Channels == 1 && channels == 2)
        {
            return new MonoToStereoSampleProvider(current);
        }

        if (current.WaveFormat.Channels == 2 && channels == 1)
        {
            return new StereoToMonoSampleProvider(current);
        }

        throw new InvalidOperationException(
            $"Unable to convert audio from {current.WaveFormat.Channels} channels to {channels} channels.");
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }
}
