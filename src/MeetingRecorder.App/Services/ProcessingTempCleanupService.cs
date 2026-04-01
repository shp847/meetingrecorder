using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed class ProcessingTempCleanupService
{
    private const string OneTimeMarkerFileName = "responsive-processing-recovery-v1.done";
    private static readonly TimeSpan OneTimeRetention = TimeSpan.FromHours(2);
    private static readonly TimeSpan RecurringRetention = TimeSpan.FromHours(24);
    private readonly string _appRoot;
    private readonly string _diarizationTempRoot;
    private readonly string _transcriptionTempRoot;
    private readonly FileLogWriter _logger;

    public ProcessingTempCleanupService(
        string appRoot,
        string diarizationTempRoot,
        string transcriptionTempRoot,
        FileLogWriter logger)
    {
        _appRoot = appRoot;
        _diarizationTempRoot = diarizationTempRoot;
        _transcriptionTempRoot = transcriptionTempRoot;
        _logger = logger;
    }

    public Task<TempCleanupResult> RunStartupCleanupAsync(CancellationToken cancellationToken)
    {
        var result = File.Exists(GetOneTimeMarkerPath())
            ? RunCleanup(RecurringRetention, cancellationToken)
            : RunCleanup(OneTimeRetention, cancellationToken);

        if (!File.Exists(GetOneTimeMarkerPath()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GetOneTimeMarkerPath())
                ?? throw new InvalidOperationException("Cleanup marker path must include a directory."));
            File.WriteAllText(GetOneTimeMarkerPath(), DateTimeOffset.UtcNow.ToString("O"));
        }

        if (result.BytesReclaimed > 0)
        {
            _logger.Log($"Startup temp cleanup reclaimed {result.BytesReclaimed} bytes across {result.FilesDeleted} file(s).");
        }

        return Task.FromResult(result);
    }

    public Task<TempCleanupResult> RunRecurringCleanupAsync(CancellationToken cancellationToken)
    {
        var result = RunCleanup(RecurringRetention, cancellationToken);
        if (result.BytesReclaimed > 0)
        {
            _logger.Log($"Recurring temp cleanup reclaimed {result.BytesReclaimed} bytes across {result.FilesDeleted} file(s).");
        }

        return Task.FromResult(result);
    }

    private TempCleanupResult RunCleanup(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow.Subtract(retention);
        var deletedFiles = 0;
        long bytesReclaimed = 0;

        foreach (var root in new[] { _diarizationTempRoot, _transcriptionTempRoot })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch
                {
                    continue;
                }

                if (fileInfo.LastWriteTimeUtc > cutoffUtc)
                {
                    continue;
                }

                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch
                {
                    continue;
                }

                try
                {
                    bytesReclaimed += fileInfo.Length;
                    File.Delete(filePath);
                    deletedFiles++;
                }
                catch
                {
                }
            }

            TryDeleteEmptyDirectories(root);
        }

        return new TempCleanupResult(deletedFiles, bytesReclaimed);
    }

    private string GetOneTimeMarkerPath()
    {
        return Path.Combine(_appRoot, "maintenance", OneTimeMarkerFileName);
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
            }
        }
    }
}

internal sealed record TempCleanupResult(int FilesDeleted, long BytesReclaimed);
