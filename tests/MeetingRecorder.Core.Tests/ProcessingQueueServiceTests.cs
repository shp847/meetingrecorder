using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;
using System.Diagnostics;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

[CollectionDefinition("ProcessingQueueService", DisableParallelization = true)]
public sealed class ProcessingQueueServiceCollection
{
}

[Collection("ProcessingQueueService")]
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
        await WaitForConditionAsync(() => enrichGate.WasCalled);

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
        await WaitForConditionAsync(() => service.IsProcessingInProgress);

        var stopTask = service.StopAsync();

        await Task.Delay(50);

        Assert.True(process.KillCalled);
        Assert.False(process.LastKillEntireProcessTree);
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
        using var recordingGate = new ManualResetEventSlim(initialState: true);
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => recordingGate.IsSet);

        var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
        await service.EnqueueAsync(manifestPath);
        await Task.Delay(150);

        Assert.Equal(0, processFactory.StartCount);

        recordingGate.Reset();
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => process.PriorityClass is not null);

        Assert.Equal(ProcessPriorityClass.BelowNormal, process.PriorityClass);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_Dispatches_Transcript_Then_Labels_Then_Summary_With_Lease_Arguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess(),
            new FakeWorkerProcess(),
            new FakeWorkerProcess());
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording,
            isSpeakerLabelingAvailableProvider: () => true);

        var transcriptManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        var labelsManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12));
        var summaryManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(14));
        await SetPublishedStageStateAsync(manifestStore, labelsManifestPath, diarizationState: StageExecutionState.NotStarted, summarizationState: StageExecutionState.Skipped);
        await SetPublishedStageStateAsync(manifestStore, summaryManifestPath, diarizationState: StageExecutionState.Succeeded, summarizationState: StageExecutionState.NotStarted);

        await service.EnqueueAsync(transcriptManifestPath);
        await service.EnqueueAsync(labelsManifestPath);
        await service.EnqueueAsync(summaryManifestPath);

        isRecording = false;
        var transcriptProcess = await processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(2));
        AssertWorkerHasStagedLease(processFactory.StartInfos[0], "transcript");
        transcriptProcess.ConfigureStagedReceipt(processFactory.StartInfos[0]);
        transcriptProcess.CompleteExit();

        var labelsProcess = await processFactory.WaitForStartAsync(1).WaitAsync(TimeSpan.FromSeconds(2));
        AssertWorkerHasStagedLease(processFactory.StartInfos[1], "diarization");
        labelsProcess.ConfigureStagedReceipt(processFactory.StartInfos[1]);
        labelsProcess.CompleteExit();

        var summaryProcess = await processFactory.WaitForStartAsync(2).WaitAsync(TimeSpan.FromSeconds(2));
        AssertWorkerHasStagedLease(processFactory.StartInfos[2], "summary");
        summaryProcess.ConfigureStagedReceipt(processFactory.StartInfos[2]);
        summaryProcess.CompleteExit();

        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_IdleCpuCapacityStartsTwoTranscriptWorkersWithoutPreempting()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync(BacklogAccelerationProfileResolver.Apply(
                await configStore.LoadOrCreateAsync(),
                BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration)));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
        var probe = new SequencedCapacityProbe(
            new(0, 0, 0),
            new(120, 100, 100),
            new(240, 200, 200),
            new(360, 300, 300));
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        using var capacityMonitor = new ResourceCapacityMonitor(
            isRecording: () => false,
            hasEligibleBacklog: () => true,
            probe,
            () => now = now.AddSeconds(30));
        capacityMonitor.Sample();
        capacityMonitor.Sample();
        capacityMonitor.Sample();
        capacityMonitor.Sample();
        Assert.True(capacityMonitor.Snapshot.IsAvailable);

        using var recordingGate = new ManualResetEventSlim(initialState: true);
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => recordingGate.IsSet,
            resourceCapacityMonitor: capacityMonitor);

        await service.EnqueueAsync(await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10)));
        await service.EnqueueAsync(await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12)));
        recordingGate.Reset();

        var first = await processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(2));
        var second = await processFactory.WaitForStartAsync(1).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.All(processFactory.StartInfos, startInfo => AssertWorkerHasStagedLease(startInfo, "transcript"));
        Assert.False(first.KillCalled);
        Assert.False(second.KillCalled);

        first.ConfigureStagedReceipt(processFactory.StartInfos[0]);
        second.ConfigureStagedReceipt(processFactory.StartInfos[1]);
        first.CompleteExit();
        second.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_ReadyGpuCapacityStartsOneAutoAndOneCpuOnlyDiarizationWorker()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
                DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
                BacklogAccelerationProfile = BacklogAccelerationProfile.OvernightAndIdleCapacityAcceleration,
                BacklogAccelerationProfileMigrationVersion = 1,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
        var cpuProbe = new SequencedCapacityProbe(
            new(0, 0, 0),
            new(120, 100, 100),
            new(240, 200, 200),
            new(360, 300, 300));
        var gpuProbe = new SequencedGpuCapacityProbe(34d, 34d, 34d);
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        using var cpuCapacityMonitor = new ResourceCapacityMonitor(
            isRecording: () => false,
            hasEligibleBacklog: () => true,
            probe: cpuProbe,
            utcNow: () => now = now.AddSeconds(30));
        using var gpuCapacityMonitor = new GpuCapacityMonitor(
            hasEligibleBacklog: () => true,
            probe: gpuProbe,
            utcNow: () => now = now.AddSeconds(30));
        for (var sample = 0; sample < 4; sample++)
        {
            cpuCapacityMonitor.Sample();
        }

        for (var sample = 0; sample < 3; sample++)
        {
            gpuCapacityMonitor.Sample();
        }

        Assert.True(cpuCapacityMonitor.Snapshot.IsAvailable);
        Assert.True(gpuCapacityMonitor.Snapshot.IsAvailable);

        using var recordingGate = new ManualResetEventSlim(initialState: true);
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => recordingGate.IsSet,
            isSpeakerLabelingAvailableProvider: () => true,
            localNowProvider: () => AtLocal(2026, 9, 28, 12, 0),
            resourceCapacityMonitor: cpuCapacityMonitor,
            gpuCapacityMonitor: gpuCapacityMonitor,
            isGpuDiarizationReadyProvider: () => true);

        try
        {
            var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
            var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12));
            await SetPublishedStageStateAsync(manifestStore, firstManifestPath, StageExecutionState.NotStarted, StageExecutionState.Skipped);
            await SetPublishedStageStateAsync(manifestStore, secondManifestPath, StageExecutionState.NotStarted, StageExecutionState.Skipped);
            await service.EnqueueAsync(firstManifestPath);
            await service.EnqueueAsync(secondManifestPath);
            recordingGate.Reset();

            var first = await processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(5));
            var second = await processFactory.WaitForStartAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(processFactory.StartInfos, startInfo => AssertWorkerHasStagedLease(startInfo, "diarization"));
            var configPaths = processFactory.StartInfos.Select(startInfo => ExtractConfigPath(startInfo.Arguments)).ToArray();
            Assert.Contains(AppDataPaths.GetConfigPath(), configPaths);
            var cpuConfigPath = Assert.Single(configPaths.Where(path => !string.Equals(path, AppDataPaths.GetConfigPath(), StringComparison.Ordinal)));
            Assert.NotNull(cpuConfigPath);
            var cpuConfig = await new AppConfigStore(cpuConfigPath!).LoadOrCreateAsync();
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, cpuConfig.DiarizationAccelerationPreference);
            Assert.False(first.KillCalled);
            Assert.False(second.KillCalled);

            first.CompleteExit();
            second.CompleteExit();
        }
        finally
        {
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task EnqueueAsync_Does_Not_Acknowledge_Staged_Work_When_The_Worker_Receipt_Is_Missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new FakeWorkerProcessFactory();
        var completions = new List<ProcessingWorkCompletion>();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);
        service.WorkCompleted += completions.Add;

        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        await service.EnqueueAsync(manifestPath);
        var process = await processFactory.WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        process.StandardOutputText = string.Empty;
        process.CompleteExit();

        await WaitForConditionAsync(() => completions.Count > 0);
        Assert.False(Assert.Single(completions).Succeeded);
        Assert.Contains("receipt", Assert.Single(completions).Detail, StringComparison.OrdinalIgnoreCase);
        await service.StopAsync();
    }

    [Fact]
    public async Task Overnight_Acceleration_Starts_Three_Transcript_Stages_But_Only_Within_Its_Window()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
                BacklogAccelerationProfile = BacklogAccelerationProfile.OvernightAcceleration,
                BacklogAccelerationProfileMigrationVersion = 1,
                OvernightInitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
                OvernightDrainStartLocal = "22:00",
                OvernightDrainEndLocal = "06:00",
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess(),
            new FakeWorkerProcess(),
            new FakeWorkerProcess());
        using var recordingGate = new ManualResetEventSlim(initialState: true);
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => recordingGate.IsSet,
            isSpeakerLabelingAvailableProvider: () => true,
            localNowProvider: () => AtLocal(2026, 9, 28, 23, 0));

        var manifests = new[]
        {
            await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10)),
            await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12)),
            await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(14)),
        };
        foreach (var manifestPath in manifests)
        {
            await service.EnqueueAsync(manifestPath);
        }
        recordingGate.Reset();

        var processes = await Task.WhenAll(
            processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(5)),
            processFactory.WaitForStartAsync(1).WaitAsync(TimeSpan.FromSeconds(5)),
            processFactory.WaitForStartAsync(2).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(3, processFactory.StartCount);
        Assert.All(processFactory.StartInfos, startInfo => AssertWorkerHasStagedLease(startInfo, "transcript"));
        Assert.Contains("Overnight acceleration: Transcripts", service.GetStatusSnapshot().BackgroundPolicyStatusText, StringComparison.Ordinal);

        foreach (var process in processes)
        {
            process.CompleteExit();
        }

        await service.StopAsync();
    }

    [Fact]
    public async Task Overnight_Acceleration_Pauses_New_Staged_Work_While_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.MaximumThroughput,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
                BacklogAccelerationProfile = BacklogAccelerationProfile.OvernightAcceleration,
                BacklogAccelerationProfileMigrationVersion = 1,
                OvernightInitialProcessingStrategy = InitialProcessingStrategy.ConfiguredStages,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new FakeWorkerProcessFactory();
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording,
            isSpeakerLabelingAvailableProvider: () => true,
            localNowProvider: () => AtLocal(2026, 9, 28, 23, 0));

        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        await service.EnqueueAsync(manifestPath);

        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);
        Assert.Equal(0, processFactory.StartCount);
        Assert.Equal(ProcessingQueuePauseReason.LiveRecordingOvernightAcceleration, service.GetStatusSnapshot().PauseReason);

        isRecording = false;
        var process = await processFactory.WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_Daytime_Cleanup_Waits_For_A_Normal_Job()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                OvernightDrainStartLocal = "00:00",
                OvernightDrainEndLocal = "00:00",
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
        var isRecording = true;
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording);

        var normalManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        var cleanupManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        await service.EnqueueAsync(normalManifestPath, ProcessingWorkPriority.Normal);
        await service.EnqueueAsync(cleanupManifestPath, ProcessingWorkPriority.Cleanup);

        isRecording = false;
        var firstProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() => string.Equals(service.GetStatusSnapshot().CurrentManifestPath, normalManifestPath, StringComparison.Ordinal));
        firstProcess.CompleteExit();

        var secondProcess = await processFactory.WaitForStartAsync(1);
        await WaitForConditionAsync(() => string.Equals(service.GetStatusSnapshot().CurrentManifestPath, cleanupManifestPath, StringComparison.Ordinal));
        secondProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task EnqueueAsync_Overnight_Cleanup_Drains_All_Cleanup_And_Normal_Work()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BacklogAccelerationProfile = BacklogAccelerationProfile.OvernightAcceleration,
                BacklogAccelerationProfileMigrationVersion = 1,
                OvernightInitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
                OvernightDrainStartLocal = DateTimeOffset.Now.AddMinutes(-10).ToString("HH:mm"),
                OvernightDrainEndLocal = DateTimeOffset.Now.AddMinutes(10).ToString("HH:mm"),
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess(), new FakeWorkerProcess(), new FakeWorkerProcess(),
            new FakeWorkerProcess(), new FakeWorkerProcess(), new FakeWorkerProcess());
        using var recordingGate = new ManualResetEventSlim(initialState: true);
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => recordingGate.IsSet);

        var cleanupManifestPaths = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
            cleanupManifestPaths.Add(manifestPath);
            await service.EnqueueAsync(manifestPath, ProcessingWorkPriority.Cleanup);
        }

        var normalManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        await service.EnqueueAsync(normalManifestPath, ProcessingWorkPriority.Normal);

        recordingGate.Reset();
        var launches = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var process = await processFactory.WaitForStartAsync(index);
            launches.Add(processFactory.StartInfos[index].Arguments);
            process.CompleteExit();
        }

        Assert.All(cleanupManifestPaths, cleanupManifestPath =>
            Assert.Contains(launches, arguments =>
                arguments.Contains(cleanupManifestPath, StringComparison.Ordinal)));
        Assert.Contains(launches, arguments => arguments.Contains(normalManifestPath, StringComparison.Ordinal));
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
    public async Task RequestRushProcessingAsync_Does_Not_Interrupt_Current_Transcription_And_Runs_Rushed_Item_Next()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(configStore, await configStore.LoadOrCreateAsync());
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(
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
        await Task.Delay(50);
        Assert.False(firstProcess.KillCalled);

        firstProcess.CompleteExit();
        var secondProcess = await processFactory.WaitForStartAsync(1);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, secondManifestPath, StringComparison.Ordinal));

        secondProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RequestRushProcessingAsync_Only_Interrupts_AppOwned_Speaker_Labeling_When_Not_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess { AutoCompleteOnKill = true },
            new FakeWorkerProcess());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => false);

        var activeManifestPath = await CreateProcessingManifestAsync(
            manifestStore,
            liveConfig.Current.WorkDir,
            new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "Transcript ready."),
            new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, "Speaker labels running."));
        var rushedManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));

        await service.EnqueueAsync(activeManifestPath);
        var activeProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().CurrentStageName == "diarization");
        await service.EnqueueAsync(rushedManifestPath);

        await service.RequestRushProcessingAsync(rushedManifestPath, RushProcessingBehavior.RunNextOnly);
        await WaitForConditionAsync(() => activeProcess.KillCalled);

        Assert.False(activeProcess.LastKillEntireProcessTree);
        var rushedProcess = await processFactory.WaitForStartAsync(1);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, rushedManifestPath, StringComparison.Ordinal));

        rushedProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RequestRushProcessingAsync_Does_Not_Interrupt_Speaker_Labeling_During_Active_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.FastestDrain,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new SequencedWorkerProcessFactory(new FakeWorkerProcess(), new FakeWorkerProcess());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => true);

        var activeManifestPath = await CreateProcessingManifestAsync(
            manifestStore,
            liveConfig.Current.WorkDir,
            new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "Transcript ready."),
            new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, "Speaker labels running."));
        var rushedManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));

        await service.EnqueueAsync(activeManifestPath);
        var activeProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().CurrentStageName == "diarization");
        await service.EnqueueAsync(rushedManifestPath);

        await service.RequestRushProcessingAsync(rushedManifestPath, RushProcessingBehavior.RunNextIgnoreRecordingPause);
        await Task.Delay(50);

        Assert.False(activeProcess.KillCalled);
        activeProcess.CompleteExit();
        var rushedProcess = await processFactory.WaitForStartAsync(1);
        rushedProcess.CompleteExit();
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
    public async Task RushBacklogAsync_Defers_Current_Backlog_Without_Changing_Future_Mode()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
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

        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(15));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(20));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);

        var result = await service.RushBacklogAsync(deferFutureMeetings: false);

        var firstManifest = await manifestStore.LoadAsync(firstManifestPath);
        var secondManifest = await manifestStore.LoadAsync(secondManifestPath);

        Assert.Equal(2, result.DeferredMeetingCount);
        Assert.False(result.FutureMeetingsDeferred);
        Assert.False(result.InterruptedCurrentDiarization);
        Assert.Equal(BackgroundSpeakerLabelingMode.Inline, liveConfig.Current.BackgroundSpeakerLabelingMode);
        Assert.True(firstManifest.ProcessingOverrides?.SkipSpeakerLabeling);
        Assert.True(secondManifest.ProcessingOverrides?.SkipSpeakerLabeling);

        await service.StopAsync();
    }

    [Fact]
    public async Task RushBacklogAsync_Can_Defer_Current_Backlog_And_Future_Meetings()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            new FakeWorkerProcessFactory(),
            () => true);

        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(15));

        await service.EnqueueAsync(manifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);

        var result = await service.RushBacklogAsync(deferFutureMeetings: true);

        var manifest = await manifestStore.LoadAsync(manifestPath);

        Assert.Equal(1, result.DeferredMeetingCount);
        Assert.True(result.FutureMeetingsDeferred);
        Assert.Equal(BackgroundSpeakerLabelingMode.Deferred, liveConfig.Current.BackgroundSpeakerLabelingMode);
        Assert.True(manifest.ProcessingOverrides?.SkipSpeakerLabeling);

        await service.StopAsync();
    }

    [Fact]
    public async Task RushBacklogAsync_Interrupts_Current_Diarization_And_Requeues_Without_Labels()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(
            new FakeWorkerProcess { AutoCompleteOnKill = true },
            new FakeWorkerProcess());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            logger,
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory);

        var manifestPath = await CreateProcessingManifestAsync(
            manifestStore,
            liveConfig.Current.WorkDir,
            new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "Transcript ready."),
            new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, "Speaker labeling running."));

        await service.EnqueueAsync(manifestPath);
        var firstProcess = await processFactory.WaitForStartAsync(0);
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, manifestPath, StringComparison.Ordinal));

        var result = await service.RushBacklogAsync(deferFutureMeetings: false);

        await WaitForConditionAsync(() => firstProcess.KillCalled);
        Assert.False(firstProcess.LastKillEntireProcessTree);

        var secondProcess = await processFactory.WaitForStartAsync(1);
        Assert.Contains(manifestPath, processFactory.StartInfos[1].Arguments, StringComparison.Ordinal);
        var manifest = await manifestStore.LoadAsync(manifestPath);

        Assert.Equal(1, result.DeferredMeetingCount);
        Assert.True(result.InterruptedCurrentDiarization);
        Assert.True(manifest.ProcessingOverrides?.SkipSpeakerLabeling);
        Assert.Equal(manifestPath, service.GetStatusSnapshot().CurrentManifestPath);

        secondProcess.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RushBacklogAsync_Does_Not_Interrupt_Current_Speaker_Labeling_During_Recording()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.FastestDrain,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => true);

        var manifestPath = await CreateProcessingManifestAsync(
            manifestStore,
            liveConfig.Current.WorkDir,
            new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, "Transcript ready."),
            new ProcessingStageStatus("diarization", StageExecutionState.Running, DateTimeOffset.UtcNow, "Speaker labels running."));

        await service.EnqueueAsync(manifestPath);
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() => service.GetStatusSnapshot().CurrentStageName == "diarization");

        var result = await service.RushBacklogAsync(deferFutureMeetings: false);

        Assert.Equal(0, result.DeferredMeetingCount);
        Assert.False(result.InterruptedCurrentDiarization);
        Assert.False(process.KillCalled);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RushBacklogAsync_Does_Not_Interrupt_Current_Transcription()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
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
            processFactory);

        var manifestPath = await CreateProcessingManifestAsync(
            manifestStore,
            liveConfig.Current.WorkDir,
            new ProcessingStageStatus("transcription", StageExecutionState.Running, DateTimeOffset.UtcNow, "Transcription running."),
            new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null));

        await service.EnqueueAsync(manifestPath);
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() =>
            service.GetStatusSnapshot().CurrentStageName == "transcription" &&
            service.GetStatusSnapshot().CurrentStageState == StageExecutionState.Running);

        var result = await service.RushBacklogAsync(deferFutureMeetings: false);
        var manifest = await manifestStore.LoadAsync(manifestPath);

        Assert.Equal(0, result.DeferredMeetingCount);
        Assert.False(result.InterruptedCurrentDiarization);
        Assert.False(process.KillCalled);
        Assert.False(manifest.ProcessingOverrides?.SkipSpeakerLabeling == true);

        process.CompleteExit();
        await service.StopAsync();
    }

    [Fact]
    public async Task RushBacklogAsync_Does_Not_Defer_Speaker_Labels_For_The_Explicit_Asap_Meeting()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.Responsive,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Inline,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            new FakeWorkerProcessFactory(),
            () => true);
        var asapManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        var ordinaryManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12));

        await service.EnqueueAsync(asapManifestPath);
        await service.EnqueueAsync(ordinaryManifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);
        await service.RequestRushProcessingAsync(asapManifestPath, RushProcessingBehavior.RunNextOnly);

        var result = await service.RushBacklogAsync(deferFutureMeetings: false);
        var asapManifest = await manifestStore.LoadAsync(asapManifestPath);
        var ordinaryManifest = await manifestStore.LoadAsync(ordinaryManifestPath);

        Assert.Equal(1, result.DeferredMeetingCount);
        Assert.False(asapManifest.ProcessingOverrides?.SkipSpeakerLabeling == true);
        Assert.True(ordinaryManifest.ProcessingOverrides?.SkipSpeakerLabeling);
        Assert.Equal(asapManifestPath, liveConfig.Current.RushProcessingRequest?.ManifestPath);

        await service.StopAsync();
    }

    [Fact]
    public async Task ClearRushProcessingAsync_Removes_Only_The_Request_And_Does_Not_Cancel_Queued_Work()
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
        var isRecording = true;
        var processFactory = new FakeWorkerProcessFactory();
        var service = new ProcessingQueueService(
            liveConfig,
            manifestStore,
            new FileLogWriter(Path.Combine(root, "logs", "app.log")),
            meetingMetadataEnricher: null,
            () => new WorkerLaunch("fake-worker.exe", string.Empty),
            processFactory,
            () => isRecording,
            localNowProvider: () => AtLocal(2026, 9, 28, 12, 0));
        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));
        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(12));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(manifestPath);
        await WaitForConditionAsync(() => service.GetStatusSnapshot().RunState == ProcessingQueueRunState.Paused);
        await service.RequestRushProcessingAsync(manifestPath, RushProcessingBehavior.RunNextOnly);
        await service.ClearRushProcessingAsync(manifestPath);

        Assert.Null(liveConfig.Current.RushProcessingRequest);
        Assert.Equal(2, service.GetStatusSnapshot().TotalRemainingCount);
        Assert.Equal(SessionState.Queued, (await manifestStore.LoadAsync(manifestPath)).State);

        isRecording = false;
        var process = await processFactory.WaitForStartAsync();
        await WaitForConditionAsync(() =>
            string.Equals(service.GetStatusSnapshot().CurrentManifestPath, firstManifestPath, StringComparison.Ordinal));
        Assert.Equal(firstManifestPath, service.GetStatusSnapshot().CurrentManifestPath);
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
            processFactory,
            localNowProvider: () => AtLocal(2026, 9, 28, 12, 0));

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
            () => isRecording,
            localNowProvider: () => AtLocal(2026, 9, 28, 12, 0));

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
            processFactory,
            localNowProvider: () => AtLocal(2026, 9, 28, 12, 0));

        var firstManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(30));
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(10));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);

        var firstProcess = await processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(2));
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
    public async Task Maximum_Resource_Usage_Starts_Two_Workers()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configStore = new AppConfigStore(Path.Combine(root, "config", "appsettings.json"), Path.Combine(root, "documents"));
        var liveConfig = new LiveAppConfig(
            configStore,
            await configStore.SaveAsync((await configStore.LoadOrCreateAsync()) with
            {
                BackgroundProcessingMode = BackgroundProcessingMode.MaximumThroughput,
                InitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
                OvernightInitialProcessingStrategy = InitialProcessingStrategy.TranscriptFirst,
                BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
                SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled,
            }));
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
        var processFactory = new SequencedWorkerProcessFactory(
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
        var secondManifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir, TimeSpan.FromMinutes(20));

        Assert.Equal(2, BackgroundProcessingPolicy.GetMaxWorkerCount(liveConfig.Current));

        await service.EnqueueAsync(firstManifestPath);
        await service.EnqueueAsync(secondManifestPath);

        var firstProcess = await processFactory.WaitForStartAsync(0).WaitAsync(TimeSpan.FromSeconds(2));
        var secondProcess = await processFactory.WaitForStartAsync(1).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, processFactory.StartCount);

        firstProcess.CompleteExit();
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
    public async Task EnqueueAsync_ForceSpeakerLabeling_Bypasses_Deferred_Primary_Pass_Override()
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
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Deferred,
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
                processFactory);
            var manifestPath = await CreateQueuedManifestAsync(manifestStore, liveConfig.Current.WorkDir);
            var manifest = await manifestStore.LoadAsync(manifestPath);
            await manifestStore.SaveAsync(
                manifest with
                {
                    ProcessingOverrides = new MeetingProcessingOverrides(
                        TranscriptionModelPath: null,
                        TranscriptionModelFileName: null,
                        ForceSpeakerLabeling: true),
                },
                manifestPath);

            await service.EnqueueAsync(manifestPath);

            var process = await processFactory.WaitForStartAsync();
            var launchedManifest = await manifestStore.LoadAsync(manifestPath);
            Assert.False(launchedManifest.ProcessingOverrides?.SkipSpeakerLabeling == true);
            Assert.True(launchedManifest.ProcessingOverrides?.ForceSpeakerLabeling);

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
    public async Task EnqueueAsync_Retries_DirectMlDiarizationCrash_With_CpuSpeakerLabeling_And_Preserves_RunMode()
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
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Throttled,
                    DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
                    DiarizationAccelerationSecurityPromptMigrationApplied = true,
                }));
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
            Assert.False(updatedManifest.ProcessingOverrides?.SkipSpeakerLabeling == true);
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, liveConfig.Current.DiarizationAccelerationPreference);
            Assert.Equal(BackgroundSpeakerLabelingMode.Throttled, liveConfig.Current.BackgroundSpeakerLabelingMode);

            var retryConfig = await new AppConfigStore(secondConfigPath!).LoadOrCreateAsync();
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, retryConfig.DiarizationAccelerationPreference);
            Assert.Equal(BackgroundSpeakerLabelingMode.Throttled, retryConfig.BackgroundSpeakerLabelingMode);
            Assert.Equal(liveConfig.Current.DiarizationAssetPath, retryConfig.DiarizationAssetPath);

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
    public async Task WorkerFailure_Marks_Manifest_Failed_When_Worker_Does_Not_Update_State()
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

        process.ExitCode = 2;
        process.StandardErrorText = "No raw audio chunks were available, and no existing merged audio file could be found for this session.";
        process.CompleteExit();

        await WaitForConditionAsync(async () =>
        {
            try
            {
                return (await manifestStore.LoadAsync(manifestPath)).State == SessionState.Failed;
            }
            catch (IOException)
            {
                return false;
            }
        });
        var failedManifest = await manifestStore.LoadAsync(manifestPath);

        Assert.Equal(SessionState.Failed, failedManifest.State);
        Assert.Equal(StageExecutionState.Failed, failedManifest.TranscriptionStatus.State);
        Assert.Equal(StageExecutionState.Skipped, failedManifest.DiarizationStatus.State);
        Assert.Equal(StageExecutionState.Skipped, failedManifest.PublishStatus.State);
        Assert.Contains("No raw audio chunks", failedManifest.ErrorSummary, StringComparison.Ordinal);

        await service.StopAsync();
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
    public async Task EnqueueAsync_Falls_Back_To_NoDiarization_When_DirectMl_And_Cpu_Diarization_Crash()
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
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Throttled,
                    DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto,
                    DiarizationAccelerationSecurityPromptMigrationApplied = true,
                }));
            var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
            var logger = new FileLogWriter(Path.Combine(root, "logs", "app.log"));
            var processFactory = new SequencedWorkerProcessFactory(
                new FakeWorkerProcess
                {
                    ExitCode = unchecked((int)0xC0000005),
                    StandardErrorText = "Fatal error. System.AccessViolationException\r\n   at SherpaOnnx.OfflineSpeakerDiarization.Process(Single[])",
                },
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
            secondProcess.CompleteExit();

            var thirdProcess = await processFactory.WaitForStartAsync(2);
            var thirdConfigPath = ExtractConfigPath(processFactory.StartInfos[2].Arguments);
            var updatedManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(3, processFactory.StartCount);
            Assert.NotNull(thirdConfigPath);
            Assert.True(File.Exists(thirdConfigPath));
            Assert.True(updatedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, liveConfig.Current.DiarizationAccelerationPreference);
            Assert.Equal(BackgroundSpeakerLabelingMode.Deferred, liveConfig.Current.BackgroundSpeakerLabelingMode);

            var noLabelConfig = await new AppConfigStore(thirdConfigPath!).LoadOrCreateAsync();
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, noLabelConfig.DiarizationAccelerationPreference);
            Assert.NotEqual(liveConfig.Current.DiarizationAssetPath, noLabelConfig.DiarizationAssetPath);
            Assert.Empty(Directory.GetFiles(noLabelConfig.DiarizationAssetPath, "*", SearchOption.AllDirectories));

            thirdProcess.CompleteExit();
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
    public async Task EnqueueAsync_CpuOnly_DiarizationCrash_Uses_NoDiarizationRecovery_And_DefersFutureLabels()
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
                    BackgroundSpeakerLabelingMode = BackgroundSpeakerLabelingMode.Throttled,
                    DiarizationAccelerationPreference = InferenceAccelerationPreference.CpuOnly,
                    DiarizationAccelerationSecurityPromptMigrationApplied = true,
                }));
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
            Assert.True(File.Exists(secondConfigPath));
            Assert.True(updatedManifest.ProcessingOverrides?.SkipSpeakerLabeling);
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, liveConfig.Current.DiarizationAccelerationPreference);
            Assert.Equal(BackgroundSpeakerLabelingMode.Deferred, liveConfig.Current.BackgroundSpeakerLabelingMode);

            var noLabelConfig = await new AppConfigStore(secondConfigPath!).LoadOrCreateAsync();
            Assert.Equal(InferenceAccelerationPreference.CpuOnly, noLabelConfig.DiarizationAccelerationPreference);
            Assert.NotEqual(liveConfig.Current.DiarizationAssetPath, noLabelConfig.DiarizationAssetPath);
            Assert.Empty(Directory.GetFiles(noLabelConfig.DiarizationAssetPath, "*", SearchOption.AllDirectories));

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
    public async Task ResumePendingSessionsAsync_Recovers_Stale_Recording_Manifest_With_Raw_Chunks()
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

            var lastChunkWriteUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            var manifestPath = await CreateInterruptedRecordingManifestAsync(
                manifestStore,
                liveConfig.Current.WorkDir,
                lastChunkWriteUtc);

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(2));
            var recoveredManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(SessionState.Queued, recoveredManifest.State);
            Assert.Equal(lastChunkWriteUtc, recoveredManifest.EndedAtUtc);
            Assert.All(recoveredManifest.LoopbackCaptureSegments, segment => Assert.NotNull(segment.EndedAtUtc));
            Assert.All(recoveredManifest.MicrophoneCaptureSegments, segment => Assert.NotNull(segment.EndedAtUtc));
            Assert.Equal(CaptureTimelineEventKind.Stopped, recoveredManifest.CaptureTimeline[^1].Kind);
            Assert.Equal(StageExecutionState.Queued, recoveredManifest.TranscriptionStatus.State);
            Assert.Equal(StageExecutionState.NotStarted, recoveredManifest.DiarizationStatus.State);
            Assert.Equal(StageExecutionState.NotStarted, recoveredManifest.PublishStatus.State);
            Assert.Equal(1, processFactory.StartCount);
            Assert.Contains(manifestPath, processFactory.StartInfos[0].Arguments, StringComparison.Ordinal);

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
    public async Task ResumePendingSessionsAsync_Repairs_Stale_Queued_Manifest_With_Open_Capture_Segments()
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

            var firstChunkWriteUtc = DateTimeOffset.UtcNow.AddMinutes(-12);
            var lastChunkWriteUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            var manifestPath = await CreateInterruptedRecordingManifestAsync(
                manifestStore,
                liveConfig.Current.WorkDir,
                firstChunkWriteUtc);
            var rawDir = Path.Combine(Path.GetDirectoryName(manifestPath)!, "raw");
            var extraLoopbackChunkPath = Path.Combine(rawDir, "loopback-0001-chunk-0002.wav");
            var extraMicrophoneChunkPath = Path.Combine(rawDir, "microphone-0001-chunk-0002.wav");
            CreateWaveFile(extraLoopbackChunkPath, TimeSpan.FromSeconds(1));
            CreateWaveFile(extraMicrophoneChunkPath, TimeSpan.FromSeconds(1));
            File.SetLastWriteTimeUtc(extraLoopbackChunkPath, lastChunkWriteUtc.UtcDateTime);
            File.SetLastWriteTimeUtc(extraMicrophoneChunkPath, lastChunkWriteUtc.UtcDateTime);
            var malformedQueuedManifest = await manifestStore.LoadAsync(manifestPath);
            await manifestStore.SaveAsync(
                malformedQueuedManifest with
                {
                    State = SessionState.Queued,
                    EndedAtUtc = firstChunkWriteUtc,
                },
                manifestPath);

            await service.ResumePendingSessionsAsync();

            var process = await processFactory.WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(2));
            var recoveredManifest = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(SessionState.Queued, recoveredManifest.State);
            Assert.Equal(lastChunkWriteUtc, recoveredManifest.EndedAtUtc);
            Assert.Equal(2, recoveredManifest.RawChunkPaths.Count);
            Assert.Equal(2, recoveredManifest.MicrophoneChunkPaths.Count);
            Assert.All(recoveredManifest.LoopbackCaptureSegments, segment => Assert.NotNull(segment.EndedAtUtc));
            Assert.All(recoveredManifest.MicrophoneCaptureSegments, segment => Assert.NotNull(segment.EndedAtUtc));
            Assert.Equal(CaptureTimelineEventKind.Stopped, recoveredManifest.CaptureTimeline[^1].Kind);
            Assert.Equal(StageExecutionState.Queued, recoveredManifest.TranscriptionStatus.State);
            Assert.Equal(1, processFactory.StartCount);
            Assert.Contains(manifestPath, processFactory.StartInfos[0].Arguments, StringComparison.Ordinal);

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
    public async Task ResumePendingSessionsAsync_Leaves_Recent_Recording_Manifest_Alone()
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

            var manifestPath = await CreateInterruptedRecordingManifestAsync(
                manifestStore,
                liveConfig.Current.WorkDir,
                DateTimeOffset.UtcNow);

            await service.ResumePendingSessionsAsync();
            await Task.Delay(100);

            var manifest = await manifestStore.LoadAsync(manifestPath);
            Assert.Equal(SessionState.Recording, manifest.State);
            Assert.Null(manifest.EndedAtUtc);
            Assert.Equal(0, processFactory.StartCount);

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
        private readonly object _syncRoot = new();

        public int StartCount { get; private set; }

        public List<ProcessStartInfo> StartInfos { get; } = [];

        public IWorkerProcess Start(ProcessStartInfo startInfo)
        {
            var process = new FakeWorkerProcess();
            lock (_syncRoot)
            {
                StartCount++;
                StartInfos.Add(startInfo);
                process.ConfigureStagedReceipt(startInfo);
                _started.TrySetResult(process);
            }

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
        private readonly object _syncRoot = new();

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
            lock (_syncRoot)
            {
                var index = StartCount;
                var process = _processes[index];
                StartInfos.Add(startInfo);
                process.ConfigureStagedReceipt(startInfo);
                StartCount++;
                _startedSignals[index].TrySetResult(process);
                return process;
            }
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

        public bool? LastKillEntireProcessTree { get; private set; }

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
            LastKillEntireProcessTree = entireProcessTree;
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

        public void ConfigureStagedReceipt(ProcessStartInfo startInfo)
        {
            var tokens = Tokenize(startInfo.Arguments);
            var workIdIndex = tokens.FindIndex(token => string.Equals(token, "--work-id", StringComparison.Ordinal));
            var revisionIndex = tokens.FindIndex(token => string.Equals(token, "--work-revision", StringComparison.Ordinal));
            var leaseIndex = tokens.FindIndex(token => string.Equals(token, "--lease-token", StringComparison.Ordinal));
            var stageIndex = tokens.FindIndex(token => string.Equals(token, "--stage", StringComparison.Ordinal));
            if (workIdIndex < 0 || revisionIndex < 0 || leaseIndex < 0 || stageIndex < 0 ||
                workIdIndex + 1 >= tokens.Count || revisionIndex + 1 >= tokens.Count ||
                leaseIndex + 1 >= tokens.Count || stageIndex + 1 >= tokens.Count ||
                !Guid.TryParse(tokens[workIdIndex + 1], out var workId) ||
                !SessionProcessingStageParser.TryParse(tokens[stageIndex + 1], out var stage))
            {
                return;
            }

            StandardOutputText = JsonSerializer.Serialize(new SessionProcessingWorkReceipt(
                SessionProcessingWorkReceipt.CurrentSchemaVersion,
                workId,
                tokens[revisionIndex + 1],
                tokens[leaseIndex + 1],
                stage));
        }

        private static List<string> Tokenize(string arguments)
        {
            var tokens = new List<string>();
            var builder = new System.Text.StringBuilder();
            var quoted = false;
            foreach (var character in arguments)
            {
                if (character == '\"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (char.IsWhiteSpace(character) && !quoted)
                {
                    if (builder.Length > 0)
                    {
                        tokens.Add(builder.ToString());
                        builder.Clear();
                    }

                    continue;
                }

                builder.Append(character);
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
            }

            return tokens;
        }

        public void Dispose()
        {
        }
    }

    private sealed class SequencedCapacityProbe : IResourceCapacityProbe
    {
        private readonly Queue<SystemCpuTimes> _samples;

        public SequencedCapacityProbe(params SystemCpuTimes[] samples)
        {
            _samples = new Queue<SystemCpuTimes>(samples);
        }

        public bool TryGetSystemTimes(out SystemCpuTimes times)
        {
            times = _samples.Dequeue();
            return true;
        }

        public bool TryGetAcPower(out bool isPluggedIn)
        {
            isPluggedIn = true;
            return true;
        }
    }

    private sealed class SequencedGpuCapacityProbe : IGpuCapacityProbe
    {
        private readonly Queue<double> _samples;

        public SequencedGpuCapacityProbe(params double[] samples) => _samples = new Queue<double>(samples);

        public bool TryGetAggregateUtilization(out double utilizationPercent)
        {
            if (_samples.Count == 0)
            {
                utilizationPercent = 0;
                return false;
            }

            utilizationPercent = _samples.Dequeue();
            return true;
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

    private static async Task SetPublishedStageStateAsync(
        SessionManifestStore manifestStore,
        string manifestPath,
        StageExecutionState diarizationState,
        StageExecutionState summarizationState)
    {
        var manifest = await manifestStore.LoadAsync(manifestPath);
        var now = DateTimeOffset.UtcNow;
        await manifestStore.SaveAsync(
            manifest with
            {
                State = SessionState.Published,
                TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, now, null),
                DiarizationStatus = new ProcessingStageStatus("diarization", diarizationState, now, null),
                SummarizationStatus = new ProcessingStageStatus("summarization", summarizationState, now, null),
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, now, null),
            },
            manifestPath);
    }

    private static void AssertWorkerHasStagedLease(ProcessStartInfo startInfo, string stage)
    {
        Assert.Contains($"--stage {stage}", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("--work-id", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("--work-revision", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("--lease-token", startInfo.Arguments, StringComparison.Ordinal);
    }

    private static DateTimeOffset AtLocal(int year, int month, int day, int hour, int minute)
    {
        var localDateTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
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

    private static async Task<string> CreateProcessingManifestAsync(
        SessionManifestStore manifestStore,
        string workDir,
        ProcessingStageStatus transcriptionStatus,
        ProcessingStageStatus diarizationStatus)
    {
        var manifestPath = await CreateCompletedQueuedManifestAsync(manifestStore, workDir, TimeSpan.FromMinutes(25));
        var manifest = await manifestStore.LoadAsync(manifestPath);
        await manifestStore.SaveAsync(
            manifest with
            {
                State = SessionState.Processing,
                TranscriptionStatus = transcriptionStatus,
                DiarizationStatus = diarizationStatus,
                PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null),
            },
            manifestPath);
        return manifestPath;
    }

    private static async Task<string> CreateInterruptedRecordingManifestAsync(
        SessionManifestStore manifestStore,
        string workDir,
        DateTimeOffset lastChunkWriteUtc)
    {
        var manifest = await manifestStore.CreateAsync(
            workDir,
            MeetingPlatform.GoogleMeet,
            "Meet - upa-prqe-huf and 28 more pages - Work - Microsoft Edge",
            Array.Empty<DetectionSignal>());
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var sessionRoot = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Manifest path must include a directory.");
        var rawDir = Path.Combine(sessionRoot, "raw");
        Directory.CreateDirectory(rawDir);

        var loopbackChunkPath = Path.Combine(rawDir, "loopback-0001-chunk-0001.wav");
        var microphoneChunkPath = Path.Combine(rawDir, "microphone-0001-chunk-0001.wav");
        CreateWaveFile(loopbackChunkPath, TimeSpan.FromSeconds(1));
        CreateWaveFile(microphoneChunkPath, TimeSpan.FromSeconds(1));
        File.SetLastWriteTimeUtc(loopbackChunkPath, lastChunkWriteUtc.UtcDateTime);
        File.SetLastWriteTimeUtc(microphoneChunkPath, lastChunkWriteUtc.UtcDateTime);

        await manifestStore.SaveAsync(
            manifest with
            {
                State = SessionState.Recording,
                StartedAtUtc = lastChunkWriteUtc.AddMinutes(-30),
                EndedAtUtc = null,
                LoopbackCaptureSegments =
                [
                    new LoopbackCaptureSegment(
                        lastChunkWriteUtc.AddMinutes(-30),
                        null,
                        [loopbackChunkPath],
                        "device-1",
                        "Laptop speakers",
                        "Multimedia"),
                ],
                MicrophoneCaptureSegments =
                [
                    new MicrophoneCaptureSegment(
                        lastChunkWriteUtc.AddMinutes(-30),
                        null,
                        [microphoneChunkPath]),
                ],
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
        for (var index = 0; index < 80; index++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for the expected condition.");
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition)
    {
        for (var index = 0; index < 80; index++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(await condition(), "Timed out waiting for the expected condition.");
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
