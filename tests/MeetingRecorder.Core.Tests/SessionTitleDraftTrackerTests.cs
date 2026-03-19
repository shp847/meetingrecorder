using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SessionTitleDraftTrackerTests
{
    [Fact]
    public void GetDisplayTitle_Keeps_User_Draft_For_The_Same_Session()
    {
        var tracker = new SessionTitleDraftTracker();

        var initial = tracker.GetDisplayTitle("session-1", "Detected Title");
        tracker.UpdateDraft("session-1", "Detected Title", "Custom Title");
        var refreshed = tracker.GetDisplayTitle("session-1", "Detected Title");

        Assert.Equal("Detected Title", initial);
        Assert.Equal("Custom Title", refreshed);
    }

    [Fact]
    public void GetDisplayTitle_Resets_To_New_Session_Title_When_Session_Changes()
    {
        var tracker = new SessionTitleDraftTracker();

        tracker.GetDisplayTitle("session-1", "Detected Title");
        tracker.UpdateDraft("session-1", "Detected Title", "Custom Title");
        var nextSession = tracker.GetDisplayTitle("session-2", "Next Title");

        Assert.Equal("Next Title", nextSession);
    }
}
