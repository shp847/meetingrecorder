using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingsAttendeeBackfillPlannerTests : IDisposable
{
    private readonly string _root;
    private readonly MeetingsAttendeeBackfillCacheService _cacheService;

    public MeetingsAttendeeBackfillPlannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _cacheService = new MeetingsAttendeeBackfillCacheService(
            Path.Combine(_root, "cache", "meetings-attendee-backfill-v1.json"));
    }

    [Fact]
    public void SelectMeetingsForAutomaticBackfill_Returns_Recent_First_And_Limits_To_TwentyFive()
    {
        var nowUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");
        var meetings = Enumerable.Range(0, 40)
            .Select(index => CreateMeeting(
                stem: $"2026-03-{index + 1:00}_130000_teams_meeting-{index:00}",
                startedAtUtc: nowUtc.AddMinutes(-index).ToString("O"),
                duration: TimeSpan.FromMinutes(30)))
            .ToArray();

        var selected = MeetingsAttendeeBackfillPlanner.SelectMeetingsForBackfill(
            meetings,
            _cacheService,
            nowUtc,
            maxCount: 25,
            forcedStems: null);

        Assert.Equal(25, selected.Count);
        Assert.True(selected.Zip(selected.Skip(1), (left, right) => left.StartedAtUtc >= right.StartedAtUtc).All(result => result));
        Assert.Equal(meetings[0].Stem, selected[0].Stem);
        Assert.Equal(meetings[24].Stem, selected[^1].Stem);
    }

    [Fact]
    public void Forced_Stems_Bypass_Cached_NoMatch_Suppression()
    {
        var nowUtc = DateTimeOffset.Parse("2026-03-23T12:00:00Z");
        var cachedMeeting = CreateMeeting(
            stem: "2026-03-21_130000_teams_cached",
            startedAtUtc: "2026-03-21T13:00:00Z",
            duration: TimeSpan.FromMinutes(30));
        var uncachedMeeting = CreateMeeting(
            stem: "2026-03-20_130000_teams_uncached",
            startedAtUtc: "2026-03-20T13:00:00Z",
            duration: TimeSpan.FromMinutes(30));
        _cacheService.RecordNoMatch(cachedMeeting, nowUtc);

        var automaticSelection = MeetingsAttendeeBackfillPlanner.SelectMeetingsForBackfill(
            [cachedMeeting, uncachedMeeting],
            _cacheService,
            nowUtc,
            maxCount: 25,
            forcedStems: null);
        var forcedSelection = MeetingsAttendeeBackfillPlanner.SelectMeetingsForBackfill(
            [cachedMeeting, uncachedMeeting],
            _cacheService,
            nowUtc,
            maxCount: 25,
            forcedStems: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cachedMeeting.Stem });

        Assert.DoesNotContain(automaticSelection, meeting => string.Equals(meeting.Stem, cachedMeeting.Stem, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(forcedSelection, meeting => string.Equals(meeting.Stem, cachedMeeting.Stem, StringComparison.OrdinalIgnoreCase));
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
