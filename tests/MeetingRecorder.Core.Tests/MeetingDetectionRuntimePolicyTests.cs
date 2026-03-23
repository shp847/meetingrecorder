using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingDetectionRuntimePolicyTests
{
    [Fact]
    public void ShouldRun_Returns_False_When_AutoDetect_Is_Disabled()
    {
        var result = MeetingDetectionRuntimePolicy.ShouldRun(
            autoDetectEnabled: false,
            isRecording: false,
            activeSessionWasAutoStarted: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRun_Returns_True_When_AutoDetect_Is_Enabled_And_App_Is_Idle()
    {
        var result = MeetingDetectionRuntimePolicy.ShouldRun(
            autoDetectEnabled: true,
            isRecording: false,
            activeSessionWasAutoStarted: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRun_Returns_False_During_Manual_Recording()
    {
        var result = MeetingDetectionRuntimePolicy.ShouldRun(
            autoDetectEnabled: true,
            isRecording: true,
            activeSessionWasAutoStarted: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRun_Returns_True_For_AutoStarted_Recording_When_AutoDetect_Remains_Enabled()
    {
        var result = MeetingDetectionRuntimePolicy.ShouldRun(
            autoDetectEnabled: true,
            isRecording: true,
            activeSessionWasAutoStarted: true);

        Assert.True(result);
    }
}
