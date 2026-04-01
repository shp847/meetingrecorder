using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using System.Threading;

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

    [Fact]
    public void TryGetMeetingTitle_Reuses_A_Single_PointInTime_Calendar_Read_Across_Several_Detection_Ticks()
    {
        var source = new StubOutlookCalendarAppointmentSource(
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:05Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:05Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:58Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:58Z"));

        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public void TryGetMeetingTitle_Reuses_The_Same_Day_Calendar_Read_For_PointInTime_And_Historic_Lookups()
    {
        var source = new StubOutlookCalendarAppointmentSource(
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T15:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T15:30:00Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:10Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:10Z"));

        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public void TryGetMeetingTitle_Keeps_Separate_Day_Cache_Entries_So_A_Different_Day_Lookup_Does_Not_Evict_The_Live_PointInTime_Cache()
    {
        var source = new StubOutlookCalendarAppointmentSource(
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-22T15:00:00Z"),
            DateTimeOffset.Parse("2026-03-22T15:30:00Z"));
        _ = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:10Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:10Z"));

        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public void TryGetMeetingTitle_Backs_Off_After_The_Outlook_Source_Throws()
    {
        var source = new ThrowingOutlookCalendarAppointmentSource();
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        var firstCandidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"),
            DateTimeOffset.Parse("2026-03-21T13:00:01Z"));
        var secondCandidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:02:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:02:00Z"));

        Assert.Null(firstCandidate);
        Assert.Null(secondCandidate);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task TryGetMeetingTitle_Reads_Outlook_On_An_Sta_Thread_Even_When_Called_From_A_Background_Task()
    {
        var source = new ApartmentRecordingOutlookCalendarAppointmentSource(
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        var candidate = await Task.Run(() => provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z")));

        Assert.NotNull(candidate);
        Assert.Equal(ApartmentState.STA, source.ApartmentState);
    }

    [Fact]
    public async Task TryGetMeetingTitle_Coalesces_Concurrent_SameDay_Lookups_Into_A_Single_Source_Read()
    {
        using var release = new ManualResetEventSlim(false);
        using var firstCallObserved = new ManualResetEventSlim(false);
        var source = new BlockingOutlookCalendarAppointmentSource(
            release,
            firstCallObserved,
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(source);

        var firstLookup = Task.Run(() => provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z")));

        Assert.True(firstCallObserved.Wait(TimeSpan.FromSeconds(2)));

        var secondLookup = Task.Run(() => provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T15:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T15:30:00Z")));

        await Task.Delay(150);

        Assert.Equal(1, source.CallCount);

        release.Set();

        var candidates = await Task.WhenAll(firstLookup, secondLookup);

        Assert.All(candidates, Assert.NotNull);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public void TryGetMeetingTitle_Backs_Off_After_A_Timed_Out_Outlook_Read()
    {
        using var release = new ManualResetEventSlim(false);
        using var firstCallObserved = new ManualResetEventSlim(false);
        var source = new BlockingOutlookCalendarAppointmentSource(
            release,
            firstCallObserved,
        [
            new OutlookCalendarAppointmentDetails(
                "Weekly Sync",
                DateTime.Parse("2026-03-21T09:00:00"),
                3,
                ["Jane Smith"]),
        ]);
        var provider = new OutlookCalendarMeetingTitleProvider(
            source,
            appointmentReadTimeout: TimeSpan.FromMilliseconds(100));

        var firstCandidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:30:00Z"));
        var secondCandidate = provider.TryGetMeetingTitle(
            MeetingPlatform.Teams,
            DateTimeOffset.Parse("2026-03-21T13:02:00Z"),
            DateTimeOffset.Parse("2026-03-21T13:32:00Z"));

        Assert.True(firstCallObserved.Wait(TimeSpan.FromSeconds(2)));
        Assert.Null(firstCandidate);
        Assert.Null(secondCandidate);
        Assert.Equal(1, source.CallCount);
    }

    private sealed class StubOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        private readonly IReadOnlyList<OutlookCalendarAppointmentDetails> _appointments;

        public StubOutlookCalendarAppointmentSource(IReadOnlyList<OutlookCalendarAppointmentDetails> appointments)
        {
            _appointments = appointments;
        }

        public int CallCount { get; private set; }

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            CallCount++;
            return _appointments;
        }
    }

    private sealed class ThrowingOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            CallCount++;
            throw new InvalidOperationException("Outlook is unavailable.");
        }
    }

    private sealed class ApartmentRecordingOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        private readonly IReadOnlyList<OutlookCalendarAppointmentDetails> _appointments;

        public ApartmentRecordingOutlookCalendarAppointmentSource(
            IReadOnlyList<OutlookCalendarAppointmentDetails> appointments)
        {
            _appointments = appointments;
        }

        public ApartmentState ApartmentState { get; private set; }

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            ApartmentState = Thread.CurrentThread.GetApartmentState();
            return _appointments;
        }
    }

    private sealed class BlockingOutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        private readonly ManualResetEventSlim _release;
        private readonly ManualResetEventSlim _firstCallObserved;
        private readonly IReadOnlyList<OutlookCalendarAppointmentDetails> _appointments;
        private int _callCount;

        public BlockingOutlookCalendarAppointmentSource(
            ManualResetEventSlim release,
            ManualResetEventSlim firstCallObserved,
            IReadOnlyList<OutlookCalendarAppointmentDetails> appointments)
        {
            _release = release;
            _firstCallObserved = firstCallObserved;
            _appointments = appointments;
        }

        public int CallCount => _callCount;

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            Interlocked.Increment(ref _callCount);
            _firstCallObserved.Set();
            _release.Wait();
            return _appointments;
        }
    }
}
