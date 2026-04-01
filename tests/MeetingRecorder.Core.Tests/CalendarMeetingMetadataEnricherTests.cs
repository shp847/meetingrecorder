using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class CalendarMeetingMetadataEnricherTests
{
    [Fact]
    public async Task TryEnrichAsync_Persists_And_Merges_Outlook_Attendees_Into_Manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-calendar-enrich-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = new MeetingSessionManifest
            {
                SessionId = "session-001",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Weekly Sync",
                StartedAtUtc = DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-03-21T13:30:00Z"),
                Attendees = [new MeetingAttendee("Jane Smith", [MeetingAttendeeSource.TeamsLiveRoster])],
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await manifestStore.SaveAsync(manifest, manifestPath);

            var enricher = new CalendarMeetingMetadataEnricher(
                new StubCalendarMeetingTitleProvider(
                    new CalendarMeetingDetailsCandidate(
                        "Weekly Sync",
                        [
                            new MeetingAttendee("jane smith", [MeetingAttendeeSource.OutlookCalendar]),
                            new MeetingAttendee("John Doe", [MeetingAttendeeSource.OutlookCalendar]),
                        ],
                        "Outlook calendar")),
                manifestStore);

            var updated = await enricher.TryEnrichAsync(manifest, manifestPath);
            var reloaded = await manifestStore.LoadAsync(manifestPath);

            Assert.Collection(updated.Attendees,
                attendee =>
                {
                    Assert.Equal("Jane Smith", attendee.Name);
                    Assert.Equal(
                        [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                        attendee.Sources);
                },
                attendee =>
                {
                    Assert.Equal("John Doe", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
                });
            Assert.Collection(reloaded.Attendees,
                attendee =>
                {
                    Assert.Equal("Jane Smith", attendee.Name);
                    Assert.Equal(
                        [MeetingAttendeeSource.TeamsLiveRoster, MeetingAttendeeSource.OutlookCalendar],
                        attendee.Sources);
                },
                attendee =>
                {
                    Assert.Equal("John Doe", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
                });
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }

    [Fact]
    public async Task TryEnrichAsync_Merges_Key_Attendees_Using_Reasonable_Partial_Matches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-calendar-enrich-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = new MeetingSessionManifest
            {
                SessionId = "session-001",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Weekly Sync",
                StartedAtUtc = DateTimeOffset.Parse("2026-03-21T13:00:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-03-21T13:30:00Z"),
                KeyAttendees = ["Pranav"],
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await manifestStore.SaveAsync(manifest, manifestPath);

            var enricher = new CalendarMeetingMetadataEnricher(
                new StubCalendarMeetingTitleProvider(
                    new CalendarMeetingDetailsCandidate(
                        "Weekly Sync",
                        [
                            new MeetingAttendee("Pranav Sharma", [MeetingAttendeeSource.OutlookCalendar]),
                        ],
                        "Outlook calendar")),
                manifestStore);

            var updated = await enricher.TryEnrichAsync(manifest, manifestPath);
            var reloaded = await manifestStore.LoadAsync(manifestPath);

            Assert.Equal(["Pranav Sharma"], updated.KeyAttendees);
            Assert.Equal(["Pranav Sharma"], reloaded.KeyAttendees);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }

    [Fact]
    public async Task TryEnrichAsync_Does_Not_Merge_Outlook_Attendees_When_The_Calendar_Title_Is_Unrelated()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-calendar-enrich-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = new MeetingSessionManifest
            {
                SessionId = "session-001",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Jain, Himanshu",
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T16:30:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-03-24T16:35:49Z"),
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await manifestStore.SaveAsync(manifest, manifestPath);

            var enricher = new CalendarMeetingMetadataEnricher(
                new StubCalendarMeetingTitleProvider(
                    new CalendarMeetingDetailsCandidate(
                        "AI Super Users",
                        [
                            new MeetingAttendee("Paredes, Miguel", [MeetingAttendeeSource.OutlookCalendar]),
                            new MeetingAttendee("Yee, Candice", [MeetingAttendeeSource.OutlookCalendar]),
                            new MeetingAttendee("Deschamp, Cyrille", [MeetingAttendeeSource.OutlookCalendar]),
                            new MeetingAttendee("Bhagavatula, Krishna", [MeetingAttendeeSource.OutlookCalendar]),
                            new MeetingAttendee("Sharma, Pranav", [MeetingAttendeeSource.OutlookCalendar]),
                        ],
                        "Outlook calendar")),
                manifestStore);

            var updated = await enricher.TryEnrichAsync(manifest, manifestPath);
            var reloaded = await manifestStore.LoadAsync(manifestPath);

            Assert.Empty(updated.Attendees);
            Assert.Empty(reloaded.Attendees);
            Assert.Empty(updated.KeyAttendees);
            Assert.Empty(reloaded.KeyAttendees);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }

    [Fact]
    public async Task TryEnrichAsync_Merges_Outlook_Attendees_For_A_One_On_One_When_The_Calendar_Attendee_Matches_The_Recorded_Title()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-calendar-enrich-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var pathBuilder = new ArtifactPathBuilder();
            var manifestStore = new SessionManifestStore(pathBuilder);
            var manifest = new MeetingSessionManifest
            {
                SessionId = "session-001",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Jain, Himanshu",
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T16:30:00Z"),
                EndedAtUtc = DateTimeOffset.Parse("2026-03-24T16:35:49Z"),
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await manifestStore.SaveAsync(manifest, manifestPath);

            var enricher = new CalendarMeetingMetadataEnricher(
                new StubCalendarMeetingTitleProvider(
                    new CalendarMeetingDetailsCandidate(
                        "1:1",
                        [
                            new MeetingAttendee("Himanshu Jain", [MeetingAttendeeSource.OutlookCalendar]),
                        ],
                        "Outlook calendar")),
                manifestStore);

            var updated = await enricher.TryEnrichAsync(manifest, manifestPath);
            var reloaded = await manifestStore.LoadAsync(manifestPath);

            Assert.Collection(updated.Attendees,
                attendee =>
                {
                    Assert.Equal("Himanshu Jain", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
                });
            Assert.Collection(reloaded.Attendees,
                attendee =>
                {
                    Assert.Equal("Himanshu Jain", attendee.Name);
                    Assert.Equal([MeetingAttendeeSource.OutlookCalendar], attendee.Sources);
                });
            Assert.Equal(["Himanshu Jain"], updated.KeyAttendees);
            Assert.Equal(["Himanshu Jain"], reloaded.KeyAttendees);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
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
}
