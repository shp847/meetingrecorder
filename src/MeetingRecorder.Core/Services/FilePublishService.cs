using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed class FilePublishService
{
    public Task<string> PublishAudioAsync(
        string sourceAudioPath,
        string destinationAudioDir,
        string stem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationAudioDir);

        var audioDestination = Path.Combine(destinationAudioDir, $"{stem}{Path.GetExtension(sourceAudioPath)}");
        PublishFile(sourceAudioPath, audioDestination, cancellationToken);
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

        var audioDestination = PublishAudioAsync(finalAudioPath, destinationAudioDir, stem, cancellationToken)
            .GetAwaiter()
            .GetResult();
        var markdownDestination = Path.Combine(destinationTranscriptDir, $"{stem}{Path.GetExtension(markdownPath)}");
        var jsonDestination = Path.Combine(destinationTranscriptDir, $"{stem}{Path.GetExtension(jsonPath)}");
        var readyDestination = Path.Combine(destinationTranscriptDir, $"{stem}.ready");

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
}
