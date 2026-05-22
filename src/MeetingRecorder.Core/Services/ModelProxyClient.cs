using MeetingRecorder.Core.Configuration;
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
}

public sealed record ModelProxyRoutingInfo(
    string? RequestId,
    string? RequestedBackend,
    string? EffectiveBackend,
    string? WebSearchBackend,
    bool? AppServerWebSearchSupported,
    string? FallbackReason);

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
        return new SummaryChatProviderOptions(
            SummaryChatProviderKind.ModelProxy,
            "ModelProxy",
            baseUrl,
            apiKey)
        {
            ModelProxyBackend = string.IsNullOrWhiteSpace(backend) ? null : backend.Trim(),
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
        var endpoint = new Uri(new Uri(NormalizeBaseUrl(providerOptions.BaseUrl)), "chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
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

        var payload = new SummaryChatCompletionPayload(
            request.Model.Trim(),
            request.Messages.Select(message => new SummaryChatCompletionMessagePayload(
                ConvertRole(message.Role),
                message.Content)).ToArray())
        {
            Stream = request.Stream,
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var completionOption = request.Stream
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;
            using var response = await _httpClient.SendAsync(httpRequest, completionOption, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(BuildHttpFailureMessage(providerOptions, response, responseBody));
            }

            var content = request.Stream
                ? ExtractStreamingAssistantContent(providerOptions.ProviderName, responseBody)
                : ExtractAssistantContent(providerOptions.ProviderName, responseBody);
            return new SummaryChatResponse(content, providerOptions.ProviderName, request.Model.Trim())
            {
                ProviderKind = providerOptions.ProviderKind,
                ModelProxyRouting = ExtractModelProxyRoutingInfo(providerOptions.ProviderKind, response.Headers),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{providerOptions.ProviderName} summary validation timed out after {effectiveTimeout.TotalSeconds:0} seconds.");
        }
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

    private static string ExtractAssistantContent(string providerName, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"{providerName} response did not include choices.");
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{providerName} response did not include assistant text.");
        }

        return content.GetString() ?? string.Empty;
    }

    private static string ExtractStreamingAssistantContent(string providerName, string responseBody)
    {
        var builder = new StringBuilder();
        using var reader = new StringReader(responseBody);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmedLine = line.TrimStart();
            if (trimmedLine.Length == 0 ||
                trimmedLine.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            if (!trimmedLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = trimmedLine["data:".Length..].TrimStart();
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            if (data.Length == 0)
            {
                continue;
            }

            builder.Append(ExtractStreamingChunkContent(providerName, data));
        }

        return builder.ToString();
    }

    private static string ExtractStreamingChunkContent(string providerName, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("delta", out var delta) &&
                delta.ValueKind == JsonValueKind.Object &&
                delta.TryGetProperty("content", out var deltaContent) &&
                deltaContent.ValueKind == JsonValueKind.String)
            {
                return deltaContent.GetString() ?? string.Empty;
            }

            if (firstChoice.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var messageContent) &&
                messageContent.ValueKind == JsonValueKind.String)
            {
                return messageContent.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{providerName} streaming response included a malformed assistant chunk.", exception);
        }
    }

    private static string BuildHttpFailureMessage(
        SummaryChatProviderOptions providerOptions,
        HttpResponseMessage response,
        string responseBody)
    {
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : response.ReasonPhrase;
        var message = $"{providerOptions.ProviderName} returned HTTP {(int)response.StatusCode} {reason}.";
        var safeDetails = TryExtractSafeErrorDetails(responseBody);
        var routingInfo = ExtractModelProxyRoutingInfo(providerOptions.ProviderKind, response.Headers);
        var routingDetails = BuildSafeRoutingDetails(routingInfo);
        var capabilityMessage = BuildForcedAppServerSearchCapabilityMessage(providerOptions, response, routingInfo);
        var parts = new[]
            {
                message,
                capabilityMessage,
                routingDetails,
                safeDetails,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" ", parts);
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
        if (routingInfo.AppServerWebSearchSupported is not null)
        {
            parts.Add($"App-server web search supported: {routingInfo.AppServerWebSearchSupported.Value.ToString().ToLowerInvariant()}.");
        }

        AddSafePart(parts, "Fallback reason", routingInfo.FallbackReason);
        return string.Join(" ", parts);
    }

    private static string BuildForcedAppServerSearchCapabilityMessage(
        SummaryChatProviderOptions providerOptions,
        HttpResponseMessage response,
        ModelProxyRoutingInfo? routingInfo)
    {
        if (providerOptions.ProviderKind != SummaryChatProviderKind.ModelProxy ||
            response.StatusCode != System.Net.HttpStatusCode.BadRequest ||
            !providerOptions.ModelProxyWebSearchEnabled ||
            !string.Equals(providerOptions.ModelProxyBackend, "app-server", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var appServerSearchUnsupported =
            routingInfo is null ||
            string.Equals(routingInfo?.WebSearchBackend, "unsupported", StringComparison.OrdinalIgnoreCase) ||
            routingInfo?.AppServerWebSearchSupported == false;
        return appServerSearchUnsupported
            ? "App-server web search is not available in this ModelProxy instance; retry without web search or allow ModelProxy to route web search through CLI."
            : string.Empty;
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
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                AddSafeJsonProperty(parts, detail, "category", "Category");
                AddSafeJsonProperty(parts, detail, "backend", "Backend");
                AddSafeJsonProperty(parts, detail, "request_id", "Request");
                AddSafeJsonProperty(parts, detail, "next_step", "Next step");
                return string.Join(" ", parts);
            }

            if (root.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                AddSafeJsonProperty(parts, error, "type", "Type");
                AddSafeJsonProperty(parts, error, "code", "Code");
                return string.Join(" ", parts);
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
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

    private sealed record SummaryChatCompletionPayload(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<SummaryChatCompletionMessagePayload> Messages)
    {
        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Stream { get; init; }
    }

    private sealed record SummaryChatCompletionMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}

public sealed class SummaryProviderValidationService
{
    public const string SyntheticValidationPrompt = "Reply exactly: summary-provider-ok";

    private readonly ISummaryChatClient _chatClient;

    public SummaryProviderValidationService(ISummaryChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public Task<SummaryProviderValidationResult> ValidateModelProxyAsync(
        AppConfig config,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            string.IsNullOrWhiteSpace(apiKey) ? MeetingSummaryDefaults.ModelProxyLocalApiKey : apiKey,
            config.SummaryModelProxyBaseUrl);
        var request = BuildSyntheticRequest(config.SummaryModelProxyModel, config.SummaryRequestTimeoutSeconds);
        return ValidateAsync(providerOptions, request, cancellationToken);
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
        var request = BuildSyntheticRequest(config.SummaryOpenAiModel, config.SummaryRequestTimeoutSeconds);
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

    private static SummaryChatRequest BuildSyntheticRequest(
        string model,
        int timeoutSeconds)
    {
        return new SummaryChatRequest(
            model,
            [new SummaryChatMessage(SummaryChatRole.User, SyntheticValidationPrompt)],
            TimeSpan.FromSeconds(timeoutSeconds));
    }
}

public sealed record SummaryProviderValidationResult(
    SummaryChatProviderKind ProviderKind,
    bool IsConfigured,
    bool Success,
    string StatusText,
    string? ResponseText)
{
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

public sealed class ModelProxyClient
{
    private readonly SummaryChatClient _chatClient;
    private readonly ModelProxyOptions _options;

    public ModelProxyClient(HttpClient httpClient, ModelProxyOptions? options = null)
    {
        _chatClient = new SummaryChatClient(httpClient);
        _options = options ?? new ModelProxyOptions();
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
}

public sealed record ModelProxyOptions
{
    public string BaseUrl { get; init; } = MeetingSummaryDefaults.ModelProxyBaseUrl;

    public string ApiKey { get; init; } = MeetingSummaryDefaults.ModelProxyLocalApiKey;

    public string Model { get; init; } = MeetingSummaryDefaults.ModelProxyModel;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(MeetingSummaryDefaults.RequestTimeoutSeconds);
}

public sealed record ModelProxyCompletionResult(string Content);
