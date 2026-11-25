using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingLibraryStatusResolverTests
{
    [Fact]
    public void Published_Artifacts_Stay_Ready_Without_Queue_Status()
    {
        var status = MeetingLibraryStatusResolver.Resolve(Meeting(), [], true);
        Assert.Equal("Ready", status.Label);
        Assert.Equal(MeetingViewWorkState.Complete, status.WorkState);
    }

    [Fact]
    public void Transient_Retry_Is_Processing_And_Exhaustion_Needs_Attention()
    {
        var failure = new CleanupWorkLedgerEntry("item", CleanupWorkState.Failed, DateTimeOffset.UtcNow,
            Detail: "Temporary timeout", Action: MeetingCleanupAction.GenerateSpeakerLabels);
        var retry = MeetingLibraryStatusResolver.Resolve(Meeting(), [failure], true);
        var exhausted = MeetingLibraryStatusResolver.Resolve(Meeting(), [failure with { AutomaticRetryCount = 3 }], true);
        Assert.Equal("Retry scheduled", retry.Label);
        Assert.Equal(MeetingViewWorkState.RetryNeeded, retry.WorkState);
        Assert.Equal("Cleanup paused", MeetingLibraryStatusResolver.Resolve(Meeting(), [failure], false).Label);
        Assert.Equal(MeetingViewWorkState.Failed, exhausted.WorkState);
    }

    [Fact]
    public void Partial_Artifacts_Are_Described_Without_Claiming_Completion()
    {
        Assert.Equal("Audio only", MeetingLibraryStatusResolver.Resolve(Meeting() with { MarkdownPath = null }, [], true).Label);
        Assert.Equal("Transcript only", MeetingLibraryStatusResolver.Resolve(Meeting() with { AudioPath = null }, [], true).Label);
        Assert.Equal(MeetingViewWorkState.Blocked, MeetingLibraryStatusResolver.Resolve(Meeting() with { AudioPath = null }, [], true).WorkState);
    }

    [Fact]
    public void Retired_Summary_Work_Does_Not_Create_Attention_Or_Processing_Status()
    {
        var oldSummary = new CleanupWorkLedgerEntry("summary", CleanupWorkState.ManualReview, DateTimeOffset.UtcNow,
            Action: MeetingCleanupAction.GenerateSummary);
        var status = MeetingLibraryStatusResolver.Resolve(Meeting(), [oldSummary], true);
        Assert.Equal("Ready", status.Label);
        Assert.Equal(MeetingViewWorkState.Complete, status.WorkState);
    }

    private static MeetingOutputRecord Meeting() => new(
        "meeting", "Meeting", DateTimeOffset.UtcNow, MeetingPlatform.Teams, TimeSpan.FromMinutes(5),
        "meeting.wav", "meeting.md", null, "meeting.ready", null, SessionState.Published,
        Array.Empty<MeetingAttendee>(), false, null);
}
