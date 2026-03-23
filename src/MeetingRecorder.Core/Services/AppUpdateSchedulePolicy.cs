namespace MeetingRecorder.Core.Services;

public enum AppUpdateCheckTrigger
{
    Scheduled = 0,
    Startup = 1,
    Shutdown = 2,
}

public sealed class AppUpdateSchedulePolicy
{
    public bool ShouldRunAutomaticCheck(
        bool updateChecksEnabled,
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset nowUtc,
        TimeSpan cadence,
        AppUpdateCheckTrigger trigger)
    {
        if (!updateChecksEnabled)
        {
            return false;
        }

        return trigger switch
        {
            AppUpdateCheckTrigger.Startup => true,
            AppUpdateCheckTrigger.Shutdown => true,
            _ => ShouldRunScheduledCheck(lastCheckedUtc, nowUtc, cadence),
        };
    }

    public bool ShouldRunScheduledCheck(
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset nowUtc,
        TimeSpan cadence)
    {
        if (cadence <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), "The update cadence must be greater than zero.");
        }

        if (!lastCheckedUtc.HasValue)
        {
            return true;
        }

        if (lastCheckedUtc.Value > nowUtc)
        {
            return false;
        }

        return nowUtc - lastCheckedUtc.Value >= cadence;
    }

    public DateTimeOffset? GetNextCheckUtc(DateTimeOffset? lastCheckedUtc, TimeSpan cadence)
    {
        if (cadence <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), "The update cadence must be greater than zero.");
        }

        return lastCheckedUtc?.Add(cadence);
    }
}
