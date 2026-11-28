using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCatalogPreloaderTests
{
    [Fact]
    public async Task BackgroundScanIsReusedWhenTheTabOpens()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        using var preloader = new MeetingCatalogPreloader<string>((_, _) => { starts++; return pending.Task; });
        var paths = new MeetingCatalogPaths("audio", "transcripts", "work");
        preloader.Start(paths, CancellationToken.None);
        preloader.Start(paths, CancellationToken.None);
        var whenOpened = preloader.TryGet(paths);
        Assert.NotNull(whenOpened);
        Assert.False(whenOpened.IsCompleted);
        pending.SetResult("loaded");
        Assert.Equal("loaded", await whenOpened);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task InvalidatingOrChangingFoldersCannotReturnAnOlderScan()
    {
        var starts = new List<TaskCompletionSource<string>>();
        var tokens = new List<CancellationToken>();
        using var preloader = new MeetingCatalogPreloader<string>((_, token) =>
        {
            tokens.Add(token);
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            starts.Add(pending);
            return pending.Task;
        });
        var oldPaths = new MeetingCatalogPaths("old", "transcripts", "work");
        var newPaths = oldPaths with { AudioDirectory = "new" };
        preloader.Start(oldPaths, CancellationToken.None);
        var oldTask = preloader.TryGet(oldPaths)!;
        preloader.Invalidate();
        Assert.True(tokens[0].IsCancellationRequested);
        Assert.Null(preloader.TryGet(oldPaths));
        preloader.Start(newPaths, CancellationToken.None);
        starts[1].SetResult("new");
        starts[0].SetResult("old");
        Assert.Equal("old", await oldTask);
        Assert.Equal("new", await preloader.TryGet(newPaths)!);
        Assert.Null(preloader.TryGet(oldPaths));
    }

    [Fact]
    public async Task FailedScanCanBeRetriedAndDisposedScanIsCanceled()
    {
        var calls = 0;
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        using var preloader = new MeetingCatalogPreloader<string>((_, token) =>
        {
            observed = token;
            return ++calls == 1 ? Task.FromException<string>(new IOException("failure")) : pending.Task;
        });
        var paths = new MeetingCatalogPaths("audio", "transcripts", "work");
        preloader.Start(paths, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => preloader.TryGet(paths)!);
        preloader.Start(paths, CancellationToken.None);
        Assert.Equal(2, calls);
        preloader.Dispose();
        Assert.True(observed.IsCancellationRequested);
    }
}
