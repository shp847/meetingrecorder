using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Services;

public sealed class PublishedMeetingSummaryService
{
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IMeetingSummarizationProvider _summarizationProvider;
    private readonly SessionManifestStore _manifestStore;

    public PublishedMeetingSummaryService(
        IMeetingSummarizationProvider summarizationProvider,
        SessionManifestStore? manifestStore = null)
    {
        _summarizationProvider = summarizationProvider;
        _manifestStore = manifestStore ?? new SessionManifestStore(new ArtifactPathBuilder());
    }

    public async Task<PublishedMeetingSummaryUpdateResult> GenerateAsync(
        MeetingOutputRecord meeting,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (config.SummaryGenerationMode != MeetingSummaryGenerationMode.Enabled)
        {
            return new PublishedMeetingSummaryUpdateResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    "Summary generation disabled."),
                null,
                UpdatedJson: false,
                UpdatedMarkdown: false,
                UpdatedManifest: false,
                UpdatedSnapshot: false);
        }

        var transcript = MeetingTranscriptDocumentReader.Read(meeting.JsonPath, meeting.MarkdownPath);
        if (!transcript.HasStructuredJson || transcript.StructuredSegments.Count == 0)
        {
            return new PublishedMeetingSummaryUpdateResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Skipped,
                    DateTimeOffset.UtcNow,
                    "Summary generation requires a readable transcript JSON sidecar."),
                null,
                UpdatedJson: false,
                UpdatedMarkdown: false,
                UpdatedManifest: false,
                UpdatedSnapshot: false);
        }

        var manifest = await LoadOrCreateManifestAsync(meeting, cancellationToken);
        MeetingSummaryResult providerResult;
        try
        {
            providerResult = await _summarizationProvider.SummarizeAsync(
                new MeetingSummaryRequest(manifest, transcript.StructuredSegments, config),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            providerResult = new MeetingSummaryResult(
                new ProcessingStageStatus(
                    "summarization",
                    StageExecutionState.Failed,
                    DateTimeOffset.UtcNow,
                    BuildSafeFailureMessage(exception)),
                null);
        }

        var status = NormalizeStatus(providerResult.Status);
        var summary = status.State == StageExecutionState.Succeeded ? providerResult.Summary : null;
        if (status.State == StageExecutionState.Succeeded && summary is null)
        {
            status = new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Failed,
                DateTimeOffset.UtcNow,
                "Summary provider reported success without summary content.");
        }

        var updatedJson = await TryUpdateJsonAsync(
            meeting.JsonPath,
            status,
            status.State == StageExecutionState.Succeeded ? summary : null,
            clearSummaryWhenNotSucceeded: transcript.Summary is null || transcript.SummarizationStatus?.State != StageExecutionState.Succeeded,
            cancellationToken);
        var updatedMarkdown = false;
        var updatedManifest = false;
        var updatedSnapshot = false;

        if (status.State == StageExecutionState.Succeeded && summary is not null)
        {
            updatedMarkdown = await TryUpdateMarkdownAsync(meeting.MarkdownPath, summary, cancellationToken);
            updatedManifest = await TryUpdateManifestAsync(meeting.ManifestPath, manifest, status, summary, cancellationToken);
            updatedSnapshot = await TryUpdateSnapshotAsync(meeting.ManifestPath, summary, cancellationToken);
        }

        return new PublishedMeetingSummaryUpdateResult(
            status,
            status.State == StageExecutionState.Succeeded ? summary : null,
            updatedJson,
            updatedMarkdown,
            updatedManifest,
            updatedSnapshot);
    }

    private async Task<MeetingSessionManifest> LoadOrCreateManifestAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(meeting.ManifestPath) && File.Exists(meeting.ManifestPath))
        {
            try
            {
                return await _manifestStore.LoadAsync(meeting.ManifestPath, cancellationToken);
            }
            catch
            {
                // Published transcript artifacts remain the source of truth for manual summary generation.
            }
        }

        return new MeetingSessionManifest
        {
            SessionId = meeting.Stem,
            Platform = meeting.Platform,
            DetectedTitle = meeting.Title,
            StartedAtUtc = meeting.StartedAtUtc,
            EndedAtUtc = meeting.Duration is { } duration ? meeting.StartedAtUtc + duration : null,
            State = meeting.ManifestState ?? SessionState.Published,
            ProjectName = meeting.ProjectName,
            KeyAttendees = meeting.KeyAttendees ?? Array.Empty<string>(),
            DetectedAudioSource = meeting.DetectedAudioSource,
            Attendees = meeting.Attendees,
            ProcessingMetadata = new MeetingProcessingMetadata(
                meeting.TranscriptionModelFileName ?? string.Empty,
                meeting.HasSpeakerLabels),
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, null),
            DiarizationStatus = meeting.HasSpeakerLabels
                ? new ProcessingStageStatus("diarization", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, null)
                : new ProcessingStageStatus("diarization", StageExecutionState.Skipped, DateTimeOffset.UtcNow, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.Succeeded, DateTimeOffset.UtcNow, null),
        };
    }

    private static ProcessingStageStatus NormalizeStatus(ProcessingStageStatus status)
    {
        return status with
        {
            StageName = "summarization",
        };
    }

    private static async Task<bool> TryUpdateJsonAsync(
        string? jsonPath,
        ProcessingStageStatus status,
        MeetingSummary? summary,
        bool clearSummaryWhenNotSucceeded,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath, cancellationToken)) as JsonObject;
            if (root is null)
            {
                return false;
            }

            root["summarizationStatus"] = JsonSerializer.SerializeToNode(status);
            if (summary is not null)
            {
                root["summary"] = BuildSummaryJson(summary);
            }
            else if (clearSummaryWhenNotSucceeded)
            {
                root["summary"] = null;
            }

            await File.WriteAllTextAsync(
                jsonPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonObject BuildSummaryJson(MeetingSummary summary)
    {
        return new JsonObject
        {
            ["overview"] = summary.Overview,
            ["keyPoints"] = BuildStringArray(summary.KeyPoints),
            ["decisions"] = BuildStringArray(summary.Decisions),
            ["actionItems"] = new JsonArray(summary.ActionItems.Select(actionItem => new JsonObject
            {
                ["text"] = actionItem.Text,
                ["owner"] = actionItem.Owner,
                ["dueDateText"] = actionItem.DueDateText,
            }).Cast<JsonNode?>().ToArray()),
            ["risksAndOpenQuestions"] = BuildStringArray(summary.RisksAndOpenQuestions),
            ["provider"] = new JsonObject
            {
                ["providerKind"] = summary.Provider.ProviderKind.ToString(),
                ["providerName"] = summary.Provider.ProviderName,
                ["model"] = summary.Provider.Model,
                ["fallbackUsed"] = summary.Provider.FallbackUsed,
            },
            ["generatedAtUtc"] = summary.GeneratedAtUtc,
            ["transcriptFingerprint"] = summary.TranscriptFingerprint,
        };
    }

    private static JsonArray BuildStringArray(IReadOnlyList<string> values)
    {
        return new JsonArray(values.Select(value => JsonValue.Create(value)).Cast<JsonNode?>().ToArray());
    }

    private static async Task<bool> TryUpdateMarkdownAsync(
        string? markdownPath,
        MeetingSummary summary,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return false;
        }

        try
        {
            var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
            var transcriptIndex = markdown.IndexOf("## Transcript", StringComparison.Ordinal);
            if (transcriptIndex < 0)
            {
                return false;
            }

            var summaryMarkdown = BuildSummaryMarkdown(summary);
            var existingSummaryIndex = markdown.IndexOf("## Summary", StringComparison.Ordinal);
            string updated;
            if (existingSummaryIndex >= 0 && existingSummaryIndex < transcriptIndex)
            {
                var beforeSummary = markdown[..existingSummaryIndex].TrimEnd();
                var transcript = markdown[transcriptIndex..].TrimStart();
                updated = string.Join(
                    Environment.NewLine,
                    beforeSummary,
                    string.Empty,
                    summaryMarkdown,
                    string.Empty,
                    transcript);
            }
            else
            {
                var beforeTranscript = markdown[..transcriptIndex].TrimEnd();
                var transcript = markdown[transcriptIndex..].TrimStart();
                updated = string.Join(
                    Environment.NewLine,
                    beforeTranscript,
                    string.Empty,
                    summaryMarkdown,
                    string.Empty,
                    transcript);
            }

            await File.WriteAllTextAsync(markdownPath, updated, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSummaryMarkdown(MeetingSummary summary)
    {
        var lines = new List<string>
        {
            "## Summary",
            string.Empty,
            summary.Overview,
            string.Empty,
        };

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

        return string.Join(Environment.NewLine, lines).TrimEnd();
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

    private async Task<bool> TryUpdateManifestAsync(
        string? manifestPath,
        MeetingSessionManifest currentManifest,
        ProcessingStageStatus status,
        MeetingSummary summary,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var updated = currentManifest with
            {
                SummarizationStatus = status,
                Summary = summary,
            };
            await _manifestStore.SaveAsync(updated, manifestPath, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryUpdateSnapshotAsync(
        string? manifestPath,
        MeetingSummary summary,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            var sessionRoot = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(sessionRoot))
            {
                return false;
            }

            var processingRoot = Path.Combine(sessionRoot, "processing");
            Directory.CreateDirectory(processingRoot);
            var snapshotPath = Path.Combine(processingRoot, "summary.snapshot.json");
            await using var stream = File.Create(snapshotPath);
            await JsonSerializer.SerializeAsync(stream, summary, SnapshotSerializerOptions, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSafeFailureMessage(Exception exception)
    {
        return exception is HttpRequestException or TimeoutException
            ? exception.Message
            : "Summary generation failed before a usable summary was returned.";
    }
}

public sealed record PublishedMeetingSummaryUpdateResult(
    ProcessingStageStatus Status,
    MeetingSummary? Summary,
    bool UpdatedJson,
    bool UpdatedMarkdown,
    bool UpdatedManifest,
    bool UpdatedSnapshot);
