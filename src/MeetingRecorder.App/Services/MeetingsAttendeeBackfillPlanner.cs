using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class MeetingsAttendeeBackfillPlanner
{
    public static IReadOnlyList<MeetingOutputRecord> SelectMeetingsForBackfill(
        IReadOnlyList<MeetingOutputRecord> records,
        MeetingsAttendeeBackfillCacheService cacheService,
        DateTimeOffset nowUtc,
        int maxCount,
        IReadOnlySet<string>? forcedStems,
        IReadOnlySet<string>? attemptedStems = null)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<MeetingOutputRecord>();
        }

        return records
            .Where(IsEligibleForBackfill)
            .Where(record => attemptedStems is null || !attemptedStems.Contains(record.Stem))
            .OrderByDescending(record => record.StartedAtUtc)
            .ThenBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .Where(record =>
                forcedStems?.Contains(record.Stem) == true ||
                !cacheService.ShouldSkipAutomaticBackfill(record, nowUtc))
            .Take(maxCount)
            .ToArray();
    }

    private static bool IsEligibleForBackfill(MeetingOutputRecord record)
    {
        return record.Attendees.Count == 0 &&
               record.Platform != MeetingPlatform.Unknown &&
               record.StartedAtUtc != DateTimeOffset.MinValue &&
               (!string.IsNullOrWhiteSpace(record.ManifestPath) || !string.IsNullOrWhiteSpace(record.JsonPath));
    }
}
