using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

internal static class MeetingOutputStatusResolver
{
    public static string ResolveDisplayStatus(MeetingOutputRecord record)
    {
        if (record.ManifestState is { } manifestState)
        {
            return manifestState.ToString();
        }

        var hasAudio = !string.IsNullOrWhiteSpace(record.AudioPath);
        var hasTranscript = !string.IsNullOrWhiteSpace(record.MarkdownPath) ||
            !string.IsNullOrWhiteSpace(record.JsonPath);

        if (!string.IsNullOrWhiteSpace(record.ReadyMarkerPath))
        {
            return SessionState.Published.ToString();
        }

        if (hasAudio && hasTranscript)
        {
            return SessionState.Published.ToString();
        }

        if (hasTranscript)
        {
            return "Transcript files present";
        }

        if (hasAudio)
        {
            return "Audio only";
        }

        return "Unknown";
    }
}
