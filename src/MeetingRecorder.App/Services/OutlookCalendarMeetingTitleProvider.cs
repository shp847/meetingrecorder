using MeetingRecorder.Core.Domain;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MeetingRecorder.App.Services;

internal interface IOutlookCalendarAppointmentSource
{
    IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        CancellationToken cancellationToken);
}

internal sealed record OutlookCalendarAppointmentDetails(
    string Subject,
    DateTime StartLocal,
    int PlatformMatchScore,
    IReadOnlyList<string> AttendeeNames);

internal sealed class OutlookCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AppointmentCacheLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultAppointmentReadTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PointLookupBucket = TimeSpan.FromMinutes(1);
    private const int MaxCacheEntries = 32;
    private const int MaxAppointmentCacheEntries = 16;
    private readonly object _cacheGate = new();
    private readonly IOutlookCalendarAppointmentSource _appointmentSource;
    private readonly TimeSpan _appointmentReadTimeout;

    private readonly Dictionary<LookupCacheKey, CachedLookupEntry> _cache = new();
    private readonly Dictionary<AppointmentDayCacheKey, CachedAppointmentEntry> _appointmentCache = new();
    private readonly Dictionary<AppointmentDayCacheKey, InFlightAppointmentRead> _appointmentReadsInFlight = new();
    private DateTimeOffset _disabledUntilUtc;

    public OutlookCalendarMeetingTitleProvider()
        : this(new OutlookCalendarAppointmentSource())
    {
    }

    internal OutlookCalendarMeetingTitleProvider(
        IOutlookCalendarAppointmentSource appointmentSource,
        TimeSpan? appointmentReadTimeout = null)
    {
        _appointmentSource = appointmentSource;
        _appointmentReadTimeout = appointmentReadTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : DefaultAppointmentReadTimeout;
    }

    public CalendarMeetingDetailsCandidate? TryGetMeetingTitle(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        if (platform == MeetingPlatform.Unknown)
        {
            return null;
        }

        var lookupKey = LookupCacheKey.Create(platform, startedAtUtc, endedAtUtc);
        var nowUtc = DateTimeOffset.UtcNow;

        lock (_cacheGate)
        {
            PruneExpiredCacheEntries(nowUtc);
            PruneExpiredAppointmentCacheEntries(nowUtc);
            if (_cache.TryGetValue(lookupKey, out var cachedEntry) &&
                nowUtc < cachedEntry.ExpiresAtUtc)
            {
                return cachedEntry.Candidate;
            }

            if (nowUtc < _disabledUntilUtc)
            {
                return null;
            }
        }

        CalendarMeetingDetailsCandidate? resolved;
        try
        {
            resolved = QueryMeetingTitle(platform, startedAtUtc, endedAtUtc);
        }
        catch
        {
            resolved = null;
        }

        lock (_cacheGate)
        {
            PruneExpiredCacheEntries(nowUtc);
            PruneExpiredAppointmentCacheEntries(nowUtc);
            _cache[lookupKey] = new CachedLookupEntry(nowUtc.Add(CacheLifetime), resolved);
            EnforceCacheLimit();
        }

        return resolved;
    }

    private void PruneExpiredCacheEntries(DateTimeOffset nowUtc)
    {
        if (_cache.Count == 0)
        {
            return;
        }

        var expiredKeys = _cache
            .Where(pair => nowUtc >= pair.Value.ExpiresAtUtc)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var expiredKey in expiredKeys)
        {
            _cache.Remove(expiredKey);
        }
    }

    private void PruneExpiredAppointmentCacheEntries(DateTimeOffset nowUtc)
    {
        if (_appointmentCache.Count == 0)
        {
            return;
        }

        var expiredKeys = _appointmentCache
            .Where(pair => nowUtc >= pair.Value.ExpiresAtUtc)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var expiredKey in expiredKeys)
        {
            _appointmentCache.Remove(expiredKey);
        }
    }

    private void EnforceCacheLimit()
    {
        if (_cache.Count <= MaxCacheEntries)
        {
            return;
        }

        foreach (var key in _cache
                     .OrderBy(pair => pair.Value.ExpiresAtUtc)
                     .Select(pair => pair.Key)
                     .Take(_cache.Count - MaxCacheEntries)
                     .ToArray())
        {
            _cache.Remove(key);
        }
    }

    private void EnforceAppointmentCacheLimit()
    {
        if (_appointmentCache.Count <= MaxAppointmentCacheEntries)
        {
            return;
        }

        foreach (var key in _appointmentCache
                     .OrderBy(pair => pair.Value.ExpiresAtUtc)
                     .Select(pair => pair.Key)
                     .Take(_appointmentCache.Count - MaxAppointmentCacheEntries)
                     .ToArray())
        {
            _appointmentCache.Remove(key);
        }
    }

    private CalendarMeetingDetailsCandidate? QueryMeetingTitle(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        var candidates = ReadAppointmentsForLookup(platform, startedAtUtc, endedAtUtc);
        if (candidates.Count == 0)
        {
            return null;
        }

        var matchedCandidate = candidates
            .Where(candidate => candidate.PlatformMatchScore > 0)
            .OrderByDescending(candidate => candidate.PlatformMatchScore)
            .ThenBy(candidate => Math.Abs((candidate.StartLocal - startedAtUtc.LocalDateTime).TotalMinutes))
            .FirstOrDefault();

        if (matchedCandidate is not null)
        {
            return BuildCalendarCandidate(matchedCandidate);
        }

        return candidates.Count == 1
            ? BuildCalendarCandidate(candidates[0])
            : null;
    }

    private IReadOnlyList<OutlookCalendarAppointmentDetails> ReadAppointmentsForLookup(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        var normalizedEndedAtUtc = endedAtUtc.HasValue && endedAtUtc.Value >= startedAtUtc
            ? endedAtUtc.Value
            : startedAtUtc;
        var startDay = DateOnly.FromDateTime(startedAtUtc.LocalDateTime);
        var endDay = DateOnly.FromDateTime(normalizedEndedAtUtc.LocalDateTime);

        if (startDay == endDay)
        {
            return GetAppointmentsForLocalDay(platform, startDay);
        }

        var combined = new List<OutlookCalendarAppointmentDetails>();
        for (var localDay = startDay;
             localDay <= endDay;
             localDay = localDay.AddDays(1))
        {
            combined.AddRange(GetAppointmentsForLocalDay(platform, localDay));
        }

        return combined;
    }

    private IReadOnlyList<OutlookCalendarAppointmentDetails> GetAppointmentsForLocalDay(
        MeetingPlatform platform,
        DateOnly localDay)
    {
        var cacheKey = new AppointmentDayCacheKey(platform, localDay);
        var nowUtc = DateTimeOffset.UtcNow;
        InFlightAppointmentRead readState;

        lock (_cacheGate)
        {
            PruneExpiredAppointmentCacheEntries(nowUtc);
            if (_appointmentCache.TryGetValue(cacheKey, out var cachedEntry) &&
                nowUtc < cachedEntry.ExpiresAtUtc)
            {
                return cachedEntry.Appointments;
            }

            if (nowUtc < _disabledUntilUtc)
            {
                return Array.Empty<OutlookCalendarAppointmentDetails>();
            }

            if (!_appointmentReadsInFlight.TryGetValue(cacheKey, out readState!))
            {
                var cancellationSource = new CancellationTokenSource();
                var readTask = StaThreadRunner.RunAsync(
                    () => _appointmentSource.ReadOverlappingAppointments(
                        platform,
                        CreateLocalDayStart(localDay),
                        CreateLocalDayEnd(localDay),
                        cancellationSource.Token),
                    cancellationSource.Token);
                readState = new InFlightAppointmentRead(readTask, cancellationSource);
                _appointmentReadsInFlight[cacheKey] = readState;
            }
        }

        IReadOnlyList<OutlookCalendarAppointmentDetails> appointments;
        try
        {
            if (!readState.Task.Wait(_appointmentReadTimeout))
            {
                BackOffAfterAppointmentReadFailure(cacheKey, nowUtc, readState);
                return Array.Empty<OutlookCalendarAppointmentDetails>();
            }

            appointments = readState.Task.GetAwaiter().GetResult();
        }
        catch
        {
            BackOffAfterAppointmentReadFailure(cacheKey, nowUtc, readState);
            return Array.Empty<OutlookCalendarAppointmentDetails>();
        }

        lock (_cacheGate)
        {
            CompleteInFlightAppointmentRead(cacheKey, readState);
            PruneExpiredAppointmentCacheEntries(nowUtc);
            _appointmentCache[cacheKey] = new CachedAppointmentEntry(
                nowUtc.Add(AppointmentCacheLifetime),
                appointments);
            EnforceAppointmentCacheLimit();
        }

        return appointments;
    }

    private void BackOffAfterAppointmentReadFailure(
        AppointmentDayCacheKey cacheKey,
        DateTimeOffset nowUtc,
        InFlightAppointmentRead readState)
    {
        lock (_cacheGate)
        {
            CancelInFlightAppointmentRead(cacheKey, readState);
            _disabledUntilUtc = nowUtc.Add(FailureBackoff);
        }
    }

    private void CompleteInFlightAppointmentRead(
        AppointmentDayCacheKey cacheKey,
        InFlightAppointmentRead readState)
    {
        if (_appointmentReadsInFlight.TryGetValue(cacheKey, out var existingRead) &&
            ReferenceEquals(existingRead, readState))
        {
            _appointmentReadsInFlight.Remove(cacheKey);
            readState.CancellationSource.Dispose();
        }
    }

    private void CancelInFlightAppointmentRead(
        AppointmentDayCacheKey cacheKey,
        InFlightAppointmentRead readState)
    {
        if (_appointmentReadsInFlight.TryGetValue(cacheKey, out var existingRead) &&
            ReferenceEquals(existingRead, readState))
        {
            _appointmentReadsInFlight.Remove(cacheKey);
            try
            {
                readState.CancellationSource.Cancel();
            }
            catch
            {
                // Best-effort cancellation only.
            }

            _ = readState.Task.ContinueWith(
                static (_, state) =>
                {
                    if (state is CancellationTokenSource cancellationSource)
                    {
                        cancellationSource.Dispose();
                    }
                },
                readState.CancellationSource,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static DateTimeOffset CreateLocalDayStart(DateOnly localDay)
    {
        return new DateTimeOffset(new DateTime(
            localDay.Year,
            localDay.Month,
            localDay.Day,
            0,
            0,
            0,
            DateTimeKind.Local));
    }

    private static DateTimeOffset CreateLocalDayEnd(DateOnly localDay)
    {
        return new DateTimeOffset(new DateTime(
                localDay.Year,
                localDay.Month,
                localDay.Day,
                0,
                0,
                0,
                DateTimeKind.Local)
            .AddDays(1)
            .AddTicks(-1));
    }

    private static CalendarMeetingDetailsCandidate BuildCalendarCandidate(OutlookCalendarAppointmentDetails appointment)
    {
        return new CalendarMeetingDetailsCandidate(
            appointment.Subject.Trim(),
            NormalizeAttendees(appointment.AttendeeNames),
            "Outlook calendar");
    }

    private static IReadOnlyList<MeetingAttendee> NormalizeAttendees(IReadOnlyList<string>? attendeeNames)
    {
        if (attendeeNames is null || attendeeNames.Count == 0)
        {
            return Array.Empty<MeetingAttendee>();
        }

        var dedupedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attendeeName in attendeeNames)
        {
            if (string.IsNullOrWhiteSpace(attendeeName))
            {
                continue;
            }

            var trimmedName = attendeeName.Trim();
            if (!dedupedNames.ContainsKey(trimmedName))
            {
                dedupedNames[trimmedName] = trimmedName;
            }
        }

        return dedupedNames.Values
            .Select(name => new MeetingAttendee(name, [MeetingAttendeeSource.OutlookCalendar]))
            .ToArray();
    }

    private sealed class OutlookCalendarAppointmentSource : IOutlookCalendarAppointmentSource
    {
        private static readonly TimeSpan CalendarLookupTolerance = TimeSpan.FromMinutes(5);

        public IReadOnlyList<OutlookCalendarAppointmentDetails> ReadOverlappingAppointments(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outlookApplication = TryGetActiveOutlookApplication();
            if (outlookApplication is null)
            {
                return Array.Empty<OutlookCalendarAppointmentDetails>();
            }

            object? outlookNamespace = null;
            object? calendarFolder = null;
            object? calendarItems = null;
            object? restrictedCalendarItems = null;

            try
            {
                outlookNamespace = InvokeComMethod(outlookApplication, "GetNamespace", "MAPI");
                if (outlookNamespace is null)
                {
                    return Array.Empty<OutlookCalendarAppointmentDetails>();
                }

                calendarFolder = InvokeComMethod(outlookNamespace, "GetDefaultFolder", 9);
                if (calendarFolder is null)
                {
                    return Array.Empty<OutlookCalendarAppointmentDetails>();
                }

                calendarItems = GetComProperty(calendarFolder, "Items");
                if (calendarItems is null)
                {
                    return Array.Empty<OutlookCalendarAppointmentDetails>();
                }

                TrySetComProperty(calendarItems, "IncludeRecurrences", true);
                TryInvokeComMethod(calendarItems, "Sort", "[Start]");
                restrictedCalendarItems = TryRestrictCalendarItems(calendarItems, startedAtUtc, endedAtUtc);

                return ReadAppointmentsFromItems(
                    restrictedCalendarItems ?? calendarItems,
                    platform,
                    startedAtUtc,
                    endedAtUtc,
                    cancellationToken);
            }
            finally
            {
                if (restrictedCalendarItems is not null &&
                    !ReferenceEquals(restrictedCalendarItems, calendarItems))
                {
                    ReleaseComObject(restrictedCalendarItems);
                }

                ReleaseComObject(calendarItems);
                ReleaseComObject(calendarFolder);
                ReleaseComObject(outlookNamespace);
                ReleaseComObject(outlookApplication);
            }
        }

        private static object? TryGetActiveOutlookApplication()
        {
            try
            {
                var lookupResult = CLSIDFromProgID("Outlook.Application", out var clsid);
                if (lookupResult < 0)
                {
                    return null;
                }

                var activationResult = GetActiveObject(ref clsid, nint.Zero, out var activeObject);
                return activationResult >= 0
                    ? activeObject
                    : null;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
        private static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(
            ref Guid rclsid,
            nint reserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);

        private static IReadOnlyList<OutlookCalendarAppointmentDetails> ReadAppointmentsFromItems(
            object calendarItems,
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            CancellationToken cancellationToken)
        {
            var meetingStartLocal = startedAtUtc.LocalDateTime;
            var meetingEndLocal = (endedAtUtc ?? startedAtUtc).LocalDateTime;
            if (meetingEndLocal < meetingStartLocal)
            {
                meetingEndLocal = meetingStartLocal;
            }

            var lowerBoundLocal = meetingStartLocal.Subtract(CalendarLookupTolerance);
            var upperBoundLocal = meetingEndLocal.Add(CalendarLookupTolerance);
            var candidates = new List<OutlookCalendarAppointmentDetails>();

            if (calendarItems is not IEnumerable enumerable)
            {
                return candidates;
            }

            var enumerator = enumerable.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = enumerator.Current;
                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        var subject = GetComStringProperty(item, "Subject");
                        if (string.IsNullOrWhiteSpace(subject))
                        {
                            continue;
                        }

                        var startLocal = GetComDateTimeProperty(item, "Start");
                        var endLocal = GetComDateTimeProperty(item, "End");
                        if (!startLocal.HasValue || !endLocal.HasValue)
                        {
                            continue;
                        }

                        if (startLocal.Value > upperBoundLocal)
                        {
                            break;
                        }

                        if (endLocal.Value < lowerBoundLocal)
                        {
                            continue;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        var location = GetComStringProperty(item, "Location");
                        var body = GetComStringProperty(item, "Body");
                        var attendees = ReadAttendeeNames(item, cancellationToken);
                        candidates.Add(new OutlookCalendarAppointmentDetails(
                            subject.Trim(),
                            startLocal.Value,
                            ScorePlatformMatch(platform, subject, location, body),
                            attendees));
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }
            }
            finally
            {
                DisposeEnumerator(enumerator);
            }

            return candidates;
        }

        private static object? TryRestrictCalendarItems(
            object calendarItems,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            try
            {
                var meetingStartLocal = startedAtUtc.LocalDateTime;
                var meetingEndLocal = (endedAtUtc ?? startedAtUtc).LocalDateTime;
                if (meetingEndLocal < meetingStartLocal)
                {
                    meetingEndLocal = meetingStartLocal;
                }

                var lowerBoundLocal = meetingStartLocal.Subtract(CalendarLookupTolerance);
                var upperBoundLocal = meetingEndLocal.Add(CalendarLookupTolerance);
                var restriction =
                    $"[Start] <= '{FormatRestrictionDateTime(upperBoundLocal)}' AND [End] >= '{FormatRestrictionDateTime(lowerBoundLocal)}'";
                return InvokeComMethod(calendarItems, "Restrict", restriction);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatRestrictionDateTime(DateTime value)
        {
            return value.ToString("g", CultureInfo.CurrentCulture);
        }

        private static IReadOnlyList<string> ReadAttendeeNames(object appointmentItem, CancellationToken cancellationToken)
        {
            object? recipients = null;

            try
            {
                recipients = GetComProperty(appointmentItem, "Recipients");
                if (recipients is not IEnumerable enumerable)
                {
                    return Array.Empty<string>();
                }

                var attendeeNames = new List<string>();
                var enumerator = enumerable.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var recipient = enumerator.Current;
                        if (recipient is null)
                        {
                            continue;
                        }

                        try
                        {
                            var name = GetComStringProperty(recipient, "Name");
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                attendeeNames.Add(name.Trim());
                            }
                        }
                        finally
                        {
                            ReleaseComObject(recipient);
                        }
                    }
                }
                finally
                {
                    DisposeEnumerator(enumerator);
                }

                return attendeeNames;
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                ReleaseComObject(recipients);
            }
        }

        private static int ScorePlatformMatch(MeetingPlatform platform, string subject, string? location, string? body)
        {
            var haystack = string.Join(
                    "\n",
                    new[] { subject, location ?? string.Empty, body ?? string.Empty })
                .ToLowerInvariant();

            return platform switch
            {
                MeetingPlatform.Teams when haystack.Contains("teams.microsoft.com", StringComparison.Ordinal) => 3,
                MeetingPlatform.Teams when haystack.Contains("microsoft teams", StringComparison.Ordinal) => 2,
                MeetingPlatform.Teams when haystack.Contains("teams", StringComparison.Ordinal) => 1,
                MeetingPlatform.GoogleMeet when haystack.Contains("meet.google.com", StringComparison.Ordinal) => 3,
                MeetingPlatform.GoogleMeet when haystack.Contains("google meet", StringComparison.Ordinal) => 2,
                MeetingPlatform.GoogleMeet when haystack.Contains("meet", StringComparison.Ordinal) => 1,
                _ => 0,
            };
        }
    }

    private static object? GetComProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target: target,
            args: null,
            culture: CultureInfo.InvariantCulture);
    }

    private static string? GetComStringProperty(object target, string propertyName)
    {
        return GetComProperty(target, propertyName) as string;
    }

    private static DateTime? GetComDateTimeProperty(object target, string propertyName)
    {
        var value = GetComProperty(target, propertyName);
        return value switch
        {
            DateTime dateTime => dateTime,
            _ => null,
        };
    }

    private static object? InvokeComMethod(object target, string methodName, params object[] args)
    {
        return target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target: target,
            args: args,
            culture: CultureInfo.InvariantCulture);
    }

    private static void TryInvokeComMethod(object target, string methodName, params object[] args)
    {
        try
        {
            _ = InvokeComMethod(target, methodName, args);
        }
        catch
        {
            // Best-effort calendar access only.
        }
    }

    private static void TrySetComProperty(object target, string propertyName, object value)
    {
        try
        {
            target.GetType().InvokeMember(
                propertyName,
                BindingFlags.SetProperty,
                binder: null,
                target: target,
                args: new[] { value },
                culture: CultureInfo.InvariantCulture);
        }
        catch
        {
            // Best-effort calendar access only.
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            try
            {
                Marshal.FinalReleaseComObject(instance);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static void DisposeEnumerator(IEnumerator? enumerator)
    {
        if (enumerator is null)
        {
            return;
        }

        try
        {
            if (enumerator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }

        ReleaseComObject(enumerator);
    }

    private readonly record struct LookupCacheKey(
        MeetingPlatform Platform,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc)
    {
        public static LookupCacheKey Create(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            var normalizedStartedAtUtc = NormalizeUtc(startedAtUtc);
            DateTimeOffset? normalizedEndedAtUtc = endedAtUtc.HasValue
                ? NormalizeUtc(endedAtUtc.Value)
                : null;
            if (!normalizedEndedAtUtc.HasValue ||
                normalizedEndedAtUtc.Value == normalizedStartedAtUtc)
            {
                var pointBucket = FloorToBucket(normalizedStartedAtUtc, PointLookupBucket);
                return new LookupCacheKey(platform, pointBucket, pointBucket);
            }

            return new LookupCacheKey(platform, normalizedStartedAtUtc, normalizedEndedAtUtc);
        }

        private static DateTimeOffset NormalizeUtc(DateTimeOffset value)
        {
            return value.ToUniversalTime();
        }

        private static DateTimeOffset FloorToBucket(DateTimeOffset value, TimeSpan bucket)
        {
            var utcValue = value.ToUniversalTime();
            var ticks = utcValue.UtcDateTime.Ticks;
            var bucketTicks = bucket.Ticks;
            var flooredTicks = ticks - (ticks % bucketTicks);
            return new DateTimeOffset(flooredTicks, TimeSpan.Zero);
        }
    }

    private sealed record CachedLookupEntry(
        DateTimeOffset ExpiresAtUtc,
        CalendarMeetingDetailsCandidate? Candidate);

    private readonly record struct AppointmentDayCacheKey(
        MeetingPlatform Platform,
        DateOnly LocalDay);

    private sealed record CachedAppointmentEntry(
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<OutlookCalendarAppointmentDetails> Appointments);

    private sealed record InFlightAppointmentRead(
        Task<IReadOnlyList<OutlookCalendarAppointmentDetails>> Task,
        CancellationTokenSource CancellationSource);
}
