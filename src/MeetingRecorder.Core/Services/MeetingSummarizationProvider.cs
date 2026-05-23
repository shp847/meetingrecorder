using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public interface IMeetingSummarizationProvider
{
    Task<MeetingSummaryResult> SummarizeAsync(
        MeetingSummaryRequest request,
        CancellationToken cancellationToken);
}

public sealed record MeetingSummaryRequest(
    MeetingSessionManifest Manifest,
    IReadOnlyList<TranscriptSegment> Segments,
    AppConfig Config);

public sealed record MeetingSummaryResult(
    ProcessingStageStatus Status,
    MeetingSummary? Summary);

public sealed class NoOpMeetingSummarizationProvider : IMeetingSummarizationProvider
{
    public static NoOpMeetingSummarizationProvider Instance { get; } = new();

    private NoOpMeetingSummarizationProvider()
    {
    }

    public Task<MeetingSummaryResult> SummarizeAsync(
        MeetingSummaryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MeetingSummaryResult(
            new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Skipped,
                DateTimeOffset.UtcNow,
                "Summary generation disabled."),
            null));
    }
}

public sealed class MeetingSummarizationProvider : IMeetingSummarizationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ISummarySecretStore _secretStore;
    private readonly ISummaryChatClient _chatClient;

    public MeetingSummarizationProvider(
        ISummarySecretStore secretStore,
        ISummaryChatClient chatClient)
    {
        _secretStore = secretStore;
        _chatClient = chatClient;
    }

    public async Task<MeetingSummaryResult> SummarizeAsync(
        MeetingSummaryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Config.SummaryGenerationMode != MeetingSummaryGenerationMode.Enabled)
        {
            return Skipped("Summary generation disabled.");
        }

        if (request.Segments.Count == 0)
        {
            return Skipped("Summary generation skipped because no transcript segments were available.");
        }

        var candidates = await ResolveProviderCandidatesAsync(request.Config, cancellationToken);
        if (candidates.Count == 0)
        {
            return Skipped("No summary provider configured for the selected preference.");
        }

        var fingerprint = MeetingSummaryTranscriptFingerprint.Compute(request.Segments);
        var chunks = SummaryTranscriptChunker.BuildChunks(
            request.Segments,
            request.Config.SummaryTranscriptChunkTokenTarget,
            request.Config.SummaryTranscriptChunkOverlapTokens);
        var failureMessages = new List<string>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var content = chunks.Count == 1
                    ? await SummarizeSingleChunkAsync(candidate, chunks[0], request.Config, cancellationToken)
                    : await SummarizeAndCombineChunksAsync(candidate, chunks, request.Config, cancellationToken);
                var summary = new MeetingSummary(
                    content.Overview,
                    content.KeyPoints,
                    content.Decisions,
                    content.ActionItems,
                    content.RisksAndOpenQuestions,
                    new MeetingSummaryProviderInfo(
                        candidate.ProviderOptions.ProviderKind,
                        candidate.ProviderOptions.ProviderName,
                        candidate.Model,
                        candidate.FallbackUsed),
                    DateTimeOffset.UtcNow,
                    fingerprint);
                return new MeetingSummaryResult(
                    new ProcessingStageStatus(
                        "summarization",
                        StageExecutionState.Succeeded,
                        DateTimeOffset.UtcNow,
                        "Summary generated."),
                    summary);
            }
            catch (Exception exception) when (IsProviderFailure(exception))
            {
                failureMessages.Add(BuildSafeFailureMessage(candidate.ProviderOptions.ProviderName, exception));
            }
        }

        return new MeetingSummaryResult(
            new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Failed,
                DateTimeOffset.UtcNow,
                failureMessages.Count == 0
                    ? "Summary generation failed."
                    : failureMessages[^1]),
            null);
    }

    private async Task<IReadOnlyList<ProviderCandidate>> ResolveProviderCandidatesAsync(
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ProviderCandidate>();

        async Task AddModelProxyAsync()
        {
            var secret = await _secretStore.LoadAsync(SummarySecretKind.ModelProxy, cancellationToken);
            var apiKey = string.IsNullOrWhiteSpace(secret)
                ? MeetingSummaryDefaults.ModelProxyLocalApiKey
                : secret;

            candidates.Add(new ProviderCandidate(
                SummaryChatProviderOptions.ForModelProxy(
                    apiKey,
                    config.SummaryModelProxyBaseUrl),
                config.SummaryModelProxyModel,
                FallbackUsed: false));
        }

        async Task AddOpenAiAsync(bool fallbackUsed)
        {
            var secret = await _secretStore.LoadAsync(SummarySecretKind.OpenAi, cancellationToken);
            if (string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            candidates.Add(new ProviderCandidate(
                SummaryChatProviderOptions.ForOpenAi(secret),
                config.SummaryOpenAiModel,
                fallbackUsed));
        }

        switch (config.SummaryProviderPreference)
        {
            case MeetingSummaryProviderPreference.LocalOnly:
                await AddModelProxyAsync();
                break;
            case MeetingSummaryProviderPreference.OpenAiOnly:
                await AddOpenAiAsync(fallbackUsed: false);
                break;
            case MeetingSummaryProviderPreference.LocalThenOpenAi:
                await AddModelProxyAsync();
                await AddOpenAiAsync(fallbackUsed: candidates.Count > 0);
                break;
            default:
                await AddModelProxyAsync();
                await AddOpenAiAsync(fallbackUsed: candidates.Count > 0);
                break;
        }

        return candidates;
    }

    private async Task<MeetingSummaryContent> SummarizeSingleChunkAsync(
        ProviderCandidate candidate,
        string transcriptChunk,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = GetSummaryRequestTimeoutSeconds(candidate, config);
        var response = await _chatClient.CompleteAsync(
            candidate.ProviderOptions,
            BuildSummaryRequest(candidate.Model, transcriptChunk, timeoutSeconds),
            cancellationToken);
        return ParseSummaryContent(candidate.ProviderOptions.ProviderName, response.Content);
    }

    private async Task<MeetingSummaryContent> SummarizeAndCombineChunksAsync(
        ProviderCandidate candidate,
        IReadOnlyList<string> chunks,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var partialSummaries = new List<MeetingSummaryContent>(chunks.Count);
        var timeoutSeconds = GetSummaryRequestTimeoutSeconds(candidate, config);
        for (var index = 0; index < chunks.Count; index++)
        {
            var response = await _chatClient.CompleteAsync(
                candidate.ProviderOptions,
                BuildSummaryRequest(
                    candidate.Model,
                    $"Transcript chunk {index + 1} of {chunks.Count}:{Environment.NewLine}{chunks[index]}",
                    timeoutSeconds),
                cancellationToken);
            partialSummaries.Add(ParseSummaryContent(candidate.ProviderOptions.ProviderName, response.Content));
        }

        var combinedInput = JsonSerializer.Serialize(partialSummaries, JsonOptions);
        var combinePrompt = $$"""
        Combine these partial meeting summaries into one final meeting summary.
        Return strict JSON only using this schema:
        {
          "overview": "short paragraph",
          "keyPoints": ["point"],
          "decisions": ["decision"],
          "actionItems": [{"text": "action", "owner": "name or null", "dueDateText": "date or null"}],
          "risksAndOpenQuestions": ["risk or open question"]
        }

        Partial summaries:
        {{combinedInput}}
        """;
        var combineResponse = await _chatClient.CompleteAsync(
            candidate.ProviderOptions,
            new SummaryChatRequest(
                candidate.Model,
                [
                    new SummaryChatMessage(SummaryChatRole.System, BuildSystemPrompt()),
                    new SummaryChatMessage(SummaryChatRole.User, combinePrompt),
                ],
                TimeSpan.FromSeconds(timeoutSeconds)),
            cancellationToken);
        return ParseSummaryContent(candidate.ProviderOptions.ProviderName, combineResponse.Content);
    }

    private static int GetSummaryRequestTimeoutSeconds(
        ProviderCandidate candidate,
        AppConfig config)
    {
        var configuredTimeoutSeconds = Math.Max(1, config.SummaryRequestTimeoutSeconds);
        return candidate.ProviderOptions.ProviderKind == SummaryChatProviderKind.ModelProxy
            ? Math.Max(
                configuredTimeoutSeconds,
                MeetingSummaryDefaults.MinimumModelProxySummaryRequestTimeoutSeconds)
            : configuredTimeoutSeconds;
    }

    private static SummaryChatRequest BuildSummaryRequest(
        string model,
        string transcriptText,
        int timeoutSeconds)
    {
        var prompt = $$"""
        Summarize this speaker-labeled transcript. Use only the transcript content below.
        Return strict JSON only using this schema:
        {
          "overview": "short paragraph",
          "keyPoints": ["point"],
          "decisions": ["decision"],
          "actionItems": [{"text": "action", "owner": "name or null", "dueDateText": "date or null"}],
          "risksAndOpenQuestions": ["risk or open question"]
        }

        Transcript:
        {{transcriptText}}
        """;
        return new SummaryChatRequest(
            model,
            [
                new SummaryChatMessage(SummaryChatRole.System, BuildSystemPrompt()),
                new SummaryChatMessage(SummaryChatRole.User, prompt),
            ],
            TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
    }

    private static string BuildSystemPrompt()
    {
        return "You create concise meeting summaries and return only valid JSON. Do not invent facts beyond the supplied content.";
    }

    private static MeetingSummaryContent ParseSummaryContent(string providerName, string responseContent)
    {
        try
        {
            var normalized = StripJsonFence(responseContent);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            return new MeetingSummaryContent(
                GetRequiredString(root, "overview", providerName),
                GetStringArray(root, "keyPoints"),
                GetStringArray(root, "decisions"),
                GetActionItems(root),
                GetStringArray(root, "risksAndOpenQuestions"));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{providerName} returned summary content that was not valid summary JSON.", exception);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    private static string StripJsonFence(string responseContent)
    {
        var trimmed = responseContent.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineBreak = trimmed.IndexOf('\n');
        if (firstLineBreak < 0)
        {
            return trimmed;
        }

        var withoutOpeningFence = trimmed[(firstLineBreak + 1)..].Trim();
        return withoutOpeningFence.EndsWith("```", StringComparison.Ordinal)
            ? withoutOpeningFence[..^3].Trim()
            : withoutOpeningFence;
    }

    private static string GetRequiredString(JsonElement root, string propertyName, string providerName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException($"{providerName} summary JSON did not include '{propertyName}'.");
        }

        return property.GetString()!.Trim();
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!.Trim())
            .ToArray();
    }

    private static IReadOnlyList<MeetingSummaryActionItem> GetActionItems(JsonElement root)
    {
        if (!root.TryGetProperty("actionItems", out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MeetingSummaryActionItem>();
        }

        var actionItems = new List<MeetingSummaryActionItem>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(textElement.GetString()))
            {
                continue;
            }

            actionItems.Add(new MeetingSummaryActionItem(
                textElement.GetString()!.Trim(),
                GetOptionalString(item, "owner"),
                GetOptionalString(item, "dueDateText")));
        }

        return actionItems;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;
    }

    private static bool IsProviderFailure(Exception exception)
    {
        return exception is HttpRequestException or TimeoutException or InvalidOperationException or JsonException or ArgumentException;
    }

    private static string BuildSafeFailureMessage(string providerName, Exception exception)
    {
        if (exception is InvalidOperationException invalidOperationException &&
            invalidOperationException.InnerException is JsonException)
        {
            return $"{providerName} returned summary content that was not valid summary JSON.";
        }

        return exception is HttpRequestException or TimeoutException
            ? exception.Message
            : $"{providerName} summary generation failed before a usable summary was returned.";
    }

    private static MeetingSummaryResult Skipped(string message)
    {
        return new MeetingSummaryResult(
            new ProcessingStageStatus(
                "summarization",
                StageExecutionState.Skipped,
                DateTimeOffset.UtcNow,
                message),
            null);
    }

    private sealed record ProviderCandidate(
        SummaryChatProviderOptions ProviderOptions,
        string Model,
        bool FallbackUsed);

    private sealed record MeetingSummaryContent(
        string Overview,
        IReadOnlyList<string> KeyPoints,
        IReadOnlyList<string> Decisions,
        IReadOnlyList<MeetingSummaryActionItem> ActionItems,
        IReadOnlyList<string> RisksAndOpenQuestions);
}

