using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingRecorder.Core.Services;

public sealed class WaveChunkMerger
{
    private const float LoopbackBleedDetectionThreshold = 0.015f;
    private const float LoopbackBleedMaximumMicRatio = 1.15f;
    private const float ReducedMicrophoneGain = 0.18f;
    private const double MicGainAttackSeconds = 0.010d;
    private const double MicGainReleaseSeconds = 0.180d;
    private const int MixBufferFrameCount = 2048;

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

    public async Task<string> MergeAsync(
        IReadOnlyList<string> loopbackChunkPaths,
        IReadOnlyList<MicrophoneCaptureSegment> microphoneCaptureSegments,
        DateTimeOffset sessionStartedAtUtc,
        DateTimeOffset? sessionEndedAtUtc,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (microphoneCaptureSegments.Count == 0)
        {
            return await MergeWaveChunksAsync(loopbackChunkPaths, outputPath, cancellationToken);
        }

        if (loopbackChunkPaths.Count == 0)
        {
            var mergedMicrophoneChunks = microphoneCaptureSegments
                .SelectMany(segment => segment.ChunkPaths)
                .ToArray();
            return await MergeWaveChunksAsync(mergedMicrophoneChunks, outputPath, cancellationToken);
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must have a directory.");
        Directory.CreateDirectory(outputDirectory);

        var outputFileName = Path.GetFileNameWithoutExtension(outputPath);
        var loopbackMergedPath = Path.Combine(outputDirectory, $"{outputFileName}.loopback.tmp.wav");
        var microphoneSegmentArtifacts = new List<(string Path, MicrophoneCaptureSegment Segment)>(microphoneCaptureSegments.Count);

        try
        {
            await MergeWaveChunksAsync(loopbackChunkPaths, loopbackMergedPath, cancellationToken);

            for (var index = 0; index < microphoneCaptureSegments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = microphoneCaptureSegments[index];
                if (segment.ChunkPaths.Count == 0)
                {
                    continue;
                }

                var microphoneSegmentPath = Path.Combine(outputDirectory, $"{outputFileName}.microphone-segment-{index:0000}.tmp.wav");
                await MergeWaveChunksAsync(segment.ChunkPaths, microphoneSegmentPath, cancellationToken);
                microphoneSegmentArtifacts.Add((microphoneSegmentPath, segment));
            }

            MixWaveFiles(
                loopbackMergedPath,
                microphoneSegmentArtifacts,
                sessionStartedAtUtc,
                sessionEndedAtUtc,
                outputPath,
                cancellationToken);
            return outputPath;
        }
        finally
        {
            TryDelete(loopbackMergedPath);
            foreach (var microphoneMergedPath in microphoneSegmentArtifacts)
            {
                TryDelete(microphoneMergedPath.Path);
            }
        }
    }

    public Task<string> MergePublishedAudioFilesAsync(
        IReadOnlyList<string> audioPaths,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (audioPaths.Count == 0)
        {
            throw new InvalidOperationException("At least one published audio file is required.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must have a directory.");
        Directory.CreateDirectory(outputDirectory);

        var readers = new List<AudioFileReader>(audioPaths.Count);
        try
        {
            foreach (var audioPath in audioPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(audioPath))
                {
                    throw new FileNotFoundException("A published audio file could not be found.", audioPath);
                }

                readers.Add(new AudioFileReader(audioPath));
            }

            var targetSampleRate = readers[0].WaveFormat.SampleRate;
            var targetChannels = readers[0].WaveFormat.Channels;
            var providers = readers
                .Select(reader => MatchWaveFormat(reader, targetSampleRate, targetChannels))
                .ToArray();

            var concatenated = new ConcatenatingSampleProvider(providers);
            var waveProvider = new SampleToWaveProvider16(concatenated);
            var outputFormat = waveProvider.WaveFormat;
            using var writer = new WaveFileWriter(outputPath, outputFormat);
            var buffer = new byte[16_384];
            int bytesRead;
            while ((bytesRead = waveProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, bytesRead);
            }

            return Task.FromResult(outputPath);
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    public Task<(string FirstOutputPath, string SecondOutputPath)> SplitPublishedAudioFileAsync(
        string audioPath,
        TimeSpan splitPoint,
        string firstOutputPath,
        string secondOutputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("A published audio file could not be found.", audioPath);
        }

        using var durationReader = new AudioFileReader(audioPath);
        var totalDuration = durationReader.TotalTime;
        if (splitPoint <= TimeSpan.Zero || splitPoint >= totalDuration)
        {
            throw new InvalidOperationException("The split point must fall inside the published audio duration.");
        }

        WriteAudioSegment(audioPath, TimeSpan.Zero, splitPoint, firstOutputPath, cancellationToken);
        WriteAudioSegment(audioPath, splitPoint, totalDuration - splitPoint, secondOutputPath, cancellationToken);
        return Task.FromResult((firstOutputPath, secondOutputPath));
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

        WriteBleedAwareMix(loopbackProvider, microphoneProvider, outputPath, cancellationToken);
    }

    private static void MixWaveFiles(
        string loopbackPath,
        IReadOnlyList<(string Path, MicrophoneCaptureSegment Segment)> microphoneSegments,
        DateTimeOffset sessionStartedAtUtc,
        DateTimeOffset? sessionEndedAtUtc,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var loopbackReader = new AudioFileReader(loopbackPath);
        var targetFormat = loopbackReader.WaveFormat;

        var microphoneReaders = new List<AudioFileReader>(microphoneSegments.Count);
        try
        {
            var loopbackProvider = MatchWaveFormat(loopbackReader, targetFormat.SampleRate, targetFormat.Channels);
            var microphoneMixer = new MixingSampleProvider(targetFormat);

            var boundedEndUtc = sessionEndedAtUtc.GetValueOrDefault(sessionStartedAtUtc);
            foreach (var microphoneSegment in microphoneSegments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var microphoneReader = new AudioFileReader(microphoneSegment.Path);
                microphoneReaders.Add(microphoneReader);

                var matchedProvider = MatchWaveFormat(microphoneReader, targetFormat.SampleRate, targetFormat.Channels);
                var boundedStartUtc = microphoneSegment.Segment.StartedAtUtc < sessionStartedAtUtc
                    ? sessionStartedAtUtc
                    : microphoneSegment.Segment.StartedAtUtc;
                var delay = boundedStartUtc <= sessionStartedAtUtc
                    ? TimeSpan.Zero
                    : boundedStartUtc - sessionStartedAtUtc;
                var offsetProvider = new OffsetSampleProvider(matchedProvider)
                {
                    DelayBy = delay,
                };

                if (microphoneSegment.Segment.EndedAtUtc is { } segmentEndedAtUtc && boundedEndUtc > sessionStartedAtUtc)
                {
                    var boundedSegmentEndUtc = segmentEndedAtUtc > boundedEndUtc ? boundedEndUtc : segmentEndedAtUtc;
                    if (boundedSegmentEndUtc > boundedStartUtc)
                    {
                        offsetProvider.Take = boundedSegmentEndUtc - boundedStartUtc;
                    }
                }

                microphoneMixer.AddMixerInput(offsetProvider);
            }

            WriteBleedAwareMix(loopbackProvider, microphoneMixer, outputPath, cancellationToken);
        }
        finally
        {
            foreach (var microphoneReader in microphoneReaders)
            {
                microphoneReader.Dispose();
            }
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

    private static void WriteBleedAwareMix(
        ISampleProvider loopbackProvider,
        ISampleProvider microphoneProvider,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must have a directory.");
        Directory.CreateDirectory(outputDirectory);

        var format = loopbackProvider.WaveFormat;
        var channelCount = format.Channels;
        var samplesPerBuffer = MixBufferFrameCount * channelCount;
        var loopbackBuffer = new float[samplesPerBuffer];
        var microphoneBuffer = new float[samplesPerBuffer];
        var mixedBuffer = new float[samplesPerBuffer];
        var attackFactor = CalculateMicGainSmoothingFactor(format.SampleRate, MicGainAttackSeconds);
        var releaseFactor = CalculateMicGainSmoothingFactor(format.SampleRate, MicGainReleaseSeconds);
        var currentMicrophoneGain = 1f;

        using var writer = new WaveFileWriter(
            outputPath,
            WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var loopbackRead = loopbackProvider.Read(loopbackBuffer, 0, loopbackBuffer.Length);
            var microphoneRead = microphoneProvider.Read(microphoneBuffer, 0, microphoneBuffer.Length);
            var samplesToWrite = Math.Max(loopbackRead, microphoneRead);
            if (samplesToWrite == 0)
            {
                break;
            }

            if (loopbackRead < samplesToWrite)
            {
                Array.Clear(loopbackBuffer, loopbackRead, samplesToWrite - loopbackRead);
            }

            if (microphoneRead < samplesToWrite)
            {
                Array.Clear(microphoneBuffer, microphoneRead, samplesToWrite - microphoneRead);
            }

            samplesToWrite -= samplesToWrite % channelCount;
            if (samplesToWrite == 0)
            {
                continue;
            }

            for (var sampleIndex = 0; sampleIndex < samplesToWrite; sampleIndex += channelCount)
            {
                var loopbackMagnitude = 0f;
                var microphoneMagnitude = 0f;
                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    loopbackMagnitude = Math.Max(loopbackMagnitude, Math.Abs(loopbackBuffer[sampleIndex + channelIndex]));
                    microphoneMagnitude = Math.Max(microphoneMagnitude, Math.Abs(microphoneBuffer[sampleIndex + channelIndex]));
                }

                var likelyLoopbackBleed = loopbackMagnitude >= LoopbackBleedDetectionThreshold &&
                    microphoneMagnitude > 0f &&
                    microphoneMagnitude <= loopbackMagnitude * LoopbackBleedMaximumMicRatio;
                var targetMicrophoneGain = likelyLoopbackBleed ? ReducedMicrophoneGain : 1f;
                var smoothingFactor = targetMicrophoneGain < currentMicrophoneGain
                    ? attackFactor
                    : releaseFactor;
                currentMicrophoneGain += (targetMicrophoneGain - currentMicrophoneGain) * smoothingFactor;

                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    var mixedSample = loopbackBuffer[sampleIndex + channelIndex] +
                        (microphoneBuffer[sampleIndex + channelIndex] * currentMicrophoneGain);
                    mixedBuffer[sampleIndex + channelIndex] = Math.Clamp(mixedSample, -1f, 1f);
                }
            }

            writer.WriteSamples(mixedBuffer, 0, samplesToWrite);
        }
    }

    private static float CalculateMicGainSmoothingFactor(int sampleRate, double timeSeconds)
    {
        if (sampleRate <= 0 || timeSeconds <= 0d)
        {
            return 1f;
        }

        return 1f - (float)Math.Exp(-1d / (sampleRate * timeSeconds));
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

    private static void WriteAudioSegment(
        string audioPath,
        TimeSpan skip,
        TimeSpan take,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must have a directory.");
        Directory.CreateDirectory(outputDirectory);

        using var reader = new AudioFileReader(audioPath);
        var segmentProvider = new OffsetSampleProvider(reader)
        {
            SkipOver = skip,
            Take = take,
        };
        var waveProvider = new SampleToWaveProvider16(segmentProvider);
        using var writer = new WaveFileWriter(outputPath, waveProvider.WaveFormat);

        var buffer = new byte[16_384];
        int bytesRead;
        while ((bytesRead = waveProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(buffer, 0, bytesRead);
        }
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
