using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingsAttendeeBackfillCacheServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _cachePath;

    public MeetingsAttendeeBackfillCacheServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _cachePath = Path.Combine(_root, "cache", "meetings-attendee-backfill-v1.json");
    }

    [Fact]
    public void RecordNoMatch_Suppresses_Automatic_Backfill_For_Seven_Days_When_Fingerprint_Is_Unchanged()
    {
        var service = new MeetingsAttendeeBackfillCacheService(_cachePath);
        var record = CreateMeeting(
            stem: "2026-03-21_130000_teams_weekly-sync",
            startedAtUtc: "2026-03-21T13:00:00Z",
            duration: TimeSpan.FromMinutes(30));
        var nowUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");

        service.RecordNoMatch(record, nowUtc);

        Assert.True(service.ShouldSkipAutomaticBackfill(record, nowUtc.AddDays(6)));
        Assert.False(service.ShouldSkipAutomaticBackfill(record, nowUtc.AddDays(8)));
    }

    [Fact]
    public void Changed_Fingerprint_Invalidates_A_Previously_Cached_NoMatch()
    {
        var service = new MeetingsAttendeeBackfillCacheService(_cachePath);
        var original = CreateMeeting(
            stem: "2026-03-21_130000_teams_weekly-sync",
            startedAtUtc: "2026-03-21T13:00:00Z",
            duration: TimeSpan.FromMinutes(30));
        var changed = CreateMeeting(
            stem: "2026-03-21_130000_teams_weekly-sync",
            startedAtUtc: "2026-03-21T13:00:00Z",
            duration: TimeSpan.FromMinutes(45));
        var nowUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");

        service.RecordNoMatch(original, nowUtc);

        Assert.False(service.ShouldSkipAutomaticBackfill(changed, nowUtc.AddDays(1)));
    }

    [Fact]
    public void Clear_NoMatch_Removes_A_Cached_Entry()
    {
        var service = new MeetingsAttendeeBackfillCacheService(_cachePath);
        var record = CreateMeeting(
            stem: "2026-03-21_130000_teams_weekly-sync",
            startedAtUtc: "2026-03-21T13:00:00Z",
            duration: TimeSpan.FromMinutes(30));
        var nowUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");

        service.RecordNoMatch(record, nowUtc);
        service.Clear(record);

        Assert.False(service.ShouldSkipAutomaticBackfill(record, nowUtc.AddHours(1)));
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

    private static MeetingOutputRecord CreateMeeting(
        string stem,
        string startedAtUtc,
        TimeSpan duration,
        IReadOnlyList<MeetingAttendee>? attendees = null)
    {
        return new MeetingOutputRecord(
            stem,
            "Weekly Sync",
            DateTimeOffset.Parse(startedAtUtc),
            MeetingPlatform.Teams,
            duration,
            AudioPath: @"C:\audio.wav",
            MarkdownPath: @"C:\transcript.md",
            JsonPath: @"C:\transcript.json",
            ReadyMarkerPath: null,
            ManifestPath: @"C:\manifest.json",
            ManifestState: SessionState.Published,
            Attendees: attendees ?? Array.Empty<MeetingAttendee>(),
            HasSpeakerLabels: false,
            TranscriptionModelFileName: "ggml-base.bin",
            ProjectName: null);
    }
}
