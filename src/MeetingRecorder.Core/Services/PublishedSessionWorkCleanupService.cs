using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal static class PublishedSessionWorkCleanupService
{
    public static async Task<PublishedSessionWorkCleanupResult> PrunePublishedSessionsAsync(
        SessionManifestStore manifestStore,
        string workDir,
        string? publishedAudioOutputDir = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return PublishedSessionWorkCleanupResult.Empty;
        }

        var sessionsPruned = 0;
        long bytesReclaimed = 0;

        foreach (var sessionRoot in Directory.EnumerateDirectories(workDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(sessionRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var result = await PrunePublishedSessionAsync(
                manifestStore,
                manifestPath,
                publishedAudioOutputDir,
                cancellationToken);
            sessionsPruned += result.SessionsPruned;
            bytesReclaimed += result.BytesReclaimed;
        }

        return new PublishedSessionWorkCleanupResult(sessionsPruned, bytesReclaimed);
    }

    public static async Task<PublishedSessionWorkCleanupResult> PrunePublishedSessionAsync(
        SessionManifestStore manifestStore,
        string manifestPath,
        string? publishedAudioOutputDir = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return PublishedSessionWorkCleanupResult.Empty;
        }

        var manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken);
        return await PrunePublishedSessionAsync(
            manifestStore,
            manifestPath,
            manifest,
            publishedAudioOutputDir,
            cancellationToken);
    }

    public static async Task<PublishedSessionWorkCleanupResult> PrunePublishedSessionAsync(
        SessionManifestStore manifestStore,
        string manifestPath,
        MeetingSessionManifest manifest,
        string? publishedAudioOutputDir = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.State != SessionState.Published)
        {
            return PublishedSessionWorkCleanupResult.Empty;
        }

        var retainedAudioPath = ResolveRetainedAudioPath(manifest, publishedAudioOutputDir);
        if (string.IsNullOrWhiteSpace(retainedAudioPath))
        {
            return PublishedSessionWorkCleanupResult.Empty;
        }

        var sessionRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Manifest path must include a session directory.");
        var rawRoot = Path.Combine(sessionRoot, "raw");
        var hadRetainedCaptureArtifacts = manifest.RawChunkPaths.Count > 0 ||
                                         manifest.MicrophoneChunkPaths.Count > 0 ||
                                         manifest.LoopbackCaptureSegments.Count > 0 ||
                                         manifest.MicrophoneCaptureSegments.Count > 0 ||
                                         Directory.Exists(rawRoot);
        if (!hadRetainedCaptureArtifacts)
        {
            return PublishedSessionWorkCleanupResult.Empty;
        }

        var updatedManifest = manifest with
        {
            MergedAudioPath = retainedAudioPath,
            RawChunkPaths = Array.Empty<string>(),
            LoopbackCaptureSegments = Array.Empty<LoopbackCaptureSegment>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            MicrophoneCaptureSegments = Array.Empty<MicrophoneCaptureSegment>(),
        };
        await manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);

        long bytesReclaimed = 0;
        if (Directory.Exists(rawRoot))
        {
            bytesReclaimed += DeleteFilesUnderDirectory(rawRoot);
            TryDeleteEmptyDirectories(rawRoot);
            TryDeleteDirectoryIfEmpty(rawRoot);
        }

        foreach (var path in manifest.RawChunkPaths.Concat(manifest.MicrophoneChunkPaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsPathWithinDirectory(path, rawRoot))
            {
                continue;
            }

            bytesReclaimed += DeleteFileIfPresent(path);
        }

        return new PublishedSessionWorkCleanupResult(1, bytesReclaimed);
    }

    private static long DeleteFilesUnderDirectory(string root)
    {
        long bytesReclaimed = 0;

        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            bytesReclaimed += DeleteFileIfPresent(filePath);
        }

        return bytesReclaimed;
    }

    private static long DeleteFileIfPresent(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return 0;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            File.Delete(filePath);
            return fileInfo.Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            TryDeleteDirectoryIfEmpty(directory);
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            var fullCandidatePath = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectoryPath = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return fullCandidatePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveRetainedAudioPath(MeetingSessionManifest manifest, string? publishedAudioOutputDir)
    {
        if (!string.IsNullOrWhiteSpace(manifest.MergedAudioPath) && File.Exists(manifest.MergedAudioPath))
        {
            return manifest.MergedAudioPath;
        }

        if (string.IsNullOrWhiteSpace(publishedAudioOutputDir) ||
            string.IsNullOrWhiteSpace(manifest.MergedAudioPath))
        {
            return null;
        }

        var candidatePath = Path.Combine(
            publishedAudioOutputDir,
            Path.GetFileName(manifest.MergedAudioPath));
        return File.Exists(candidatePath)
            ? candidatePath
            : null;
    }
}

internal sealed record PublishedSessionWorkCleanupResult(int SessionsPruned, long BytesReclaimed)
{
    public static PublishedSessionWorkCleanupResult Empty { get; } = new(0, 0);
}
