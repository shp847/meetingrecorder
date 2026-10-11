using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportRecoveryActionServiceTests
{
    [Fact]
    public async Task RetryFromStagedWorkAsync_RechecksReceiptQueuesAndRecordsMetadataOnlyReceipt()
    {
        var context = await CreateFailedImportAsync();
        var originalContents = await File.ReadAllTextAsync(context.SourcePath);
        var service = new ExternalAudioImportRecoveryActionService();

        var result = await service.RetryFromStagedWorkAsync(
            context.JobPath,
            context.Job.Revision,
            Ready(context.Now),
            context.Now.AddMinutes(1));
        var stored = (await new ExternalAudioImportJobStore(context.JobPath).LoadAsync()).Job;

        Assert.True(result.Applied);
        Assert.Equal(ExternalAudioImportJobState.Queued, result.Job!.State);
        Assert.Equal(1, result.Job.RetryCount);
        Assert.Equal("Queued", result.Receipt!.Result);
        Assert.Equal(context.Job.Revision, result.Receipt.ExpectedRevision);
        Assert.Equal(result.Job.Revision, result.Receipt.ResultingRevision);
        Assert.Equal(result.Receipt, Assert.Single(stored!.RecoveryReceipts));
        Assert.Equal(originalContents, await File.ReadAllTextAsync(context.SourcePath));
        var receiptJson = JsonSerializer.Serialize(result.Receipt);
        Assert.DoesNotContain(context.SourcePath, receiptJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(context.StagedPath, receiptJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryFromStagedWorkAsync_DoesNotChangeJobWhenStageNoLongerMatchesReceipt()
    {
        var context = await CreateFailedImportAsync();
        File.Delete(context.StagedPath);

        var result = await new ExternalAudioImportRecoveryActionService().RetryFromStagedWorkAsync(
            context.JobPath,
            context.Job.Revision,
            Ready(context.Now),
            context.Now.AddMinutes(1));
        var stored = (await new ExternalAudioImportJobStore(context.JobPath).LoadAsync()).Job;

        Assert.False(result.Applied);
        Assert.Equal(ExternalAudioImportRecoveryActionFailure.StagedWorkUnavailable, result.Failure);
        Assert.Equal(ExternalAudioImportJobState.Failed, stored!.State);
        Assert.Equal(context.Job.Revision, stored.Revision);
        Assert.Empty(stored.RecoveryReceipts);
    }

    [Fact]
    public async Task RetryFromStagedWorkAsync_RejectsAStaleClickWithoutOverwritingNewerJobState()
    {
        var context = await CreateFailedImportAsync();
        var service = new ExternalAudioImportRecoveryActionService();

        var first = await service.RetryFromStagedWorkAsync(
            context.JobPath,
            context.Job.Revision,
            Ready(context.Now),
            context.Now.AddMinutes(1));
        var stale = await service.RetryFromStagedWorkAsync(
            context.JobPath,
            context.Job.Revision,
            Ready(context.Now),
            context.Now.AddMinutes(2));
        var stored = (await new ExternalAudioImportJobStore(context.JobPath).LoadAsync()).Job;

        Assert.True(first.Applied);
        Assert.False(stale.Applied);
        Assert.Equal(ExternalAudioImportRecoveryActionFailure.RevisionConflict, stale.Failure);
        Assert.Equal(first.Job!.Revision, stored!.Revision);
        Assert.Single(stored.RecoveryReceipts);
    }

    [Fact]
    public async Task RetryFromStagedWorkAsync_SerializesRepeatedClicksToOneCommittedAttempt()
    {
        var context = await CreateFailedImportAsync();
        var service = new ExternalAudioImportRecoveryActionService();

        var results = await Task.WhenAll(
            service.RetryFromStagedWorkAsync(context.JobPath, context.Job.Revision, Ready(context.Now), context.Now.AddMinutes(1)),
            service.RetryFromStagedWorkAsync(context.JobPath, context.Job.Revision, Ready(context.Now), context.Now.AddMinutes(1)));
        var stored = (await new ExternalAudioImportJobStore(context.JobPath).LoadAsync()).Job;

        Assert.Single(results.Where(result => result.Applied));
        Assert.Single(results.Where(result => result.Failure == ExternalAudioImportRecoveryActionFailure.RevisionConflict));
        Assert.Single(stored!.RecoveryReceipts);
    }

    [Fact]
    public async Task RetryFromStagedWorkAsync_PreservesVerifiedWorkWhenReadinessIsBlocked()
    {
        var context = await CreateFailedImportAsync();
        var blocked = ExternalAudioImportReadinessSnapshot.Blocked(
            ExternalAudioImportReadinessReason.TranscriptionModelMissing,
            "configuration-a",
            context.Now);

        var result = await new ExternalAudioImportRecoveryActionService().RetryFromStagedWorkAsync(
            context.JobPath,
            context.Job.Revision,
            blocked,
            context.Now.AddMinutes(1));
        var stored = (await new ExternalAudioImportJobStore(context.JobPath).LoadAsync()).Job;

        Assert.True(result.Applied);
        Assert.Equal(ExternalAudioImportJobState.BlockedBySetup, result.Job!.State);
        Assert.Equal(ExternalAudioImportJobReason.SetupRequired, result.Job.Reason);
        Assert.Equal("BlockedBySetup", result.Receipt!.Result);
        Assert.True(File.Exists(context.StagedPath));
        Assert.Equal(result.Job.Revision, stored!.Revision);
    }

    private static ExternalAudioImportReadinessSnapshot Ready(DateTimeOffset nowUtc) =>
        ExternalAudioImportReadinessSnapshot.Ready("configuration-a", nowUtc);

    private static async Task<FailedImportContext> CreateFailedImportAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sessionRoot = Path.Combine(root, "work", "session-a");
        var sourcePath = Path.Combine(root, "imports", "memo.wav");
        var stagedPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllTextAsync(sourcePath, "external source remains untouched");
        await File.WriteAllTextAsync(stagedPath, "app owned staged input");
        var now = DateTimeOffset.Parse("2026-09-27T15:00:00Z");
        var sourceInfo = new FileInfo(sourcePath);
        var stagedInfo = new FileInfo(stagedPath);
        var source = ExternalAudioImportSourceObservation.Create(
            sourcePath,
            "memo.wav",
            ExternalAudioImportMethod.FilePicker,
            sourceInfo.Length,
            new DateTimeOffset(sourceInfo.LastWriteTimeUtc),
            now);
        var receipt = ExternalAudioImportProbeReceipt.CreateReady(
            source,
            ExternalAudioImportIdentity.BuildObservationKey(
                stagedPath,
                stagedInfo.Length,
                new DateTimeOffset(stagedInfo.LastWriteTimeUtc)),
            "test-decoder",
            TimeSpan.FromSeconds(10),
            16_000,
            1,
            now);
        var queued = ExternalAudioImportJobFactory.CreateQueued(
            source,
            "session-a",
            Path.Combine("processing", "imported-source.wav"),
            now,
            receipt,
            Ready(now));
        var processing = ExternalAudioImportJobTransitions.TryTransition(
            queued,
            queued.Revision,
            ExternalAudioImportJobState.Processing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1)).Job!;
        var failed = ExternalAudioImportJobTransitions.TryTransition(
            processing,
            processing.Revision,
            ExternalAudioImportJobState.Failed,
            ExternalAudioImportJobReason.ProcessingFailed,
            now.AddSeconds(2)).Job!;
        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        await new ExternalAudioImportJobStore(jobPath).SaveAsync(failed);
        return new FailedImportContext(now, sourcePath, stagedPath, jobPath, failed);
    }

    private sealed record FailedImportContext(
        DateTimeOffset Now,
        string SourcePath,
        string StagedPath,
        string JobPath,
        ExternalAudioImportJob Job);
}
