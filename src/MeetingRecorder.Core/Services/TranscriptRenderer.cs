using MeetingRecorder.Core.Domain;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class TranscriptRenderer
{
    public string RenderMarkdown(MeetingSessionManifest manifest, IReadOnlyList<TranscriptSegment> segments)
    {
        var lines = new List<string>
        {
            $"# {manifest.DetectedTitle}",
            string.Empty,
            $"- Session ID: {manifest.SessionId}",
            $"- Platform: {manifest.Platform}",
            $"- Started (UTC): {manifest.StartedAtUtc:O}",
            string.Empty,
            "## Transcript",
            string.Empty,
        };

        foreach (var segment in segments)
        {
            var speaker = string.IsNullOrWhiteSpace(segment.SpeakerLabel) ? "Speaker" : segment.SpeakerLabel;
            lines.Add($"[{segment.Start:hh\\:mm\\:ss} - {segment.End:hh\\:mm\\:ss}] **{speaker}:** {segment.Text}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string RenderJson(MeetingSessionManifest manifest, IReadOnlyList<TranscriptSegment> segments)
    {
        var payload = new TranscriptDocument(
            manifest.SessionId,
            manifest.Platform.ToString(),
            manifest.DetectedTitle,
            manifest.StartedAtUtc,
            manifest.EndedAtUtc,
            manifest.TranscriptionStatus,
            manifest.DiarizationStatus,
            manifest.PublishStatus,
            segments.Select(segment => new TranscriptDocumentSegment(
                segment.Start,
                segment.End,
                segment.SpeakerLabel,
                segment.Text)).ToArray());

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private sealed record TranscriptDocument(
        string SessionId,
        string Platform,
        string Title,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        ProcessingStageStatus TranscriptionStatus,
        ProcessingStageStatus DiarizationStatus,
        ProcessingStageStatus PublishStatus,
        IReadOnlyList<TranscriptDocumentSegment> Segments);

    private sealed record TranscriptDocumentSegment(
        TimeSpan Start,
        TimeSpan End,
        string? SpeakerLabel,
        string Text);
}
