namespace MeetingRecorder.Core.Domain;

public sealed record PublishedArtifactSet(
    string AudioPath,
    string MarkdownPath,
    string JsonPath,
    string ReadyMarkerPath);
