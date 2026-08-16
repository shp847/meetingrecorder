using MeetingRecorder.Core.Configuration;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

public enum SummarySecretKind
{
    ModelProxy = 0,
    OpenAi = 1,
}

public enum SummaryChatProviderKind
{
    ModelProxy = 0,
    OpenAi = 1,
}

public enum SummaryChatRole
{
    System = 0,
    User = 1,
    Assistant = 2,
}

public sealed record SummaryChatMessage(SummaryChatRole Role, string Content);

public sealed record SummaryChatRequest(
    string Model,
    IReadOnlyList<SummaryChatMessage> Messages,
    TimeSpan Timeout)
{
    public bool Stream { get; init; }

    public bool JsonOutput { get; init; }

    public SummaryReasoningEffort ReasoningEffort { get; init; } = SummaryReasoningEffort.ProviderDefault;
}

public sealed record ModelProxyRoutingInfo(
    string? RequestId,
    string? RequestedBackend,
    string? EffectiveBackend,
    string? WebSearchBackend,
    bool? AppServerWebSearchSupported,
    string? FallbackReason,
    int? StatusCode = null,
    TimeSpan? Latency = null);

public sealed class ModelProxyRequestException : HttpRequestException
{
    public ModelProxyRequestException(
        string message,
        System.Net.HttpStatusCode statusCode,
        bool requiresCatalogRefresh,
        ModelProxyRoutingInfo? routing)
        : base(message, null, statusCode)
    {
        RequiresCatalogRefresh = requiresCatalogRefresh;
        Routing = routing;
    }

    public bool RequiresCatalogRefresh { get; }

    public ModelProxyRoutingInfo? Routing { get; }
}

public sealed record SummaryChatResponse(
    string Content,
    string ProviderName,
    string Model)
{
    public SummaryChatProviderKind ProviderKind { get; init; }

    public ModelProxyRoutingInfo? ModelProxyRouting { get; init; }
}

public sealed record SummaryChatProviderOptions(
    SummaryChatProviderKind ProviderKind,
    string ProviderName,
    string BaseUrl,
    string ApiKey)
{
    public static SummaryChatProviderOptions ForModelProxy(
        string apiKey = MeetingSummaryDefaults.ModelProxyLocalApiKey,
        string baseUrl = MeetingSummaryDefaults.ModelProxyBaseUrl,
        string? backend = null,
        bool webSearchEnabled = false)
    {
        var normalizedBackend = string.IsNullOrWhiteSpace(backend)
            ? "codex"
            : backend.Trim();
        return new SummaryChatProviderOptions(
            SummaryChatProviderKind.ModelProxy,
            "ModelProxy",
            baseUrl,
            apiKey)
        {
            ModelProxyBackend = normalizedBackend,
            ModelProxyWebSearchEnabled = webSearchEnabled,
        };
    }

    public static SummaryChatProviderOptions ForOpenAi(
        string apiKey,
        string baseUrl = "https://api.openai.com/v1")
    {
        return new SummaryChatProviderOptions(
            SummaryChatProviderKind.OpenAi,
            "OpenAI",
            baseUrl,
            apiKey);
    }

    public string? ModelProxyBackend { get; init; }

    public bool ModelProxyWebSearchEnabled { get; init; }

}

public sealed record ModelProxyModelCatalog(
    IReadOnlyList<ModelProxyModelInfo> Models,
    string? DefaultModel = null,
    string? CatalogState = null,
    string? CatalogSource = null,
    string? AudioCapability = null)
{
    public string ResolveModel(string? projectSpecificOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(projectSpecificOverride))
        {
            return projectSpecificOverride.Trim();
        }

        return Models.FirstOrDefault(model => model.IsDefault)?.Id
            ?? DefaultModel?.Trim()
            ?? throw new InvalidOperationException("ModelProxy did not advertise a default text model.");
    }

    public bool ContainsModel(string modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
               Models.Any(model => string.Equals(model.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ModelProxyModelInfo(
    string Id,
    string? ObjectType,
    long? Created,
    string? OwnedBy,
    bool IsDefault = false,
    IReadOnlyList<SummaryReasoningEffort>? SupportedReasoningEfforts = null);

internal sealed record ModelProxyAudioFallbackPlan(
    bool UseLocalFallback,
    bool RetryAsCloud,
    bool UploadAudio);

internal static class ModelProxyAudioContract
{
    public const string TranscriptionModel = "gpt-4o-transcribe";
    public const string DiarizedTranscriptionModel = "gpt-4o-transcribe-diarize";

    private static readonly HashSet<string> LocalFallbackErrorKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio_disabled",
        "unsupported_model",
        "backend_unavailable",
        "backend_busy",
        "timeout",
        "quota",
        "config_error",
        "protocol_mismatch",
        "protocol mismatch",
    };

    public static bool CanUseRemoteAudio(
        ModelProxyModelCatalog catalog,
        bool diarized)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        // Golden Consumer Contract v1 deliberately excludes all audio capabilities.
        return false;
    }

    public static ModelProxyAudioFallbackPlan BuildFallbackPlan(string? errorKind)
    {
        var normalized = errorKind?.Trim();
        var useLocalFallback = !string.IsNullOrWhiteSpace(normalized) &&
                               LocalFallbackErrorKinds.Contains(normalized);
        return new ModelProxyAudioFallbackPlan(
            UseLocalFallback: useLocalFallback,
            RetryAsCloud: false,
            UploadAudio: false);
    }
}

public interface ISummarySecretStore
{
    Task SaveAsync(
        SummarySecretKind kind,
        string secret,
        CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default);

    Task<bool> HasSecretAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default);
}

