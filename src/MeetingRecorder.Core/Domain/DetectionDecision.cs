namespace MeetingRecorder.Core.Domain;

public sealed record DetectionDecision(
    MeetingPlatform Platform,
    bool ShouldStart,
    bool ShouldKeepRecording,
    double Confidence,
    string SessionTitle,
    IReadOnlyList<DetectionSignal> Signals,
    string Reason);
