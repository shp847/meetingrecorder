using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingRecorder.Core.Services;

public sealed class TranscriptionAudioPreparer
{
    public const int WhisperSampleRate = 16_000;
    public const int WhisperChannelCount = 1;
    public const int WhisperBitsPerSample = 16;

    public Task<string> PrepareAsync(
        string sourceAudioPath,
        string preparedAudioPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceAudioPath))
        {
            throw new ArgumentException("A source audio path is required.", nameof(sourceAudioPath));
        }

        if (!File.Exists(sourceAudioPath))
        {
            throw new FileNotFoundException("The source audio file does not exist.", sourceAudioPath);
        }

        var outputDirectory = Path.GetDirectoryName(preparedAudioPath)
            ?? throw new InvalidOperationException("Prepared audio path must include a directory.");
        Directory.CreateDirectory(outputDirectory);

        using var reader = new AudioFileReader(sourceAudioPath);
        ISampleProvider sampleProvider = reader;

        sampleProvider = MatchChannelCount(sampleProvider, WhisperChannelCount);
        if (sampleProvider.WaveFormat.SampleRate != WhisperSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, WhisperSampleRate);
        }

        using var writer = new WaveFileWriter(
            preparedAudioPath,
            new WaveFormat(WhisperSampleRate, WhisperBitsPerSample, WhisperChannelCount));

        var buffer = new float[8192];
        int samplesRead;
        while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteSamples(buffer, 0, samplesRead);
        }

        return Task.FromResult(preparedAudioPath);
    }

    private static ISampleProvider MatchChannelCount(ISampleProvider provider, int targetChannelCount)
    {
        if (provider.WaveFormat.Channels == targetChannelCount)
        {
            return provider;
        }

        if (provider.WaveFormat.Channels == 2 && targetChannelCount == 1)
        {
            return new StereoToMonoSampleProvider(provider);
        }

        if (provider.WaveFormat.Channels == 1 && targetChannelCount == 2)
        {
            return new MonoToStereoSampleProvider(provider);
        }

        throw new InvalidOperationException(
            $"Unable to convert audio from {provider.WaveFormat.Channels} channels to {targetChannelCount} channels.");
    }
}
