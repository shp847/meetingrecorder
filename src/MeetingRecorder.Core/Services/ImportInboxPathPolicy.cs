using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public enum ImportInboxPathValidationStatus
{
    Valid = 0,
    MissingPath = 1,
    UnsupportedLocation = 2,
    ReparsePoint = 3,
    OverlapsProtectedStorage = 4,
}

public sealed record ImportInboxPathValidationResult(
    ImportInboxPathValidationStatus Status,
    string Message)
{
    public bool IsValid => Status == ImportInboxPathValidationStatus.Valid;
}

public enum ImportInboxStorageHealthStatus
{
    Ready = 0,
    InsufficientSpace = 1,
    Unavailable = 2,
}

public sealed record ImportInboxStorageHealth(
    ImportInboxStorageHealthStatus Status,
    long? AvailableBytes,
    long RequiredBytes,
    string Message)
{
    public bool IsReady => Status == ImportInboxStorageHealthStatus.Ready;
}

/// <summary>
/// Validates an Inbox as a distinct, local source location. It intentionally
/// reports classes and recovery text, never a user's absolute path.
/// </summary>
public static class ImportInboxPathPolicy
{
    public static string GetDefaultInboxPath(string? documentsDirectoryOverride = null) =>
        Path.Combine(AppDataPaths.GetManagedMeetingsRoot(documentsDirectoryOverride), "Import Inbox");

    public static ImportInboxPathValidationResult Validate(
        string? inboxPath,
        IEnumerable<string?> protectedPaths)
    {
        if (string.IsNullOrWhiteSpace(inboxPath))
        {
            return new ImportInboxPathValidationResult(
                ImportInboxPathValidationStatus.MissingPath,
                "Choose an Import Inbox folder before enabling Inbox intake.");
        }

        string normalizedInboxPath;
        try
        {
            normalizedInboxPath = Path.GetFullPath(inboxPath);
        }
        catch
        {
            return new ImportInboxPathValidationResult(
                ImportInboxPathValidationStatus.UnsupportedLocation,
                "Choose a local Import Inbox folder on this PC.");
        }

        if (IsRemotePath(normalizedInboxPath))
        {
            return new ImportInboxPathValidationResult(
                ImportInboxPathValidationStatus.UnsupportedLocation,
                "Choose a local Import Inbox folder on this PC.");
        }

        if (HasExistingReparsePointAncestor(normalizedInboxPath))
        {
            return new ImportInboxPathValidationResult(
                ImportInboxPathValidationStatus.ReparsePoint,
                "Choose an Import Inbox outside linked or redirected folders.");
        }

        foreach (var protectedPath in protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string normalizedProtectedPath;
            try
            {
                normalizedProtectedPath = Path.GetFullPath(protectedPath!);
            }
            catch
            {
                continue;
            }

            if (PathsOverlap(normalizedInboxPath, normalizedProtectedPath))
            {
                return new ImportInboxPathValidationResult(
                    ImportInboxPathValidationStatus.OverlapsProtectedStorage,
                    "Choose an Import Inbox separate from Meeting Recorder recordings, transcripts, work, archive, and error storage.");
            }
        }

        return new ImportInboxPathValidationResult(
            ImportInboxPathValidationStatus.Valid,
            "Import Inbox location is ready.");
    }

    public static ImportInboxStorageHealth CheckStorageHealth(
        string inboxPath,
        long requiredBytes)
    {
        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBytes));
        }

        try
        {
            var normalizedInboxPath = Path.GetFullPath(inboxPath);
            Directory.CreateDirectory(normalizedInboxPath);
            var driveRoot = Path.GetPathRoot(normalizedInboxPath);
            var availableBytes = string.IsNullOrWhiteSpace(driveRoot)
                ? (long?)null
                : new DriveInfo(driveRoot).AvailableFreeSpace;
            if (availableBytes is { } available && available < requiredBytes)
            {
                return new ImportInboxStorageHealth(
                    ImportInboxStorageHealthStatus.InsufficientSpace,
                    available,
                    requiredBytes,
                    "Free space is too low to safely stage this import.");
            }

            var probePath = Path.Combine(normalizedInboxPath, $".meeting-recorder-inbox-probe-{Guid.NewGuid():N}.tmp");
            try
            {
                using (File.Open(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                }
            }
            finally
            {
                TryDeleteProbe(probePath);
            }

            return new ImportInboxStorageHealth(
                ImportInboxStorageHealthStatus.Ready,
                availableBytes,
                requiredBytes,
                "Import Inbox storage is ready.");
        }
        catch
        {
            return new ImportInboxStorageHealth(
                ImportInboxStorageHealthStatus.Unavailable,
                AvailableBytes: null,
                requiredBytes,
                "Meeting Recorder could not safely use this Import Inbox folder.");
        }
    }

    /// <summary>
    /// Checks the app-owned locations required for a staged import immediately
    /// before work starts. All probes use a unique temporary app-owned file and
    /// messages deliberately name storage roles rather than local paths.
    /// </summary>
    public static ImportInboxStorageHealth CheckImportStorageHealth(
        AppConfig config,
        long sourceSizeBytes,
        TimeSpan? duration)
    {
        ArgumentNullException.ThrowIfNull(config);
        var requirements = ImportStorageRequirementEstimator.Estimate(sourceSizeBytes, duration);

        var workHealth = CheckStorageHealth(config.WorkDir, requirements.RequiredWorkBytes);
        if (!workHealth.IsReady)
        {
            return workHealth with
            {
                Message = "Meeting Recorder work storage is not ready for this import.",
            };
        }

        var recordingsHealth = CheckStorageHealth(config.AudioOutputDir, requirements.RequiredOutputBytes);
        if (!recordingsHealth.IsReady)
        {
            return recordingsHealth with
            {
                Message = "Meeting Recorder recordings storage is not ready for this import.",
            };
        }

        var transcriptsHealth = CheckStorageHealth(config.TranscriptOutputDir, requirements.RequiredOutputBytes);
        if (!transcriptsHealth.IsReady)
        {
            return transcriptsHealth with
            {
                Message = "Meeting Recorder transcript storage is not ready for this import.",
            };
        }

        return new ImportInboxStorageHealth(
            ImportInboxStorageHealthStatus.Ready,
            workHealth.AvailableBytes,
            requirements.RequiredWorkBytes,
            "Import storage is ready.");
    }

    private static bool IsRemotePath(string path) => path.StartsWith("\\", StringComparison.Ordinal);

    private static bool HasExistingReparsePointAncestor(string path)
    {
        var currentPath = path;
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            if (Directory.Exists(currentPath) || File.Exists(currentPath))
            {
                try
                {
                    if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentPath = parentPath;
        }

        return false;
    }

    private static bool PathsOverlap(string firstPath, string secondPath)
    {
        var normalizedFirst = AppendDirectorySeparator(firstPath);
        var normalizedSecond = AppendDirectorySeparator(secondPath);
        return normalizedFirst.StartsWith(normalizedSecond, StringComparison.OrdinalIgnoreCase) ||
            normalizedSecond.StartsWith(normalizedFirst, StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static void TryDeleteProbe(string probePath)
    {
        try
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
        catch
        {
            // The probe result stays conservative; a stale app-owned probe is harmless.
        }
    }
}