public interface ISummaryChatClient
{
    Task<SummaryChatResponse> CompleteAsync(
        SummaryChatProviderOptions providerOptions,
        SummaryChatRequest request,
        CancellationToken cancellationToken = default);
}

public interface IModelProxyModelCatalogClient
{
    Task<ModelProxyModelCatalog> GetModelsAsync(
        SummaryChatProviderOptions providerOptions,
        CancellationToken cancellationToken = default);
}

public sealed class FileSummarySecretStore : ISummarySecretStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeetingRecorder.SummaryProviderSecrets.v1");

    private readonly string _secretPath;

    public FileSummarySecretStore(string secretPath)
    {
        _secretPath = secretPath;
    }

    public static FileSummarySecretStore CreateDefault()
    {
        return new FileSummarySecretStore(AppDataPaths.GetSummaryProviderSecretStorePath());
    }

    public async Task SaveAsync(
        SummarySecretKind kind,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Summary provider secret is required.", nameof(secret));
        }

        var document = await LoadDocumentAsync(cancellationToken);
        document.Secrets[GetLogicalSecretKey(kind)] = Protect(secret.Trim());
        await SaveDocumentAsync(document, cancellationToken);
    }

    public async Task<string?> LoadAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentAsync(cancellationToken);
        return document.Secrets.TryGetValue(GetLogicalSecretKey(kind), out var protectedSecret)
            ? Unprotect(protectedSecret)
            : null;
    }

    public async Task<bool> HasSecretAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentAsync(cancellationToken);
        return document.Secrets.ContainsKey(GetLogicalSecretKey(kind));
    }

    public async Task DeleteAsync(
        SummarySecretKind kind,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentAsync(cancellationToken);
        if (document.Secrets.Remove(GetLogicalSecretKey(kind)))
        {
            await SaveDocumentAsync(document, cancellationToken);
        }
    }

    private static string GetLogicalSecretKey(SummarySecretKind kind)
    {
        return kind switch
        {
            SummarySecretKind.ModelProxy => "summary:modelproxy",
            SummarySecretKind.OpenAi => "summary:openai",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown summary secret kind."),
        };
    }

    private async Task<SummarySecretDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_secretPath))
        {
            return new SummarySecretDocument(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        try
        {
            var json = await File.ReadAllTextAsync(_secretPath, cancellationToken);
            var document = JsonSerializer.Deserialize<SummarySecretDocument>(json, SerializerOptions);
            return document?.WithOrdinalKeys() ??
                new SummarySecretDocument(new Dictionary<string, string>(StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return new SummarySecretDocument(new Dictionary<string, string>(StringComparer.Ordinal));
        }
        catch (CryptographicException)
        {
            return new SummarySecretDocument(new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    private async Task SaveDocumentAsync(
        SummarySecretDocument document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_secretPath)
            ?? throw new InvalidOperationException("Secret store path must have a parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = _secretPath + ".tmp";
        var backupPath = _secretPath + ".bak";
        var json = JsonSerializer.Serialize(document.WithOrdinalKeys(), SerializerOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);

        if (File.Exists(_secretPath))
        {
            File.Replace(tempPath, _secretPath, backupPath, ignoreMetadataErrors: true);
            File.Delete(backupPath);
            return;
        }

        File.Move(tempPath, _secretPath);
    }

    private static string Protect(string secret)
    {
        var clearBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string protectedSecret)
    {
        var protectedBytes = Convert.FromBase64String(protectedSecret);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }

    private sealed record SummarySecretDocument(Dictionary<string, string> Secrets)
    {
        public SummarySecretDocument WithOrdinalKeys()
        {
            return new SummarySecretDocument(new Dictionary<string, string>(Secrets, StringComparer.Ordinal));
        }
    }
}

public sealed class SummaryChatClient : ISummaryChatClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MinimumModelProxyWebSearchTimeout = TimeSpan.FromSeconds(60);
    private const int MaxBackendBusyAttempts = 3;
    private static readonly TimeSpan BackendBusyRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _httpClient;

    public SummaryChatClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SummaryChatResponse> CompleteAsync(
        SummaryChatProviderOptions providerOptions,
        SummaryChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateProviderOptions(providerOptions);
        ValidateRequest(request);

        var effectiveTimeout = GetEffectiveTimeout(providerOptions, request.Timeout);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);
        var endpoint = new Uri(new Uri(NormalizeBaseUrl(providerOptions.BaseUrl)), "responses");
        var payload = BuildResponsesPayload(providerOptions, request);
        var json = JsonSerializer.Serialize(payload, SerializerOptions);

        try
        {
            var completionOption = request.Stream
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;
            for (var attempt = 1; attempt <= MaxBackendBusyAttempts; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                using var httpRequest = BuildResponsesRequest(providerOptions, endpoint, json);
                using var response = await _httpClient.SendAsync(httpRequest, completionOption, timeoutCts.Token);
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var modelProxyError = TryExtractModelProxyStructuredError(responseBody);
                    if (ShouldRetryModelProxyFailure(providerOptions, response.StatusCode, modelProxyError, attempt))
                    {
                        await Task.Delay(BackendBusyRetryDelay, timeoutCts.Token);
                        continue;
                    }

                    var failureMessage = BuildHttpFailureMessage(
                        providerOptions,
                        response,
                        responseBody,
                        modelProxyError,
                        IsBackendBusy(modelProxyError) && providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy);
                    if (providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy &&
                        RequiresCatalogRefresh(modelProxyError))
                    {
                        var routing = (ExtractModelProxyRoutingInfo(providerOptions.ProviderKind, response.Headers) ??
                                       new ModelProxyRoutingInfo(null, null, null, null, null, null)) with
                        {
                            StatusCode = (int)response.StatusCode,
                            Latency = stopwatch.Elapsed,
                        };
                        throw new ModelProxyRequestException(
                            failureMessage,
                            response.StatusCode,
                            requiresCatalogRefresh: true,
                            routing);
                    }

                    throw new HttpRequestException(failureMessage, null, response.StatusCode);
                }

                var content = request.Stream
                    ? ExtractStreamingAssistantContent(providerOptions.ProviderName, responseBody)
                    : ExtractAssistantContent(providerOptions.ProviderName, responseBody);
                var successRouting = ExtractModelProxyRoutingInfo(providerOptions.ProviderKind, response.Headers);
                return new SummaryChatResponse(content, providerOptions.ProviderName, request.Model.Trim())
                {
                    ProviderKind = providerOptions.ProviderKind,
                    ModelProxyRouting = successRouting is null
                        ? null
                        : successRouting with { StatusCode = (int)response.StatusCode, Latency = stopwatch.Elapsed },
                };
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{providerOptions.ProviderName} summary request timed out after {effectiveTimeout.TotalSeconds:0} seconds.");
        }

        throw new InvalidOperationException($"{providerOptions.ProviderName} summary request did not complete.");
    }

    private static SummaryResponseRequestPayload BuildResponsesPayload(
        SummaryChatProviderOptions providerOptions,
        SummaryChatRequest request)
    {
        var instructions = string.Join(
            Environment.NewLine + Environment.NewLine,
            request.Messages
                .Where(message => message.Role == SummaryChatRole.System)
                .Select(message => message.Content.Trim())
                .Where(content => content.Length > 0));
        var input = request.Messages
            .Where(message => message.Role != SummaryChatRole.System)
            .Select(message => new SummaryResponseInputMessagePayload(
                ConvertRole(message.Role),
                [new SummaryResponseInputTextPayload(message.Content)]))
            .ToArray();

        if (input.Length == 0)
        {
            throw new ArgumentException("At least one non-system summary chat message is required.", nameof(request));
        }

        return new SummaryResponseRequestPayload(request.Model.Trim(), input)
        {
            Instructions = instructions.Length == 0 ? null : instructions,
            Stream = request.Stream,
            Text = request.JsonOutput && providerOptions.ProviderKind != SummaryChatProviderKind.ModelProxy
                ? new SummaryResponseTextPayload(new SummaryResponseTextFormatPayload("json_object"))
                : null,
            Reasoning = request.ReasoningEffort == SummaryReasoningEffort.ProviderDefault
                ? null
                : new SummaryResponseReasoningPayload(ToReasoningEffortText(request.ReasoningEffort)),
        };
    }

    private static HttpRequestMessage BuildResponsesRequest(
        SummaryChatProviderOptions providerOptions,
        Uri endpoint,
        string json)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey.Trim());
        if (providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy)
        {
            httpRequest.Headers.TryAddWithoutValidation(
                "X-ModelProxy-Web-Search",
                providerOptions.ModelProxyWebSearchEnabled ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(providerOptions.ModelProxyBackend))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-ModelProxy-Backend", providerOptions.ModelProxyBackend);
            }

        }

        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static bool ShouldRetryModelProxyFailure(
        SummaryChatProviderOptions providerOptions,
        System.Net.HttpStatusCode statusCode,
        ModelProxyStructuredError? modelProxyError,
        int attempt)
    {
        return providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy &&
               attempt < MaxBackendBusyAttempts &&
               (IsBackendBusy(modelProxyError) ||
                ((statusCode == System.Net.HttpStatusCode.BadGateway || statusCode == System.Net.HttpStatusCode.ServiceUnavailable) &&
                 modelProxyError?.Retryable == true)) &&
               modelProxyError?.Retryable == true;
    }

    private static TimeSpan GetEffectiveTimeout(
        SummaryChatProviderOptions providerOptions,
        TimeSpan requestedTimeout)
    {
        return providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy &&
               providerOptions.ModelProxyWebSearchEnabled &&
               requestedTimeout < MinimumModelProxyWebSearchTimeout
            ? MinimumModelProxyWebSearchTimeout
            : requestedTimeout;
    }

    private static void ValidateProviderOptions(SummaryChatProviderOptions providerOptions)
    {
        if (string.IsNullOrWhiteSpace(providerOptions.BaseUrl))
        {
            throw new ArgumentException("Summary provider base URL is required.", nameof(providerOptions));
        }

        if (string.IsNullOrWhiteSpace(providerOptions.ApiKey))
        {
            throw new ArgumentException("Summary provider API key is required.", nameof(providerOptions));
        }
    }

    private static void ValidateRequest(SummaryChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("Summary model is required.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Summary request timeout must be positive.", nameof(request));
        }

        if (request.Messages.Count == 0 ||
            request.Messages.Any(message => string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new ArgumentException("At least one summary chat message is required.", nameof(request));
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/') + "/";
    }

    private static string ConvertRole(SummaryChatRole role)
    {
        return role switch
        {
            SummaryChatRole.System => "system",
            SummaryChatRole.User => "user",
            SummaryChatRole.Assistant => "assistant",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown summary chat role."),
        };
    }

    private static string ToReasoningEffortText(SummaryReasoningEffort effort)
    {
        return effort switch
        {
            SummaryReasoningEffort.Minimal => "minimal",
            SummaryReasoningEffort.Low => "low",
            SummaryReasoningEffort.Medium => "medium",
            SummaryReasoningEffort.High => "high",
            SummaryReasoningEffort.XHigh => "xhigh",
            _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "A concrete reasoning effort is required."),
        };
    }

    private static string ExtractAssistantContent(string providerName, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (TryExtractResponsesOutputText(document.RootElement, out var content))
        {
            return content;
        }

        if (TryExtractLegacyChatCompletionText(document.RootElement, out content))
        {
            return content;
        }

        throw new InvalidOperationException($"{providerName} response did not include assistant text.");
    }

    private static string ExtractStreamingAssistantContent(string providerName, string responseBody)
    {
        var builder = new StringBuilder();
        var eventName = "message";
        var dataLines = new List<string>();
        using var reader = new StringReader(responseBody);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmedLine = line.TrimStart();
            if (trimmedLine.Length == 0)
            {
                if (ProcessStreamingSseEvent(providerName, eventName, dataLines, builder))
                {
                    break;
                }

                eventName = "message";
                dataLines.Clear();
                continue;
            }

            if (trimmedLine.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmedLine.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = trimmedLine["event:".Length..].Trim();
                continue;
            }

            if (!trimmedLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = trimmedLine["data:".Length..].TrimStart();
            if (data.Length == 0)
            {
                continue;
            }

            dataLines.Add(data);
        }

        if (dataLines.Count > 0)
        {
            ProcessStreamingSseEvent(providerName, eventName, dataLines, builder);
        }

        return builder.ToString();
    }

    private static bool ProcessStreamingSseEvent(
        string providerName,
        string eventName,
        IReadOnlyList<string> dataLines,
        StringBuilder builder)
    {
        if (dataLines.Count == 0)
        {
            return false;
        }

        var data = string.Join("\n", dataLines).Trim();
        if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(BuildStreamingFailureMessage(providerName, data));
        }

        return ProcessStreamingChunk(providerName, data, builder);
    }

    private static string BuildStreamingFailureMessage(
        string providerName,
        string errorPayload)
    {
        var modelProxyError = TryExtractModelProxyStructuredError(errorPayload);
        var classificationMessage = BuildStructuredErrorClassificationMessage(
            SummaryChatProviderKind.ModelProxy,
            modelProxyError,
            backendBusyRetriesExhausted: false);
        var safeDetails = TryExtractSafeErrorDetails(errorPayload);
        var parts = new[] { $"{providerName} streaming request failed.", classificationMessage, safeDetails }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" ", parts);
    }

    private static bool ProcessStreamingChunk(
        string providerName,
        string json,
        StringBuilder builder)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                var eventType = typeElement.GetString();
                if (string.Equals(eventType, "response.output_text.delta", StringComparison.Ordinal) ||
                    string.Equals(eventType, "response.text.delta", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(deltaElement.GetString());
                    }

                    return false;
                }

                if (string.Equals(eventType, "response.output_text.done", StringComparison.Ordinal) ||
                    string.Equals(eventType, "response.text.done", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String &&
                        builder.Length == 0)
                    {
                        builder.Append(textElement.GetString());
                    }

                    return false;
                }

                if (string.Equals(eventType, "response.completed", StringComparison.Ordinal) ||
                    string.Equals(eventType, "response.done", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(eventType, "error", StringComparison.Ordinal) ||
                    string.Equals(eventType, "response.error", StringComparison.Ordinal))
                {
                    throw new HttpRequestException(BuildStreamingFailureMessage(providerName, json));
                }
            }

            if (TryExtractLegacyStreamingChunk(root, out var legacyChunk))
            {
                builder.Append(legacyChunk);
                return false;
            }

            return false;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{providerName} streaming response included a malformed assistant chunk.", exception);
        }
    }

    private static bool TryExtractResponsesOutputText(
        JsonElement root,
        out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!string.Equals(GetJsonString(item, "type"), "message", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetJsonString(item, "role"), "assistant", StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("content", out var contentItems) ||
                contentItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentItems.EnumerateArray())
            {
                if (string.Equals(GetJsonString(contentItem, "type"), "output_text", StringComparison.OrdinalIgnoreCase) &&
                    contentItem.TryGetProperty("text", out var textElement) &&
                    textElement.ValueKind == JsonValueKind.String)
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
            }
        }

        content = string.Concat(parts);
        return parts.Count > 0;
    }

    private static bool TryExtractLegacyChatCompletionText(
        JsonElement root,
        out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var textElement) ||
            textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        content = textElement.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryExtractLegacyStreamingChunk(
        JsonElement root,
        out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        if (firstChoice.TryGetProperty("delta", out var delta) &&
            delta.ValueKind == JsonValueKind.Object &&
            delta.TryGetProperty("content", out var deltaContent) &&
            deltaContent.ValueKind == JsonValueKind.String)
        {
            content = deltaContent.GetString() ?? string.Empty;
            return true;
        }

        if (firstChoice.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var messageContent) &&
            messageContent.ValueKind == JsonValueKind.String)
        {
            content = messageContent.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string BuildHttpFailureMessage(
        SummaryChatProviderOptions providerOptions,
        HttpResponseMessage response,
        string responseBody,
        ModelProxyStructuredError? modelProxyError = null,
        bool backendBusyRetriesExhausted = false)
    {
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : response.ReasonPhrase;
        var message = $"{providerOptions.ProviderName} returned HTTP {(int)response.StatusCode} {reason}.";
        modelProxyError ??= TryExtractModelProxyStructuredError(responseBody);
        var classificationMessage = BuildStructuredErrorClassificationMessage(
            providerOptions.ProviderKind,
            modelProxyError,
            backendBusyRetriesExhausted);
        var safeDetails = TryExtractSafeErrorDetails(responseBody);
        var routingInfo = ExtractModelProxyRoutingInfo(providerOptions.ProviderKind, response.Headers);
        var routingDetails = BuildSafeRoutingDetails(routingInfo);
        var parts = new[]
            {
                message,
                classificationMessage,
                routingDetails,
                safeDetails,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" ", parts);
    }

    private static string BuildStructuredErrorClassificationMessage(
        SummaryChatProviderKind providerKind,
        ModelProxyStructuredError? modelProxyError,
        bool backendBusyRetriesExhausted)
    {
        if (providerKind != SummaryChatProviderKind.ModelProxy || modelProxyError is null)
        {
            return string.Empty;
        }

        if (IsBackendBusy(modelProxyError))
        {
            return backendBusyRetriesExhausted
                ? "ModelProxy is temporarily saturated after retry attempts; retry shortly."
                : "ModelProxy is temporarily saturated; retry shortly.";
        }

        if (HasErrorKind(modelProxyError, "cli_timeout"))
        {
            return "ModelProxy request timed out inside the selected backend; retry shortly or simplify the request.";
        }

        if (HasErrorKind(modelProxyError, "unsupported_capability") ||
            ErrorKindContains(modelProxyError, "capability"))
        {
            return "ModelProxy rejected an unsupported capability; adjust the request shape or disable the unsupported feature.";
        }

        return string.Empty;
    }

    private static string BuildSafeRoutingDetails(ModelProxyRoutingInfo? routingInfo)
    {
        if (routingInfo is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddSafePart(parts, "Request", routingInfo.RequestId);
        AddSafePart(parts, "Requested backend", routingInfo.RequestedBackend);
        AddSafePart(parts, "Effective backend", routingInfo.EffectiveBackend);
        AddSafePart(parts, "Web search backend", routingInfo.WebSearchBackend);
        AddSafePart(parts, "Fallback reason", routingInfo.FallbackReason);
        return string.Join(" ", parts);
    }

    private static string TryExtractSafeErrorDetails(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (TryGetStructuredErrorObject(document.RootElement, out var error))
            {
                return BuildSafeErrorDetails(error);
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static ModelProxyStructuredError? TryExtractModelProxyStructuredError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (TryGetStructuredErrorObject(document.RootElement, out var error))
            {
                return BuildModelProxyStructuredError(error);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryGetStructuredErrorObject(
        JsonElement root,
        out JsonElement error)
    {
        if (root.TryGetProperty("detail", out var detail) &&
            detail.ValueKind == JsonValueKind.Object)
        {
            if (detail.TryGetProperty("error", out var nestedError) &&
                nestedError.ValueKind == JsonValueKind.Object)
            {
                error = nestedError;
                return true;
            }

            error = detail;
            return true;
        }

        if (root.TryGetProperty("error", out error) &&
            error.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        error = default;
        return false;
    }

    private static ModelProxyStructuredError BuildModelProxyStructuredError(JsonElement error)
    {
        return new ModelProxyStructuredError(
            GetSafeJsonString(error, "type"),
            GetSafeJsonString(error, "category"),
            GetSafeJsonString(error, "code"),
            GetSafeJsonBoolean(error, "web_search"),
            GetSafeJsonBoolean(error, "retryable"),
            GetSafeJsonString(error, "next_step"));
    }

    private static string? GetSafeJsonString(JsonElement source, string propertyName)
    {
        return source.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;
    }

    private static bool? GetSafeJsonBoolean(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.String &&
               bool.TryParse(property.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool IsBackendBusy(ModelProxyStructuredError? modelProxyError)
    {
        return HasErrorKind(modelProxyError, "backend_busy");
    }

    private static bool RequiresCatalogRefresh(ModelProxyStructuredError? modelProxyError)
    {
        return modelProxyError is not null &&
               (HasErrorKind(modelProxyError, "unsupported_model") ||
                ErrorKindContains(modelProxyError, "reasoning"));
    }

    private static bool HasErrorKind(ModelProxyStructuredError? modelProxyError, string expectedKind)
    {
        return string.Equals(modelProxyError?.Type, expectedKind, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(modelProxyError?.Category, expectedKind, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(modelProxyError?.Code, expectedKind, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ErrorKindContains(ModelProxyStructuredError modelProxyError, string text)
    {
        return Contains(modelProxyError.Type) ||
               Contains(modelProxyError.Category) ||
               Contains(modelProxyError.Code);

        bool Contains(string? value)
        {
            return value?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static string BuildSafeErrorDetails(JsonElement error)
    {
        var parts = new List<string>();
        AddSafeJsonProperty(parts, error, "type", "Type");
        AddSafeJsonProperty(parts, error, "category", "Category");
        AddSafeJsonProperty(parts, error, "code", "Code");
        AddSafeJsonProperty(parts, error, "backend", "Backend");
        AddSafeJsonProperty(parts, error, "requested_backend", "Requested backend");
        AddSafeJsonProperty(parts, error, "request_id", "Request");
        AddSafeJsonNumberProperty(parts, error, "elapsed_seconds", "Elapsed seconds");
        AddSafeJsonNumberProperty(parts, error, "timeout_seconds", "Timeout seconds");
        AddSafeJsonProperty(parts, error, "next_step", "Next step");
        return string.Join(" ", parts);
    }

    private static void AddSafeJsonProperty(
        ICollection<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (source.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            parts.Add($"{label}: {property.GetString()}.");
        }
    }

    private static void AddSafeJsonNumberProperty(
        ICollection<string> parts,
        JsonElement source,
        string propertyName,
        string label)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            parts.Add($"{label}: {property.GetRawText()}.");
            return;
        }

        if (property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            parts.Add($"{label}: {property.GetString()}.");
        }
    }

    private static void AddSafePart(
        ICollection<string> parts,
        string label,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value.Trim()}.");
        }
    }

    private static ModelProxyRoutingInfo? ExtractModelProxyRoutingInfo(
        SummaryChatProviderKind providerKind,
        HttpResponseHeaders headers)
    {
        if (providerKind != SummaryChatProviderKind.ModelProxy)
        {
            return null;
        }

        var routingInfo = new ModelProxyRoutingInfo(
            GetHeaderValue(headers, "X-ModelProxy-Request-Id"),
            GetHeaderValue(headers, "X-ModelProxy-Requested-Backend"),
            GetHeaderValue(headers, "X-ModelProxy-Effective-Backend"),
            GetHeaderValue(headers, "X-ModelProxy-Web-Search-Backend"),
            GetHeaderBoolean(headers, "X-ModelProxy-App-Server-Web-Search-Supported"),
            GetHeaderValue(headers, "X-ModelProxy-Fallback-Reason"));

        return routingInfo.RequestId is null &&
               routingInfo.RequestedBackend is null &&
               routingInfo.EffectiveBackend is null &&
               routingInfo.WebSearchBackend is null &&
               routingInfo.AppServerWebSearchSupported is null &&
               routingInfo.FallbackReason is null
            ? null
            : routingInfo;
    }

    private static string? GetHeaderValue(
        HttpResponseHeaders headers,
        string headerName)
    {
        return headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
            : null;
    }

    private static bool? GetHeaderBoolean(
        HttpResponseHeaders headers,
        string headerName)
    {
        var value = GetHeaderValue(headers, headerName);
        return bool.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private sealed record SummaryResponseRequestPayload(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<SummaryResponseInputMessagePayload> Input)
    {
        [JsonPropertyName("instructions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Instructions { get; init; }

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Stream { get; init; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SummaryResponseTextPayload? Text { get; init; }

        [JsonPropertyName("reasoning")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SummaryResponseReasoningPayload? Reasoning { get; init; }
    }

    private sealed record SummaryResponseInputMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<SummaryResponseInputTextPayload> Content);

    private sealed record SummaryResponseInputTextPayload(
        [property: JsonPropertyName("text")] string Text)
    {
        [JsonPropertyName("type")]
        public string Type { get; } = "input_text";
    }

    private sealed record SummaryResponseTextPayload(
        [property: JsonPropertyName("format")] SummaryResponseTextFormatPayload Format);

    private sealed record SummaryResponseReasoningPayload(
        [property: JsonPropertyName("effort")] string Effort);

    private sealed record SummaryResponseTextFormatPayload(
        [property: JsonPropertyName("type")] string Type);

    private sealed record ModelProxyStructuredError(
        string? Type,
        string? Category,
        string? Code,
        bool? WebSearch,
        bool? Retryable,
        string? NextStep);
}

public sealed class SummaryProviderValidationService
{
    public const string SyntheticValidationPrompt = "Reply exactly: summary-provider-ok";

    private readonly ISummaryChatClient _chatClient;
    private readonly IModelProxyModelCatalogClient? _modelCatalogClient;

    public SummaryProviderValidationService(
        ISummaryChatClient chatClient,
        IModelProxyModelCatalogClient? modelCatalogClient = null)
    {
        _chatClient = chatClient;
        _modelCatalogClient = modelCatalogClient;
    }

    public async Task<SummaryProviderValidationResult> ValidateModelProxyAsync(
        AppConfig config,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            string.IsNullOrWhiteSpace(apiKey) ? MeetingSummaryDefaults.ModelProxyLocalApiKey : apiKey,
            config.SummaryModelProxyBaseUrl);
        try
        {
            var catalog = _modelCatalogClient is null
                ? null
                : await _modelCatalogClient.GetModelsAsync(providerOptions, cancellationToken);
            var model = ResolveModelProxyModel(catalog, config.SummaryModelProxyModel);
            EnsureReasoningEffortSupported(catalog, model, config.SummaryReasoningEffort);
            var result = await ValidateAsync(
                providerOptions,
                BuildSyntheticRequest(model, config.SummaryRequestTimeoutSeconds, config.SummaryReasoningEffort),
                cancellationToken);
            return result with
            {
                ResolvedModel = model,
                CatalogState = catalog?.CatalogState,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or TimeoutException or ArgumentException)
        {
            return new SummaryProviderValidationResult(
                SummaryChatProviderKind.ModelProxy,
                IsConfigured: true,
                Success: false,
                StatusText: exception.Message,
                ResponseText: null);
        }
    }

    public Task<SummaryProviderValidationResult> ValidateOpenAiAsync(
        AppConfig config,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(SummaryProviderValidationResult.NotConfigured(
                SummaryChatProviderKind.OpenAi,
                "OpenAI key is not saved."));
        }

        var providerOptions = SummaryChatProviderOptions.ForOpenAi(apiKey);
        var request = BuildSyntheticRequest(config.SummaryOpenAiModel, config.SummaryRequestTimeoutSeconds, config.SummaryReasoningEffort);
        return ValidateAsync(providerOptions, request, cancellationToken);
    }

    private async Task<SummaryProviderValidationResult> ValidateAsync(
        SummaryChatProviderOptions providerOptions,
        SummaryChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _chatClient.CompleteAsync(providerOptions, request, cancellationToken);
            var success = string.Equals(
                response.Content.Trim(),
                "summary-provider-ok",
                StringComparison.OrdinalIgnoreCase);
            return new SummaryProviderValidationResult(
                providerOptions.ProviderKind,
                IsConfigured: true,
                Success: success,
                StatusText: success
                    ? $"{providerOptions.ProviderName} validation succeeded."
                    : $"{providerOptions.ProviderName} returned an unexpected validation response.",
                ResponseText: response.Content);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or TimeoutException or ArgumentException)
        {
            return new SummaryProviderValidationResult(
                providerOptions.ProviderKind,
                IsConfigured: true,
                Success: false,
                StatusText: exception.Message,
                ResponseText: null);
        }
    }

    private static string ResolveModelProxyModel(ModelProxyModelCatalog? catalog, string configuredModel)
    {
        if (catalog is not null)
        {
            return catalog.ResolveModel(configuredModel);
        }

        return string.IsNullOrWhiteSpace(configuredModel)
            ? MeetingSummaryDefaults.ModelProxyModel
            : configuredModel.Trim();
    }

    private static void EnsureReasoningEffortSupported(
        ModelProxyModelCatalog? catalog,
        string model,
        SummaryReasoningEffort effort)
    {
        if (catalog is null || effort == SummaryReasoningEffort.ProviderDefault)
        {
            return;
        }

        var supported = catalog.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, model, StringComparison.OrdinalIgnoreCase))?.SupportedReasoningEfforts;
        if (supported is null || !supported.Contains(effort))
        {
            throw new InvalidOperationException($"ModelProxy model '{model}' does not advertise {effort} reasoning effort.");
        }
    }

    private static SummaryChatRequest BuildSyntheticRequest(
        string model,
        int timeoutSeconds,
        SummaryReasoningEffort reasoningEffort = SummaryReasoningEffort.ProviderDefault)
    {
        return new SummaryChatRequest(
            model,
            [new SummaryChatMessage(SummaryChatRole.User, SyntheticValidationPrompt)],
            TimeSpan.FromSeconds(timeoutSeconds))
        {
            ReasoningEffort = reasoningEffort,
        };
    }
}

public sealed record SummaryProviderValidationResult(
    SummaryChatProviderKind ProviderKind,
    bool IsConfigured,
    bool Success,
    string StatusText,
    string? ResponseText)
{
    public string? ResolvedModel { get; init; }

    public string? CatalogState { get; init; }
    public static SummaryProviderValidationResult NotConfigured(
        SummaryChatProviderKind providerKind,
        string statusText)
    {
        return new SummaryProviderValidationResult(
            providerKind,
            IsConfigured: false,
            Success: false,
            statusText,
            ResponseText: null);
    }
}

public sealed class ModelProxyClient : IModelProxyModelCatalogClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SummaryChatClient _chatClient;
    private readonly HttpClient _httpClient;
    private readonly ModelProxyOptions _options;

    public ModelProxyClient(HttpClient httpClient, ModelProxyOptions? options = null)
    {
        _httpClient = httpClient;
        _chatClient = new SummaryChatClient(httpClient);
        _options = options ?? new ModelProxyOptions();
    }

    public async Task<ModelProxyModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            _options.ApiKey,
            _options.BaseUrl);
        return await GetModelsAsync(providerOptions, cancellationToken);
    }

    public async Task<ModelProxyModelCatalog> GetModelsAsync(
        SummaryChatProviderOptions providerOptions,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(NormalizeBaseUrl(providerOptions.BaseUrl)), "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey.Trim());
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                ? response.StatusCode.ToString()
                : response.ReasonPhrase;
            throw new HttpRequestException($"{providerOptions.ProviderName} returned HTTP {(int)response.StatusCode} {reason} while retrieving models.");
        }

        return ParseModelCatalog(responseBody);
    }

    public async Task<ModelProxyCompletionResult> CompleteSyntheticPromptAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Synthetic validation prompt is required.", nameof(prompt));
        }

        var request = new SummaryChatRequest(
            _options.Model,
            [new SummaryChatMessage(SummaryChatRole.User, prompt)],
            _options.Timeout);
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            _options.ApiKey,
            _options.BaseUrl);

        var response = await _chatClient.CompleteAsync(providerOptions, request, cancellationToken);
        return new ModelProxyCompletionResult(response.Content);
    }

    private static ModelProxyModelCatalog ParseModelCatalog(string responseBody)
    {
        var payload = JsonSerializer.Deserialize<ModelProxyModelsPayload>(responseBody, SerializerOptions)
            ?? throw new InvalidOperationException("ModelProxy models response was empty.");
        var models = (payload.Data ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new ModelProxyModelInfo(
                model.Id!.Trim(),
                string.IsNullOrWhiteSpace(model.ObjectType) ? null : model.ObjectType.Trim(),
                model.Created,
                string.IsNullOrWhiteSpace(model.OwnedBy) ? null : model.OwnedBy.Trim(),
                model.IsDefault == true,
                ParseReasoningEfforts(model.ReasoningEfforts)))
            .ToArray();

        return new ModelProxyModelCatalog(
            models,
            payload.DefaultModel,
            payload.ModelProxy?.CatalogState,
            payload.ModelProxy?.CatalogSource,
            payload.ModelProxy?.AudioCapability);
    }

    private static IReadOnlyList<SummaryReasoningEffort>? ParseReasoningEfforts(IReadOnlyList<string>? efforts)
    {
        if (efforts is null)
        {
            return null;
        }

        return efforts
            .Select(effort => effort.Trim().ToLowerInvariant())
            .Select(effort => effort switch
            {
                "minimal" => SummaryReasoningEffort.Minimal,
                "low" => SummaryReasoningEffort.Low,
                "medium" => SummaryReasoningEffort.Medium,
                "high" => SummaryReasoningEffort.High,
                "xhigh" => SummaryReasoningEffort.XHigh,
                _ => SummaryReasoningEffort.ProviderDefault,
            })
            .Where(effort => effort != SummaryReasoningEffort.ProviderDefault)
            .Distinct()
            .ToArray();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/') + "/";
    }

    private sealed record ModelProxyModelsPayload(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelProxyModelPayload>? Data,
        [property: JsonPropertyName("default_model")] string? DefaultModel,
        [property: JsonPropertyName("modelproxy")] ModelProxyCatalogMetadataPayload? ModelProxy);

    private sealed record ModelProxyCatalogMetadataPayload(
        [property: JsonPropertyName("catalog_state")] string? CatalogState,
        [property: JsonPropertyName("catalog_source")] string? CatalogSource,
        [property: JsonPropertyName("audio_capability")] string? AudioCapability);

    private sealed record ModelProxyModelPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("object")] string? ObjectType,
        [property: JsonPropertyName("created")] long? Created,
        [property: JsonPropertyName("owned_by")] string? OwnedBy,
        [property: JsonPropertyName("default")] bool? IsDefault,
        [property: JsonPropertyName("reasoning_efforts")] IReadOnlyList<string>? ReasoningEfforts);
}

public sealed record ModelProxyOptions
{
    public string BaseUrl { get; init; } = MeetingSummaryDefaults.ModelProxyBaseUrl;

    public string ApiKey { get; init; } = MeetingSummaryDefaults.ModelProxyLocalApiKey;

    public string Model { get; init; } = MeetingSummaryDefaults.ModelProxyModel;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(MeetingSummaryDefaults.RequestTimeoutSeconds);
}

public sealed record ModelProxyCompletionResult(string Content);
