using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static partial class GeneratedArchiveBackupRetentionService
{
    [GeneratedRegex("^\\d{8}-\\d{6}-echo-repair-\\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex EchoRepairArchiveDirectoryNamePattern();

    [GeneratedRegex("^published-meeting-repair-v\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex PublishedMeetingRepairArchiveDirectoryNamePattern();

    public static GeneratedArchiveBackupRetentionCleanupResult DeleteExpiredBackups(
        string archiveRoot,
        TimeSpan retention,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archiveRoot) || retention <= TimeSpan.Zero || !Directory.Exists(archiveRoot))
        {
            return GeneratedArchiveBackupRetentionCleanupResult.Empty;
        }

        var cutoffUtc = nowUtc.ToUniversalTime().UtcDateTime.Subtract(retention);
        var deletedDirectoryCount = 0;
        var deletedFileCount = 0;
        long bytesReclaimed = 0;

        foreach (var directory in EnumerateImmediateDirectories(archiveRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsGeneratedRepairBackupDirectory(directory.Name) ||
                directory.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                directory.LastWriteTimeUtc > cutoffUtc)
            {
                continue;
            }

            var inspection = InspectDirectory(directory.FullName, cancellationToken);
            if (!TryDeleteDirectory(directory.FullName))
            {
                continue;
            }

            deletedDirectoryCount++;
            deletedFileCount += inspection.FileCount;
            bytesReclaimed += inspection.TotalBytes;
        }

        return new GeneratedArchiveBackupRetentionCleanupResult(
            deletedDirectoryCount,
            deletedFileCount,
            bytesReclaimed);
    }

    private static bool IsGeneratedRepairBackupDirectory(string directoryName)
    {
        return EchoRepairArchiveDirectoryNamePattern().IsMatch(directoryName) ||
            PublishedMeetingRepairArchiveDirectoryNamePattern().IsMatch(directoryName);
    }

    private static IReadOnlyList<DirectoryInfo> EnumerateImmediateDirectories(string archiveRoot)
    {
        try
        {
            return new DirectoryInfo(archiveRoot).EnumerateDirectories().ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<DirectoryInfo>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DirectoryInfo>();
        }
        catch (IOException)
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static ArchiveDirectoryInspection InspectDirectory(string directoryPath, CancellationToken cancellationToken)
    {
        var fileCount = 0;
        long totalBytes = 0;

        foreach (var filePath in EnumerateFiles(directoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
                fileCount++;
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return new ArchiveDirectoryInspection(fileCount, totalBytes);
    }

    private static IReadOnlyList<string> EnumerateFiles(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryDeleteDirectory(string directoryPath)
    {
        try
        {
            Directory.Delete(directoryPath, recursive: true);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private sealed record ArchiveDirectoryInspection(int FileCount, long TotalBytes);
}

internal sealed record GeneratedArchiveBackupRetentionCleanupResult(
    int DeletedDirectoryCount,
    int DeletedFileCount,
    long BytesReclaimed)
{
    public static GeneratedArchiveBackupRetentionCleanupResult Empty { get; } = new(0, 0, 0);
}
