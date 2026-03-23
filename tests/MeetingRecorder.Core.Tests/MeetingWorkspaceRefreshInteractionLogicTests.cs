using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Globalization;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingWorkspaceRefreshInteractionLogicTests
{
    [Fact]
    public void FormatMeetingWorkspaceStartedAt_Uses_Local_Short_Date_And_Time()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-03-21T00:21:55Z", null, DateTimeStyles.RoundtripKind);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        var formatted = MainWindowInteractionLogic.FormatMeetingWorkspaceStartedAt(startedAtUtc, culture, eastern);

        Assert.Equal("3/20/2026 8:21 PM", formatted);
    }

    [Fact]
    public void MeetingMatchesWorkspaceSearch_Matches_Project_Name()
    {
        var result = MainWindowInteractionLogic.MeetingMatchesWorkspaceSearch(
            "apollo",
            "Weekly Sync",
            "Project Apollo",
            "Teams",
            "Published",
            Array.Empty<MeetingAttendee>());

        Assert.True(result);
    }

    [Fact]
    public void BuildMeetingWorkspaceGroupLabel_Uses_Local_Week_Boundary()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-03-16T01:30:00Z", null, DateTimeStyles.RoundtripKind);
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var label = MainWindowInteractionLogic.BuildMeetingWorkspaceGroupLabel(
            MeetingsGroupKey.Week,
            startedAtUtc,
            "Teams",
            "Published",
            culture: CultureInfo.GetCultureInfo("en-US"),
            localTimeZone: pacific);

        Assert.Equal("Week of 2026-03-09", label);
    }

    [Fact]
    public void InitializeMeetingWorkspaceGroupExpansionState_Expands_Only_The_First_Group()
    {
        var states = MainWindowInteractionLogic.InitializeMeetingWorkspaceGroupExpansionState(
            ["Week of 2026-03-16", "Week of 2026-03-09", "Week of 2026-03-02"]);

        Assert.True(states["Week of 2026-03-16"]);
        Assert.False(states["Week of 2026-03-09"]);
        Assert.False(states["Week of 2026-03-02"]);
    }

    [Fact]
    public void FormatMeetingWorkspaceGroupHeader_Appends_Item_Count()
    {
        var header = MainWindowInteractionLogic.FormatMeetingWorkspaceGroupHeader("Week of 2026-03-16", 12);

        Assert.Equal("Week of 2026-03-16 (12)", header);
    }

    [Fact]
    public void BuildMeetingInspectorState_Uses_Local_Started_Format_And_Project_Name()
    {
        var meeting = new MeetingOutputRecord(
            "2026-03-21_002155_teams_new-invite-americas-partner-principal-call",
            "New Invite Americas Partner Principal Call",
            DateTimeOffset.Parse("2026-03-21T00:21:55Z", null, DateTimeStyles.RoundtripKind),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(56),
            @"C:\audio.wav",
            @"C:\meeting.md",
            @"C:\meeting.json",
            @"C:\meeting.ready",
            @"C:\work\manifest.json",
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            "ggml-small.bin",
            "Project Atlas");

        var state = MainWindowInteractionLogic.BuildMeetingInspectorState(
            meeting,
            Array.Empty<MeetingCleanupRecommendation>(),
            CultureInfo.GetCultureInfo("en-US"),
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));

        Assert.Equal("3/20/2026 8:21 PM", state.StartedAtUtc);
        Assert.Equal("Project Atlas", state.ProjectName);
    }
}
