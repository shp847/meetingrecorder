using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class CalendarMeetingAttendeeMergePolicy
{
    public static bool ShouldMergeAttendees(
        MeetingPlatform platform,
        string? recordedTitle,
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<string>? existingKeyAttendees,
        CalendarMeetingDetailsCandidate candidate)
    {
        if (candidate.Attendees.Count == 0)
        {
            return false;
        }

        if (HasStrongTitleMatch(recordedTitle, candidate.Title))
        {
            return true;
        }

        if (IsGenericMeetingTitle(platform, recordedTitle))
        {
            return true;
        }

        return HasIdentityNameMatch(recordedTitle, existingAttendees, existingKeyAttendees, candidate.Attendees);
    }

    private static bool HasStrongTitleMatch(string? recordedTitle, string? calendarTitle)
    {
        var normalizedRecordedTitle = MeetingTitleNormalizer.NormalizeForComparison(recordedTitle);
        var normalizedCalendarTitle = MeetingTitleNormalizer.NormalizeForComparison(calendarTitle);
        if (string.IsNullOrWhiteSpace(normalizedRecordedTitle) || string.IsNullOrWhiteSpace(normalizedCalendarTitle))
        {
            return false;
        }

        return string.Equals(normalizedRecordedTitle, normalizedCalendarTitle, StringComparison.Ordinal) ||
               MeetingMetadataNameMatcher.AreReasonableMatch(recordedTitle, calendarTitle);
    }

    private static bool HasIdentityNameMatch(
        string? recordedTitle,
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<string>? existingKeyAttendees,
        IReadOnlyList<MeetingAttendee> candidateAttendees)
    {
        var identityNames = new List<string>();
        AddIdentityName(recordedTitle, identityNames);

        foreach (var attendee in existingAttendees)
        {
            AddIdentityName(attendee.Name, identityNames);
        }

        if (existingKeyAttendees is not null)
        {
            foreach (var keyAttendee in existingKeyAttendees)
            {
                AddIdentityName(keyAttendee, identityNames);
            }
        }

        foreach (var identityName in identityNames)
        {
            foreach (var candidateAttendee in candidateAttendees)
            {
                if (MeetingMetadataNameMatcher.AreReasonableMatch(identityName, candidateAttendee.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AddIdentityName(string? value, IList<string> identityNames)
    {
        var normalizedValue = MeetingMetadataNameMatcher.NormalizeDisplayName(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        if (!identityNames.Contains(normalizedValue, StringComparer.OrdinalIgnoreCase))
        {
            identityNames.Add(normalizedValue);
        }
    }

    private static bool IsGenericMeetingTitle(MeetingPlatform platform, string? title)
    {
        var normalizedTitle = MeetingTitleNormalizer.NormalizeForComparison(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return true;
        }

        if (normalizedTitle is "detected meeting" or "meeting")
        {
            return true;
        }

        return platform switch
        {
            MeetingPlatform.Teams => normalizedTitle is "microsoft teams" or "teams" or "ms teams" or "sharing control bar" or "search",
            MeetingPlatform.GoogleMeet => normalizedTitle is "google meet" or "meet",
            _ => false,
        };
    }
}
