using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static class MeetingTranscriptDocumentReader
{
    private static readonly Regex MarkdownTranscriptLinePattern = new(
        @"^\[(?<start>\d{2}:\d{2}:\d{2})\s*-\s*(?<end>\d{2}:\d{2}:\d{2})\]\s+\*\*(?<speaker>.*?):\*\*\s*(?<text>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MeetingTranscriptReaderResult Read(string? jsonPath, string? markdownPath)
    {
        var hasJson = !string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath);
        if (hasJson && TryReadJsonTranscript(jsonPath!, out var jsonSegments))
        {
            return new MeetingTranscriptReaderResult(
                jsonSegments.Count > 0,
                BuildStatusText(jsonSegments.Count, "JSON sidecar"),
                jsonSegments);
        }

        var hasMarkdown = !string.IsNullOrWhiteSpace(markdownPath) && File.Exists(markdownPath);
        if (hasMarkdown && TryReadMarkdownTranscript(markdownPath!, out var markdownSegments))
        {
            return new MeetingTranscriptReaderResult(
                markdownSegments.Count > 0,
                BuildStatusText(markdownSegments.Count, "Markdown transcript"),
                markdownSegments);
        }

        if (!hasJson && !hasMarkdown)
        {
            return new MeetingTranscriptReaderResult(
                false,
                "No transcript artifact is available for this meeting yet.",
                Array.Empty<MeetingTranscriptSegmentRow>());
        }

        return new MeetingTranscriptReaderResult(
            false,
            "Transcript artifact did not contain readable app transcript segments.",
            Array.Empty<MeetingTranscriptSegmentRow>());
    }

    private static bool TryReadJsonTranscript(string jsonPath, out IReadOnlyList<MeetingTranscriptSegmentRow> segments)
    {
        segments = Array.Empty<MeetingTranscriptSegmentRow>();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            var segmentNodes = GetSegmentsArray(root);
            if (segmentNodes is null)
            {
                return false;
            }

            var rows = new List<MeetingTranscriptSegmentRow>();
            foreach (var node in segmentNodes)
            {
                if (node is not JsonObject segmentObject)
                {
                    continue;
                }

                var text = GetJsonString(segmentObject, "text", "Text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var start = GetJsonTimeSpan(segmentObject, "start", "Start") ?? TimeSpan.Zero;
                var speakerLabel = GetJsonString(
                    segmentObject,
                    "speakerLabel",
                    "displaySpeakerLabel",
                    "SpeakerLabel",
                    "DisplaySpeakerLabel",
                    "speakerId",
                    "SpeakerId");
                rows.Add(new MeetingTranscriptSegmentRow(
                    FormatTranscriptTimestamp(start),
                    string.IsNullOrWhiteSpace(speakerLabel) ? "Speaker" : speakerLabel.Trim(),
                    text.Trim()));
            }

            segments = rows;
            return rows.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMarkdownTranscript(string markdownPath, out IReadOnlyList<MeetingTranscriptSegmentRow> segments)
    {
        segments = Array.Empty<MeetingTranscriptSegmentRow>();
        try
        {
            var rows = new List<MeetingTranscriptSegmentRow>();
            foreach (var line in File.ReadLines(markdownPath))
            {
                var match = MarkdownTranscriptLinePattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var text = match.Groups["text"].Value.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var start = TimeSpan.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, out var parsedStart)
                    ? parsedStart
                    : TimeSpan.Zero;
                var speakerLabel = match.Groups["speaker"].Value.Trim();
                rows.Add(new MeetingTranscriptSegmentRow(
                    FormatTranscriptTimestamp(start),
                    string.IsNullOrWhiteSpace(speakerLabel) ? "Speaker" : speakerLabel,
                    text));
            }

            segments = rows;
            return rows.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static JsonArray? GetSegmentsArray(JsonObject? root)
    {
        return root?["segments"] as JsonArray ?? root?["Segments"] as JsonArray;
    }

    private static string? GetJsonString(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static TimeSpan? GetJsonTimeSpan(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is not JsonValue value)
            {
                continue;
            }

            if (value.TryGetValue<TimeSpan>(out var timeSpan))
            {
                return timeSpan;
            }

            if (value.TryGetValue<string>(out var text) &&
                TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string FormatTranscriptTimestamp(TimeSpan timestamp)
    {
        return timestamp.TotalHours >= 1d
            ? timestamp.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : timestamp.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildStatusText(int segmentCount, string sourceLabel)
    {
        return segmentCount == 0
            ? $"No readable transcript segments found in {sourceLabel}."
            : $"Showing {segmentCount} transcript segment(s) from {sourceLabel}.";
    }
}
