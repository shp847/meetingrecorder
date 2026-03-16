namespace MeetingRecorder.Core.Domain;

public sealed record DetectionDecision(
    MeetingPlatform Platform,
    bool ShouldStart,
    double Confidence,
    string SessionTitle,
    IReadOnlyList<DetectionSignal> Signals,
    string Reason);
