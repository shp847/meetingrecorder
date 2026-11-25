using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AutomaticMeetingRecoveryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Snapshot_VerifiesInputs_AndRecoversMissingOriginalsWithoutOverwritingChanges()
    {
        Directory.CreateDirectory(_root);
        var audio = Path.Combine(_root, "meeting.wav");
        var transcript = Path.Combine(_root, "meeting.md");
        await File.WriteAllTextAsync(audio, "original audio");
        await File.WriteAllTextAsync(transcript, "original transcript");
        var service = new AutomaticMeetingRecoveryService();
        var snapshot = await service.PrepareAsync(Path.Combine(_root, "Archive"), "fingerprint", "Archive", ["meeting"], [audio, transcript]);
        Assert.True(await service.AreOriginalsUnchangedAsync(snapshot));
        await service.SetStateAsync(snapshot, "Dispatched");

        File.Delete(audio);
        await File.WriteAllTextAsync(transcript, "new user edit");
        service = new AutomaticMeetingRecoveryService();
        snapshot = Assert.Single(await service.FindAsync(Path.Combine(_root, "Archive")));
        Assert.Equal("Dispatched", snapshot.Receipt.State);
        Assert.False(await service.AreOriginalsUnchangedAsync(snapshot));
        var result = await service.RestoreAsync(snapshot, [_root]);

        Assert.Equal("original audio", await File.ReadAllTextAsync(audio));
        Assert.Equal("new user edit", await File.ReadAllTextAsync(transcript));
        Assert.Single(result.ConflictCopies);
        Assert.Equal("original transcript", await File.ReadAllTextAsync(result.ConflictCopies[0]));
    }

    [Fact]
    public async Task Snapshot_DoesNotRestoreIfBackupWasAltered()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "meeting.md");
        await File.WriteAllTextAsync(source, "original");
        var service = new AutomaticMeetingRecoveryService();
        var snapshot = await service.PrepareAsync(Path.Combine(_root, "Archive"), "fingerprint", "Merge", ["meeting"], [source]);
        await File.WriteAllTextAsync(Path.Combine(snapshot.DirectoryPath, snapshot.Receipt.Artifacts[0].BackupFile), "changed");
        Assert.False(await service.AreOriginalsUnchangedAsync(snapshot));
        File.Delete(source);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(snapshot, [_root]));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task Snapshot_RequiresAllowedOriginalLocationsAndVerifiedCopiesBeforeDispatch()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "meeting.md");
        await File.WriteAllTextAsync(source, "original");
        var service = new AutomaticMeetingRecoveryService();
        var snapshot = await service.PrepareAsync(Path.Combine(_root, "Archive"), "fingerprint", "Merge", ["meeting"], [source]);
        Assert.True(await service.CanRestoreAsync(snapshot, [_root]));
        Assert.False(await service.CanRestoreAsync(snapshot, [Path.Combine(_root, "other")]));
        await File.WriteAllTextAsync(Path.Combine(snapshot.DirectoryPath, snapshot.Receipt.Artifacts[0].BackupFile), "altered");
        Assert.False(await service.CanRestoreAsync(snapshot, [_root]));
    }

    [Fact]
    public async Task InputRevision_ChangesOnlyForRelevantFilesOrReadiness()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "meeting.md");
        await File.WriteAllTextAsync(path, "one");
        var initial = AutomaticMeetingRecoveryService.ComputeInputRevision([path], "ready");
        Assert.Equal(initial, AutomaticMeetingRecoveryService.ComputeInputRevision([path], "ready"));
        Assert.NotEqual(initial, AutomaticMeetingRecoveryService.ComputeInputRevision([path], "new setup"));
        await File.WriteAllTextAsync(path, "different length");
        Assert.NotEqual(initial, AutomaticMeetingRecoveryService.ComputeInputRevision([path], "ready"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
