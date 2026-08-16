using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingRecorder.Core.Services;

public sealed class TranscriptionAudioPreparer
{
    public const int WhisperSampleRate = 16_000;
    public const int WhisperChannelCount = 1;
    public const int WhisperBitsPerSample = 16;

    private readonly WavInputInspector _wavInputInspector;

    public TranscriptionAudioPreparer(WavInputInspector? wavInputInspector = null)
    {
        _wavInputInspector = wavInputInspector ?? new WavInputInspector();
    }

    public async Task<string> PrepareAsync(
        string sourceAudioPath,
        string preparedAudioPath,
        CancellationToken cancellationToken = default)
    {
        var result = await PrepareWithInspectionAsync(sourceAudioPath, preparedAudioPath, cancellationToken);
        return result.PreparedAudioPath;
    }

    public async Task<PreparedAudioResult> PrepareWithInspectionAsync(
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

        var inspection = _wavInputInspector.Inspect(sourceAudioPath);
        var normalizedSourcePath = preparedAudioPath + ".normalized-source.wav";
        var decoderSourcePath = sourceAudioPath;
        try
        {
            decoderSourcePath = await _wavInputInspector.CreateNormalizedTemporaryCopyAsync(
                sourceAudioPath,
                normalizedSourcePath,
                inspection,
                cancellationToken);

            using var reader = new AudioFileReader(decoderSourcePath);
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

            return new PreparedAudioResult(preparedAudioPath, inspection);
        }
        finally
        {
            if (!string.Equals(decoderSourcePath, sourceAudioPath, StringComparison.OrdinalIgnoreCase) && File.Exists(normalizedSourcePath))
            {
                File.Delete(normalizedSourcePath);
            }
        }
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

public sealed record PreparedAudioResult(string PreparedAudioPath, WavInputInspection InputInspection);
