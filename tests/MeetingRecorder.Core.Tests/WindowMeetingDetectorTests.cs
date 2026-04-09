using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Threading;

namespace MeetingRecorder.Core.Tests;

public sealed class WindowMeetingDetectorTests
{
    [Fact]
    public void LooksLikeTeamsWindowTitle_Returns_False_For_Transcript_File_Open_In_VisualStudioCode()
    {
        var result = WindowMeetingDetector.LooksLikeTeamsWindowTitle(
            "2026-03-20_001940_teams_ducey-gallina-nick.md - Meeting Recorder - Visual Studio Code");

        Assert.False(result);
    }

    [Fact]
    public void IsBetterCandidate_Prefers_GoogleMeet_Browser_Candidate_Over_Generic_Teams_Tie()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var googleMeetCandidate = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            true,
            true,
            1d,
            "Meet - mna-sfqg-htr",
            new[]
            {
                new DetectionSignal("window-title", "Meet - mna-sfqg-htr", 0.85d, timestamp),
                new DetectionSignal("browser-window", "Meet - mna-sfqg-htr", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.391", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var teamsCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "Microsoft Teams",
            new[]
            {
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.391", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");

        var result = WindowMeetingDetector.IsBetterCandidate(googleMeetCandidate, teamsCandidate);

        Assert.True(result);
    }

    [Fact]
    public void IsBetterCandidate_Prefers_Specific_Teams_Candidate_With_Attributed_Audio_Over_Unattributed_GoogleMeet_Browser_Candidate()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var googleMeetCandidate = new DetectionDecision(
            MeetingPlatform.GoogleMeet,
            true,
            true,
            1d,
            "Meet - mna-sfqg-htr",
            new[]
            {
                new DetectionSignal("window-title", "Meet - mna-sfqg-htr", 0.85d, timestamp),
                new DetectionSignal("browser-window", "Meet - mna-sfqg-htr", 0.15d, timestamp),
                new DetectionSignal("audio-browser-unverified", "Display Audio; peak=0.391; status=active", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var teamsAudioSource = new DetectedAudioSource(
            "Microsoft Teams",
            "HR Kickoff Plan | anonymous | Microsoft Teams",
            null,
            AudioSourceMatchKind.Process,
            AudioSourceConfidence.Medium,
            timestamp);
        var teamsCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "HR Kickoff Plan | anonymous",
            new[]
            {
                new DetectionSignal("window-title", "HR Kickoff Plan | anonymous | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.05d, timestamp),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, timestamp),
                new DetectionSignal("audio-process", "Microsoft Teams; window=HR Kickoff Plan | anonymous | Microsoft Teams; process=ms-teams; peak=0.412; confidence=Medium", 0.15d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.",
            teamsAudioSource);

        var result = WindowMeetingDetector.IsBetterCandidate(teamsCandidate, googleMeetCandidate);

        Assert.True(result);
    }

    [Fact]
    public void ApplyTeamsPlaybackHeuristic_Demotes_Plain_Teams_Content_Window_When_Matching_Chat_Window_Exists()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var plainCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "Jain, Himanshu",
            new[]
            {
                new DetectionSignal("window-title", "Jain, Himanshu | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.158", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var chatCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            false,
            false,
            0.15d,
            "Jain, Himanshu",
            new[]
            {
                new DetectionSignal("window-title", "Chat | Jain, Himanshu | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-silence", "peak=0.000", 0d, timestamp),
            },
            "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var result = WindowMeetingDetector.ApplyTeamsPlaybackHeuristic(plainCandidate, new[] { plainCandidate, chatCandidate });

        Assert.False(result.ShouldStart);
        Assert.False(result.ShouldKeepRecording);
        Assert.Contains("playback", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyTeamsPlaybackHeuristic_DoesNot_Demote_Explicit_Teams_Meeting_Surface_When_Matching_Chat_Window_Exists()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var meetingCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "Meeting compact view | Jain, Himanshu  | Pinned window",
            new[]
            {
                new DetectionSignal("window-title", "Meeting compact view | Jain, Himanshu | Microsoft Teams | Pinned window", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-activity", "peak=0.158", 0.2d, timestamp),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var chatCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            false,
            false,
            0.15d,
            "Jain, Himanshu",
            new[]
            {
                new DetectionSignal("window-title", "Chat | Jain, Himanshu | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-silence", "peak=0.000", 0d, timestamp),
            },
            "The detected Teams window appears to be a chat or navigation view, not an active meeting.");

        var result = WindowMeetingDetector.ApplyTeamsPlaybackHeuristic(meetingCandidate, new[] { meetingCandidate, chatCandidate });

        Assert.True(result.ShouldStart);
        Assert.True(result.ShouldKeepRecording);
    }

    [Fact]
    public void ApplyTeamsPlaybackHeuristic_Demotes_Plain_Teams_Content_Window_When_No_Teams_Render_Evidence_Exists()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var plainCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            false,
            true,
            1d,
            "Jain, Himanshu",
            new[]
            {
                new DetectionSignal("window-title", "Jain, Himanshu | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-silence", "peak=0.000", 0d, timestamp),
            },
            "Meeting-like window detected, but no active system audio was observed.");

        var result = WindowMeetingDetector.ApplyTeamsPlaybackHeuristic(plainCandidate, new[] { plainCandidate });

        Assert.False(result.ShouldStart);
        Assert.False(result.ShouldKeepRecording);
        Assert.Contains("not a live meeting", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyTeamsPlaybackHeuristic_Does_Not_Demote_Plain_Teams_Content_Window_When_Teams_Render_Evidence_Still_Exists()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var plainCandidate = new DetectionDecision(
            MeetingPlatform.Teams,
            false,
            true,
            1d,
            "Jain, Himanshu",
            new[]
            {
                new DetectionSignal("window-title", "Jain, Himanshu | Microsoft Teams", 0.85d, timestamp),
                new DetectionSignal("process-name", "ms-teams", 0.15d, timestamp),
                new DetectionSignal("audio-session-match", "Microsoft Teams; process=ms-teams; peak=0.000; confidence=Medium", 0d, timestamp),
            },
            "Meeting-like window detected, but no active system audio was observed.");

        var result = WindowMeetingDetector.ApplyTeamsPlaybackHeuristic(plainCandidate, new[] { plainCandidate });

        Assert.True(result.ShouldKeepRecording);
    }

    [Fact]
    public async Task DetectBestCandidate_Uses_Window_Title_When_Process_Metadata_Is_Unavailable()
    {
        var detector = await CreateDetectorAsync(
            new MeetingWindowCandidate(string.Empty, "Chat | Alex Johnson | Microsoft Teams", "Chrome_WidgetWin_1"));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.Equal("Alex Johnson", result.SessionTitle);
    }

    [Fact]
    public async Task DetectBestCandidate_Uses_GoogleMeet_Audio_Metadata_When_Browser_Window_Title_Is_Shared_Content()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "L2 Integration Categories Repository.pptx - Google Slides and 7 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)8124,
                    8124),
            ],
            audioActivityProbe: new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0.32d,
                true,
                "active",
                [
                    new AudioSourceSessionSnapshot(
                        8124,
                        "msedge",
                        0.32d,
                        true,
                        false,
                        false,
                        "Meet - yzz-yeqg-mdc",
                        "https://meet.google.com/yzz-yeqg-mdc"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.GoogleMeet, result.Platform);
        Assert.True(result.ShouldStart);
        Assert.Equal("Meet - yzz-yeqg-mdc", result.SessionTitle);
    }

    [Fact]
    public void DetectBestCandidate_Does_Not_Inspect_Browser_Tab_Ui_For_GoogleMeet_Detection()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(WindowMeetingDetectorTests).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var detectorPath = Path.Combine(repoRoot, "src", "MeetingRecorder.App", "Services", "WindowMeetingDetector.cs");

        Assert.True(File.Exists(detectorPath), $"Expected detector source at '{detectorPath}'.");

        var source = File.ReadAllText(detectorPath);

        Assert.DoesNotContain("System.Windows.Automation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationElement.FromHandle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlType.TabItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectBestCandidate_Ignores_Unsupported_Window_Classes_Even_When_Their_Window_Title_Looks_Like_A_Meeting()
    {
        var detector = await CreateDetectorAsync(
            new MeetingWindowCandidate(string.Empty, "Google Meet", "HwndWrapper[DefaultDomain;;12345]"),
            new MeetingWindowCandidate(string.Empty, "Chat | Alex Johnson | Microsoft Teams", "Chrome_WidgetWin_1"));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.Equal("Alex Johnson", result.SessionTitle);
    }

    [Fact]
    public async Task DetectBestCandidate_Does_Not_Use_Tab_Titles_From_Unsupported_Window_Classes()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    string.Empty,
                    "Google Slides",
                    "HwndWrapper[DefaultDomain;;12345]",
                    (nint)123),
            ]);

        var result = detector.DetectBestCandidate();

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectBestCandidate_Prefers_Teams_Window_With_Matched_Audio_Source_Over_Stale_GoogleMeet_Browser_Window()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "Google Meet and 10 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)100,
                    1000),
                new MeetingWindowCandidate(
                    "ms-teams",
                    "GF/Bharat | AI workshop Sync Sourcing",
                    "Chrome_WidgetWin_1",
                    (nint)200,
                    2000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0.40d,
                true,
                "active",
                [
                    new AudioSourceSessionSnapshot(
                        2000,
                        "ms-teams",
                        0.40d,
                        true,
                        false,
                        false,
                        "Microsoft Teams",
                        "teams-session"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.Equal("GF/Bharat | AI workshop Sync Sourcing", result.SessionTitle);
        Assert.NotNull(result.DetectedAudioSource);
        Assert.Equal("Microsoft Teams", result.DetectedAudioSource!.AppName);
    }

    [Fact]
    public async Task DetectBestCandidate_Preserves_A_Matched_Teams_Audio_Source_When_The_Endpoint_Is_Quiet()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "ms-teams",
                    "[INT] GlobalFoundries AI SC daily | Microsoft Teams",
                    "TeamsWebView",
                    (nint)200,
                    2000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0d,
                false,
                "below-threshold",
                [
                    new AudioSourceSessionSnapshot(
                        2000,
                        "ms-teams",
                        0d,
                        true,
                        false,
                        false,
                        "Microsoft Teams",
                        "teams-session"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.False(result.ShouldStart);
        Assert.True(result.ShouldKeepRecording);
        Assert.NotNull(result.DetectedAudioSource);
        Assert.Equal("Microsoft Teams", result.DetectedAudioSource!.AppName);
    }

    [Fact]
    public async Task DetectBestCandidate_Preserves_A_Quiet_New_Teams_Call_Title_When_A_Matched_Teams_Audio_Session_Exists()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "ms-teams",
                    "Jain, Himanshu",
                    "TeamsWebView",
                    (nint)200,
                    2000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Bluetooth Headset",
                0d,
                false,
                "below-threshold",
                [
                    new AudioSourceSessionSnapshot(
                        2000,
                        "ms-teams",
                        0d,
                        true,
                        false,
                        false,
                        "Microsoft Teams",
                        "teams-session"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.Equal("Jain, Himanshu", result.SessionTitle);
        Assert.False(result.ShouldStart);
        Assert.True(result.ShouldKeepRecording);
        Assert.NotNull(result.DetectedAudioSource);
        Assert.Equal("Microsoft Teams", result.DetectedAudioSource!.AppName);
    }

    [Fact]
    public async Task DetectBestCandidate_Maps_Browser_Audio_Process_To_GoogleMeet_Browser_Tab()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "Quarterly Slides - Google Slides and 8 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)8124,
                    1000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0.35d,
                true,
                "active",
                [
                    new AudioSourceSessionSnapshot(
                        3456,
                        "msedge",
                        0.35d,
                        true,
                        false,
                        false,
                        "Meet - yzz-yeqg-mdc",
                        "https://meet.google.com/yzz-yeqg-mdc"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.GoogleMeet, result.Platform);
        Assert.NotNull(result.DetectedAudioSource);
        Assert.Equal(AudioSourceMatchKind.BrowserTab, result.DetectedAudioSource!.MatchKind);
        Assert.Equal("Meet - yzz-yeqg-mdc", result.DetectedAudioSource.BrowserTabTitle);
    }

    [Fact]
    public async Task DetectBestCandidate_Starts_GoogleMeet_When_Explicit_Meet_Window_Has_Active_Browser_Audio_But_Tab_Attribution_Is_Ambiguous()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "Meet - vwt-vyrn-bas and 14 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)8124,
                    1000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0.34d,
                true,
                "active",
                [
                    new AudioSourceSessionSnapshot(
                        3456,
                        "msedge",
                        0.34d,
                        true,
                        false,
                        false,
                        null,
                        null),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.GoogleMeet, result.Platform);
        Assert.True(result.ShouldStart);
        Assert.Equal("Meet - vwt-vyrn-bas and 14 more pages - Work - Microsoft Edge", result.SessionTitle);
        Assert.Null(result.DetectedAudioSource);
    }

    [Fact]
    public async Task DetectBestCandidate_Starts_GoogleMeet_When_A_Specific_Meet_Window_Is_Present_But_Render_Audio_Is_Quiet()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "Meet - jbz-oabg-rpe and 4 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)8124,
                    1000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0d,
                false,
                "below-threshold",
                Array.Empty<AudioSourceSessionSnapshot>(),
                null)));

        var result = detector.DetectBestCandidate();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.GoogleMeet, result.Platform);
        Assert.True(result.ShouldStart);
        Assert.True(result.ShouldKeepRecording);
        Assert.Equal("Meet - jbz-oabg-rpe and 4 more pages - Work - Microsoft Edge", result.SessionTitle);
        Assert.Null(result.DetectedAudioSource);
    }

