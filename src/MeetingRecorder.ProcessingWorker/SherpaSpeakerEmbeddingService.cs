using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using SherpaOnnx;

namespace MeetingRecorder.ProcessingWorker;

internal sealed class SherpaSpeakerEmbeddingService
{
    private static readonly TimeSpan MinimumEnrollmentSpeechDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinimumTurnDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumEnrollmentSpeechDuration = TimeSpan.FromSeconds(60);

    public IReadOnlyList<SpeakerVoiceSample> ExtractSpeakerVoiceSamples(
        string preparedAudioPath,
        IReadOnlyList<SpeakerTurn> speakerTurns,
        DiarizationAssetInstallStatus installedAssets,
        string provider,
        int threadCount,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(installedAssets.EmbeddingModelPath))
        {
            return Array.Empty<SpeakerVoiceSample>();
        }

        using var reader = new AudioFileReader(preparedAudioPath);
        var samples = ReadSamples(reader, cancellationToken);
        var sampleRate = reader.WaveFormat.SampleRate;
        using var extractor = CreateExtractor(installedAssets.EmbeddingModelPath, provider, threadCount);
        var embeddingModelFileName = Path.GetFileName(installedAssets.EmbeddingModelPath)
            ?? throw new InvalidOperationException("The speaker embedding model file name could not be resolved.");

        var voiceSamples = new List<SpeakerVoiceSample>();
        foreach (var speakerGroup in speakerTurns
                     .Where(turn => turn.End - turn.Start >= MinimumTurnDuration)
                     .GroupBy(turn => turn.SpeakerId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var speakerSamples = CollectSpeakerSamples(
                samples,
                sampleRate,
                speakerGroup.OrderBy(turn => turn.Start).ToArray(),
                out var speechDuration);
            if (speechDuration < MinimumEnrollmentSpeechDuration || speakerSamples.Length == 0)
            {
                continue;
            }

            using var stream = extractor.CreateStream();
            stream.AcceptWaveform(sampleRate, speakerSamples);
            stream.InputFinished();
            if (!extractor.IsReady(stream))
            {
                continue;
            }

            var embedding = extractor.Compute(stream);
            if (embedding.Length != extractor.Dim)
            {
                continue;
            }

            voiceSamples.Add(new SpeakerVoiceSample(
                speakerGroup.Key,
                embeddingModelFileName,
                embedding.Length,
                VoiceProfileStore.NormalizeVector(embedding),
                speechDuration,
                createdAtUtc));
        }

        return voiceSamples;
    }

    private static SpeakerEmbeddingExtractor CreateExtractor(
        string embeddingModelPath,
        string provider,
        int threadCount)
    {
        var config = new SpeakerEmbeddingExtractorConfig
        {
            Model = embeddingModelPath,
            Provider = provider,
            NumThreads = Math.Max(1, threadCount),
            Debug = 0,
        };
        return new SpeakerEmbeddingExtractor(config);
    }

    private static float[] CollectSpeakerSamples(
        IReadOnlyList<float> samples,
        int sampleRate,
        IReadOnlyList<SpeakerTurn> speakerTurns,
        out TimeSpan speechDuration)
    {
        var collected = new List<float>();
        var collectedDuration = TimeSpan.Zero;
        foreach (var turn in speakerTurns)
        {
            if (collectedDuration >= MaximumEnrollmentSpeechDuration)
            {
                break;
            }

            var turnDuration = turn.End - turn.Start;
            if (turnDuration < MinimumTurnDuration)
            {
                continue;
            }

            var startSample = Math.Clamp(
                (int)Math.Round(turn.Start.TotalSeconds * sampleRate),
                0,
                samples.Count);
            var endSample = Math.Clamp(
                (int)Math.Round(turn.End.TotalSeconds * sampleRate),
                startSample,
                samples.Count);
            var requestedCount = endSample - startSample;
            if (requestedCount <= 0)
            {
                continue;
            }

            var remainingCount = (int)Math.Round(
                Math.Max(0d, (MaximumEnrollmentSpeechDuration - collectedDuration).TotalSeconds) * sampleRate);
            var sampleCount = Math.Min(requestedCount, remainingCount);
            if (sampleCount <= 0)
            {
                break;
            }

            for (var index = 0; index < sampleCount; index++)
            {
                collected.Add(samples[startSample + index]);
            }

            collectedDuration += TimeSpan.FromSeconds(sampleCount / (double)sampleRate);
        }

        speechDuration = collectedDuration;
        return collected.ToArray();
    }

    private static float[] ReadSamples(AudioFileReader reader, CancellationToken cancellationToken)
    {
        var samples = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate];
        int samplesRead;
        while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.AddRange(buffer.Take(samplesRead));
        }

        return samples.ToArray();
    }
}
