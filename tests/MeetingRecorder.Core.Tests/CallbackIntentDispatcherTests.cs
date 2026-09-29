using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class CallbackIntentDispatcherTests
{
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
    public void Bounds_Queue_And_Trace()
    {
        var dispatcher = new CallbackIntentDispatcher();
        for (var index = 0; index < 70; index++)
            dispatcher.Enqueue(new($"key-{index}", "c", $"edge-{index}", index));
        Assert.Equal(64, dispatcher.Snapshot().Count(entry => entry.Outcome == CallbackIntentOutcome.Accepted));
        Assert.Contains(dispatcher.Snapshot(), entry => entry.Outcome == CallbackIntentOutcome.DeclinedOverload);
    }
}
