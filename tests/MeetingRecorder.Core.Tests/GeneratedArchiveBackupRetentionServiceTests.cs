using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class GeneratedArchiveBackupRetentionServiceTests : IDisposable
{
    private readonly string _root;

    public GeneratedArchiveBackupRetentionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void DeleteExpiredBackups_Removes_Only_Expired_Generated_Repair_Archive_Folders()
    {
        var nowUtc = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        var retention = TimeSpan.FromDays(14);
        var expiredEchoRepair = CreateArchiveDirectory("20260401-010203-echo-repair-20260328", "old-echo.wav", nowUtc - TimeSpan.FromDays(20));
        var expiredPublishedRepair = CreateArchiveDirectory("published-meeting-repair-v6", "old-published.wav", nowUtc - TimeSpan.FromDays(15));
        var recentEchoRepair = CreateArchiveDirectory("20260428-010203-echo-repair-20260427", "recent-echo.wav", nowUtc - TimeSpan.FromDays(1));
        var manualArchive = CreateArchiveDirectory("20260401-010203-context-single-archive", "manual.wav", nowUtc - TimeSpan.FromDays(30));

        var result = GeneratedArchiveBackupRetentionService.DeleteExpiredBackups(
            _root,
            retention,
            nowUtc,
            CancellationToken.None);

        Assert.Equal(2, result.DeletedDirectoryCount);
        Assert.True(result.DeletedFileCount >= 2);
        Assert.True(result.BytesReclaimed > 0);
        Assert.False(Directory.Exists(expiredEchoRepair));
        Assert.False(Directory.Exists(expiredPublishedRepair));
        Assert.True(Directory.Exists(recentEchoRepair));
        Assert.True(Directory.Exists(manualArchive));
    }

    [Fact]
    public void DeleteExpiredBackups_Does_Not_Delete_When_Retention_Is_Zero_Or_Negative()
    {
        var nowUtc = DateTimeOffset.Parse("2026-04-29T12:00:00Z");
        var generatedArchive = CreateArchiveDirectory("20260401-010203-echo-repair-20260328", "old-echo.wav", nowUtc - TimeSpan.FromDays(20));

        var result = GeneratedArchiveBackupRetentionService.DeleteExpiredBackups(
            _root,
            TimeSpan.Zero,
            nowUtc,
            CancellationToken.None);

        Assert.Equal(0, result.DeletedDirectoryCount);
        Assert.Equal(0, result.DeletedFileCount);
        Assert.Equal(0, result.BytesReclaimed);
        Assert.True(Directory.Exists(generatedArchive));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string CreateArchiveDirectory(string name, string fileName, DateTimeOffset lastWriteTimeUtc)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4 });
        File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc.UtcDateTime);
        Directory.SetLastWriteTimeUtc(directory, lastWriteTimeUtc.UtcDateTime);
        return directory;
    }
}
