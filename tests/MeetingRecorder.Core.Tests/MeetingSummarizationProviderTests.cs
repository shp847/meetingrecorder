using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingSummarizationProviderTests
{
    [Fact]
    public async Task SummarizeAsync_Skips_Disabled_Config_Without_Loading_Secrets_Or_Calling_Provider()
    {
        var secrets = new TrackingSummarySecretStore();
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        var result = await provider.SummarizeAsync(
            CreateRequest(new AppConfig { SummaryGenerationMode = MeetingSummaryGenerationMode.Disabled }),
            CancellationToken.None);

        Assert.Equal(StageExecutionState.Skipped, result.Status.State);
        Assert.Null(result.Summary);
        Assert.Equal(0, secrets.LoadCount);
        Assert.Empty(chatClient.Calls);
    }

    [Fact]
    public async Task SummarizeAsync_Falls_Back_To_OpenAi_When_ModelProxy_Fails()
    {
        var secrets = new TrackingSummarySecretStore
        {
            ModelProxySecret = "sk-modelproxy-test",
            OpenAiSecret = "sk-openai-test",
        };
        var chatClient = new FakeSummaryChatClient
        {
            OnComplete = call =>
            {
                if (call.ProviderOptions.ProviderKind == SummaryChatProviderKind.ModelProxy)
                {
                    throw new HttpRequestException("ModelProxy returned HTTP 503 Failure.");
                }

                return CreateChatResponse(call.ProviderOptions, call.Request, "OpenAI fallback summary.");
            },
        };
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        var result = await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
            }),
            CancellationToken.None);

        Assert.Equal(StageExecutionState.Succeeded, result.Status.State);
        Assert.NotNull(result.Summary);
        Assert.Equal(SummaryChatProviderKind.OpenAi, result.Summary.Provider.ProviderKind);
        Assert.True(result.Summary.Provider.FallbackUsed);
        Assert.Equal(
            [SummaryChatProviderKind.ModelProxy, SummaryChatProviderKind.OpenAi],
            chatClient.Calls.Select(call => call.ProviderOptions.ProviderKind).ToArray());
    }

    [Fact]
    public async Task SummarizeAsync_Skips_Enabled_Config_When_Selected_Provider_Has_No_Saved_Key()
    {
        var secrets = new TrackingSummarySecretStore();
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        var result = await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
            }),
            CancellationToken.None);

        Assert.Equal(StageExecutionState.Skipped, result.Status.State);
        Assert.Contains("No summary provider configured", result.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(chatClient.Calls);
    }

    [Fact]
    public async Task SummarizeAsync_Fails_Malformed_Json_Without_Leaking_Raw_Response_Content()
    {
        var secrets = new TrackingSummarySecretStore { OpenAiSecret = "sk-openai-test" };
        var chatClient = new FakeSummaryChatClient
        {
            OnComplete = call => new SummaryChatResponse(
                "not json with transcript secret and sk-openai-test",
                call.ProviderOptions.ProviderName,
                call.Request.Model)
            {
                ProviderKind = call.ProviderOptions.ProviderKind,
            },
        };
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        var result = await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
            }),
            CancellationToken.None);

        Assert.Equal(StageExecutionState.Failed, result.Status.State);
        Assert.Null(result.Summary);
        Assert.Contains("valid summary JSON", result.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript secret", result.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-openai-test", result.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SummarizeAsync_Chunks_Long_Transcripts_And_Combines_Chunk_Summaries()
    {
        var secrets = new TrackingSummarySecretStore { OpenAiSecret = "sk-openai-test" };
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);
        var longSegments = Enumerable.Range(0, 8)
            .Select(index => new TranscriptSegment(
                TimeSpan.FromSeconds(index),
                TimeSpan.FromSeconds(index + 1),
                $"speaker_{index % 2}",
                null,
                $"segment {index} " + new string('x', 80)))
            .ToArray();

        var result = await provider.SummarizeAsync(
            new MeetingSummaryRequest(
                CreateManifest(),
                longSegments,
                new AppConfig
                {
                    SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                    SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
                    SummaryTranscriptChunkTokenTarget = 20,
                    SummaryTranscriptChunkOverlapTokens = 2,
                }),
            CancellationToken.None);

        Assert.Equal(StageExecutionState.Succeeded, result.Status.State);
        Assert.True(chatClient.Calls.Count > 1);
        Assert.Contains(
            chatClient.Calls,
            call => call.Request.Messages.Any(message =>
                message.Content.Contains("Combine these partial meeting summaries", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SummarizeAsync_Prompt_Contains_Transcript_But_Not_Synthetic_Validation_Text()
    {
        var secrets = new TrackingSummarySecretStore { ModelProxySecret = "sk-modelproxy-test" };
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalOnly,
            }),
            CancellationToken.None);

        var body = string.Join(
            "\n",
            chatClient.Calls.SelectMany(call => call.Request.Messages.Select(message => message.Content)));
        Assert.Contains("hello launch transcript", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SummaryProviderValidationService.SyntheticValidationPrompt, body, StringComparison.Ordinal);
        Assert.DoesNotContain("summary-provider-ok", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SummarizeAsync_Uses_ModelProxy_Minimum_Timeout_For_Transcript_Summaries()
    {
        var secrets = new TrackingSummarySecretStore { ModelProxySecret = "sk-modelproxy-test" };
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.LocalOnly,
                SummaryRequestTimeoutSeconds = 120,
            }),
            CancellationToken.None);

        var call = Assert.Single(chatClient.Calls);
        Assert.Equal(SummaryChatProviderKind.ModelProxy, call.ProviderOptions.ProviderKind);
        Assert.Equal(
            TimeSpan.FromSeconds(MeetingSummaryDefaults.MinimumModelProxySummaryRequestTimeoutSeconds),
            call.Request.Timeout);
    }

    [Fact]
    public async Task SummarizeAsync_Keeps_Configured_Timeout_For_OpenAi_Summaries()
    {
        var secrets = new TrackingSummarySecretStore { OpenAiSecret = "sk-openai-test" };
        var chatClient = new FakeSummaryChatClient();
        var provider = new MeetingSummarizationProvider(secrets, chatClient);

        await provider.SummarizeAsync(
            CreateRequest(new AppConfig
            {
                SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
                SummaryProviderPreference = MeetingSummaryProviderPreference.OpenAiOnly,
                SummaryRequestTimeoutSeconds = 90,
            }),
            CancellationToken.None);

        var call = Assert.Single(chatClient.Calls);
        Assert.Equal(SummaryChatProviderKind.OpenAi, call.ProviderOptions.ProviderKind);
        Assert.Equal(TimeSpan.FromSeconds(90), call.Request.Timeout);
    }

    private static MeetingSummaryRequest CreateRequest(AppConfig config)
    {
        return new MeetingSummaryRequest(
            CreateManifest(),
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "speaker_00", "Pranav", "hello launch transcript")],
            config);
    }

    private static MeetingSessionManifest CreateManifest()
    {
        return new MeetingSessionManifest
        {
            SessionId = "session-1",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Launch Sync",
            StartedAtUtc = DateTimeOffset.Parse("2026-05-22T14:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            State = SessionState.Processing,
        };
    }

    private static SummaryChatResponse CreateChatResponse(
        SummaryChatProviderOptions providerOptions,
        SummaryChatRequest request,
        string overview)
    {
        return new SummaryChatResponse(CreateSummaryJson(overview), providerOptions.ProviderName, request.Model)
        {
            ProviderKind = providerOptions.ProviderKind,
        };
    }

    private static string CreateSummaryJson(string overview)
    {
        return $$"""
        {
          "overview": "{{overview}}",
          "keyPoints": ["Launch remains on track."],
          "decisions": ["Proceed with the pilot."],
          "actionItems": [
            {
              "text": "Send pilot checklist.",
              "owner": "Pranav",
              "dueDateText": "Friday"
            }
          ],
          "risksAndOpenQuestions": ["Confirm legal review timing."]
        }
        """;
    }

    private sealed class TrackingSummarySecretStore : ISummarySecretStore
    {
        public string? ModelProxySecret { get; init; }

        public string? OpenAiSecret { get; init; }

        public int LoadCount { get; private set; }

        public Task SaveAsync(SummarySecretKind kind, string secret, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string?> LoadAsync(SummarySecretKind kind, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(kind switch
            {
                SummarySecretKind.ModelProxy => ModelProxySecret,
                SummarySecretKind.OpenAi => OpenAiSecret,
                _ => null,
            });
        }

        public async Task<bool> HasSecretAsync(SummarySecretKind kind, CancellationToken cancellationToken = default)
        {
            return !string.IsNullOrWhiteSpace(await LoadAsync(kind, cancellationToken));
        }

        public Task DeleteAsync(SummarySecretKind kind, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSummaryChatClient : ISummaryChatClient
    {
        public List<SummaryChatCall> Calls { get; } = [];

        public Func<SummaryChatCall, SummaryChatResponse>? OnComplete { get; init; }

        public Task<SummaryChatResponse> CompleteAsync(
            SummaryChatProviderOptions providerOptions,
            SummaryChatRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = new SummaryChatCall(providerOptions, request);
            Calls.Add(call);
            var response = OnComplete?.Invoke(call) ?? CreateChatResponse(providerOptions, request, "Summary generated.");
            return Task.FromResult(response);
        }
    }

    private sealed record SummaryChatCall(
        SummaryChatProviderOptions ProviderOptions,
        SummaryChatRequest Request);
}
