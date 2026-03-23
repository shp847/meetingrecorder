using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal enum MeetingTitleSuggestionMode
{
    Passive = 0,
    Interactive = 1,
}

internal sealed record MeetingTitleSuggestion(string Title, string Source);

internal sealed class MeetingTitleSuggestionService
{
    private readonly ICalendarMeetingTitleProvider _calendarMeetingTitleProvider;

    public MeetingTitleSuggestionService(ICalendarMeetingTitleProvider calendarMeetingTitleProvider)
    {
        _calendarMeetingTitleProvider = calendarMeetingTitleProvider;
    }

    public MeetingTitleSuggestion? TrySuggestTitle(
        MeetingOutputRecord record,
        MeetingSessionManifest? manifest,
        MeetingTitleSuggestionMode mode = MeetingTitleSuggestionMode.Interactive)
    {
        var currentTitle = record.Title.Trim();
        var meetingWindow = BuildMeetingWindow(record, manifest);

        var signalSuggestion = TrySuggestFromSignals(record.Platform, currentTitle, manifest?.DetectionEvidence);
        if (signalSuggestion is not null)
        {
            return signalSuggestion;
        }

        if (mode == MeetingTitleSuggestionMode.Passive || meetingWindow is null)
        {
            return null;
        }

        try
        {
            var calendarCandidate = _calendarMeetingTitleProvider.TryGetMeetingTitle(
                record.Platform,
                meetingWindow.Value.StartedAtUtc,
                meetingWindow.Value.EndedAtUtc);
            if (calendarCandidate is null || !IsUsableSuggestion(record.Platform, currentTitle, calendarCandidate.Title))
            {
                return null;
            }

            return new MeetingTitleSuggestion(calendarCandidate.Title.Trim(), calendarCandidate.Source);
        }
        catch
        {
            return null;
        }
    }

    private static (DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc)? BuildMeetingWindow(
        MeetingOutputRecord record,
        MeetingSessionManifest? manifest)
    {
        if (record.StartedAtUtc == DateTimeOffset.MinValue)
        {
            return null;
        }

        var endedAtUtc = manifest?.EndedAtUtc;
        if (!endedAtUtc.HasValue && record.Duration is { } duration && duration > TimeSpan.Zero)
        {
            endedAtUtc = record.StartedAtUtc + duration;
        }

        return (record.StartedAtUtc, endedAtUtc);
    }

    private static MeetingTitleSuggestion? TrySuggestFromSignals(
        MeetingPlatform platform,
        string currentTitle,
        IReadOnlyList<DetectionSignal>? signals)
    {
        if (signals is null || signals.Count == 0)
        {
            return null;
        }

        foreach (var signal in signals
                     .OrderByDescending(signal => signal.Weight)
                     .ThenByDescending(signal => signal.CapturedAtUtc))
        {
            if (TryExtractCalendarSignalTitle(signal, out var calendarTitle) &&
                IsUsableSuggestion(platform, currentTitle, calendarTitle))
            {
                return new MeetingTitleSuggestion(calendarTitle, "Outlook calendar");
            }

            if (platform != MeetingPlatform.Teams)
            {
                continue;
            }

            if (TryExtractTeamsSignalTitle(signal, out var teamsTitle) &&
                IsUsableSuggestion(platform, currentTitle, teamsTitle))
            {
                return new MeetingTitleSuggestion(teamsTitle, "Teams title history");
            }
        }

        return null;
    }

    private static bool TryExtractCalendarSignalTitle(DetectionSignal signal, out string title)
    {
        title = string.Empty;
        if (!string.Equals(signal.Source, "calendar-title-fallback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = signal.Value.IndexOf(':');
        var candidate = separatorIndex >= 0
            ? signal.Value[(separatorIndex + 1)..]
            : signal.Value;
        candidate = candidate.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        title = candidate;
        return true;
    }

    private static bool TryExtractTeamsSignalTitle(DetectionSignal signal, out string title)
    {
        title = string.Empty;
        if (!string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = signal.Value.Trim();
        if (candidate.Contains("visual studio code", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryExtractSuppressedTeamsAttendeeTitle(candidate, out var attendeeTitle))
        {
            title = attendeeTitle;
            return true;
        }

        candidate = candidate
            .Replace("- Microsoft Teams", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("| Microsoft Teams", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('|', ' ');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        title = candidate;
        return true;
    }

    private static bool TryExtractSuppressedTeamsAttendeeTitle(string value, out string title)
    {
        title = string.Empty;

        var prefixes = new[]
        {
            "Chat |",
            "Calls |",
            "Search |",
            "Activity |",
            "Calendar |",
        };
        const string teamsSuffix = "| Microsoft Teams";

        if (!value.EndsWith(teamsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = value[prefix.Length..^teamsSuffix.Length].Trim().Trim('|', ' ');
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            title = candidate;
            return true;
        }

        return false;
    }

    private static bool IsUsableSuggestion(MeetingPlatform platform, string currentTitle, string candidateTitle)
    {
        var normalizedCurrent = NormalizeTitle(currentTitle);
        var normalizedCandidate = NormalizeTitle(candidateTitle);
        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return false;
        }

        if (string.Equals(currentTitle.Trim(), candidateTitle.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return !IsGenericTitle(platform, normalizedCandidate) &&
               !string.Equals(normalizedCurrent, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        return string.Join(
                " ",
                title.Trim()
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static bool IsGenericTitle(MeetingPlatform platform, string normalizedTitle)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return true;
        }

        return platform switch
        {
            MeetingPlatform.Teams => normalizedTitle is "microsoft teams" or "teams" or "ms-teams" or "sharing control bar" or "search" or "calls" or "chat",
            MeetingPlatform.GoogleMeet => normalizedTitle is "google meet" or "meet",
            _ => normalizedTitle is "meeting" or "detected meeting",
        };
    }
}
