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
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "ms-teams",
            Signals:
            [
                new DetectionSignal("window-title", "Weekly Sync | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
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
    public void GetAutoStopTimeout_DoesNotExtend_Timeout_For_Weak_Teams_Process_Only_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "Detected meeting",
            Signals:
            [
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-activity", "Speakers; peak=0.270; status=active", 0.2d, now),
            ],
            Reason: "Detection confidence did not meet the recording threshold.");

        var timeout = policy.GetAutoStopTimeout(
            decision,
            MeetingPlatform.Teams,
            "Jain, Himanshu",
            configuredTimeout);

        Assert.Equal(configuredTimeout, timeout);
    }

    [Fact]
    public void GetAutoStopTimeout_Returns_Extended_Timeout_For_Generic_Teams_Shell_When_Active_Title_Is_Specific()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
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
                new DetectionSignal("audio-silence", "Speakers; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var timeout = policy.GetAutoStopTimeout(
            decision,
            MeetingPlatform.Teams,
            "Wang Stein",
            configuredTimeout);

        Assert.Equal(TimeSpan.FromSeconds(90), timeout);
    }

    [Fact]
    public void GetAutoStopTimeout_Returns_Extended_Timeout_For_Suppressed_Teams_Navigation_Signal_When_Active_Title_Is_Specific()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "AI Gurus",
            Signals:
            [
                new DetectionSignal("window-title", "Chat | AI Gurus | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Speakers; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var timeout = policy.GetAutoStopTimeout(
            decision,
            MeetingPlatform.Teams,
            "Wang Stein",
            configuredTimeout);

        Assert.Equal(TimeSpan.FromSeconds(90), timeout);
    }

    [Fact]
    public void GetAutoStopTimeout_DoesNotExtend_Timeout_For_Generic_Teams_Shell_When_Active_Title_Is_Generic()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
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
                new DetectionSignal("audio-silence", "Speakers; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var timeout = policy.GetAutoStopTimeout(
            decision,
            MeetingPlatform.Teams,
            "Microsoft Teams",
            configuredTimeout);

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
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Weak_Teams_Process_Only_Signal_When_Loopback_Activity_Is_Recent()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "Detected meeting",
            Signals:
            [
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-activity", "Speakers; peak=0.270; status=active", 0.2d, now),
            ],
            Reason: "Detection confidence did not meet the recording threshold.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Jain, Himanshu",
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldReclassifyAutoStartedSession_Returns_True_For_HighConfidence_AudioBacked_Teams_Takeover()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "GF/Bharat | AI workshop Sync Sourcing",
            Signals:
            [
                new DetectionSignal("window-title", "GF/Bharat | AI workshop Sync Sourcing", 0.85d, DateTimeOffset.UtcNow),
                new DetectionSignal("audio-window", "Microsoft Teams; process=ms-teams; peak=0.337; confidence=High", 0.35d, DateTimeOffset.UtcNow),
            ],
            Reason: "Detection confidence met the recording threshold and active system audio was present.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "GF/Bharat | AI workshop Sync Sourcing",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.High,
                DateTimeOffset.UtcNow));

        var shouldReclassify = policy.ShouldReclassifyAutoStartedSession(decision, MeetingPlatform.GoogleMeet);

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Suppressed_Teams_Chat_Title_When_It_Matches_The_Active_Meeting()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.15d,
            SessionTitle: "Chao, Adam",
            Signals:
            [
                new DetectionSignal("window-title", "Chat | Chao, Adam | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Chao, Adam",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Punctuation_Only_Title_Variants()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Wang, Stein",
            Signals:
            [
                new DetectionSignal("window-title", "Wang, Stein | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Wang Stein",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldReclassifyAutoStartedSession_Returns_True_For_Strong_Teams_Signal_Over_Google_Meet_Session()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "GF/Bharat | AI workshop Sync Sourcing",
            Signals:
            [
                new DetectionSignal("window-title", "GF/Bharat | AI workshop Sync Sourcing | Microsoft Teams", 0.85d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-activity", "Speakers; peak=0.237; status=active", 0.2d, now),
            ],
            Reason: "Detection confidence met the recording threshold and active system audio was present.");

        var shouldReclassify = policy.ShouldReclassifyAutoStartedSession(
            decision,
            MeetingPlatform.GoogleMeet);

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldReclassifyAutoStartedSession_Returns_False_For_Different_Platform_When_Target_Is_Not_Teams()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Meet - abc-defg-hij",
            Signals:
            [
                new DetectionSignal("window-title", "Meet - abc-defg-hij", 0.85d, now),
                new DetectionSignal("audio-activity", "Speakers; peak=0.237; status=active", 0.2d, now),
            ],
            Reason: "Detection confidence met the recording threshold and active system audio was present.");

        var shouldReclassify = policy.ShouldReclassifyAutoStartedSession(
            decision,
            MeetingPlatform.Teams);

        Assert.False(shouldReclassify);
    }

    [Fact]
    public void ShouldRecoverFromRecentAutoStop_Returns_True_For_Recent_Strong_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var recentStop = new RecentAutoStopContext(MeetingPlatform.Teams, now.AddSeconds(-30));
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
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

    [Fact]
    public void ShouldRecoverFromRecentAutoStop_Returns_False_For_Recent_MeetingLike_NoAudio_Signal()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var recentStop = new RecentAutoStopContext(MeetingPlatform.Teams, now.AddSeconds(-30));
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
                new DetectionSignal("audio-silence", "Speakers; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRecover = policy.ShouldRecoverFromRecentAutoStop(decision, recentStop, now);

        Assert.False(shouldRecover);
    }
}
