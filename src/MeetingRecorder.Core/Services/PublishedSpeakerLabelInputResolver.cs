using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using NAudio.Wave;

namespace MeetingRecorder.Core.Services;

/// <summary>Confirms a published meeting can be labeled without reopening raw capture chunks.</summary>
internal static class PublishedSpeakerLabelInputResolver
{
    public static bool IsReady(MeetingOutputRecord meeting, MeetingSessionManifest manifest, AppConfig config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(meeting.AudioPath) || string.IsNullOrWhiteSpace(meeting.MarkdownPath) ||
                string.IsNullOrWhiteSpace(meeting.JsonPath) || string.IsNullOrWhiteSpace(meeting.ReadyMarkerPath)) return false;
            var paths = new ArtifactPathBuilder();
            var title = string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle;
            var stem = manifest.ImportedSourceAudio is null
                ? paths.BuildFileStem(manifest.Platform, manifest.StartedAtUtc, title)
                : paths.BuildImportedFileStem(manifest.Platform, manifest.StartedAtUtc, title, manifest.SessionId);
            var sidecars = ArtifactPathBuilder.BuildTranscriptSidecarRoot(config.TranscriptOutputDir);
            if (!string.Equals(meeting.Stem, stem, StringComparison.OrdinalIgnoreCase) ||
                !IsSamePath(meeting.AudioPath, Path.Combine(config.AudioOutputDir, stem + ".wav")) ||
                !IsSamePath(meeting.MarkdownPath, Path.Combine(config.TranscriptOutputDir, stem + ".md")) ||
                !IsSamePath(meeting.JsonPath, Path.Combine(sidecars, stem + ".json")) ||
                !IsSamePath(meeting.ReadyMarkerPath, Path.Combine(sidecars, stem + ".ready")) ||
                !File.Exists(meeting.MarkdownPath) || !File.Exists(meeting.JsonPath) || !File.Exists(meeting.ReadyMarkerPath) ||
                !string.Equals(File.ReadAllText(meeting.ReadyMarkerPath).Trim(), "ready", StringComparison.OrdinalIgnoreCase))
                return false;
            var transcript = MeetingTranscriptDocumentReader.Read(meeting.JsonPath, meeting.MarkdownPath);
            if (!transcript.HasStructuredJson || transcript.StructuredSegments.Count == 0) return false;
            using var reader = new WaveFileReader(meeting.AudioPath);
            return reader.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsSamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
