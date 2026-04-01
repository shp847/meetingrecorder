using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AudioLevelHistoryTests
{
    [Fact]
    public void CopySnapshot_Fills_The_Provided_Buffer_Without_Allocating_A_New_Array()
    {
        var history = new AudioLevelHistory(4);
        history.AddSample(0.25d);
        history.AddSample(0.5d);

        var destination = new[] { 9d, 9d, 9d, 9d };

        history.CopySnapshot(destination);

        Assert.Equal([0d, 0d, 0.25d, 0.5d], destination);
    }
}
