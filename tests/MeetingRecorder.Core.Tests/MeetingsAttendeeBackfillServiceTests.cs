using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingsAttendeeBackfillServiceTests : IDisposable
{
    private readonly string _root;

    public MeetingsAttendeeBackfillServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task BackfillBatchAsync_Does_Not_Merge_Attendees_When_The_Calendar_Title_Is_Unrelated()
    {
        var audioDir = Path.Combine(_root, "audio");
        var transcriptDir = Path.Combine(_root, "transcripts");
        var workDir = Path.Combine(_root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(Path.Combine(transcriptDir, "json"));
        Directory.CreateDirectory(workDir);

        var stem = "2026-03-24_163000_teams_jain-himanshu";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var jsonPath = Path.Combine(transcriptDir, "json", $"{stem}.json");
        await File.WriteAllTextAsync(audioPath, "audio");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(new
            {
                title = "Jain, Himanshu",
                attendees = Array.Empty<object>(),
                segments = Array.Empty<object>(),
            }));

        var record = new MeetingOutputRecord(
            Stem: stem,
            Title: "Jain, Himanshu",
            StartedAtUtc: DateTimeOffset.Parse("2026-03-24T16:30:00Z"),
            Platform: MeetingPlatform.Teams,
            Duration: TimeSpan.FromMinutes(6),
            AudioPath: audioPath,
            MarkdownPath: null,
            JsonPath: jsonPath,
            ReadyMarkerPath: null,
            ManifestPath: null,
            ManifestState: SessionState.Published,
            Attendees: Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: null,
            ProjectName: null,
            DetectedAudioSource: null,
            KeyAttendees: Array.Empty<string>());

        var config = new AppConfig
        {
            AudioOutputDir = audioDir,
            TranscriptOutputDir = transcriptDir,
            WorkDir = workDir,
        };
        var cacheService = new MeetingsAttendeeBackfillCacheService(
            Path.Combine(_root, "cache", "meetings-attendee-backfill-v1.json"));
        var service = new MeetingsAttendeeBackfillService(
            new StubCalendarMeetingTitleProvider(
                new CalendarMeetingDetailsCandidate(
                    "AI Super Users",
                    [
                        new MeetingAttendee("Paredes, Miguel", [MeetingAttendeeSource.OutlookCalendar]),
                        new MeetingAttendee("Yee, Candice", [MeetingAttendeeSource.OutlookCalendar]),
                    ],
                    "Outlook calendar")),
            new MeetingOutputCatalogService(new ArtifactPathBuilder()),
            cacheService);

        var result = await service.BackfillBatchAsync(
            [record],
            config,
            nowUtc: DateTimeOffset.Parse("2026-03-27T15:00:00Z"),
            forcedStems: null,
            attemptedStems: null,
            CancellationToken.None);

        Assert.False(result.UpdatedAnyMeeting);
        Assert.False(result.HasRemainingCandidates);
        Assert.Equal([record.Stem], result.ProcessedStems);
        var updatedRecord = Assert.Single(result.Records);
        Assert.Empty(updatedRecord.Attendees);
        Assert.Empty(updatedRecord.KeyAttendees ?? Array.Empty<string>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
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
