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
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Same_Specific_Meeting_Title_When_Audio_Is_Quiet_And_No_Recent_Capture_Activity()
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

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Same_Specific_Meeting_Title_When_Recent_Capture_Activity_Exists()
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
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Same_Specific_Teams_Title_When_Only_Microphone_Activity_Is_Recent()
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
            hasRecentMicrophoneActivity: true);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_When_Official_Teams_No_Current_Match_Is_Present()
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
                new DetectionSignal("official-teams-no-current-match", "No current official Teams meeting is active.", 0.25d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Advanced use cases from AI super users",
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_When_Official_Teams_Match_Is_Present_And_Recent_Render_Activity_Exists()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: false,
            Confidence: 0.35d,
            SessionTitle: "Microsoft Teams",
            Signals:
            [
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
                new DetectionSignal("official-teams-match", "Client Sync", 0.25d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Client Sync",
            hasRecentLoopbackActivity: true,
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

        Assert.False(shouldRefresh);
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

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void GetAutoStopTimeout_Returns_Extended_Timeout_For_Same_Specific_Meeting_Title_When_Audio_Is_Quiet()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var configuredTimeout = TimeSpan.FromSeconds(30);
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

        var timeout = policy.GetAutoStopTimeout(
            decision,
            MeetingPlatform.Teams,
            "Advanced use cases from AI super users",
            configuredTimeout);

        Assert.Equal(TimeSpan.FromSeconds(90), timeout);
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
    public void ShouldReclassifyActiveSession_Returns_True_For_HighConfidence_AudioBacked_Teams_Takeover()
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

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.GoogleMeet,
            activeSessionTitle: "Meet - abc-defg-hij");

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Suppressed_Teams_Chat_Title_When_It_Matches_The_Active_Meeting_But_No_Recent_Capture_Activity_Exists()
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

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Suppressed_Teams_Chat_Title_When_It_Matches_The_Active_Meeting_And_Recent_Capture_Activity_Exists()
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
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_False_For_Punctuation_Only_Teams_Title_Variants_When_No_Recent_Capture_Activity_Exists()
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

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Punctuation_Only_Teams_Title_Variants_When_Recent_Capture_Activity_Exists()
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
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Weak_CrossPlatform_GoogleMeet_Browser_Candidate_During_Active_Teams_Call()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Google Meet and 15 more pages - Work - Microsoft Edge",
            Signals:
            [
                new DetectionSignal("window-title", "Google Meet and 15 more pages - Work - Microsoft Edge", 0.85d, now),
                new DetectionSignal("browser-window", "Google Meet and 15 more pages - Work - Microsoft Edge", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "[INT] GlobalFoundries daily",
            hasRecentLoopbackActivity: true,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldReclassifyActiveSession_Returns_True_For_Strong_Teams_Signal_Over_Google_Meet_Session()
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

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.GoogleMeet,
            activeSessionTitle: "Meet - abc-defg-hij");

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldReclassifyActiveSession_Returns_True_For_Manual_Session_When_A_Specific_Teams_Call_Is_Detected_With_Window_Evidence_And_Medium_Audio_Source()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "[Int] Global Foundries Connect",
            Signals:
            [
                new DetectionSignal("window-title", "[Int] Global Foundries Connect | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-process", "Microsoft Teams; window=[Int] Global Foundries Connect | Microsoft Teams; process=ms-teams; peak=0.420; confidence=Medium", 0.15d, now),
            ],
            Reason: "Detection confidence met the recording threshold and active system audio was present.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "[Int] Global Foundries Connect | Microsoft Teams",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.Medium,
                now));

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.Manual,
            activeSessionTitle: "Henry call");

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldReclassifyActiveSession_Returns_True_For_Manual_Session_When_A_Quiet_Specific_Teams_Call_Is_Detected_With_A_Matched_Teams_Audio_Source()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "HR Track: IonQ/SkyWater Integration | anonymous",
            Signals:
            [
                new DetectionSignal("window-title", "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-session-match", "Microsoft Teams; window=HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams; process=ms-teams; peak=0.000; confidence=Medium", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams",
                null,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium,
                now));

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.Manual,
            activeSessionTitle: "HR Kickoff Plan");

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldReclassifyActiveSession_Returns_True_For_A_Different_Specific_Teams_Call_On_The_Same_Platform()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: true,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "[Int] Global Foundries Connect",
            Signals:
            [
                new DetectionSignal("window-title", "[Int] Global Foundries Connect | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-process", "Microsoft Teams; window=[Int] Global Foundries Connect | Microsoft Teams; process=ms-teams; peak=0.420; confidence=Medium", 0.15d, now),
            ],
            Reason: "Detection confidence met the recording threshold and active system audio was present.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "[Int] Global Foundries Connect | Microsoft Teams",
                null,
                AudioSourceMatchKind.Window,
                AudioSourceConfidence.Medium,
                now));

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Jain, Himanshu, Kurtz, John");

        Assert.True(shouldReclassify);
    }

    [Fact]
    public void ShouldReclassifyActiveSession_Returns_True_For_Different_Platform_When_Target_Is_Not_Teams()
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

        var shouldReclassify = policy.ShouldReclassifyActiveSession(
            decision,
            MeetingPlatform.Teams,
            activeSessionTitle: "Global Foundries Connect");

        Assert.True(shouldReclassify);
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

    [Fact]
    public void ShouldAutoStartQuietSpecificTeamsMeeting_Returns_True_After_Sustained_Specific_Teams_Evidence()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Ducey-Gallina, Nick",
            Signals:
            [
                new DetectionSignal("window-title", "Ducey-Gallina, Nick | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Speakers; peak=0.001; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "Ducey-Gallina, Nick | Microsoft Teams",
                null,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium,
                now));

        var shouldStart = policy.ShouldAutoStartQuietSpecificTeamsMeeting(
            decision,
            now.AddSeconds(-20),
            now);

        Assert.True(shouldStart);
    }

    [Fact]
    public void ShouldAutoStartQuietSpecificTeamsMeeting_Returns_True_For_Quiet_Matched_Teams_Audio_Session()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "HR Track: IonQ/SkyWater Integration | anonymous",
            Signals:
            [
                new DetectionSignal("window-title", "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-session-match", "Microsoft Teams; window=HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams; process=ms-teams; peak=0.000; confidence=Medium", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.",
            DetectedAudioSource: new DetectedAudioSource(
                "Microsoft Teams",
                "HR Track: IonQ/SkyWater Integration | anonymous | Microsoft Teams",
                null,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium,
                now));

        var shouldStart = policy.ShouldAutoStartQuietSpecificTeamsMeeting(
            decision,
            now.AddSeconds(-25),
            now);

        Assert.True(shouldStart);
    }

    [Fact]
    public void ShouldAutoStartQuietSpecificTeamsMeeting_Returns_False_Without_A_Matched_Teams_Audio_Source()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "[INT] GlobalFoundries AI SC daily",
            Signals:
            [
                new DetectionSignal("window-title", "[INT] GlobalFoundries AI SC daily | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Speakers; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldStart = policy.ShouldAutoStartQuietSpecificTeamsMeeting(
            decision,
            now.AddSeconds(-30),
            now);

        Assert.False(shouldStart);
    }

    [Fact]
    public void ShouldAutoStartQuietSpecificTeamsMeeting_Returns_False_Before_The_Sustained_Delay_Elapses()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "Ducey-Gallina, Nick",
            Signals:
            [
                new DetectionSignal("window-title", "Ducey-Gallina, Nick | Microsoft Teams", 0.85d, now),
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Speakers; peak=0.001; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldStart = policy.ShouldAutoStartQuietSpecificTeamsMeeting(
            decision,
            now.AddSeconds(-10),
            now);

        Assert.False(shouldStart);
    }

    [Fact]
    public void ShouldAutoStartQuietSpecificTeamsMeeting_Returns_False_For_Generic_Teams_Shell()
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
                new DetectionSignal("process-name", "ms-teams", 0.05d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-silence", "Speakers; peak=0.001; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldStart = policy.ShouldAutoStartQuietSpecificTeamsMeeting(
            decision,
            now.AddMinutes(-2),
            now);

        Assert.False(shouldStart);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Google_Meet_Title_Variants_Sharing_The_Same_Meet_Code()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 0.75d,
            SessionTitle: "Meet - dbh-eecx-utm - Camera and microphone recording - Memory usage - 437 MB",
            Signals:
            [
                new DetectionSignal("window-title", "Meet - dbh-eecx-utm - Camera and microphone recording - Memory usage - 437 MB", 0.70d, now),
                new DetectionSignal("browser-tab", "Meet - dbh-eecx-utm - Camera and microphone recording - Memory usage - 437 MB", 0.05d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.GoogleMeet,
            activeSessionTitle: "Meet - dbh-eecx-utm and 1 more page - Work - Microsoft Edge",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshLastPositiveSignal_Returns_True_For_Google_Meet_Shared_Window_Title_When_Active_Title_Is_Specific()
    {
        var policy = new AutoRecordingContinuityPolicy();
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            ShouldStart: false,
            ShouldKeepRecording: true,
            Confidence: 1d,
            SessionTitle: "meet.google.com is sharing a window.",
            Signals:
            [
                new DetectionSignal("window-title", "meet.google.com is sharing a window.", 0.85d, now),
                new DetectionSignal("browser-window", "meet.google.com is sharing a window.", 0.15d, now),
                new DetectionSignal("audio-silence", "Device; peak=0.000; status=below-threshold", 0d, now),
            ],
            Reason: "Meeting-like window detected, but no active system audio was observed.");

        var shouldRefresh = policy.ShouldRefreshLastPositiveSignal(
            decision,
            MeetingPlatform.GoogleMeet,
            activeSessionTitle: "Meet - eip-fhbv-wze and 19 more pages - Work - Microsoft Edge",
            hasRecentLoopbackActivity: false,
            hasRecentMicrophoneActivity: false);

        Assert.True(shouldRefresh);
    }
}
