using MeetingRecorder.Core.Domain;
using NAudio.Wave;

namespace MeetingRecorder.Core.Services;

public sealed class FilePublishService
{
    private readonly TranscriptionAudioPreparer _publishedAudioPreparer = new();

    public Task<string> PublishAudioAsync(
        string sourceAudioPath,
        string destinationAudioDir,
        string stem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationAudioDir);

        var audioDestination = Path.Combine(destinationAudioDir, $"{stem}.wav");
        if (string.Equals(
                Path.GetFullPath(sourceAudioPath),
                Path.GetFullPath(audioDestination),
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(audioDestination);
        }

        PublishSpeechOptimizedAudio(sourceAudioPath, audioDestination, cancellationToken);
        return Task.FromResult(audioDestination);
    }

    public Task<PublishedArtifactSet> PublishAsync(
        string finalAudioPath,
        string markdownPath,
        string jsonPath,
        string destinationAudioDir,
        string destinationTranscriptDir,
        string stem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationTranscriptDir);
        var sidecarTranscriptDir = ArtifactPathBuilder.BuildTranscriptSidecarRoot(destinationTranscriptDir);
        Directory.CreateDirectory(sidecarTranscriptDir);

        var audioDestination = PublishAudioAsync(finalAudioPath, destinationAudioDir, stem, cancellationToken)
            .GetAwaiter()
            .GetResult();
        var markdownDestination = Path.Combine(destinationTranscriptDir, $"{stem}{Path.GetExtension(markdownPath)}");
        var jsonDestination = Path.Combine(sidecarTranscriptDir, $"{stem}{Path.GetExtension(jsonPath)}");
        var readyDestination = Path.Combine(sidecarTranscriptDir, $"{stem}.ready");

        PublishFile(markdownPath, markdownDestination, cancellationToken);
        PublishFile(jsonPath, jsonDestination, cancellationToken);

        if (!File.Exists(audioDestination) || !File.Exists(markdownDestination) || !File.Exists(jsonDestination))
        {
            throw new IOException("Not all required artifacts were published successfully.");
        }

        File.WriteAllText(readyDestination, "ready");

        return Task.FromResult(new PublishedArtifactSet(
            audioDestination,
            markdownDestination,
            jsonDestination,
            readyDestination));
    }

    private static void PublishFile(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = $"{destinationPath}.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        File.Copy(sourcePath, tempPath, overwrite: true);
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private void PublishSpeechOptimizedAudio(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = $"{destinationPath}.tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        _publishedAudioPreparer.PrepareAsync(sourcePath, tempPath, cancellationToken)
            .GetAwaiter()
            .GetResult();

        using (var reader = new WaveFileReader(tempPath))
        {
            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
                reader.WaveFormat.SampleRate != TranscriptionAudioPreparer.WhisperSampleRate ||
                reader.WaveFormat.Channels != TranscriptionAudioPreparer.WhisperChannelCount ||
                reader.WaveFormat.BitsPerSample != TranscriptionAudioPreparer.WhisperBitsPerSample)
            {
                throw new IOException("Published audio did not match the expected speech-optimized WAV format.");
            }
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }
}
