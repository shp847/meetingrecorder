namespace MeetingRecorder.Core.Domain;

public sealed record RecordingArtifactSet(
    IReadOnlyList<string> RawChunkPaths,
    IReadOnlyList<string> MicrophoneChunkPaths,
    string? MergedAudioPath,
    string? PublishedAudioPath);
