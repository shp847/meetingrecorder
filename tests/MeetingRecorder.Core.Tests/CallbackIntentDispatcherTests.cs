using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class CallbackIntentDispatcherTests
{
    [Fact]
    public async Task TraceStore_Is_Atomic_And_Treats_Corrupt_Trace_As_Empty()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "callback-trace.json");
            var store = new CallbackIntentTraceStore(path);
            var trace = new[] { new CallbackIntentTraceEntry(1, "refresh", "c", "edge", 1, CallbackIntentOutcome.Accepted) };
            await store.SaveAsync(trace);
            Assert.Equal(trace, await store.TryLoadAsync());
            Assert.False(File.Exists(path + ".tmp"));
            await File.WriteAllTextAsync(path, "not-json");
            Assert.Empty(await store.TryLoadAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Coalesces_Queued_Work_And_Declines_Reentrant_Cycles()
    {
        var dispatcher = new CallbackIntentDispatcher();
        var intent = new CallbackIntent("refresh", "c1", "refresh-to-refresh", 1);
        Assert.Equal(CallbackIntentOutcome.Accepted, dispatcher.Enqueue(intent));
        Assert.Equal(CallbackIntentOutcome.Coalesced, dispatcher.Enqueue(intent));
        Assert.True(dispatcher.TryBegin(out var active));
        Assert.Equal(CallbackIntentOutcome.DeclinedCycle, dispatcher.Enqueue(intent));
        dispatcher.Complete(active!);
        Assert.Equal(CallbackIntentOutcome.Accepted, dispatcher.Enqueue(intent));
    }

    [Fact]
    public void Begins_Requested_Key_Without_Reordering_Other_Callback_Lanes()
    {
        var dispatcher = new CallbackIntentDispatcher();
        dispatcher.Enqueue(new("refresh", "c", "refresh", 1));
        dispatcher.Enqueue(new("recording", "c", "recording", 2));
        Assert.True(dispatcher.TryBegin("recording", out var recording));
        Assert.Equal("recording", recording!.Key);
        dispatcher.Complete(recording);
        Assert.True(dispatcher.TryBegin("refresh", out var refresh));
        Assert.Equal("refresh", refresh!.Key);
    }

    [Fact]
    public void Bounds_Queue_And_Trace()
    {
        var dispatcher = new CallbackIntentDispatcher();
        for (var index = 0; index < 70; index++)
            dispatcher.Enqueue(new($"key-{index}", "c", $"edge-{index}", index));
        Assert.Equal(64, dispatcher.Snapshot().Count(entry => entry.Outcome == CallbackIntentOutcome.Accepted));
        Assert.Contains(dispatcher.Snapshot(), entry => entry.Outcome == CallbackIntentOutcome.DeclinedOverload);
    }
}
