using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed class MeetingsAttendeeBackfillService
{
    internal const int DefaultBatchSize = 25;

    private readonly ICalendarMeetingTitleProvider _calendarMeetingTitleProvider;
    private readonly MeetingOutputCatalogService _meetingOutputCatalogService;
    private readonly MeetingsAttendeeBackfillCacheService _cacheService;

    public MeetingsAttendeeBackfillService(
        ICalendarMeetingTitleProvider calendarMeetingTitleProvider,
        MeetingOutputCatalogService meetingOutputCatalogService,
        MeetingsAttendeeBackfillCacheService cacheService)
    {
        _calendarMeetingTitleProvider = calendarMeetingTitleProvider;
        _meetingOutputCatalogService = meetingOutputCatalogService;
        _cacheService = cacheService;
    }

    public async Task<MeetingsAttendeeBackfillBatchResult> BackfillBatchAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        AppConfig config,
        DateTimeOffset nowUtc,
        IReadOnlySet<string>? forcedStems,
        IReadOnlySet<string>? attemptedStems,
        CancellationToken cancellationToken)
    {
        var candidates = MeetingsAttendeeBackfillPlanner.SelectMeetingsForBackfill(
            records,
            _cacheService,
            nowUtc,
            DefaultBatchSize,
            forcedStems,
            attemptedStems);
        if (candidates.Count == 0)
        {
            return new MeetingsAttendeeBackfillBatchResult(
                records,
                Array.Empty<string>(),
                UpdatedAnyMeeting: false,
                HasRemainingCandidates: false);
        }

        var updatedAnyMeeting = false;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CalendarMeetingDetailsCandidate? calendarCandidate;
            try
            {
                calendarCandidate = await StaThreadRunner.RunAsync(
                    () => _calendarMeetingTitleProvider.TryGetMeetingTitle(
                        candidate.Platform,
                        candidate.StartedAtUtc,
                        candidate.Duration is { } duration && duration > TimeSpan.Zero
                            ? candidate.StartedAtUtc.Add(duration)
                            : null),
                    cancellationToken);
            }
            catch
            {
                continue;
            }

            if (calendarCandidate is null || calendarCandidate.Attendees.Count == 0)
            {
                _cacheService.RecordNoMatch(candidate, nowUtc);
                continue;
            }

            var updatedMeeting = await _meetingOutputCatalogService.MergeMeetingAttendeesAsync(
                config.AudioOutputDir,
                config.TranscriptOutputDir,
                candidate.Stem,
                calendarCandidate.Attendees,
                config.WorkDir,
                cancellationToken);
            _cacheService.Clear(updatedMeeting);
            updatedAnyMeeting |= updatedMeeting.Attendees.Count > candidate.Attendees.Count;
        }

        var updatedRecords = updatedAnyMeeting
            ? _meetingOutputCatalogService.ListMeetings(
                config.AudioOutputDir,
                config.TranscriptOutputDir,
                config.WorkDir)
            : records;
        var processedStems = candidates
            .Select(candidate => candidate.Stem)
            .ToArray();
        var processedStemSet = new HashSet<string>(processedStems, StringComparer.OrdinalIgnoreCase);
        var combinedAttemptedStems = attemptedStems is null
            ? processedStemSet
            : new HashSet<string>(attemptedStems.Concat(processedStems), StringComparer.OrdinalIgnoreCase);
        var hasRemainingCandidates = MeetingsAttendeeBackfillPlanner.SelectMeetingsForBackfill(
            updatedRecords,
            _cacheService,
            nowUtc,
            DefaultBatchSize,
            forcedStems,
            combinedAttemptedStems).Count > 0;

        return new MeetingsAttendeeBackfillBatchResult(
            updatedRecords,
            processedStems,
            updatedAnyMeeting,
            hasRemainingCandidates);
    }
}

internal sealed record MeetingsAttendeeBackfillBatchResult(
    IReadOnlyList<MeetingOutputRecord> Records,
    IReadOnlyList<string> ProcessedStems,
    bool UpdatedAnyMeeting,
    bool HasRemainingCandidates);
