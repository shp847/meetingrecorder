using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Diagnostics;

namespace MeetingRecorder.Core.Tests;

public sealed class ProcessingQueueServiceTests
{
    [Fact]
    public async Task EnqueueAsync_Enriches_Attendees_Before_Starting_The_Worker_When_Enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var initialConfig = await configStore.LoadOrCreateAsync();
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync(initialConfig with { MeetingAttendeeEnrichmentEnabled = true }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var enrichGate = new DelayedMeetingMetadataEnricher();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            enrichGate,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);

        await service.EnqueueAsync(manifestPath);
        await Task.Delay(100);

        Assert.True(enrichGate.WasCalled);
        Assert.Equal(0, processFactory.StartCount);

        enrichGate.Release();
        var process = await processFactory.WaitForStartAsync();

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_Waits_For_Active_Worker_Cleanup_Before_Returning()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = Path.Combine(root, "work", "queued-session", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{}");

        var enqueueTask = service.EnqueueAsync(manifestPath);
        var process = await processFactory.WaitForStartAsync();

        var stopTask = service.StopAsync();

        await Task.Delay(50);

        Assert.True(process.KillCalled);
        Assert.False(stopTask.IsCompleted);

        process.CompleteExit();

        await stopTask.WaitAsync(TimeSpan.FromSeconds(2));
        await enqueueTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task EnqueueAsync_After_Shutdown_Does_Not_Start_A_Worker()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = Path.Combine(root, "work", "queued-session", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{}");

        await service.StopAsync();
        await service.EnqueueAsync(manifestPath);

        Assert.Equal(0, processFactory.StartCount);
    }

    [Fact]
    public async Task EnqueueAsync_Returns_Without_Waiting_For_Worker_Completion()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = Path.Combine(root, "work", "queued-session", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{}");

        var enqueueTask = service.EnqueueAsync(manifestPath);
        var process = await processFactory.WaitForStartAsync();

        await enqueueTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(process.HasExited);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_Does_Not_Start_A_New_Worker_While_Responsive_Mode_Recording_Is_Active()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording);

        var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
        await service.EnqueueAsync(manifestPath);
        await Task.Delay(150);

        Assert.Equal(0, processFactory.StartCount);

        isRecording = false;
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => process.PriorityClass is not null);

