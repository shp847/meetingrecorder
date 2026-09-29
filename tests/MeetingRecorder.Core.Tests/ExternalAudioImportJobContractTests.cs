using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportJobContractTests
{
    [Fact]
    public void CreateNew_Assigns_Versioned_Ids_And_A_Local_Source_Observation()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var source = Source("C:\\imports\\phone-memo.wav", now);

        var job = ExternalAudioImportJobFactory.CreateNew(source, now);

        Assert.Equal(ExternalAudioImportJob.CurrentSchemaVersion, job.SchemaVersion);
        Assert.NotEqual(Guid.Empty, job.JobId);
        Assert.NotEqual(Guid.Empty, job.Source.ObservationId);
        Assert.Equal(ExternalAudioImportJobState.PendingReview, job.State);
        Assert.Equal(0, job.Revision);
        Assert.False(job.IsReadOnly);
        Assert.True(job.Source.SourceRetained);
        Assert.Equal(ExternalAudioSourceLocatorClass.LocalFile, job.Source.LocatorClass);
        Assert.Equal(64, job.Source.ObservationKey.Length);
    }

    [Fact]
    public void SourceIdentity_Changes_For_New_Observation_But_Not_Path_Casing()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var first = Source("C:\\imports\\Phone-Memo.wav", now);
        var sameObservationDifferentCase = Source("c:\\IMPORTS\\phone-memo.wav", now);
        var changed = first with { SourceSizeBytes = first.SourceSizeBytes + 1 };

        Assert.Equal(first.ObservationKey, sameObservationDifferentCase.ObservationKey);
        Assert.NotEqual(first.ObservationKey, changed.ObservationKey);
    }

    [Fact]
    public void TryTransition_Requires_Expected_Revision_And_A_Legal_State_Change()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now);

        var probing = ExternalAudioImportJobTransitions.TryTransition(
            job,
            expectedRevision: 0,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1));
        var stale = ExternalAudioImportJobTransitions.TryTransition(
            probing.Job!,
            expectedRevision: 0,
            ExternalAudioImportJobState.ReadyToQueue,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(2));
        var ready = ExternalAudioImportJobTransitions.TryTransition(
            probing.Job!,
            expectedRevision: 1,
            ExternalAudioImportJobState.ReadyToQueue,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(2));
        var invalid = ExternalAudioImportJobTransitions.TryTransition(
            ready.Job!,
            expectedRevision: 2,
            ExternalAudioImportJobState.Published,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(3));

        Assert.True(probing.Applied);
        Assert.Equal(1, probing.Job!.Revision);
        Assert.False(stale.Applied);
        Assert.Equal(ExternalAudioImportJobTransitionFailure.RevisionConflict, stale.Failure);
        Assert.True(ready.Applied);
        Assert.False(invalid.Applied);
        Assert.Equal(ExternalAudioImportJobTransitionFailure.IllegalStateChange, invalid.Failure);
    }

    [Fact]
    public void TryTransition_Tracks_Changing_And_Missing_Sources_As_Retryable_States()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now);
        var probing = ExternalAudioImportJobTransitions.TryTransition(
            job,
            expectedRevision: 0,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1)).Job!;
        var changing = ExternalAudioImportJobTransitions.TryTransition(
            probing,
            expectedRevision: 1,
            ExternalAudioImportJobState.Changing,
            ExternalAudioImportJobReason.SourceChanged,
            now.AddSeconds(2)).Job!;
        var retry = ExternalAudioImportJobTransitions.TryTransition(
            changing,
            expectedRevision: 2,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(3)).Job!;
        var missing = ExternalAudioImportJobTransitions.TryTransition(
            retry,
            expectedRevision: 3,
            ExternalAudioImportJobState.SourceMissing,
            ExternalAudioImportJobReason.SourceMissing,
            now.AddSeconds(4));

        Assert.Equal(ExternalAudioImportJobState.Probing, retry.State);
        Assert.Equal(1, retry.RetryCount);
        Assert.True(missing.Applied);
        Assert.Equal(ExternalAudioImportJobState.SourceMissing, missing.Job!.State);
    }

    [Fact]
    public void TryTransition_Rejects_ReadOnly_And_Unknown_Future_Schema_Jobs()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now);

        var readOnly = ExternalAudioImportJobTransitions.TryTransition(
            job with { IsReadOnly = true },
            expectedRevision: 0,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1));
        var future = ExternalAudioImportJobTransitions.TryTransition(
            job with { SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion + 1 },
            expectedRevision: 0,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1));

        Assert.Equal(ExternalAudioImportJobTransitionFailure.ReadOnly, readOnly.Failure);
        Assert.Equal(ExternalAudioImportJobTransitionFailure.UnsupportedSchema, future.Failure);
    }

    [Fact]
    public void SchemaCompatibility_Marks_Newer_Records_ReadOnly_With_Safe_Recovery_Guidance()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now);

        var current = ExternalAudioImportJobSchemaCompatibility.Resolve(job);
        var future = ExternalAudioImportJobSchemaCompatibility.Resolve(job with
        {
            SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion + 1,
        });

        Assert.True(current.CanWrite);
        Assert.True(future.CanRead);
        Assert.False(future.CanWrite);
        Assert.Equal("Update Meeting Recorder before changing this import record.", future.RecoveryText);
    }

    [Fact]
    public void CreateLegacyReadOnlyProjection_Preserves_Imported_Source_And_Maps_Queue_State()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var manifest = new MeetingSessionManifest
        {
            SessionId = "legacy-session",
            State = SessionState.Queued,
            MergedAudioPath = "C:\\work\\legacy-session\\processing\\imported-source.wav",
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\legacy.wav",
                2_048,
                now.AddMinutes(-5),
                "legacy.wav",
                ExternalAudioImportMethod.WatchedFolder,
                TimeSpan.FromMinutes(5),
                sourceRetained: true),
        };

        var job = ExternalAudioImportJobFactory.CreateLegacyReadOnlyProjection(manifest, now);

        Assert.True(job.IsReadOnly);
        Assert.Equal("legacy-session", job.SessionId);
        Assert.Equal(ExternalAudioImportJobState.Queued, job.State);
        Assert.Equal("legacy.wav", job.Source.DisplayName);
        Assert.Equal("C:\\work\\legacy-session\\processing\\imported-source.wav", job.StagedWorkIdentity);
        Assert.True(job.Source.SourceRetained);
    }

    [Fact]
    public void SourceOwnershipPolicy_Allows_Read_And_Copy_But_Rejects_Source_Mutation()
    {
        Assert.True(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.Read));
        Assert.True(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.CopyToAppOwnedStaging));
        Assert.False(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.Delete));
        Assert.False(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.Move));
        Assert.False(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.Truncate));
        Assert.False(ExternalAudioImportSourceOwnershipPolicy.IsAllowed(ExternalAudioImportSourceOperation.Rename));
    }

    [Fact]
    public void PublicProjection_Excludes_Local_Locator_Staging_Path_And_Content_Hash()
    {
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var source = Source("C:\\private\\imports\\memo.wav", now) with
        {
            ContentHash = new string('a', 64),
        };
        var job = ExternalAudioImportJobFactory.CreateQueued(
            source,
            "session-a",
            "processing/imported-source.wav",
            now);

        var projection = ExternalAudioImportJobPublicProjection.Create(job);
        var json = JsonSerializer.Serialize(projection);

        Assert.Equal("phone-memo.wav", projection.SourceDisplayName);
        Assert.Equal(ExternalAudioImportJobState.Queued, projection.State);
        Assert.True(projection.SourceRetained);
        Assert.DoesNotContain(source.OriginalLocator, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(job.StagedWorkIdentity!, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source.ContentHash!, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobStore_Saves_And_Loads_Current_Job_Without_Leaving_A_Temporary_Record()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "work", "session-a", "import-job.json");
        var store = new ExternalAudioImportJobStore(path);
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now) with
        {
            SessionId = "session-a",
            StagedWorkIdentity = "session-a/processing/imported-source.wav",
        };

        await store.SaveAsync(job);
        var loaded = await store.LoadAsync();

        Assert.True(loaded.Schema.CanRead);
        Assert.True(loaded.Schema.CanWrite);
        Assert.Equal(job.JobId, loaded.Job!.JobId);
        Assert.Equal("session-a", loaded.Job.SessionId);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task JobStore_Loads_Newer_Schema_ReadOnly_And_Does_Not_Overwrite_It()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "work", "session-a", "import-job.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var futureJob = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now) with
        {
            SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion + 1,
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(futureJob));
        var originalContents = await File.ReadAllTextAsync(path);
        var store = new ExternalAudioImportJobStore(path);

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded.Job);
        Assert.True(loaded.Job!.IsReadOnly);
        Assert.False(loaded.Schema.CanWrite);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(loaded.Job));
        Assert.Equal(originalContents, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task JobStore_Replaces_A_Current_Record_Without_Leaving_Temporary_Or_Backup_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "work", "session-a", "import-job.json");
        var store = new ExternalAudioImportJobStore(path);
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var job = ExternalAudioImportJobFactory.CreateNew(Source("C:\\imports\\memo.wav", now), now);
        await store.SaveAsync(job);
        var probing = ExternalAudioImportJobTransitions.TryTransition(
            job,
            expectedRevision: 0,
            ExternalAudioImportJobState.Probing,
            ExternalAudioImportJobReason.None,
            now.AddSeconds(1)).Job!;

        await store.SaveAsync(probing);
        var loaded = await store.LoadAsync();

        Assert.Equal(ExternalAudioImportJobState.Probing, loaded.Job!.State);
        Assert.Equal(1, loaded.Job.Revision);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task JobStore_Projects_Legacy_Manifest_ReadOnly_Without_Writing_A_Companion_Record()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "work", "legacy-session", "import-job.json");
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var manifest = new MeetingSessionManifest
        {
            SessionId = "legacy-session",
            State = SessionState.Processing,
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\legacy.wav",
                2_048,
                now.AddMinutes(-2)),
        };
        var store = new ExternalAudioImportJobStore(path);

        var projected = await store.LoadOrProjectLegacyAsync(manifest, now);

        Assert.NotNull(projected.Job);
        Assert.True(projected.Job!.IsReadOnly);
        Assert.Equal(ExternalAudioImportJobState.Processing, projected.Job.State);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task JobStore_Explicitly_Migrates_A_Legacy_Imported_Manifest_Into_A_Mutable_Local_Job()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sessionRoot = Path.Combine(root, "work", "legacy-session");
        var path = Path.Combine(sessionRoot, "import-job.json");
        var stagedPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllTextAsync(stagedPath, "staged audio");
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var manifest = new MeetingSessionManifest
        {
            SessionId = "legacy-session",
            State = SessionState.Processing,
            MergedAudioPath = stagedPath,
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\legacy.wav",
                2_048,
                now.AddMinutes(-2)),
        };
        var store = new ExternalAudioImportJobStore(path);

        var migration = await store.MigrateLegacyManifestAsync(manifest, now);
        var loaded = await store.LoadAsync();

        Assert.True(migration.Migrated);
        Assert.NotNull(migration.Job);
        Assert.False(migration.Job!.IsReadOnly);
        Assert.Equal(ExternalAudioImportJobState.Processing, migration.Job.State);
        Assert.Equal(Path.Combine("processing", "imported-source.wav"), migration.Job.StagedWorkIdentity);
        Assert.Equal(migration.Job.JobId, loaded.Job!.JobId);
        Assert.False(loaded.Job.IsReadOnly);
    }

    [Fact]
    public async Task JobStore_Refuses_To_Migrate_A_Manifest_Whose_Staged_Path_Escapes_The_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sessionRoot = Path.Combine(root, "work", "legacy-session");
        var path = Path.Combine(sessionRoot, "import-job.json");
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var manifest = new MeetingSessionManifest
        {
            SessionId = "legacy-session",
            MergedAudioPath = Path.Combine(root, "outside.wav"),
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\legacy.wav",
                2_048,
                now.AddMinutes(-2)),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExternalAudioImportJobStore(path).MigrateLegacyManifestAsync(manifest, now));

        Assert.Equal("The imported-session staged audio path is outside the app-owned processing directory.", exception.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RecoveryService_Recreates_Only_A_Missing_Job_For_A_Safe_Imported_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var workDir = Path.Combine(root, "work");
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Manual,
            "Imported memo",
            Array.Empty<DetectionSignal>());
        var sessionRoot = pathBuilder.BuildSessionRoot(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var stagedPath = Path.Combine(sessionRoot, "processing", "imported-source.wav");
        await File.WriteAllTextAsync(stagedPath, "staged audio");
        await manifestStore.SaveAsync(manifest with
        {
            MergedAudioPath = stagedPath,
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\memo.wav",
                2_048,
                now.AddMinutes(-2)),
        }, manifestPath);

        var recovered = await new ExternalAudioImportJobRecoveryService(manifestStore)
            .RecoverMissingCompanionJobsAsync(workDir, now);
        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        var job = (await new ExternalAudioImportJobStore(jobPath).LoadAsync()).Job;

        Assert.Equal(1, recovered);
        Assert.NotNull(job);
        Assert.False(job!.IsReadOnly);
        Assert.Equal(manifest.SessionId, job.SessionId);
        Assert.Equal(Path.Combine("processing", "imported-source.wav"), job.StagedWorkIdentity);
    }

    [Fact]
    public async Task RecoveryService_Leaves_An_Imported_Manifest_Without_A_Safe_Staged_File_Unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var workDir = Path.Combine(root, "work");
        var now = DateTimeOffset.Parse("2026-09-27T14:30:00Z");
        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Manual,
            "Interrupted import",
            Array.Empty<DetectionSignal>());
        var sessionRoot = pathBuilder.BuildSessionRoot(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest with
        {
            MergedAudioPath = Path.Combine(sessionRoot, "processing", "missing.wav"),
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\imports\\missing.wav",
                2_048,
                now.AddMinutes(-2)),
        }, manifestPath);

        var recovered = await new ExternalAudioImportJobRecoveryService(manifestStore)
            .RecoverMissingCompanionJobsAsync(workDir, now);

        Assert.Equal(0, recovered);
        Assert.False(File.Exists(Path.Combine(sessionRoot, "import-job.json")));
    }

    private static ExternalAudioImportSourceObservation Source(string path, DateTimeOffset observedAtUtc) =>
        ExternalAudioImportSourceObservation.Create(
            path,
            "phone-memo.wav",
            ExternalAudioImportMethod.FilePicker,
            sourceSizeBytes: 12_345,
            sourceLastWriteUtc: observedAtUtc.AddMinutes(-2),
            observedAtUtc: observedAtUtc);
}
