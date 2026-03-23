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
        DateTimeOffset? endedAtUtc);
}

internal sealed record OutlookCalendarAppointmentDetails(
    string Subject,
    DateTime StartLocal,
    int PlatformMatchScore,
    IReadOnlyList<string> AttendeeNames);

internal sealed class OutlookCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);
    private readonly object _cacheGate = new();
    private readonly IOutlookCalendarAppointmentSource _appointmentSource;

    private DateTimeOffset _cacheExpiresUtc;
    private MeetingPlatform _cachedPlatform = MeetingPlatform.Unknown;
    private DateTimeOffset _cachedStartedAtUtc;
    private DateTimeOffset? _cachedEndedAtUtc;
    private CalendarMeetingDetailsCandidate? _cachedCandidate;

    public OutlookCalendarMeetingTitleProvider()
        : this(new OutlookCalendarAppointmentSource())
    {
    }

    internal OutlookCalendarMeetingTitleProvider(IOutlookCalendarAppointmentSource appointmentSource)
    {
        _appointmentSource = appointmentSource;
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

        lock (_cacheGate)
        {
            if (platform == _cachedPlatform &&
                startedAtUtc == _cachedStartedAtUtc &&
                Nullable.Equals(endedAtUtc, _cachedEndedAtUtc) &&
                DateTimeOffset.UtcNow < _cacheExpiresUtc)
            {
                return _cachedCandidate;
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
            _cachedPlatform = platform;
            _cachedStartedAtUtc = startedAtUtc;
            _cachedEndedAtUtc = endedAtUtc;
            _cachedCandidate = resolved;
            _cacheExpiresUtc = DateTimeOffset.UtcNow.Add(CacheLifetime);
        }

        return resolved;
    }

    private CalendarMeetingDetailsCandidate? QueryMeetingTitle(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        var candidates = _appointmentSource.ReadOverlappingAppointments(platform, startedAtUtc, endedAtUtc);
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
            DateTimeOffset? endedAtUtc)
        {
            var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
            if (outlookType is null)
            {
                return Array.Empty<OutlookCalendarAppointmentDetails>();
            }

            object? outlookApplication = null;
            object? outlookNamespace = null;
            object? calendarFolder = null;
            object? calendarItems = null;

            try
            {
                outlookApplication = Activator.CreateInstance(outlookType);
                if (outlookApplication is null)
                {
                    return Array.Empty<OutlookCalendarAppointmentDetails>();
                }

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

                return ReadAppointmentsFromItems(calendarItems, platform, startedAtUtc, endedAtUtc);
            }
            finally
            {
                ReleaseComObject(calendarItems);
                ReleaseComObject(calendarFolder);
                ReleaseComObject(outlookNamespace);
                ReleaseComObject(outlookApplication);
            }
        }

        private static IReadOnlyList<OutlookCalendarAppointmentDetails> ReadAppointmentsFromItems(
            object calendarItems,
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
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

            foreach (var item in enumerable)
            {
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

                    var location = GetComStringProperty(item, "Location");
                    var body = GetComStringProperty(item, "Body");
                    var attendees = ReadAttendeeNames(item);
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

            return candidates;
        }

        private static IReadOnlyList<string> ReadAttendeeNames(object appointmentItem)
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
                foreach (var recipient in enumerable)
                {
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
            Marshal.ReleaseComObject(instance);
        }
    }
}
