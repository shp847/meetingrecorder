using MeetingRecorder.Core.Domain;
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
        if (hasJson &&
            TryReadJsonTranscript(
                jsonPath!,
                out var jsonSegments,
                out var structuredSegments,
                out var summarizationStatus,
                out var summary))
        {
            return new MeetingTranscriptReaderResult(
                jsonSegments.Count > 0,
                BuildStatusText(jsonSegments.Count, structuredSegments.Count, "JSON sidecar"),
                jsonSegments,
                structuredSegments,
                summarizationStatus,
                summary,
                HasStructuredJson: true);
        }

        var hasMarkdown = !string.IsNullOrWhiteSpace(markdownPath) && File.Exists(markdownPath);
        if (hasMarkdown && TryReadMarkdownTranscript(markdownPath!, out var markdownSegments))
        {
            return new MeetingTranscriptReaderResult(
                markdownSegments.Count > 0,
                BuildStatusText(markdownSegments.Count, markdownSegments.Count, "Markdown transcript"),
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

    internal static bool TryReadStructuredSegments(
        string? jsonPath,
        string? markdownPath,
        out IReadOnlyList<TranscriptSegment> segments)
    {
        segments = Array.Empty<TranscriptSegment>();
        var hasJson = !string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath);
        if (hasJson &&
            TryReadJsonTranscript(
                jsonPath!,
                out _,
                out var jsonStructuredSegments,
                out _,
                out _) &&
            jsonStructuredSegments.Count > 0)
        {
            segments = jsonStructuredSegments;
            return true;
        }

        var hasMarkdown = !string.IsNullOrWhiteSpace(markdownPath) && File.Exists(markdownPath);
        if (hasMarkdown && TryReadMarkdownTranscriptSegments(markdownPath!, out var markdownSegments))
        {
            segments = markdownSegments;
            return markdownSegments.Count > 0;
        }

        return false;
    }

    private static bool TryReadJsonTranscript(
        string jsonPath,
        out IReadOnlyList<MeetingTranscriptSegmentRow> segments,
        out IReadOnlyList<TranscriptSegment> structuredSegments,
        out ProcessingStageStatus? summarizationStatus,
        out MeetingSummary? summary)
    {
        segments = Array.Empty<MeetingTranscriptSegmentRow>();
        structuredSegments = Array.Empty<TranscriptSegment>();
        summarizationStatus = null;
        summary = null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            var segmentNodes = GetSegmentsArray(root);
            if (segmentNodes is null)
            {
                return false;
            }

            var structuredRows = new List<TranscriptSegment>();
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
                var end = GetJsonTimeSpan(segmentObject, "end", "End") ?? start;
                var speakerId = GetJsonString(segmentObject, "speakerId", "SpeakerId");
                var speakerLabel = GetJsonString(
                    segmentObject,
                    "displaySpeakerLabel",
                    "DisplaySpeakerLabel",
                    "speakerLabel",
                    "SpeakerLabel",
                    "speakerId",
                    "SpeakerId");
                structuredRows.Add(new TranscriptSegment(
                    start,
                    end,
                    string.IsNullOrWhiteSpace(speakerId) ? null : speakerId.Trim(),
                    string.IsNullOrWhiteSpace(speakerLabel) ? null : speakerLabel.Trim(),
                    text.Trim()));
            }

            segments = BuildDisplayRows(structuredRows);
            structuredSegments = structuredRows;
            summarizationStatus = TryReadSummarizationStatus(root);
            summary = TryReadSummary(root);
            return structuredRows.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMarkdownTranscript(string markdownPath, out IReadOnlyList<MeetingTranscriptSegmentRow> segments)
    {
        segments = Array.Empty<MeetingTranscriptSegmentRow>();
        if (!TryReadMarkdownTranscriptSegments(markdownPath, out var markdownSegments))
        {
            return false;
        }

        segments = BuildDisplayRows(markdownSegments);
        return markdownSegments.Count > 0;
    }

    private static bool TryReadMarkdownTranscriptSegments(string markdownPath, out IReadOnlyList<TranscriptSegment> segments)
    {
        segments = Array.Empty<TranscriptSegment>();
        try
        {
            var markdownSegments = new List<TranscriptSegment>();
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
                var end = TimeSpan.TryParse(match.Groups["end"].Value, CultureInfo.InvariantCulture, out var parsedEnd)
                    ? parsedEnd
                    : start;
                var speakerLabel = match.Groups["speaker"].Value.Trim();
                markdownSegments.Add(new TranscriptSegment(
                    start,
                    end,
                    null,
                    string.IsNullOrWhiteSpace(speakerLabel) ? "Speaker" : speakerLabel,
                    text));
            }

            segments = markdownSegments;
            return markdownSegments.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<MeetingTranscriptSegmentRow> BuildDisplayRows(IReadOnlyList<TranscriptSegment> segments)
    {
        return TranscriptParagraphBuilder
            .Build(segments, static segment => string.IsNullOrWhiteSpace(segment.SpeakerLabel)
                ? "Speaker"
                : segment.SpeakerLabel)
            .Select(paragraph => new MeetingTranscriptSegmentRow(
                FormatTranscriptTimestamp(paragraph.Start),
                paragraph.SpeakerLabel,
                paragraph.Text))
            .ToArray();
    }

    private static JsonArray? GetSegmentsArray(JsonObject? root)
    {
        return root?["segments"] as JsonArray ?? root?["Segments"] as JsonArray;
    }

    private static ProcessingStageStatus? TryReadSummarizationStatus(JsonObject? root)
    {
        if (root?["summarizationStatus"] is not JsonObject statusObject &&
            root?["SummarizationStatus"] is not JsonObject statusObjectPascal)
        {
            return null;
        }

        var node = root["summarizationStatus"] as JsonObject ?? root["SummarizationStatus"] as JsonObject;
        if (node is null)
        {
            return null;
        }

        try
        {
            var stageName = GetJsonString(node, "stageName", "StageName") ?? "summarization";
            var state = GetJsonEnum(node, "state", "State") ?? StageExecutionState.NotStarted;
            var updatedAtUtc = GetJsonDateTimeOffset(node, "updatedAtUtc", "UpdatedAtUtc") ?? DateTimeOffset.UtcNow;
            var message = GetJsonNullableString(node, "message", "Message");
            return new ProcessingStageStatus(stageName, state, updatedAtUtc, message);
        }
        catch
        {
            return null;
        }
    }

    private static MeetingSummary? TryReadSummary(JsonObject? root)
    {
        var node = root?["summary"] as JsonObject ?? root?["Summary"] as JsonObject;
        if (node is null)
        {
            return null;
        }

        try
        {
            var overview = GetJsonString(node, "overview", "Overview");
            if (string.IsNullOrWhiteSpace(overview))
            {
                return null;
            }

            var providerObject = node["provider"] as JsonObject ?? node["Provider"] as JsonObject;
            var providerName = GetOptionalJsonString(providerObject, "providerName", "ProviderName") ?? "Unknown provider";
            var model = GetOptionalJsonString(providerObject, "model", "Model") ?? "Unknown model";
            var providerKind = GetSummaryProviderKind(providerObject);
            var fallbackUsed = GetJsonBool(providerObject, "fallbackUsed", "FallbackUsed") ?? false;
            var modelProxyRouting = GetModelProxyRouting(providerObject);
            var generatedAtUtc = GetJsonDateTimeOffset(node, "generatedAtUtc", "GeneratedAtUtc") ?? DateTimeOffset.UtcNow;
            var fingerprint = GetJsonString(node, "transcriptFingerprint", "TranscriptFingerprint") ?? string.Empty;
            return new MeetingSummary(
                overview.Trim(),
                GetJsonStringArray(node, "keyPoints", "KeyPoints"),
                GetJsonStringArray(node, "decisions", "Decisions"),
                GetJsonActionItems(node),
                GetJsonStringArray(node, "risksAndOpenQuestions", "RisksAndOpenQuestions"),
                new MeetingSummaryProviderInfo(providerKind, providerName.Trim(), model.Trim(), fallbackUsed, modelProxyRouting),
                generatedAtUtc,
                fingerprint.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static SummaryChatProviderKind GetSummaryProviderKind(JsonObject? providerObject)
    {
        if (providerObject is null)
        {
            return SummaryChatProviderKind.OpenAi;
        }

        foreach (var propertyName in new[] { "providerKind", "ProviderKind" })
        {
            if (providerObject[propertyName] is not JsonValue value)
            {
                continue;
            }

            if (value.TryGetValue<string>(out var text) &&
                Enum.TryParse<SummaryChatProviderKind>(text, ignoreCase: true, out var parsedText))
            {
                return parsedText;
            }

            if (value.TryGetValue<int>(out var integer) &&
                Enum.IsDefined(typeof(SummaryChatProviderKind), integer))
            {
                return (SummaryChatProviderKind)integer;
            }
        }

        return SummaryChatProviderKind.OpenAi;
    }

    private static ModelProxyRoutingInfo? GetModelProxyRouting(JsonObject? providerObject)
    {
        var routingObject = providerObject?["modelProxyRouting"] as JsonObject ??
                            providerObject?["ModelProxyRouting"] as JsonObject;
        if (routingObject is null)
        {
            return null;
        }

        var routingInfo = new ModelProxyRoutingInfo(
            GetJsonNullableString(routingObject, "requestId", "RequestId"),
            GetJsonNullableString(routingObject, "requestedBackend", "RequestedBackend"),
            GetJsonNullableString(routingObject, "effectiveBackend", "EffectiveBackend"),
            GetJsonNullableString(routingObject, "webSearchBackend", "WebSearchBackend"),
            GetJsonBool(routingObject, "appServerWebSearchSupported", "AppServerWebSearchSupported"),
            GetJsonNullableString(routingObject, "fallbackReason", "FallbackReason"));

        return routingInfo.RequestId is null &&
               routingInfo.RequestedBackend is null &&
               routingInfo.EffectiveBackend is null &&
               routingInfo.WebSearchBackend is null &&
               routingInfo.AppServerWebSearchSupported is null &&
               routingInfo.FallbackReason is null
            ? null
            : routingInfo;
    }

    private static IReadOnlyList<string> GetJsonStringArray(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is not JsonArray array)
            {
                continue;
            }

            return array
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<MeetingSummaryActionItem> GetJsonActionItems(JsonObject node)
    {
        var array = node["actionItems"] as JsonArray ?? node["ActionItems"] as JsonArray;
        if (array is null)
        {
            return Array.Empty<MeetingSummaryActionItem>();
        }

        return array
            .OfType<JsonObject>()
            .Select(actionObject => new MeetingSummaryActionItem(
                GetJsonString(actionObject, "text", "Text") ?? string.Empty,
                GetJsonNullableString(actionObject, "owner", "Owner"),
                GetJsonNullableString(actionObject, "dueDateText", "DueDateText")))
            .Where(actionItem => !string.IsNullOrWhiteSpace(actionItem.Text))
            .ToArray();
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

    private static string? GetOptionalJsonString(JsonObject? node, params string[] propertyNames)
    {
        return node is null ? null : GetJsonString(node, propertyNames);
    }

    private static string? GetJsonNullableString(JsonObject? node, params string[] propertyNames)
    {
        var value = GetOptionalJsonString(node, propertyNames);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static StageExecutionState? GetJsonEnum(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is not JsonValue value)
            {
                continue;
            }

            if (value.TryGetValue<int>(out var integer) &&
                Enum.IsDefined(typeof(StageExecutionState), integer))
            {
                return (StageExecutionState)integer;
            }

            if (value.TryGetValue<string>(out var text) &&
                Enum.TryParse<StageExecutionState>(text, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetJsonDateTimeOffset(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<DateTimeOffset>(out var dateTimeOffset))
            {
                return dateTimeOffset;
            }

            if (node[propertyName] is JsonValue stringValue &&
                stringValue.TryGetValue<string>(out var text) &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool? GetJsonBool(JsonObject? node, params string[] propertyNames)
    {
        if (node is null)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<bool>(out var boolean))
            {
                return boolean;
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

    private static string BuildStatusText(int displayCount, int sourceSegmentCount, string sourceLabel)
    {
        if (displayCount == 0)
        {
            return $"No readable transcript segments found in {sourceLabel}.";
        }

        return displayCount == sourceSegmentCount
            ? $"Showing {displayCount} transcript segment(s) from {sourceLabel}."
            : $"Showing {displayCount} transcript paragraph(s) from {sourceLabel} (merged from {sourceSegmentCount} segment(s)).";
    }
}
