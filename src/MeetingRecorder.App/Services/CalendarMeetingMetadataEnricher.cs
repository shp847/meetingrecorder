using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal interface IMeetingMetadataEnricher
{
    Task<MeetingSessionManifest> TryEnrichAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken = default);
}

internal sealed class CalendarMeetingMetadataEnricher
    : IMeetingMetadataEnricher
{
    private readonly ICalendarMeetingTitleProvider _calendarMeetingTitleProvider;
    private readonly SessionManifestStore _manifestStore;

    public CalendarMeetingMetadataEnricher(
        ICalendarMeetingTitleProvider calendarMeetingTitleProvider,
        SessionManifestStore manifestStore)
    {
        _calendarMeetingTitleProvider = calendarMeetingTitleProvider;
        _manifestStore = manifestStore;
    }

    public async Task<MeetingSessionManifest> TryEnrichAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.Platform == MeetingPlatform.Unknown || manifest.StartedAtUtc == DateTimeOffset.MinValue)
        {
            return manifest;
        }

        CalendarMeetingDetailsCandidate? candidate;
        try
        {
            candidate = _calendarMeetingTitleProvider.TryGetMeetingTitle(
                manifest.Platform,
                manifest.StartedAtUtc,
                manifest.EndedAtUtc);
        }
        catch
        {
            return manifest;
        }

        if (candidate is null || candidate.Attendees.Count == 0)
        {
            return manifest;
        }

        if (!CalendarMeetingAttendeeMergePolicy.ShouldMergeAttendees(
                manifest.Platform,
                manifest.DetectedTitle,
                manifest.Attendees,
                manifest.KeyAttendees,
                candidate))
        {
            return manifest;
        }

        var mergedAttendees = MergeAttendees(manifest.Attendees, candidate.Attendees);
        var mergedKeyAttendees = MeetingMetadataNameMatcher.MergeNames(
            manifest.KeyAttendees,
            candidate.Attendees.Select(attendee => attendee.Name).ToArray());
        if (AreEquivalent(manifest.Attendees, mergedAttendees) &&
            (manifest.KeyAttendees ?? Array.Empty<string>()).SequenceEqual(mergedKeyAttendees, StringComparer.Ordinal))
        {
            return manifest;
        }

        var updatedManifest = manifest with
        {
            Attendees = mergedAttendees,
            KeyAttendees = mergedKeyAttendees,
        };
        await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
        return updatedManifest;
    }

    private static IReadOnlyList<MeetingAttendee> MergeAttendees(
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<MeetingAttendee> candidateAttendees)
    {
        var merged = new List<(string Name, List<MeetingAttendeeSource> Sources)>();

        AddAttendees(existingAttendees, merged);
        AddAttendees(candidateAttendees, merged);

        return merged
            .Select(item => new MeetingAttendee(
                item.Name,
                item.Sources.Count == 0 ? [MeetingAttendeeSource.Unknown] : item.Sources.ToArray()))
            .ToArray();
    }

    private static void AddAttendees(
        IReadOnlyList<MeetingAttendee> attendees,
        IList<(string Name, List<MeetingAttendeeSource> Sources)> merged)
    {
        foreach (var attendee in attendees)
        {
            if (string.IsNullOrWhiteSpace(attendee.Name))
            {
                continue;
            }

            var name = MeetingMetadataNameMatcher.NormalizeDisplayName(attendee.Name);
            var existingIndex = FindAttendeeIndex(merged, name);

            if (existingIndex < 0)
            {
                var newSources = attendee.Sources.Distinct().ToList();
                merged.Add((name, newSources));
                continue;
            }

            var existing = merged[existingIndex];
            var existingSources = existing.Sources;
            foreach (var source in attendee.Sources.Distinct())
            {
                if (!existingSources.Contains(source))
                {
                    existingSources.Add(source);
                }
            }

            merged[existingIndex] = (
                MeetingMetadataNameMatcher.ChoosePreferredDisplayName(existing.Name, name),
                existingSources);
        }
    }

    private static int FindAttendeeIndex(
        IList<(string Name, List<MeetingAttendeeSource> Sources)> merged,
        string candidateName)
    {
        for (var index = 0; index < merged.Count; index++)
        {
            if (MeetingMetadataNameMatcher.AreReasonableMatch(merged[index].Name, candidateName))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool AreEquivalent(
        IReadOnlyList<MeetingAttendee> existingAttendees,
        IReadOnlyList<MeetingAttendee> mergedAttendees)
    {
        if (existingAttendees.Count != mergedAttendees.Count)
        {
            return false;
        }

        for (var index = 0; index < existingAttendees.Count; index++)
        {
            var existing = existingAttendees[index];
            var merged = mergedAttendees[index];
            if (!string.Equals(existing.Name, merged.Name, StringComparison.Ordinal) ||
                !existing.Sources.SequenceEqual(merged.Sources))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class PassthroughMeetingMetadataEnricher : IMeetingMetadataEnricher
{
    public Task<MeetingSessionManifest> TryEnrichAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(manifest);
    }
}
