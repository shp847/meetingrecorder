using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
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

    private sealed class FakeWorkerProcessFactory : IWorkerProcessFactory
    {
        private readonly TaskCompletionSource<FakeWorkerProcess> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public IWorkerProcess Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            var process = new FakeWorkerProcess();
            _started.TrySetResult(process);
            return process;
        }

        public Task<FakeWorkerProcess> WaitForStartAsync()
        {
            return _started.Task;
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
        }

        public void CompleteExit()
        {
            HasExited = true;
            _standardOutput.TrySetResult(string.Empty);
            _standardError.TrySetResult(string.Empty);
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
}
