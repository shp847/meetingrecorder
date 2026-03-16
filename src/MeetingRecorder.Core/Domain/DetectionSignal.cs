namespace MeetingRecorder.Core.Domain;

public sealed record DetectionSignal(
    string Source,
    string Value,
    double Weight,
    DateTimeOffset CapturedAtUtc);