        Assert.Equal(ProcessPriorityClass.BelowNormal, process.PriorityClass);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RequestRushProcessingAsync_Moves_The_Selected_Queued_Item_To_The_Front_Of_Backlog()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording);

        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(15));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(20));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);

        await service.RequestRushProcessingAsync(secondManifestPath, RushProcessingBehavior.RunNextOnly);

        isRecording = false;
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, secondManifestPath, StringComparison.Ordinal));

        var snapshot = service.GetStatusSnapshot();

        Assert.Equal(secondManifestPath, snapshot.CurrentManifestPath);
        Assert.NotNull(snapshot.RushRequest);
        Assert.Equal(secondManifestPath, snapshot.RushRequest!.ManifestPath);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RequestRushProcessingAsync_Preempts_The_Current_Worker_And_Requeues_The_Interrupted_Item_Behind_The_Rushed_Meeting()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess { AutoCompleteOnKill = true },
            new FakeWorkerProcess(),
            new FakeWorkerProcess());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(30));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);

        var firstProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, firstManifestPath, StringComparison.Ordinal));

        await service.RequestRushProcessingAsync(secondManifestPath, RushProcessingBehavior.RunNextOnly);
        await WaitForConditionAsync(() => firstProcess.KillCalled);

        var secondProcess = await processFactory.WaitForStartAsync(1);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, secondManifestPath, StringComparison.Ordinal));
        await WaitForConditionAsync(() => service.GetStatusSnapshot().HasPreemptedItem);

        var interruptedManifest = await manifestStore.LoadAsync(firstManifestPath);

        Assert.Equal(SessionState.Queued, interruptedManifest.State);
        Assert.True(interruptedManifest.TranscriptionStatus.State is StageExecutionState.Queued or StageExecutionState.Succeeded);

        secondProcess.CompleteExit();
        var resumedProcess = await processFactory.WaitForStartAsync(2);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, firstManifestPath, StringComparison.Ordinal));

        resumedProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RequestRushProcessingAsync_RunNextIgnoreRecordingPause_Starts_Despite_A_Live_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => true);

        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12));

        await service.EnqueueAsync(manifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);

        await service.RequestRushProcessingAsync(manifestPath, RushProcessingBehavior.RunNextIgnoreRecordingPause);

        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => service.GetStatusSnapshot().IsRushPauseBypassActive);

        Assert.Equal(manifestPath, service.GetStatusSnapshot().CurrentManifestPath);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Honors_A_Persisted_Rush_Request_After_Restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configPath = Path.Combine(root, "config", "appsettings.json");
        var documentsPath = Path.Combine(root, "documents");
        var configStore = new AppConfigStore(configPath, documentsPath);
        var initialConfig = await configStore.LoadOrCreateAsync();
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, initialConfig.WorkDir, TimeSpan.FromMinutes(8));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, initialConfig.WorkDir, TimeSpan.FromMinutes(22));
        await configStore.SaveAsync(initialConfig with
        {
            RushProcessingRequest = new RushProcessingRequest(
                secondManifestPath,
                RushProcessingBehavior.RunNextOnly,
                DateTimeOffset.UtcNow),
        });

        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        await service.ResumePendingSessionsAsync();

        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, secondManifestPath, StringComparison.Ordinal));

        Assert.Equal(secondManifestPath, service.GetStatusSnapshot().CurrentManifestPath);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Clears_A_Stale_Persisted_Rush_Request()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var initialConfig = await configStore.LoadOrCreateAsync();
        await configStore.SaveAsync(initialConfig with
        {
            RushProcessingRequest = new RushProcessingRequest(
                Path.Combine(root, "missing", "manifest.json"),
                RushProcessingBehavior.RunNextOnly,
                DateTimeOffset.UtcNow),
        });

        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            new FakeWorkerProcessFactory());

        await service.ResumePendingSessionsAsync();

        Assert.Null(liveConfig.Current.RushProcessingRequest);

        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatusSnapshot_Reports_Paused_State_And_Pause_Reason_While_A_Live_Recording_Blocks_Background_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording);

        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(20));

        await service.EnqueueAsync(manifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);

        var pausedSnapshot = service.GetStatusSnapshot();

        Assert.Equal(ProcessingQueueRunState.Paused, pausedSnapshot.RunState);
        Assert.Equal(ProcessingQueuePauseReason.LiveRecordingResponsiveMode, pausedSnapshot.PauseReason);
        Assert.Equal(1, pausedSnapshot.QueuedCount);
        Assert.Equal(1, pausedSnapshot.TotalRemainingCount);
        Assert.Null(pausedSnapshot.CurrentManifestPath);
        Assert.Null(pausedSnapshot.CurrentItemStartedAtUtc);

        isRecording = false;
        var process = await processFactory.WaitForStartAsync();
        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatusSnapshot_Tracks_Queued_Counts_Separately_From_The_Active_Item_And_Computes_Item_And_Overall_Etas()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Balanced,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(30));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);

        var firstProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() =>
        {
            var snapshot = service.GetStatusSnapshot();
            return snapshot.RunState == ProcessingQueueRunState.Processing &&
                   string.Equals(snapshot.CurrentManifestPath, firstManifestPath, StringComparison.Ordinal);
        });

        var snapshot = service.GetStatusSnapshot();

        Assert.Equal(ProcessingQueueRunState.Processing, snapshot.RunState);
        Assert.Equal(ProcessingQueuePauseReason.None, snapshot.PauseReason);
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Equal(2, snapshot.TotalRemainingCount);
        Assert.Equal(firstManifestPath, snapshot.CurrentManifestPath);
        Assert.Equal("Queued Session", snapshot.CurrentTitle);
        Assert.Equal(MeetingPlatform.Teams, snapshot.CurrentPlatform);
        Assert.Equal("transcription", snapshot.CurrentStageName);
        Assert.NotNull(snapshot.CurrentItemStartedAtUtc);
        Assert.NotNull(snapshot.CurrentItemEstimatedRemaining);
        Assert.NotNull(snapshot.OverallEstimatedRemaining);
        Assert.True(snapshot.OverallEstimatedRemaining > snapshot.CurrentItemEstimatedRemaining);

        firstProcess.CompleteExit();
        var secondProcess = await processFactory.WaitForStartAsync(1);
        secondProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatusSnapshot_Returns_Unavailable_Item_Eta_When_Recording_Duration_Is_Unknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);

        await service.EnqueueAsync(manifestPath);

        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Processing);

        var snapshot = service.GetStatusSnapshot();

        Assert.Null(snapshot.CurrentItemEstimatedRemaining);
        Assert.Null(snapshot.OverallEstimatedRemaining);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatusSnapshot_Computes_Item_And_Overall_Etas_For_Imported_Audio_When_EndedAtUtc_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = await CreateQueuedImportedSourceManifestAsync(
            manifestStore,
            liveConfig.Current,
            "2026-03-28_183650_teams_int-globalfoundries-ai-sc-daily",
            MeetingPlatform.Teams,
            "Imported Session",
            DateTimeOffset.UtcNow.AddMinutes(-45),
            createPublishedTranscriptArtifacts: false,
            importedAudioDuration: TimeSpan.FromMinutes(20));

        await service.EnqueueAsync(manifestPath);

        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Processing);

        var snapshot = service.GetStatusSnapshot();

        Assert.NotNull(snapshot.CurrentItemEstimatedRemaining);
        Assert.NotNull(snapshot.OverallEstimatedRemaining);
        Assert.True(snapshot.OverallEstimatedRemaining >= snapshot.CurrentItemEstimatedRemaining);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task GetStatusSnapshot_Removes_Diarization_Time_From_The_Primary_Pass_When_Speaker_Labeling_Is_Deferred()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var deferredConfigStore = new AppConfigStore(Path.Combine(root, "deferred", "config", "appsettings.json"), Path.Combine(root, "deferred", "documents"));
        var deferredLiveConfig = new LiveAppConfig(
            deferredConfigStore,
            await deferredConfigStore.SaveAsync((await deferredConfigStore.LoadOrCreateAsync()) with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
            }));
        var inlineConfigStore = new AppConfigStore(Path.Combine(root, "inline", "config", "appsettings.json"), Path.Combine(root, "inline", "documents"));
        var inlineLiveConfig = new LiveAppConfig(
            inlineConfigStore,
            await inlineConfigStore.SaveAsync((await inlineConfigStore.LoadOrCreateAsync()) with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));

        var deferredManifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var inlineManifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var deferredLogger = new FileLogWriter(Path.Combine(root, "deferred", "logs", "app.log"));
        var inlineLogger = new FileLogWriter(Path.Combine(root, "inline", "logs", "app.log"));
        var deferredProcessFactory = new FakeWorkerProcessFactory();
        var inlineProcessFactory = new FakeWorkerProcessFactory();
        var deferredService = new ProcessingQueueService(
            deferredLiveConfig,
            deferredManifestStore,
            deferredLogger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            deferredProcessFactory);
        var inlineService = new ProcessingQueueService(
            inlineLiveConfig,
            inlineManifestStore,
            inlineLogger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            inlineProcessFactory);

        var deferredManifestPath = await CreateCompletedQueuedManifestAsync(deferredManifestStore, deferredLiveConfig.Current.WorkDir, TimeSpan.FromMinutes(25));
        var inlineManifestPath = await CreateCompletedQueuedManifestAsync(inlineManifestStore, inlineLiveConfig.Current.WorkDir, TimeSpan.FromMinutes(25));

        await deferredService.EnqueueAsync(deferredManifestPath);
        await inlineService.EnqueueAsync(inlineManifestPath);

        var deferredProcess = await deferredProcessFactory.WaitForStartAsync();
        var inlineProcess = await inlineProcessFactory.WaitForStartAsync();
        await WaitForConditionAsync(() =>
            deferredService.GetStatusSnapshot().CurrentItemEstimatedRemaining is not null &&
            inlineService.GetStatusSnapshot().CurrentItemEstimatedRemaining is not null);

        var deferredSnapshot = deferredService.GetStatusSnapshot();
        var inlineSnapshot = inlineService.GetStatusSnapshot();

        Assert.NotNull(deferredSnapshot.CurrentItemEstimatedRemaining);
        Assert.NotNull(inlineSnapshot.CurrentItemEstimatedRemaining);
        Assert.True(deferredSnapshot.CurrentItemEstimatedRemaining < inlineSnapshot.CurrentItemEstimatedRemaining);

        deferredProcess.CompleteExit();
        inlineProcess.CompleteExit();
        await deferredService.StopAsync();
        await inlineService.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_Retries_Once_Without_Diarization_When_The_Worker_Crashes_During_Speaker_Labeling()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new SequencedWorkerProcessFactory(
                new FakeWorkerProcess
                {
                    ExitCode = unchecked((int)0xC0000005),
                    StandardErrorText = "Fatal error. System.AccessViolationException\r\n   at SherpaOnnx.OfflineSpeakerDiarization.Process(Single[])",
                },
                new FakeWorkerProcess());
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);

            await service.EnqueueAsync(manifestPath);

            var firstProcess = await processFactory.WaitForStartAsync(0);
            firstProcess.CompleteExit();

            var secondProcess = await processFactory.WaitForStartAsync(1);
            var secondConfigPath = ExtractConfigPath(processFactory.StartInfos[1].Arguments);
            var updatedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(2, processFactory.StartCount);
            Assert.NotNull(secondConfigPath);
            Assert.NotEqual(AppDataPaths.GetConfigPath(), secondConfigPath);
            Assert.True(File.Exists(secondConfigPath));
            Assert.True(updatedManifest.ProcessingOverrides?.SkipSpeakerLabeling);

            var retryConfig = await new AppConfigStore(secondConfigPath!).LoadOrCreateAsync();
            Assert.NotEqual(liveConfig.Current.DiarizationAssetPath, retryConfig.DiarizationAssetPath);
            Assert.Empty(Directory.GetFiles(retryConfig.DiarizationAssetPath, "*", SearchOption.AllDirectories));

            secondProcess.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }

    [Fact]
    public async Task StopAsync_Does_Not_Throw_When_A_Queued_Worker_Fails_To_Resolve_During_Shutdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => throw new FileNotFoundException("Unable to locate the MeetingRecorder processing worker output."));

        var manifestPath = Path.Combine(root, "work", "queued-session", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{}");

        await service.EnqueueAsync(manifestPath);
        await Task.Delay(100);

        var exception = await Record.ExceptionAsync(() => service.StopAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Repairs_Interrupted_Diarization_Crash_Sessions_Before_Requeueing_Them()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new FakeWorkerProcessFactory();
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
            var manifest = await manifestStore.LoadAsync(manifestPath);
            var now = DateTimeOffset.UtcNow;
            var mergedAudioPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "processing", "existing-audio.wav");
            await File.WriteAllTextAsync(mergedAudioPath, "placeholder audio");
            await manifestStore.SaveAsync(
                manifest with
                {
                    State = SessionState.Processing,
                    MergedAudioPath = mergedAudioPath,
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, now, "done"),
                    DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Running, now, null),
                    PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
                },
                manifestPath);

            var sessionLogPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "logs", "processing.log");
            await File.WriteAllTextAsync(
                sessionLogPath,
                "Fatal error. System.AccessViolationException\r\n   at SherpaOnnx.OfflineSpeakerDiarization.Process(Single[])");

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync();
            var repairedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(SessionState.Queued, repairedManifest.State);
            Assert.True(repairedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.Equal(StageExecutionState.Queued, repairedManifest.DiarizationStatus.State);
            Assert.Equal(1, processFactory.StartCount);

            process.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Repairs_Stale_Post_Transcription_Sessions_Without_Requiring_A_Crash_Log()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new FakeWorkerProcessFactory();
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
            var manifest = await manifestStore.LoadAsync(manifestPath);
            var sessionRoot = Path.GetDirectoryName(manifestPath)!;
            var mergedAudioPath = Path.Combine(sessionRoot, "processing", "existing-audio.wav");
            await File.WriteAllTextAsync(mergedAudioPath, "placeholder audio");

            var stale = DateTimeOffset.UtcNow.AddMinutes(-30);
            await manifestStore.SaveAsync(
                manifest with
                {
                    State = SessionState.Processing,
                    EndedAtUtc = manifest.StartedAtUtc.AddMinutes(10),
                    MergedAudioPath = mergedAudioPath,
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, stale, "done"),
                    DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Running, stale, null),
                    PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, stale, null),
                },
                manifestPath);

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync();
            var repairedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(SessionState.Queued, repairedManifest.State);
            Assert.True(repairedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.Equal(StageExecutionState.Succeeded, repairedManifest.TranscriptionStatus.State);
            Assert.Equal(1, processFactory.StartCount);

            process.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Applies_Deferred_Speaker_Labeling_To_Queued_And_Interrupted_Backlog_Items()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(
                configStore,
                await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
                {
                    BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                }));
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var queuedManifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
            var processingManifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
            var processingManifest = await manifestStore.LoadAsync(processingManifestPath);
            var processingAudioPath = Path.Combine(Path.GetDirectoryName(processingManifestPath)!, "processing", "existing-audio.wav");
            await File.WriteAllTextAsync(processingAudioPath, "placeholder audio");
            await manifestStore.SaveAsync(
                processingManifest with
                {
                    State = SessionState.Processing,
                    EndedAtUtc = processingManifest.StartedAtUtc.AddMinutes(5),
                    MergedAudioPath = processingAudioPath,
                    TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "done"),
                    DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, null),
                    PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
                },
                processingManifestPath);

            await service.ResumePendingSessionsAsync();

            var firstProcess = await processFactory.WaitForStartAsync(0);
            var queuedManifest = await manifestStore.LoadAsync(queuedManifestPath);
            var repairedManifest = await manifestStore.LoadAsync(processingManifestPath);

            Assert.True(queuedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.True(repairedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.Equal(SessionState.Queued, repairedManifest.State);

            firstProcess.CompleteExit();
            var secondProcess = await processFactory.WaitForStartAsync(1);
            secondProcess.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Archives_Superseded_Imported_Source_Queued_Work_And_Does_Not_Enqueue_It()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new FakeWorkerProcessFactory();
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var importedManifestPath = await CreateQueuedImportedSourceManifestAsync(
                manifestStore,
                liveConfig.Current,
                "2026-03-24_193115_teams_google-vmo-offsite-deck",
                MeetingPlatform.Teams,
                "Google Vmo Offsite Deck",
                new DateTimeOffset(2026, 03, 24, 19, 31, 15, TimeSpan.Zero),
                createPublishedTranscriptArtifacts: true);
            var importedSessionRoot = Path.GetDirectoryName(importedManifestPath)!;

            await service.ResumePendingSessionsAsync();
            await Task.Delay(150);

            var maintenanceRoot = GetMaintenanceArchiveRoot(liveConfig.ConfigPath);
            Assert.Equal(0, processFactory.StartCount);
            Assert.False(Directory.Exists(importedSessionRoot));
            Assert.Contains(Directory.EnumerateDirectories(maintenanceRoot), path => File.Exists(Path.Combine(path, "manifest.json")));

            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Does_Not_Archive_Imported_Source_Queued_Work_When_Transcript_Artifacts_Are_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new FakeWorkerProcessFactory();
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var importedManifestPath = await CreateQueuedImportedSourceManifestAsync(
                manifestStore,
                liveConfig.Current,
                "2026-03-24_201133_teams_huddle-on-google-cloud-vmo-workshop",
                MeetingPlatform.Teams,
                "Huddle On Google Cloud Vmo Workshop",
                new DateTimeOffset(2026, 03, 24, 20, 11, 33, TimeSpan.Zero),
                createPublishedTranscriptArtifacts: false);

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync();
            Assert.True(File.Exists(importedManifestPath));
            Assert.Equal(1, processFactory.StartCount);

            process.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ResumePendingSessionsAsync_Excludes_Superseded_Imported_Source_Queued_Work_But_Still_Enqueues_Real_Backlog()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
            var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new FakeWorkerProcessFactory();
            var service = new ProcessingQueueService(
                liveConfig,
                manifestStore,
                logger,
                meetingMetadataEnricher: null,
                () => new WorkerLaunch("fake-worker.exe", string.Empty),
                processFactory);

            var importedManifestPath = await CreateQueuedImportedSourceManifestAsync(
                manifestStore,
                liveConfig.Current,
                "2026-03-24_203740_gmeet_imo-call-chris-palestro",
                MeetingPlatform.GoogleMeet,
                "Imo Call Chris Palestro",
                new DateTimeOffset(2026, 03, 24, 20, 37, 40, TimeSpan.Zero),
                createPublishedTranscriptArtifacts: true);
            var realManifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync();
            Assert.Equal(1, processFactory.StartCount);
            Assert.Single(processFactory.StartInfos);
            Assert.Contains(realManifestPath, processFactory.StartInfos[0].Arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(importedManifestPath, processFactory.StartInfos[0].Arguments, StringComparison.Ordinal);

            process.CompleteExit();
            await service.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeWorkerProcessFactory : IWorkerProcessFactory
    {
        private readonly TaskCompletionSource<FakeWorkerProcess> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public IWorkerProcess Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            StartInfos.Add(startInfo);
            var process = new FakeWorkerProcess();
            _started.TrySetResult(process);
            return process;
        }

        public Task<FakeWorkerProcess> WaitForStartAsync()
        {
            return _started.Task;
        }
    }

    private sealed class SequencedWorkerProcessFactory : IWorkerProcessFactory
    {
        private readonly List<FakeWorkerProcess> _processes;
        private readonly List<TaskCompletionSource<FakeWorkerProcess>> _startedSignals;

        public SequencedWorkerProcessFactory(params FakeWorkerProcess[] processes)
        {
            _processes = processes.ToList();
            _startedSignals = processes
                .Select(_ => new TaskCompletionSource<FakeWorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToList();
        }

        public int StartCount { get; private set; }

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public IWorkerProcess Start(ProcessStartInfo startInfo)
        {
            var process = _processes[StartCount];
            StartInfos.Add(startInfo);
            _startedSignals[StartCount].TrySetResult(process);
            StartCount++;
            return process;
        }

        public Task<FakeWorkerProcess> WaitForStartAsync(int index)
        {
            return _startedSignals[index].Task;
        }
    }

    private sealed class FakeWorkerProcess : IWorkerProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _standardOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _standardError = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExitCode { get; set; }

        public bool HasExited { get; private set; }

        public bool KillCalled { get; private set; }

        public bool AutoCompleteOnKill { get; set; }

        public ProcessPriorityClass? PriorityClass { get; private set; }

        public string StandardOutputText { get; set; } = string.Empty;

        public string StandardErrorText { get; set; } = string.Empty;

        public Task<string> ReadStandardOutputToEndAsync(CancellationToken cancellationToken)
        {
            return _standardOutput.Task.WaitAsync(cancellationToken);
        }

        public Task<string> ReadStandardErrorToEndAsync(CancellationToken cancellationToken)
        {
            return _standardError.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            return _exit.Task.WaitAsync(cancellationToken);
        }

        public void Kill(bool entireProcessTree)
        {
            KillCalled = true;
            if (AutoCompleteOnKill)
            {
                CompleteExit();
            }
        }

        public void SetPriority(ProcessPriorityClass priorityClass)
        {
            PriorityClass = priorityClass;
        }

        public void CompleteExit()
        {
            HasExited = true;
            _standardOutput.TrySetResult(StandardOutputText);
            _standardError.TrySetResult(StandardErrorText);
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }
    }

    private sealed class DelayedMeetingMetadataEnricher : IMeetingMetadataEnricher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCalled { get; private set; }

        public async Task<MeetingSessionManifest> TryEnrichAsync(
            MeetingSessionManifest manifest,
            string manifestPath,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            await _release.Task.WaitAsync(cancellationToken);
            return manifest;
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private static async Task<string> CreateQueuedManifestAsync(SessionManifestStore manifestStore, string workDir)
    {
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.Teams,
            "Queued Session",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(
            manifest with
            {
                State = SessionState.Queued,
            },
            manifestPath);
        return manifestPath;
    }

    private static async Task<string> CreateCompletedQueuedManifestAsync(
        SessionManifestStore manifestStore,
        string workDir,
        TimeSpan duration)
    {
        var manifestPath = await CreateQueuedManifestAsync(manifestStore, workDir);
        var manifest = await manifestStore.LoadAsync(manifestPath);
        await manifestStore.SaveAsync(
            manifest with
            {
                EndedAtUtc = manifest.StartedAtUtc.Add(duration),
                State = SessionState.Queued,
            },
            manifestPath);
        return manifestPath;
    }

    private static string? ExtractConfigPath(string arguments)
    {
        var marker = "--config \"";
        var markerIndex = arguments.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = arguments.IndexOf('"', valueStart);
        return valueEnd < 0
            ? null
            : arguments[valueStart..valueEnd];
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (var index = 0; index < 20; index++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for the expected condition.");
    }

    private static async Task<string> CreateQueuedImportedSourceManifestAsync(
        SessionManifestStore manifestStore,
        AppConfig config,
        string stem,
        MeetingPlatform platform,
        string title,
        DateTimeOffset startedAtUtc,
        bool createPublishedTranscriptArtifacts,
        TimeSpan? importedAudioDuration = null)
    {
        Directory.CreateDirectory(config.AudioOutputDir);
        Directory.CreateDirectory(config.TranscriptOutputDir);
        Directory.CreateDirectory(ArtifactPathBuilder.BuildTranscriptSidecarRoot(config.TranscriptOutputDir));

        var sourceAudioPath = Path.Combine(config.AudioOutputDir, $"{stem}.wav");
        CreateWaveFile(sourceAudioPath, importedAudioDuration ?? TimeSpan.FromMinutes(1));
        if (createPublishedTranscriptArtifacts)
        {
            var transcriptSidecarRoot = ArtifactPathBuilder.BuildTranscriptSidecarRoot(config.TranscriptOutputDir);
            await File.WriteAllTextAsync(Path.Combine(transcriptSidecarRoot, $"{stem}.json"), "{\"title\":\"published\"}");
            await File.WriteAllTextAsync(Path.Combine(transcriptSidecarRoot, $"{stem}.ready"), "ready");
        }

        var manifest = await manifestStore.CreateAsync(
            config.WorkDir,
            platform,
            title,
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(config.WorkDir, manifest.SessionId, "manifest.json");
        await manifestStore.SaveAsync(
            manifest with
            {
                StartedAtUtc = startedAtUtc,
                State = SessionState.Queued,
                ImportedSourceAudio = new ImportedSourceAudioInfo(
                    sourceAudioPath,
                    new FileInfo(sourceAudioPath).Length,
                    DateTimeOffset.UtcNow),
                MergedAudioPath = CopyImportedAudioIntoProcessingRoot(config.WorkDir, manifest.SessionId, sourceAudioPath),
            },
            manifestPath);
        return manifestPath;
    }

    private static string CopyImportedAudioIntoProcessingRoot(string workDir, string sessionId, string sourceAudioPath)
    {
        var processingDirectory = Path.Combine(workDir, sessionId, "processing");
        Directory.CreateDirectory(processingDirectory);
        var mergedAudioPath = Path.Combine(processingDirectory, "imported-source.wav");
        File.Copy(sourceAudioPath, mergedAudioPath, overwrite: true);
        return mergedAudioPath;
    }

    private static void CreateWaveFile(string path, TimeSpan duration)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1));
        var totalBytes = (int)Math.Round(duration.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond);
        var buffer = new byte[Math.Min(totalBytes, writer.WaveFormat.AverageBytesPerSecond)];
        var remainingBytes = totalBytes;
        while (remainingBytes > 0)
        {
            var bytesToWrite = Math.Min(buffer.Length, remainingBytes);
            writer.Write(buffer, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }
    }

    private static string GetMaintenanceArchiveRoot(string configPath)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? throw new InvalidOperationException("Config path must include a directory.");
        var appRoot = Path.GetDirectoryName(configDirectory) ?? throw new InvalidOperationException("Config directory must include an app root.");
        return Path.Combine(appRoot, "maintenance", "archived-imported-source-work");
    }
}
