using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class WorkerLocatorTests
{
    [Fact]
    public void ResolveInstalledAppRoot_Prefers_ProcessPath_Directory_Over_AppContext_BaseDirectory()
    {
        var installedRoot = WorkerLocator.ResolveInstalledAppRoot(
            @"C:\Users\test\Documents\MeetingRecorder\MeetingRecorder.App.exe",
            @"C:\Users\test\AppData\Local\Temp\.net\MeetingRecorder.App\abc123\");

        Assert.Equal(@"C:\Users\test\Documents\MeetingRecorder", installedRoot);
    }

    [Fact]
    public void EnumerateCandidates_Prefers_Worker_Beside_Installed_App_Before_AppContext_BaseDirectory()
    {
        var candidates = WorkerLocator
            .EnumerateCandidates(
                @"C:\Users\test\Documents\MeetingRecorder\MeetingRecorder.App.exe",
                @"C:\Users\test\AppData\Local\Temp\.net\MeetingRecorder.App\abc123\")
            .ToArray();

        Assert.Equal(
            @"C:\Users\test\Documents\MeetingRecorder\MeetingRecorder.ProcessingWorker.exe",
            candidates[0]);
        Assert.Equal(
            @"C:\Users\test\Documents\MeetingRecorder\MeetingRecorder.ProcessingWorker.dll",
            candidates[1]);
        Assert.DoesNotContain(
            @"C:\Users\test\AppData\Local\Temp\.net\MeetingRecorder.App\abc123\MeetingRecorder.ProcessingWorker.exe",
            candidates);
    }
}