    [Fact]
    public async Task DetectBestCandidate_Does_Not_Detect_GoogleMeet_When_Only_Generic_Browser_Playback_Is_Active()
    {
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "msedge",
                    "Quarterly Slides - Google Slides and 8 more pages - Work - Microsoft Edge",
                    "Chrome_WidgetWin_1",
                    (nint)8124,
                    1000),
            ],
            new StubAudioActivityProbe(new AudioSourceAttributionSnapshot(
                "Speakers",
                0.35d,
                true,
                "active",
                [
                    new AudioSourceSessionSnapshot(
                        3456,
                        "msedge",
                        0.35d,
                        true,
                        false,
                        false,
                        "YouTube Music",
                        "https://music.youtube.com/watch?v=test"),
                ],
                null)));

        var result = detector.DetectBestCandidate();

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectBestCandidate_Does_Not_Block_For_Minutes_When_Audio_Activity_Probe_Hangs()
    {
        using var releaseAudioProbe = new ManualResetEventSlim(false);
        var detector = await CreateDetectorAsync(
            [
                new MeetingWindowCandidate(
                    "ms-teams",
                    "Meeting in IonQ + Kearney | Microsoft Teams",
                    "TeamsWebView",
                    (nint)101,
                    101),
            ],
            audioActivityProbe: new BlockingAudioActivityProbe(releaseAudioProbe),
            audioTimeout: TimeSpan.FromMilliseconds(50),
            audioBackoff: TimeSpan.FromMinutes(1));

        var stopwatch = Stopwatch.StartNew();
        var result = detector.DetectBestCandidate();
        stopwatch.Stop();

        Assert.NotNull(result);
        Assert.Equal(MeetingPlatform.Teams, result.Platform);
        Assert.True(result.ShouldKeepRecording);
        Assert.False(result.ShouldStart);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Expected detector to time out audio probing quickly, but it took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void SystemAudioActivityProbe_Combines_Communications_Render_Sessions_When_The_Multimedia_Endpoint_Is_Quiet()
    {
        var multimediaSnapshot = new AudioSourceAttributionSnapshot(
            "Display Audio",
            0d,
            false,
            "below-threshold",
            Array.Empty<AudioSourceSessionSnapshot>(),
            null);
        var communicationsSnapshot = new AudioSourceAttributionSnapshot(
            "Bluetooth Headset",
            0.42d,
            true,
            "active",
            new[]
            {
                new AudioSourceSessionSnapshot(
                    4242,
                    "ms-teams",
                    0.42d,
                    true,
                    false,
                    false,
                    "Microsoft Teams",
                    "teams-session-1"),
            },
            null);

        var probe = new SystemAudioActivityProbe((flow, role, threshold) =>
        {
            Assert.Equal(DataFlow.Render, flow);
            return role == Role.Communications ? communicationsSnapshot : multimediaSnapshot;
        });

        var result = probe.Capture(0.05d);

        Assert.True(result.IsActive);
        Assert.Equal("Bluetooth Headset", result.DeviceName);
        Assert.Contains(result.Sessions, session => string.Equals(session.ProcessName, "ms-teams", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<WindowMeetingDetector> CreateDetectorAsync(params MeetingWindowCandidate[] candidates)
    {
        return CreateDetectorAsync(candidates, null);
    }

    private static async Task<WindowMeetingDetector> CreateDetectorAsync(
        MeetingWindowCandidate[] candidates,
        IAudioActivityProbe? audioActivityProbe = null,
        TimeSpan? audioTimeout = null,
        TimeSpan? audioBackoff = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, Path.Combine(root, "documents"));
        var initial = await store.LoadOrCreateAsync();
        var liveConfig = new LiveAppConfig(store, initial with
        {
            CalendarTitleFallbackEnabled = false,
        });

        return new WindowMeetingDetector(
            liveConfig,
            new MeetingDetectionEvaluator(),
            audioActivityProbe ?? new StubAudioActivityProbe(),
            new MeetingTitleEnricher(new StubCalendarMeetingTitleProvider()),
            () => candidates,
            audioTimeout ?? TimeSpan.FromMilliseconds(750),
            audioBackoff ?? TimeSpan.FromMinutes(2));
    }

    private sealed class StubAudioActivityProbe : IAudioActivityProbe
    {
        private readonly AudioSourceAttributionSnapshot _snapshot;

        public StubAudioActivityProbe()
            : this(new AudioSourceAttributionSnapshot(
                "Test Device",
                0.5d,
                true,
                "active",
                Array.Empty<AudioSourceSessionSnapshot>(),
                null))
        {
        }

        public StubAudioActivityProbe(AudioSourceAttributionSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public AudioSourceAttributionSnapshot Capture(double threshold)
        {
            return _snapshot;
        }
    }

    private sealed class StubCalendarMeetingTitleProvider : ICalendarMeetingTitleProvider
    {
        public CalendarMeetingDetailsCandidate? TryGetMeetingTitle(
            MeetingPlatform platform,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc)
        {
            return null;
        }
    }

    private sealed class BlockingAudioActivityProbe : IAudioActivityProbe
    {
        private readonly ManualResetEventSlim _release;

        public BlockingAudioActivityProbe(ManualResetEventSlim release)
        {
            _release = release;
        }

        public AudioSourceAttributionSnapshot Capture(double threshold)
        {
            _release.Wait();
            return new AudioSourceAttributionSnapshot(
                "Blocked Device",
                threshold,
                true,
                "active",
                Array.Empty<AudioSourceSessionSnapshot>(),
                null);
        }
    }
}
