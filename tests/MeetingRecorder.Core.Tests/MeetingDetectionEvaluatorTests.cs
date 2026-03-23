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
