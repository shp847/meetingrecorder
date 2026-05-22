using MeetingRecorder.Core.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

public sealed class TranscriptRenderer
{
    public string RenderMarkdown(MeetingSessionManifest manifest, IReadOnlyList<TranscriptSegment> segments)
    {
        var speakerCatalog = BuildSpeakerCatalog(manifest.ProcessingMetadata?.Speakers);
        var metadataLines = new List<string>
        {
            $"- Session ID: {manifest.SessionId}",
            $"- Platform: {manifest.Platform}",
            $"- Started (UTC): {manifest.StartedAtUtc:O}",
        };

        if (!string.IsNullOrWhiteSpace(manifest.ProjectName))
        {
            metadataLines.Add($"- Project: {manifest.ProjectName}");
        }

        if (manifest.KeyAttendees.Count > 0)
        {
            metadataLines.Add($"- Key Attendees: {string.Join(", ", manifest.KeyAttendees)}");
        }

        if (manifest.DetectedAudioSource is not null)
        {
            metadataLines.Add($"- Detected audio source: {BuildDetectedAudioSourceMarkdown(manifest.DetectedAudioSource)}");
        }

        var lines = new List<string>
        {
            $"# {manifest.DetectedTitle}",
            string.Empty,
        };
        lines.AddRange(metadataLines);
        lines.Add(string.Empty);
        if (ShouldRenderSummary(manifest))
        {
            AddSummaryMarkdown(lines, manifest.Summary!);
            lines.Add(string.Empty);
        }

        lines.Add("## Transcript");
        lines.Add(string.Empty);

        foreach (var segment in segments)
        {
            var speaker = ResolveSpeakerLabel(segment, speakerCatalog);
            lines.Add($"[{segment.Start:hh\\:mm\\:ss} - {segment.End:hh\\:mm\\:ss}] **{speaker}:** {segment.Text}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string RenderJson(MeetingSessionManifest manifest, IReadOnlyList<TranscriptSegment> segments)
    {
        var speakerCatalog = BuildSpeakerCatalog(manifest.ProcessingMetadata?.Speakers);
        var payload = new TranscriptDocument(
            manifest.SessionId,
            manifest.Platform.ToString(),
            manifest.DetectedTitle,
            manifest.StartedAtUtc,
            manifest.EndedAtUtc,
            manifest.ProjectName,
            manifest.KeyAttendees,
            manifest.DetectedAudioSource,
            manifest.TranscriptionStatus,
            manifest.DiarizationStatus,
            manifest.SummarizationStatus,
            ShouldRenderSummary(manifest) ? BuildTranscriptSummary(manifest.Summary!) : null,
            manifest.PublishStatus,
            manifest.Attendees,
            manifest.ProcessingMetadata?.TranscriptionModelFileName,
            manifest.ProcessingMetadata?.HasSpeakerLabels ?? segments.Any(segment =>
                !string.IsNullOrWhiteSpace(segment.SpeakerId) ||
                !string.IsNullOrWhiteSpace(segment.SpeakerLabel)),
            manifest.ProcessingMetadata?.Speakers ?? Array.Empty<SpeakerIdentity>(),
            manifest.ProcessingMetadata?.SpeakerTurns ?? Array.Empty<SpeakerTurn>(),
            manifest.ProcessingMetadata?.DiarizationMetadata,
            segments.Select(segment => new TranscriptDocumentSegment(
                segment.Start,
                segment.End,
                segment.SpeakerId,
                ResolveSpeakerLabel(segment, speakerCatalog),
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
        [property: JsonPropertyName("projectName")] string? ProjectName,
        [property: JsonPropertyName("keyAttendees")] IReadOnlyList<string> KeyAttendees,
        [property: JsonPropertyName("detectedAudioSource")] DetectedAudioSource? DetectedAudioSource,
        ProcessingStageStatus TranscriptionStatus,
        ProcessingStageStatus DiarizationStatus,
        [property: JsonPropertyName("summarizationStatus")] ProcessingStageStatus SummarizationStatus,
        [property: JsonPropertyName("summary")] TranscriptDocumentSummary? Summary,
        ProcessingStageStatus PublishStatus,
        [property: JsonPropertyName("attendees")] IReadOnlyList<MeetingAttendee> Attendees,
        [property: JsonPropertyName("transcriptionModelFileName")] string? TranscriptionModelFileName,
        [property: JsonPropertyName("hasSpeakerLabels")] bool HasSpeakerLabels,
        [property: JsonPropertyName("speakers")] IReadOnlyList<SpeakerIdentity> Speakers,
        [property: JsonPropertyName("speakerTurns")] IReadOnlyList<SpeakerTurn> SpeakerTurns,
        [property: JsonPropertyName("diarizationMetadata")] DiarizationMetadata? DiarizationMetadata,
        [property: JsonPropertyName("segments")] IReadOnlyList<TranscriptDocumentSegment> Segments);

    private sealed record TranscriptDocumentSegment(
        [property: JsonPropertyName("start")] TimeSpan Start,
        [property: JsonPropertyName("end")] TimeSpan End,
        [property: JsonPropertyName("speakerId")] string? SpeakerId,
        [property: JsonPropertyName("speakerLabel")] string? SpeakerLabel,
        [property: JsonPropertyName("displaySpeakerLabel")] string? DisplaySpeakerLabel,
        [property: JsonPropertyName("text")] string Text);

    private sealed record TranscriptDocumentSummary(
        [property: JsonPropertyName("overview")] string Overview,
        [property: JsonPropertyName("keyPoints")] IReadOnlyList<string> KeyPoints,
        [property: JsonPropertyName("decisions")] IReadOnlyList<string> Decisions,
        [property: JsonPropertyName("actionItems")] IReadOnlyList<TranscriptDocumentSummaryActionItem> ActionItems,
        [property: JsonPropertyName("risksAndOpenQuestions")] IReadOnlyList<string> RisksAndOpenQuestions,
        [property: JsonPropertyName("provider")] TranscriptDocumentSummaryProvider Provider,
        [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
        [property: JsonPropertyName("transcriptFingerprint")] string TranscriptFingerprint);

    private sealed record TranscriptDocumentSummaryActionItem(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("owner")] string? Owner,
        [property: JsonPropertyName("dueDateText")] string? DueDateText);

    private sealed record TranscriptDocumentSummaryProvider(
        [property: JsonPropertyName("providerKind")] string ProviderKind,
        [property: JsonPropertyName("providerName")] string ProviderName,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("fallbackUsed")] bool FallbackUsed);

    private static bool ShouldRenderSummary(MeetingSessionManifest manifest)
    {
        return manifest.SummarizationStatus.State == StageExecutionState.Succeeded &&
               manifest.Summary is not null;
    }

    private static void AddSummaryMarkdown(ICollection<string> lines, MeetingSummary summary)
    {
        lines.Add("## Summary");
        lines.Add(string.Empty);
        lines.Add(summary.Overview);
        lines.Add(string.Empty);

        AddBulletSection(lines, "Key Points", summary.KeyPoints);
        AddBulletSection(lines, "Decisions", summary.Decisions);
        if (summary.ActionItems.Count > 0)
        {
            lines.Add("### Action Items");
            foreach (var actionItem in summary.ActionItems)
            {
                lines.Add($"- {BuildActionItemMarkdown(actionItem)}");
            }

            lines.Add(string.Empty);
        }

        AddBulletSection(lines, "Risks And Open Questions", summary.RisksAndOpenQuestions);
        lines.Add($"- Provider: {summary.Provider.ProviderName} ({summary.Provider.Model})");
        if (summary.Provider.FallbackUsed)
        {
            lines.Add("- Fallback used: Yes");
        }
    }

    private static void AddBulletSection(
        ICollection<string> lines,
        string title,
        IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lines.Add($"### {title}");
        foreach (var item in items)
        {
            lines.Add($"- {item}");
        }

        lines.Add(string.Empty);
    }

    private static string BuildActionItemMarkdown(MeetingSummaryActionItem actionItem)
    {
        var parts = new List<string> { actionItem.Text };
        if (!string.IsNullOrWhiteSpace(actionItem.Owner))
        {
            parts.Add($"Owner: {actionItem.Owner}.");
        }

        if (!string.IsNullOrWhiteSpace(actionItem.DueDateText))
        {
            parts.Add($"Due: {actionItem.DueDateText}.");
        }

        return string.Join(" ", parts);
    }

    private static TranscriptDocumentSummary BuildTranscriptSummary(MeetingSummary summary)
    {
        return new TranscriptDocumentSummary(
            summary.Overview,
            summary.KeyPoints,
            summary.Decisions,
            summary.ActionItems.Select(actionItem => new TranscriptDocumentSummaryActionItem(
                actionItem.Text,
                actionItem.Owner,
                actionItem.DueDateText)).ToArray(),
            summary.RisksAndOpenQuestions,
            new TranscriptDocumentSummaryProvider(
                summary.Provider.ProviderKind.ToString(),
                summary.Provider.ProviderName,
                summary.Provider.Model,
                summary.Provider.FallbackUsed),
            summary.GeneratedAtUtc,
            summary.TranscriptFingerprint);
    }

    private static IReadOnlyDictionary<string, string> BuildSpeakerCatalog(IReadOnlyList<SpeakerIdentity>? speakers)
    {
        return speakers is null || speakers.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : speakers
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Id) && !string.IsNullOrWhiteSpace(speaker.DisplayName))
                .GroupBy(speaker => speaker.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().DisplayName, StringComparer.Ordinal);
    }

    private static string ResolveSpeakerLabel(TranscriptSegment segment, IReadOnlyDictionary<string, string> speakerCatalog)
    {
        if (!string.IsNullOrWhiteSpace(segment.SpeakerLabel))
        {
            return segment.SpeakerLabel;
        }

        if (!string.IsNullOrWhiteSpace(segment.SpeakerId) &&
            speakerCatalog.TryGetValue(segment.SpeakerId, out var displayName) &&
            !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return "Speaker";
    }

    private static string BuildDetectedAudioSourceMarkdown(DetectedAudioSource audioSource)
    {
        var title = !string.IsNullOrWhiteSpace(audioSource.BrowserTabTitle)
            ? audioSource.BrowserTabTitle.Trim()
            : !string.IsNullOrWhiteSpace(audioSource.WindowTitle)
                ? audioSource.WindowTitle.Trim()
                : null;

        return string.IsNullOrWhiteSpace(title)
            ? audioSource.AppName
            : $"{audioSource.AppName} from '{title}'";
    }
}
