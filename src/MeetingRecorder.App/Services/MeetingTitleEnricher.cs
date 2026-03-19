using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.App.Services;

internal interface ICalendarMeetingTitleProvider
{
    CalendarMeetingTitleCandidate? TryGetCurrentMeetingTitle(MeetingPlatform platform, DateTimeOffset nowUtc);
}

internal sealed record CalendarMeetingTitleCandidate(string Title, string Source);

internal sealed class MeetingTitleEnricher
{
    private readonly ICalendarMeetingTitleProvider _calendarMeetingTitleProvider;

    public MeetingTitleEnricher(ICalendarMeetingTitleProvider calendarMeetingTitleProvider)
    {
        _calendarMeetingTitleProvider = calendarMeetingTitleProvider;
    }

    public DetectionDecision Enrich(
        DetectionDecision decision,
        bool calendarTitleFallbackEnabled,
        DateTimeOffset nowUtc)
    {
        if (!calendarTitleFallbackEnabled || !RequiresCalendarFallback(decision))
        {
            return decision;
        }

        try
        {
            var candidate = _calendarMeetingTitleProvider.TryGetCurrentMeetingTitle(decision.Platform, nowUtc);
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Title))
            {
                return decision;
            }

            var updatedSignals = decision.Signals
                .Concat(
                [
                    new DetectionSignal(
                        "calendar-title-fallback",
                        $"{candidate.Source}: {candidate.Title}",
                        0.05d,
                        nowUtc),
                ])
                .ToArray();

            return decision with
            {
                SessionTitle = candidate.Title.Trim(),
                Signals = updatedSignals,
            };
        }
        catch
        {
            // Calendar lookup is intentionally soft-fail and must never break detection.
            return decision;
        }
    }

    private static bool RequiresCalendarFallback(DetectionDecision decision)
    {
        if (decision.Platform == MeetingPlatform.Unknown)
        {
            return false;
        }

        var normalizedTitle = decision.SessionTitle.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return true;
        }

        if (normalizedTitle is "detected meeting" or "meeting")
        {
            return true;
        }

        return decision.Platform switch
        {
            MeetingPlatform.Teams => normalizedTitle is "microsoft teams" or "teams" or "ms-teams" or "sharing control bar",
            MeetingPlatform.GoogleMeet => normalizedTitle is "google meet" or "meet",
            _ => false,
        };
    }
}
