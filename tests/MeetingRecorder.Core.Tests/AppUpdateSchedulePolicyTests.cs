using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppUpdateSchedulePolicyTests
{
    [Fact]
    public void ShouldRunAutomaticCheck_Returns_True_For_Startup_When_Update_Checks_Are_Enabled()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunAutomaticCheck(
            updateChecksEnabled: true,
            lastCheckedUtc: DateTimeOffset.Parse("2026-03-19T16:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            nowUtc: DateTimeOffset.Parse("2026-03-19T16:05:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1),
            trigger: AppUpdateCheckTrigger.Startup);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunAutomaticCheck_Returns_True_For_Shutdown_When_Update_Checks_Are_Enabled()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunAutomaticCheck(
            updateChecksEnabled: true,
            lastCheckedUtc: DateTimeOffset.Parse("2026-03-19T16:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            nowUtc: DateTimeOffset.Parse("2026-03-19T16:05:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1),
            trigger: AppUpdateCheckTrigger.Shutdown);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunAutomaticCheck_Returns_False_When_Update_Checks_Are_Disabled()
    {
        var policy = new AppUpdateSchedulePolicy();

        var shouldRun = policy.ShouldRunAutomaticCheck(
            updateChecksEnabled: false,
            lastCheckedUtc: null,
            nowUtc: DateTimeOffset.Parse("2026-03-19T16:05:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            cadence: TimeSpan.FromDays(1),
            trigger: AppUpdateCheckTrigger.Startup);

        Assert.False(shouldRun);
    }

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
