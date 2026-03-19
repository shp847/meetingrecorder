using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AutoRecordingContinuityPolicyTests
{
    [Fact]
    public void GetAutoStopTimeout_Returns_Extended_Timeout_For_Weak_Same_Platform_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(10);
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "ms-teams",
            Signals: Array.Empty<DetectionSignal>(),
            Reason: "chat");

        var timeout = policy.GetAutoStopTimeout(decision, MeetingPlatform.Teams, configuredTimeout);

        Assert.Equal(TimeSpan.FromSeconds(60), timeout);
    }

    [Fact]
    public void GetAutoStopTimeout_DoesNotExtend_Timeout_For_Suppressed_Teams_Navigation_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "ms-teams",
            Signals:
            [
                new DetectionSignal("window-title", "Chat | Settings (External unfamiliar) | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var timeout = policy.GetAutoStopTimeout(decision, MeetingPlatform.Teams, configuredTimeout);

        Assert.Equal(configuredTimeout, timeout);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Weak_Same_Platform_Signal_When_Capture_Activity_Is_Recent()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "ms-teams",
            Signals:
            [
                new DetectionSignal("window-title", "Chat | Petrina Zaraszczak (External unfamiliar) | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: null,
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Weak_Same_Platform_Signal_When_Only_Microphone_Activity_Is_Recent()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "ms-teams",
            Signals:
            [
                new DetectionSignal("window-title", "Chat | AI Gurus | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: null,
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: true);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_MeetingLike_NoAudio_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Microsoft Teams",
            Signals:
            [
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: null,
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Same_Specific_Meeting_Title_When_Audio_Is_Quiet()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Advanced use cases from AI super users",
            Signals:
            [
                new DetectionSignal("window-title", "Advanced use cases from AI super users | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Advanced use cases from AI super users",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Teams_Compact_View_Title_When_Audio_Is_Quiet()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Meeting compact view | Jain, Himanshu  | Pinned window",
            Signals:
            [
                new DetectionSignal("window-title", "Meeting compact view | Jain, Himanshu | Microsoft Teams | Pinned window", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Jain, Himanshu",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Teams_Sharing_Control_Bar_When_Active_Title_Is_Specific()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Sharing control bar  | Pinned window",
            Signals:
            [
                new DetectionSignal("window-title", "Sharing control bar | Microsoft Teams | Pinned window", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Jain, Himanshu",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRecoverFromRecentAutoStop_Returns_True_For_Recent_Strong_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var recentStop = new RecentAutoStopContext(MeetingPlatform.Teams, now.AddSeconds(-30));
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Meeting",
            Signals: Array.Empty<DetectionSignal>(),
            Reason: "meeting-like");

        var shouldRecover = policy.ShouldRecoverFromRecentAutoStop(decision, recentStop, now);

        Assert.True(shouldRecover);
    }

    [Fact]
    public void ShouldRecoverFromRecentAutoStop_Returns_False_When_Recovery_Window_Has_Expired()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var recentStop = new RecentAutoStopContext(MeetingPlatform.Teams, now.AddMinutes(-3));
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Meeting",
            Signals: Array.Empty<DetectionSignal>(),
            Reason: "meeting-like");

        var shouldRecover = policy.ShouldRecoverFromRecentAutoStop(decision, recentStop, now);

        Assert.False(shouldRecover);
    }
}
