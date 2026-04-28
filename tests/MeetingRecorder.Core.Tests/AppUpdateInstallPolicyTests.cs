using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppUpdateInstallPolicyTests
{
    [Fact]
    public void GetInstallBlockReason_Returns_Null_When_Continuing_The_Current_Install()
    {
        var policy = new AppUpdateInstallPolicy();

        var blockReason = policy.GetInstallBlockReason(
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: true,
            allowCurrentInstallInProgress: true);

        Assert.Null(blockReason);
    }

    [Fact]
    public void GetInstallBlockReason_Returns_InstallInProgress_When_Starting_A_New_Install()
    {
        var policy = new AppUpdateInstallPolicy();

        var blockReason = policy.GetInstallBlockReason(
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: true,
            allowCurrentInstallInProgress: false);

        Assert.Equal("An update install is already in progress.", blockReason);
    }

    [Fact]
    public void GetInstallBlockReason_Returns_Null_When_Background_Processing_Is_Explicitly_Overridden()
    {
        var policy = new AppUpdateInstallPolicy();

        var blockReason = policy.GetInstallBlockReason(
            hasActiveRecording: false,
            isProcessingInProgress: true,
            isUpdateAlreadyInProgress: false,
            allowCurrentInstallInProgress: false,
            allowProcessingOverride: true);

        Assert.Null(blockReason);
    }

    [Fact]
    public void ShouldRetryPendingInstall_Returns_True_When_A_Pending_Zip_Is_Present_And_App_Is_Idle()
    {
        var policy = new AppUpdateInstallPolicy();

        var shouldRetry = policy.ShouldRetryPendingInstall(
            pendingUpdateZipPath: @"C:\Users\test\Downloads\MeetingRecorder-v0.2-win-x64.zip",
            pendingUpdateVersion: "0.2",
            currentVersion: "0.1",
            queuedInstallWhenIdleRequested: false,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryPendingInstall_Returns_False_When_A_Recording_Is_Active()
    {
        var policy = new AppUpdateInstallPolicy();

        var shouldRetry = policy.ShouldRetryPendingInstall(
            pendingUpdateZipPath: @"C:\Users\test\Downloads\MeetingRecorder-v0.2-win-x64.zip",
            pendingUpdateVersion: "0.2",
            currentVersion: "0.1",
            queuedInstallWhenIdleRequested: false,
            hasActiveRecording: true,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldAutoInstall_Returns_True_When_Update_Is_Available_And_App_Is_Idle()
    {
        var policy = new AppUpdateInstallPolicy();
        var result = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.1",
            "0.2",
            "https://example.com/update.zip",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-03-17T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            1024L,
            true,
            false,
            false,
            "Version 0.2 is available.");

        var shouldInstall = policy.ShouldAutoInstall(
            result,
            autoInstallEnabled: true,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.True(shouldInstall);
    }

    [Fact]
    public void ShouldAutoInstall_Returns_False_When_The_Update_Is_Only_A_Newer_Build_Of_The_Same_Version()
    {
        var policy = new AppUpdateInstallPolicy();
        var result = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.3",
            "0.3",
            "https://example.com/update.zip",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-03-23T23:42:53Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            104047461L,
            false,
            true,
            true,
            "A newer published build for version 0.3 is available.");

        var shouldInstall = policy.ShouldAutoInstall(
            result,
            autoInstallEnabled: true,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.False(shouldInstall);
    }

    [Fact]
    public void ShouldAutoInstall_Returns_False_When_A_Recording_Is_Active()
    {
        var policy = new AppUpdateInstallPolicy();
        var result = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.1",
            "0.2",
            "https://example.com/update.zip",
            "https://example.com/release",
            DateTimeOffset.Parse("2026-03-17T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            1024L,
            true,
            false,
            false,
            "Version 0.2 is available.");

        var shouldInstall = policy.ShouldAutoInstall(
            result,
            autoInstallEnabled: true,
            hasActiveRecording: true,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.False(shouldInstall);
    }

    [Fact]
    public void ShouldRetryPendingInstall_Returns_False_When_The_Pending_Update_Matches_The_Current_Version()
    {
        var policy = new AppUpdateInstallPolicy();

        var shouldRetry = policy.ShouldRetryPendingInstall(
            pendingUpdateZipPath: @"C:\Users\test\Downloads\MeetingRecorder-v0.3-win-x64.zip",
            pendingUpdateVersion: "0.3",
            currentVersion: "0.3",
            queuedInstallWhenIdleRequested: false,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryPendingInstall_Returns_True_When_A_Same_Version_Update_Was_Explicitly_Queued()
    {
        var policy = new AppUpdateInstallPolicy();

        var shouldRetry = policy.ShouldRetryPendingInstall(
            pendingUpdateZipPath: @"C:\Users\test\Downloads\MeetingRecorder-v0.3-win-x64.zip",
            pendingUpdateVersion: "0.3",
            currentVersion: "0.3",
            queuedInstallWhenIdleRequested: true,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldAutoInstall_Returns_False_When_No_Downloadable_Asset_Is_Available()
    {
        var policy = new AppUpdateInstallPolicy();
        var result = new AppUpdateCheckResult(
            AppUpdateStatusKind.UpdateAvailable,
            "0.1",
            "0.2",
            null,
            "https://example.com/release",
            DateTimeOffset.Parse("2026-03-17T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            1024L,
            true,
            false,
            false,
            "Version 0.2 is available.");

        var shouldInstall = policy.ShouldAutoInstall(
            result,
            autoInstallEnabled: true,
            hasActiveRecording: false,
            isProcessingInProgress: false,
            isUpdateAlreadyInProgress: false);

        Assert.False(shouldInstall);
    }
}
