using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportReadinessTests
{
    [Fact]
    public void Resolve_Maps_Model_State_To_Safe_Stable_Recovery()
    {
        var checkedAt = new DateTimeOffset(2026, 09, 27, 20, 30, 00, TimeSpan.Zero);

        var ready = ExternalAudioImportReadinessResolver.Resolve(
            WhisperModelStatusKind.Valid,
            @"C:\Users\person\models\ggml-base.bin",
            checkedAt);
        var missing = ExternalAudioImportReadinessResolver.Resolve(
            WhisperModelStatusKind.Missing,
            @"C:\Users\person\models\ggml-base.bin",
            checkedAt);
        var unknown = ExternalAudioImportReadinessResolver.ResolveUnknown(
            @"C:\Users\person\models\ggml-base.bin",
            checkedAt);

        Assert.True(ready.CanQueue);
        Assert.Equal(ExternalAudioImportReadinessReason.None, ready.Reason);
        Assert.False(missing.CanQueue);
        Assert.Equal(ExternalAudioImportReadinessReason.TranscriptionModelMissing, missing.Reason);
        Assert.False(unknown.CanQueue);
        Assert.Equal(ExternalAudioImportReadinessReason.ReadinessCheckFailed, unknown.Reason);
        Assert.DoesNotContain("person", missing.ConfigurationRevision, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", missing.RecoveryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResumeBlockedJobsAsync_Queues_Only_Verified_User_Requested_Staged_Work()
    {
        var root = CreateTempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 09, 27, 20, 31, 00, TimeSpan.Zero);
            var (jobPath, manifestPath) = await CreateBlockedJobAsync(root, now);
            var readiness = ExternalAudioImportReadinessResolver.Resolve(
                WhisperModelStatusKind.Valid,
                Path.Combine(root, "model.bin"),
                now.AddMinutes(1));

            var result = await new ExternalAudioImportReadinessCoordinator()
                .ResumeBlockedJobsAsync(root, readiness, now.AddMinutes(1));

            Assert.Equal([manifestPath], result.ManifestPaths);
            Assert.Equal(0, result.PreservedBlockedCount);
            var job = (await new ExternalAudioImportJobStore(jobPath).LoadAsync()).Job;
            Assert.NotNull(job);
            Assert.Equal(ExternalAudioImportJobState.Queued, job!.State);
            Assert.True(job.ReadinessSnapshot!.CanQueue);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ResumeBlockedJobsAsync_Leaves_Changed_Staged_Work_Blocked()
    {
        var root = CreateTempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 09, 27, 20, 32, 00, TimeSpan.Zero);
            var (jobPath, _) = await CreateBlockedJobAsync(root, now);
            var stagedPath = Path.Combine(Path.GetDirectoryName(jobPath)!, "processing", "imported-source.wav");
            await File.AppendAllTextAsync(stagedPath, "changed");
            var readiness = ExternalAudioImportReadinessResolver.Resolve(
                WhisperModelStatusKind.Valid,
                Path.Combine(root, "model.bin"),
                now.AddMinutes(1));

            var result = await new ExternalAudioImportReadinessCoordinator()
                .ResumeBlockedJobsAsync(root, readiness, now.AddMinutes(1));

            Assert.Empty(result.ManifestPaths);
            Assert.Equal(1, result.StagedWorkUnavailableCount);
            var job = (await new ExternalAudioImportJobStore(jobPath).LoadAsync()).Job;
            Assert.NotNull(job);
            Assert.Equal(ExternalAudioImportJobState.BlockedBySetup, job!.State);
            Assert.Equal(ExternalAudioImportJobReason.StagedWorkUnavailable, job.Reason);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task FindPendingManifestPathsAsync_Skips_Import_Blocked_By_Setup()
    {
        var root = CreateTempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 09, 27, 20, 33, 00, TimeSpan.Zero);
            var (jobPath, manifestPath) = await CreateBlockedJobAsync(root, now);
            _ = jobPath;

            var pending = await new SessionManifestStore(new ArtifactPathBuilder())
                .FindPendingManifestPathsAsync(root);

            Assert.DoesNotContain(manifestPath, pending, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<(string JobPath, string ManifestPath)> CreateBlockedJobAsync(string root, DateTimeOffset now)
    {
        var sessionRoot = Path.Combine(root, "20260927203100-import");
        var processingRoot = Path.Combine(sessionRoot, "processing");
        Directory.CreateDirectory(processingRoot);
        var stagedPath = Path.Combine(processingRoot, "imported-source.wav");
        await File.WriteAllBytesAsync(stagedPath, [1, 2, 3, 4, 5, 6]);
        var sourcePath = Path.Combine(root, "source.wav");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4, 5, 6]);
        var stagedFile = new FileInfo(stagedPath);
        var source = ExternalAudioImportSourceObservation.Create(
            sourcePath,
            "source.wav",
            ExternalAudioImportMethod.FilePicker,
            stagedFile.Length,
            new DateTimeOffset(stagedFile.LastWriteTimeUtc),
            now);
        var receipt = ExternalAudioImportProbeReceipt.CreateReady(
            source,
            ExternalAudioImportIdentity.BuildObservationKey(
                stagedPath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc)),
            "test-decoder",
            TimeSpan.FromSeconds(12),
            16000,
            1,
            now);
        var blocked = ExternalAudioImportReadinessResolver.Resolve(
            WhisperModelStatusKind.Missing,
            Path.Combine(root, "model.bin"),
            now);
        var job = ExternalAudioImportJobFactory.CreateQueued(
            source,
            "20260927203100-import",
            Path.Combine("processing", "imported-source.wav"),
            now,
            receipt,
            blocked);
        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        await new ExternalAudioImportJobStore(jobPath).SaveAsync(job);

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var manifest = new MeetingSessionManifest
        {
            SessionId = job.SessionId!,
            DetectedTitle = "Imported audio",
            StartedAtUtc = now,
            State = SessionState.Queued,
            MergedAudioPath = stagedPath,
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                sourcePath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc),
                "source.wav",
                ExternalAudioImportMethod.FilePicker,
                TimeSpan.FromSeconds(12),
                sourceRetained: true),
        };
        await new SessionManifestStore(new ArtifactPathBuilder()).SaveAsync(manifest, manifestPath);
        return (jobPath, manifestPath);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Test cleanup does not change product behavior.
        }
    }
}
