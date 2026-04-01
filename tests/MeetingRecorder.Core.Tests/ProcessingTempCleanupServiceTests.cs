using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ProcessingTempCleanupServiceTests
{
    [Fact]
    public async Task RunStartupCleanupAsync_Deletes_Stale_Unlocked_Files_And_Writes_A_One_Time_Marker()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(root, "app");
        var diarizationRoot = Path.Combine(root, "temp", "MeetingRecorderDiarization");
        var transcriptionRoot = Path.Combine(root, "temp", "MeetingRecorderTranscription");
        Directory.CreateDirectory(diarizationRoot);
        Directory.CreateDirectory(transcriptionRoot);

        var staleDiarizationPath = Path.Combine(diarizationRoot, "stale.wav");
        var staleTranscriptionPath = Path.Combine(transcriptionRoot, "stale.wav");
        var freshPath = Path.Combine(diarizationRoot, "fresh.wav");
        await File.WriteAllTextAsync(staleDiarizationPath, "old");
        await File.WriteAllTextAsync(staleTranscriptionPath, "old");
        await File.WriteAllTextAsync(freshPath, "fresh");
        File.SetLastWriteTimeUtc(staleDiarizationPath, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(staleTranscriptionPath, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(freshPath, DateTime.UtcNow);

        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var service = new ProcessingTempCleanupService(appRoot, diarizationRoot, transcriptionRoot, logger);

        var result = await service.RunStartupCleanupAsync(CancellationToken.None);

        Assert.False(File.Exists(staleDiarizationPath));
        Assert.False(File.Exists(staleTranscriptionPath));
        Assert.True(File.Exists(freshPath));
        Assert.True(result.BytesReclaimed > 0);
        Assert.True(File.Exists(Path.Combine(appRoot, "maintenance", "responsive-processing-recovery-v1.done")));
    }

    [Fact]
    public async Task RunRecurringCleanupAsync_Leaves_Fresh_And_Locked_Files_Alone()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(root, "app");
        var diarizationRoot = Path.Combine(root, "temp", "MeetingRecorderDiarization");
        var transcriptionRoot = Path.Combine(root, "temp", "MeetingRecorderTranscription");
        Directory.CreateDirectory(diarizationRoot);
        Directory.CreateDirectory(transcriptionRoot);

        var lockedPath = Path.Combine(diarizationRoot, "locked.wav");
        var freshPath = Path.Combine(transcriptionRoot, "fresh.wav");
        await File.WriteAllTextAsync(lockedPath, "locked");
        await File.WriteAllTextAsync(freshPath, "fresh");
        File.SetLastWriteTimeUtc(lockedPath, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(freshPath, DateTime.UtcNow);

        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var service = new ProcessingTempCleanupService(appRoot, diarizationRoot, transcriptionRoot, logger);

        var result = await service.RunRecurringCleanupAsync(CancellationToken.None);

        Assert.True(File.Exists(lockedPath));
        Assert.True(File.Exists(freshPath));
        Assert.Equal(0, result.BytesReclaimed);
    }
}
