using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Tests;

public sealed class OutlookCalendarMeetingTitleProviderTests
{
    [Fact]
    public void TryGetMeetingTitle_Returns_Title_And_Attendees_From_Matched_Appointment()
    {
        var provider = new OutlookCalendarMeetingTitleProvider(
            new StubOutlookCalendarAppointmentSource(
            [
                new OutlookCalendarAppointmentDetails(
                    "Weekly Sync",
                    DateTime.Parse("2026-03-21T09:00:00"),
                    3,
                    ["Jane Smith", "John Doe"]),
            ]));

        var candidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z"));

        Assert.NotNull(candidate);
        Assert.Equal("Weekly Sync", candidate.Title);
        Assert.Equal("Outlook calendar", candidate.Source);
        Assert.Collection(candidate.Attendees,
            attendee =>
            {
                Assert.Equal("Jane Smith", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
            },
            attendee =>
            {
                Assert.Equal("John Doe", attendee.Name);
                Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
            });
    }

    [Fact]
    public void TryGetMeetingTitle_SoftFails_When_SourceThrows()
    {
        var provider = new OutlookCalendarMeetingTitleProvider(new ThrowingOutlookCalendarAppointmentSource());

        var candidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z"));

        Assert.Null(candidate);
    }

    [Fact]
    public void TryGetMeetingTitle_Deduplicates_Attendees_CaseInsensitively()
    {
        var provider = new OutlookCalendarMeetingTitleProvider(
            new StubOutlookCalendarAppointmentSource(
            [
                new OutlookCalendarAppointmentDetails(
                    "Weekly Sync",
                    DateTime.Parse("2026-03-21T09:00:00"),
                    3,
                    ["Jane Smith", " jane smith ", "JOHN DOE", "John Doe"]),
            ]));

        var candidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z"));

        Assert.NotNull(candidate);
        Assert.Collection(candidate.Attendees,
            attendee => Assert.Equal("Jane Smith", attendee.Name),
            attendee => Assert.Equal("JOHN DOE", attendee.Name));
    }

    private sealed class StubOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        private readonly IReadOnlyList<OutlookCalendarAppointmentDetails> _appointments;

        public StubOutlookCalendarAppointmentSource(IReadOnlyList<OutlookCalendarAppointmentDetails> appointments)
        {
            _appointments = appointments;
        }

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            return _appointments;
        }
    }

    private sealed class ThrowingOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            throw new InvalidOperationException("Outlook is unavailable.");
        }
    }
}
