using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppUpdateSchedulePolicyTests
{
    [Fact]
    public void ShouldRunScheduledCheck_Returns_True_When_No_Check_Has_Run_Yet()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunScheduledCheck(
            lastCheckedUtc: null,
            nowUtc: DateTimeOffset.Parse("2026-03-16T18:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1));

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunScheduledCheck_Returns_False_When_The_Last_Check_Was_Recent()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunScheduledCheck(
            lastCheckedUtc: DateTimeOffset.Parse("2026-03-16T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            nowUtc: DateTimeOffset.Parse("2026-03-16T18:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1));

        Assert.False(shouldRun);
    }

    [Fact]
    public void ShouldRunScheduledCheck_Returns_True_When_The_Last_Check_Is_Older_Than_The_Cadence()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunScheduledCheck(
            lastCheckedUtc: DateTimeOffset.Parse("2026-03-15T17:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            nowUtc: DateTimeOffset.Parse("2026-03-16T18:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1));

        Assert.True(shouldRun);
    }
}
