using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingTitleEnricherTests
{
    [Fact]
    public void Enrich_Uses_Calendar_Title_When_Enabled_And_Detected_Title_Is_Generic()
    {
        var now = DateTimeOffset.Parse("2026-03-17T14:30:00Z");
        var provider = new StubCalendarMeetingTitleProvider(new CalendarMeetingTitleCandidate("Weekly Sync", "Outlook calendar"));
        var enricher = new MeetingTitleEnricher(provider);
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 0.9d,
            SessionTitle: "Microsoft Teams",
            Signals:
            [
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
            ],
            Reason: "Test");

        var enriched = enricher.Enrich(decision, calendarTitleFallbackEnabled: true, now);

        Assert.Equal("Weekly Sync", enriched.SessionTitle);
        Assert.Equal(1, provider.CallCount);
        var fallbackSignal = Assert.Single(enriched.Signals.Where(signal => signal.Source == "calendar-title-fallback"));
        Assert.Contains("Weekly Sync", fallbackSignal.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrich_Does_Not_Query_Calendar_When_Fallback_Is_Disabled()
    {
        var now = DateTimeOffset.Parse("2026-03-17T14:30:00Z");
        var provider = new StubCalendarMeetingTitleProvider(new CalendarMeetingTitleCandidate("Weekly Sync", "Outlook calendar"));
        var enricher = new MeetingTitleEnricher(provider);
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 0.9d,
            SessionTitle: "Microsoft Teams",
            Signals:
            [
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
            ],
            Reason: "Test");

        var enriched = enricher.Enrich(decision, calendarTitleFallbackEnabled: false, now);

        Assert.Equal("Microsoft Teams", enriched.SessionTitle);
        Assert.Equal(0, provider.CallCount);
        Assert.DoesNotContain(enriched.Signals, signal => signal.Source == "calendar-title-fallback");
    }

    [Fact]
    public void Enrich_Keeps_Original_Title_When_Calendar_Provider_Fails()
    {
        var now = DateTimeOffset.Parse("2026-03-17T14:30:00Z");
        var provider = new ThrowingCalendarMeetingTitleProvider();
        var enricher = new MeetingTitleEnricher(provider);
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 0.9d,
            SessionTitle: "Microsoft Teams",
            Signals:
            [
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
            ],
            Reason: "Test");

        var enriched = enricher.Enrich(decision, calendarTitleFallbackEnabled: true, now);

        Assert.Equal("Microsoft Teams", enriched.SessionTitle);
        Assert.DoesNotContain(enriched.Signals, signal => signal.Source == "calendar-title-fallback");
    }

    private sealed class StubCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        private readonly CalendarMeetingTitleCandidate? _candidate;

        public StubCalendarMeetingTitleProvider(CalendarMeetingTitleCandidate? candidate)
        {
            _candidate = candidate;
        }

        public int CallCount { get; private set; }

        public CalendarMeetingTitleCandidate? TryGetCurrentMeetingTitle(MeetingPlatform platform, DateTimeOffset nowUtc)
        {
            CallCount++;
            return _candidate;
        }
    }

    private sealed class ThrowingCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        public CalendarMeetingTitleCandidate? TryGetCurrentMeetingTitle(MeetingPlatform platform, DateTimeOffset nowUtc)
        {
            throw new InvalidOperationException("Outlook is unavailable.");
        }
    }
}
