using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingTitleSuggestionServiceTests
{
    [Fact]
    public void TrySuggestTitle_PassiveMode_Does_Not_Query_Outlook_Calendar()
    {
        var provider = new ThrowingCalendarMeetingTitleProvider();
        var service = new MeetingTitleSuggestionService(provider);
        var capturedAtUtc = DateTimeOffset.Parse("2026-03-20T00:19:40Z");
        var record = new MeetingOutputRecord(
            "2026-03-20_001940_teams_microsoft-teams",
            "Microsoft Teams",
            capturedAtUtc,
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(10),
            null,
            null,
            null,
            null,
            "manifest.json",
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);
        var manifest = new MeetingSessionManifest
        {
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Microsoft Teams",
            StartedAtUtc = capturedAtUtc,
            DetectionEvidence =
            [
                new DetectionSignal("window-title", "Chat | Ducey, Gallina Nick | Microsoft Teams", 0.85d, capturedAtUtc),
            ],
        };

        var suggestion = service.TrySuggestTitle(
            record,
            manifest,
            MeetingTitleSuggestionMode.Passive);

        Assert.NotNull(suggestion);
        Assert.Equal("Ducey, Gallina Nick", suggestion.Title);
        Assert.Equal("Teams title history", suggestion.Source);
    }

    [Fact]
    public void TrySuggestTitle_Uses_Outlook_Calendar_For_Generic_Title()
    {
        var provider = new StubCalendarMeetingTitleProvider(
            new CalendarMeetingDetailsCandidate(
                "Intel CIO Discussion",
                [new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.OutlookCalendar])],
                "Outlook calendar"));
        var service = new MeetingTitleSuggestionService(provider);
        var record = new MeetingOutputRecord(
            "2026-03-19_233212_teams_microsoft-teams",
            "Microsoft Teams",
            DateTimeOffset.Parse("2026-03-19T23:32:12Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(10),
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var suggestion = service.TrySuggestTitle(
            record,
            manifest: null,
            MeetingTitleSuggestionMode.Interactive);

        Assert.NotNull(suggestion);
        Assert.Equal("Intel CIO Discussion", suggestion.Title);
        Assert.Equal("Outlook calendar", suggestion.Source);
    }

    [Fact]
    public void TrySuggestTitle_Uses_Teams_Title_History_When_Outlook_Is_Unavailable()
    {
        var provider = new StubCalendarMeetingTitleProvider(candidate: null);
        var service = new MeetingTitleSuggestionService(provider);
        var capturedAtUtc = DateTimeOffset.Parse("2026-03-20T00:19:40Z");
        var record = new MeetingOutputRecord(
            "2026-03-20_001940_teams_microsoft-teams",
            "Microsoft Teams",
            capturedAtUtc,
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(10),
            null,
            null,
            null,
            null,
            "manifest.json",
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);
        var manifest = new MeetingSessionManifest
        {
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Microsoft Teams",
            StartedAtUtc = capturedAtUtc,
            DetectionEvidence =
            [
                new DetectionSignal("window-title", "Chat | Ducey, Gallina Nick | Microsoft Teams", 0.85d, capturedAtUtc),
            ],
        };

        var suggestion = service.TrySuggestTitle(
            record,
            manifest,
            MeetingTitleSuggestionMode.Interactive);

        Assert.NotNull(suggestion);
        Assert.Equal("Ducey, Gallina Nick", suggestion.Title);
        Assert.Equal("Teams title history", suggestion.Source);
    }

    [Fact]
    public void TrySuggestTitle_Does_Not_Throw_For_Suppressed_Teams_Shell_Title_Without_Attendee_Name()
    {
        var provider = new StubCalendarMeetingTitleProvider(candidate: null);
        var service = new MeetingTitleSuggestionService(provider);
        var capturedAtUtc = DateTimeOffset.Parse("2026-03-24T13:33:20Z");
        var record = new MeetingOutputRecord(
            "2026-03-24_133320_teams_microsoft-teams",
            "Microsoft Teams",
            capturedAtUtc,
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(5),
            null,
            null,
            null,
            null,
            "manifest.json",
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);
        var manifest = new MeetingSessionManifest
        {
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Microsoft Teams",
            StartedAtUtc = capturedAtUtc,
            DetectionEvidence =
            [
                new DetectionSignal("window-title", "Activity | Microsoft Teams", 0.85d, capturedAtUtc),
            ],
        };

        var suggestion = service.TrySuggestTitle(
            record,
            manifest,
            MeetingTitleSuggestionMode.Passive);

        Assert.Null(suggestion);
    }

    private sealed class StubCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        private readonly CalendarMeetingDetailsCandidate? _candidate;

        public StubCalendarMeetingTitleProvider(CalendarMeetingDetailsCandidate? candidate)
        {
            _candidate = candidate;
        }

        public CalendarMeetingDetailsCandidate? TryGetMeetingTitle(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            return _candidate;
        }
    }

    private sealed class ThrowingCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        public CalendarMeetingDetailsCandidate? TryGetMeetingTitle(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            throw new InvalidOperationException("Passive title suggestion should not query Outlook.");
        }
    }
}
