namespace MeetingRecorder.Core.Services;

public sealed class AppUpdateSchedulePolicy
{
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
