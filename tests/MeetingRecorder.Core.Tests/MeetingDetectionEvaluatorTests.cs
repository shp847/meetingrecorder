using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingDetectionEvaluatorTests
{
    [Fact]
    public void Evaluate_Returns_A_Start_Decision_For_Google_Meet_Signals()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Sprint Planning - Google Meet", 0.7, DateTimeOffset.UtcNow),
            new DetectionSignal("browser-url", "https://meet.google.com/abc-defg-hij", 0.8, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "peak=0.32", 0.2, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.Equal(MeetingPlatform.GoogleMeet, decision.Platform);
        Assert.True(decision.Confidence >= 0.75d);
    }

    [Fact]
    public void Evaluate_Returns_A_Start_Decision_For_Short_Google_Meet_Title_Signals()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Meet - abc-defg-hij", 0.70, DateTimeOffset.UtcNow),
            new DetectionSignal("browser-tab", "Meet - abc-defg-hij", 0.05, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "peak=0.32", 0.2, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.Equal(MeetingPlatform.GoogleMeet, decision.Platform);
        Assert.True(decision.Confidence >= 0.75d);
    }

    [Fact]
    public void Evaluate_Keeps_But_Does_Not_Start_For_A_Specific_Google_Meet_Window_When_Audio_Is_Silent()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Meet - jbz-oabg-rpe and 4 more pages - Work - Microsoft Edge", 0.85d, DateTimeOffset.UtcNow),
            new DetectionSignal("browser-window", "Meet - jbz-oabg-rpe and 4 more pages - Work - Microsoft Edge", 0.15d, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-silence", "peak=0.00", 0d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.GoogleMeet, decision.Platform);
        Assert.Contains("no active system audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Does_Not_Start_For_A_Generic_Google_Meet_Window_When_Audio_Is_Silent()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Google Meet and 15 more pages - Work - Microsoft Edge", 0.85d, DateTimeOffset.UtcNow),
            new DetectionSignal("browser-window", "Google Meet and 15 more pages - Work - Microsoft Edge", 0.15d, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-silence", "peak=0.00", 0d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.GoogleMeet, decision.Platform);
        Assert.Contains("no active system audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MeetingPlatform.Teams, "Microsoft Teams | Pinned window | Microsoft Teams")]
    [InlineData(MeetingPlatform.GoogleMeet, "Google Meet and 1 more page - Work - Microsoft Edge")]
    public void Evaluate_Does_Not_Start_A_Generic_Meeting_Shell_From_Unattributed_Endpoint_Audio(
        MeetingPlatform expectedPlatform,
        string windowTitle)
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", windowTitle, 0.85d, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "Speakers; peak=0.112; status=active", 0.10d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(expectedPlatform, decision.Platform);
        Assert.Contains("could not be attributed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MeetingPlatform.Teams, "Microsoft Teams | Pinned window | Microsoft Teams", "audio-window")]
    [InlineData(MeetingPlatform.GoogleMeet, "Google Meet and 1 more page - Work - Microsoft Edge", "audio-browser-tab")]
    public void Evaluate_Starts_A_Generic_Meeting_Shell_With_Attributed_Audio(
        MeetingPlatform expectedPlatform,
        string windowTitle,
        string audioSignalSource)
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", windowTitle, 0.85d, DateTimeOffset.UtcNow),
            new DetectionSignal(audioSignalSource, "Meeting audio; peak=0.112; confidence=High", 0.35d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(expectedPlatform, decision.Platform);
    }

    [Fact]
    public void Evaluate_Does_Not_Start_For_Teams_Signals_When_Audio_Is_Inactive()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Weekly Sync | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("process-name", "ms-teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-silence", "peak=0.00", 0d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("no active system audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Removes_Teams_Organization_And_Account_From_Meeting_Title()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal(
                "window-title",
                "Project Planning | Prep | Contoso | user@example.com | Microsoft Teams",
                0.85,
                DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.Equal("Project Planning | Prep", decision.SessionTitle);
    }

    [Fact]
    public void Evaluate_Does_Not_Start_For_Teams_Chat_Window_Even_When_Audio_Is_Active()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Chat | Muzzi, Marcelo | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("process-name", "ms-teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "peak=0.27", 0.2, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.False(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("chat", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Extracts_Attendee_Name_From_Suppressed_Teams_Chat_Window()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Chat | Chao, Adam | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("process-name", "ms-teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-silence", "peak=0.00", 0d, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.False(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Equal("Chao, Adam", decision.SessionTitle);
    }

    [Fact]
    public void Evaluate_Does_Not_Throw_For_Suppressed_Teams_Chat_Window_Without_Attendee_Name()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Chat | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("process-name", "ms-teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "peak=0.27", 0.2, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.False(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("chat", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Does_Not_Start_For_Generic_Teams_Shell_Window_Even_When_Audio_Is_Active()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("process-name", "ms-teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "peak=0.27", 0.2, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.False(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("generic teams shell", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Does_Not_Start_For_Unattributed_Email_Only_Teams_Account_Shell()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "psharm04@atkearney.com | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("teams-host", "Microsoft Teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-activity", "Headphones; peak=0.263; status=active", 0.10, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.False(decision.ShouldStart);
        Assert.False(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("generic teams shell", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Starts_Email_Titled_Teams_Call_With_Attributed_Audio()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "external.user@example.com | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("teams-host", "Microsoft Teams", 0.15, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-window", "Microsoft Teams; peak=0.263; confidence=High", 0.35, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.True(decision.ShouldKeepRecording);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
    }

    [Fact]
    public void Evaluate_Treats_Audio_Window_Attribution_As_Active_Audio_For_Teams()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "GF/Bharat | AI workshop Sync Sourcing | Microsoft Teams", 0.85, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-window", "Microsoft Teams; process=ms-teams; peak=0.27; confidence=High", 0.35, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.Equal(MeetingPlatform.Teams, decision.Platform);
        Assert.Contains("active system audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Treats_Audio_Browser_Tab_Attribution_As_Active_Audio_For_Google_Meet()
    {
        var evaluator = new MeetingDetectionEvaluator();
        var signals = new[]
        {
            new DetectionSignal("window-title", "Meet - abc-defg-hij", 0.70, DateTimeOffset.UtcNow),
            new DetectionSignal("browser-tab", "Meet - abc-defg-hij", 0.05, DateTimeOffset.UtcNow),
            new DetectionSignal("audio-browser-tab", "Google Meet; tab=Meet - abc-defg-hij; process=msedge; peak=0.32; confidence=High", 0.35, DateTimeOffset.UtcNow),
        };

        var decision = evaluator.Evaluate(signals);

        Assert.True(decision.ShouldStart);
        Assert.Equal(MeetingPlatform.GoogleMeet, decision.Platform);
        Assert.Contains("active system audio", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
