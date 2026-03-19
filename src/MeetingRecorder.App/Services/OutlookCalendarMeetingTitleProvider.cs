using MeetingRecorder.Core.Domain;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MeetingRecorder.App.Services;

internal sealed class OutlookCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CalendarLookupTolerance = TimeSpan.FromMinutes(5);
    private readonly object _cacheGate = new();

    private DateTimeOffset _cacheExpiresUtc;
    private MeetingPlatform _cachedPlatform = MeetingPlatform.Unknown;
    private CalendarMeetingTitleCandidate? _cachedCandidate;

    public CalendarMeetingTitleCandidate? TryGetCurrentMeetingTitle(MeetingPlatform platform, DateTimeOffset nowUtc)
    {
        if (platform == MeetingPlatform.Unknown)
        {
            return null;
        }

        lock (_cacheGate)
        {
            if (platform == _cachedPlatform && nowUtc < _cacheExpiresUtc)
            {
                return _cachedCandidate;
            }
        }

        CalendarMeetingTitleCandidate? resolved;
        try
        {
            resolved = QueryCurrentMeetingTitle(platform, nowUtc);
        }
        catch
        {
            resolved = null;
        }

        lock (_cacheGate)
        {
            _cachedPlatform = platform;
            _cachedCandidate = resolved;
            _cacheExpiresUtc = nowUtc.Add(CacheLifetime);
        }

        return resolved;
    }

    private static CalendarMeetingTitleCandidate? QueryCurrentMeetingTitle(MeetingPlatform platform, DateTimeOffset nowUtc)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application", throwOnError: false);
        if (outlookType is null)
        {
            return null;
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
                return null;
            }

            outlookNamespace = InvokeComMethod(outlookApplication, "GetNamespace", "MAPI");
            if (outlookNamespace is null)
            {
                return null;
            }

            calendarFolder = InvokeComMethod(outlookNamespace, "GetDefaultFolder", 9);
            if (calendarFolder is null)
            {
                return null;
            }

            calendarItems = GetComProperty(calendarFolder, "Items");
            if (calendarItems is null)
            {
                return null;
            }

            TrySetComProperty(calendarItems, "IncludeRecurrences", true);
            TryInvokeComMethod(calendarItems, "Sort", "[Start]");

            var candidates = ReadOverlappingAppointments(calendarItems, platform, nowUtc);
            if (candidates.Count == 0)
            {
                return null;
            }

            var matchedCandidate = candidates
                .Where(candidate => candidate.PlatformMatchScore > 0)
                .OrderByDescending(candidate => candidate.PlatformMatchScore)
                .ThenBy(candidate => Math.Abs((candidate.StartLocal - nowUtc.LocalDateTime).TotalMinutes))
                .FirstOrDefault();

            if (matchedCandidate is not null)
            {
                return new CalendarMeetingTitleCandidate(matchedCandidate.Subject, "Outlook calendar");
            }

            return candidates.Count == 1
                ? new CalendarMeetingTitleCandidate(candidates[0].Subject, "Outlook calendar")
                : null;
        }
        finally
        {
            ReleaseComObject(calendarItems);
            ReleaseComObject(calendarFolder);
            ReleaseComObject(outlookNamespace);
            ReleaseComObject(outlookApplication);
        }
    }

    private static List<CalendarAppointmentCandidate> ReadOverlappingAppointments(
        object calendarItems,
        MeetingPlatform platform,
        DateTimeOffset nowUtc)
    {
        var lowerBoundLocal = nowUtc.LocalDateTime.Subtract(CalendarLookupTolerance);
        var upperBoundLocal = nowUtc.LocalDateTime.Add(CalendarLookupTolerance);
        var candidates = new List<CalendarAppointmentCandidate>();

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
                candidates.Add(new CalendarAppointmentCandidate(
                    subject.Trim(),
                    startLocal.Value,
                    ScorePlatformMatch(platform, subject, location, body)));
            }
            finally
            {
                ReleaseComObject(item);
            }
        }

        return candidates;
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

    private sealed record CalendarAppointmentCandidate(string Subject, DateTime StartLocal, int PlatformMatchScore);
}
