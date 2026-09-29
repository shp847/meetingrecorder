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

    /// <summary>
    /// Replaces only transcript sidecars for a meeting that already published
    /// audio, JSON, Markdown, and its ready marker. The marker remains stable:
    /// enrichment never creates a second completion signal. If either sidecar
    /// cannot promote, the prior readable pair is restored before the failure
    /// reaches the caller.
    /// </summary>
    public Task<PublishedArtifactSet> PublishEnrichmentAsync(
        string markdownPath,
        string jsonPath,
        string destinationAudioDir,
        string destinationTranscriptDir,
        string stem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var audioDestination = Path.Combine(destinationAudioDir, $"{stem}.wav");
        var markdownDestination = Path.Combine(destinationTranscriptDir, $"{stem}{Path.GetExtension(markdownPath)}");
        var sidecarTranscriptDir = ArtifactPathBuilder.BuildTranscriptSidecarRoot(destinationTranscriptDir);
        var jsonDestination = Path.Combine(sidecarTranscriptDir, $"{stem}{Path.GetExtension(jsonPath)}");
        var readyDestination = Path.Combine(sidecarTranscriptDir, $"{stem}.ready");
        if (!File.Exists(audioDestination) || !File.Exists(markdownDestination) ||
            !File.Exists(jsonDestination) || !File.Exists(readyDestination))
        {
            throw new InvalidOperationException(
                "Enrichment requires the current published audio, transcript sidecars, and ready marker.");
        }

        PublishSidecarsWithRollback(
            markdownPath,
            markdownDestination,
            jsonPath,
            jsonDestination,
            cancellationToken);
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

    private static void PublishSidecarsWithRollback(
        string markdownSource,
        string markdownDestination,
        string jsonSource,
        string jsonDestination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(markdownSource) || !File.Exists(jsonSource))
        {
            throw new FileNotFoundException("Enrichment staging artifacts were unavailable.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var nextMarkdownPath = $"{markdownDestination}.{transactionId}.next";
        var nextJsonPath = $"{jsonDestination}.{transactionId}.next";
        var previousMarkdownPath = $"{markdownDestination}.{transactionId}.previous";
        var previousJsonPath = $"{jsonDestination}.{transactionId}.previous";
        try
        {
            File.Copy(markdownSource, nextMarkdownPath, overwrite: true);
            File.Copy(jsonSource, nextJsonPath, overwrite: true);
            File.Copy(markdownDestination, previousMarkdownPath, overwrite: true);
            File.Copy(jsonDestination, previousJsonPath, overwrite: true);

            File.Move(nextMarkdownPath, markdownDestination, overwrite: true);
            File.Move(nextJsonPath, jsonDestination, overwrite: true);
        }
        catch
        {
            RestoreIfPresent(previousMarkdownPath, markdownDestination);
            RestoreIfPresent(previousJsonPath, jsonDestination);
            throw;
        }
        finally
        {
            DeleteIfPresent(nextMarkdownPath);
            DeleteIfPresent(nextJsonPath);
            DeleteIfPresent(previousMarkdownPath);
            DeleteIfPresent(previousJsonPath);
        }
    }

    private static void RestoreIfPresent(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