public static class MeetingSummaryTranscriptFingerprint
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(IReadOnlyList<TranscriptSegment> segments)
    {
        var payload = segments.Select(segment => new FingerprintSegment(
            segment.Start.ToString("c", CultureInfo.InvariantCulture),
            segment.End.ToString("c", CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(segment.SpeakerId) ? null : segment.SpeakerId.Trim(),
            string.IsNullOrWhiteSpace(segment.SpeakerLabel) ? null : segment.SpeakerLabel.Trim(),
            segment.Text.Trim())).ToArray();
        var json = JsonSerializer.Serialize(payload, FingerprintJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record FingerprintSegment(
        string Start,
        string End,
        string? SpeakerId,
        string? SpeakerLabel,
        string Text);
}

internal static class SummaryTranscriptChunker
{
    public static IReadOnlyList<string> BuildChunks(
        IReadOnlyList<TranscriptSegment> segments,
        int chunkTokenTarget,
        int overlapTokens)
    {
        var charBudget = Math.Max(64, chunkTokenTarget * 4);
        var overlapBudget = Math.Min(Math.Max(0, overlapTokens * 4), charBudget / 2);
        var lines = segments
            .SelectMany(segment => SplitLine(FormatSegment(segment), charBudget))
            .ToArray();
        if (lines.Length == 0)
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>();
        var current = new List<string>();
        var currentLength = 0;
        foreach (var line in lines)
        {
            var separatorLength = current.Count == 0 ? 0 : Environment.NewLine.Length;
            if (current.Count > 0 && currentLength + separatorLength + line.Length > charBudget)
            {
                chunks.Add(string.Join(Environment.NewLine, current));
                current = TakeOverlap(current, overlapBudget).ToList();
                currentLength = current.Sum(item => item.Length) +
                                Math.Max(0, current.Count - 1) * Environment.NewLine.Length;
            }

            current.Add(line);
            currentLength += (current.Count == 1 ? 0 : Environment.NewLine.Length) + line.Length;
        }

        if (current.Count > 0)
        {
            chunks.Add(string.Join(Environment.NewLine, current));
        }

        return chunks;
    }

    private static string FormatSegment(TranscriptSegment segment)
    {
        var speaker = !string.IsNullOrWhiteSpace(segment.SpeakerLabel)
            ? segment.SpeakerLabel.Trim()
            : !string.IsNullOrWhiteSpace(segment.SpeakerId)
                ? segment.SpeakerId.Trim()
                : "Speaker";
        return $"[{segment.Start:hh\\:mm\\:ss} - {segment.End:hh\\:mm\\:ss}] {speaker}: {segment.Text.Trim()}";
    }

    private static IReadOnlyList<string> SplitLine(string line, int charBudget)
    {
        if (line.Length <= charBudget)
        {
            return [line];
        }

        var parts = new List<string>();
        var index = 0;
        while (index < line.Length)
        {
            var length = Math.Min(charBudget, line.Length - index);
            if (index + length < line.Length)
            {
                var lastWhitespace = line.LastIndexOf(' ', index + length - 1, length);
                if (lastWhitespace > index + (charBudget / 2))
                {
                    length = lastWhitespace - index;
                }
            }

            parts.Add(line.Substring(index, length).Trim());
            index += length;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }
        }

        return parts;
    }

    private static IEnumerable<string> TakeOverlap(IReadOnlyList<string> lines, int overlapBudget)
    {
        if (overlapBudget <= 0)
        {
            return Array.Empty<string>();
        }

        var selected = new Stack<string>();
        var length = 0;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var line = lines[index];
            var nextLength = length + line.Length + (selected.Count == 0 ? 0 : Environment.NewLine.Length);
            if (selected.Count > 0 && nextLength > overlapBudget)
            {
                break;
            }

            selected.Push(line);
            length = nextLength;
        }

        return selected;
    }
}
