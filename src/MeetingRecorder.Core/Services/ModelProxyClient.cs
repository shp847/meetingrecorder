using MeetingRecorder.Core.Configuration;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    TimeSpan Timeout);

public sealed record SummaryChatResponse(
    string Content,
    string ProviderName,
    string Model)
{
    public SummaryChatProviderKind ProviderKind { get; init; }
}

public sealed record SummaryChatProviderOptions(
    SummaryChatProviderKind ProviderKind,
    string ProviderName,
    string BaseUrl,
    string ApiKey,
    string? ModelProxyBackend,
    string? ModelProxyCodexModel)
{
    public static SummaryChatProviderOptions ForModelProxy(
        string apiKey,
        string baseUrl = MeetingSummaryDefaults.ModelProxyBaseUrl,
        string backend = MeetingSummaryDefaults.ModelProxyBackend,
        string codexModel = MeetingSummaryDefaults.ModelProxyCodexModel)
    {
        return new SummaryChatProviderOptions(
            SummaryChatProviderKind.ModelProxy,
            "ModelProxy",
            baseUrl,
            apiKey,
            backend,
            codexModel);
    }

    public static SummaryChatProviderOptions ForOpenAi(
        string apiKey,
        string baseUrl = "https://api.openai.com/v1")
    {
        return new SummaryChatProviderOptions(
            SummaryChatProviderKind.OpenAi,
            "OpenAI",
            baseUrl,
            apiKey,
            null,
            null);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);
        var endpoint = new Uri(new Uri(NormalizeBaseUrl(providerOptions.BaseUrl)), "chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey.Trim());
        if (providerOptions.ProviderKind == SummaryChatProviderKind.ModelProxy)
        {
            if (!string.IsNullOrWhiteSpace(providerOptions.ModelProxyBackend))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-ModelProxy-Backend", providerOptions.ModelProxyBackend.Trim());
            }

            if (!string.IsNullOrWhiteSpace(providerOptions.ModelProxyCodexModel))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-ModelProxy-Codex-Model", providerOptions.ModelProxyCodexModel.Trim());
            }

            httpRequest.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search", "false");
        }

        var payload = new
        {
            model = request.Model.Trim(),
            messages = request.Messages.Select(message => new
            {
                role = ConvertRole(message.Role),
                content = message.Content,
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(BuildHttpFailureMessage(providerOptions, response, responseBody));
            }

            var content = ExtractAssistantContent(providerOptions.ProviderName, responseBody);
            return new SummaryChatResponse(content, providerOptions.ProviderName, request.Model.Trim())
            {
                ProviderKind = providerOptions.ProviderKind,
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{providerOptions.ProviderName} summary validation timed out after {request.Timeout.TotalSeconds:0} seconds.");
        }
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
        return string.IsNullOrWhiteSpace(safeDetails)
            ? message
            : $"{message} {safeDetails}";
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
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(SummaryProviderValidationResult.NotConfigured(
                SummaryChatProviderKind.ModelProxy,
                "ModelProxy key is not saved."));
        }

        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            apiKey,
            config.SummaryModelProxyBaseUrl,
            config.SummaryModelProxyBackend,
            config.SummaryModelProxyCodexModel);
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
            _options.BaseUrl,
            _options.Backend,
            _options.CodexModel);

        var response = await _chatClient.CompleteAsync(providerOptions, request, cancellationToken);
        return new ModelProxyCompletionResult(response.Content);
    }
}

public sealed record ModelProxyOptions
{
    public string BaseUrl { get; init; } = MeetingSummaryDefaults.ModelProxyBaseUrl;

    public string ApiKey { get; init; } = "sk-modelproxy-meeting-recorder";

    public string Model { get; init; } = MeetingSummaryDefaults.ModelProxyModel;

    public string Backend { get; init; } = MeetingSummaryDefaults.ModelProxyBackend;

    public string CodexModel { get; init; } = MeetingSummaryDefaults.ModelProxyCodexModel;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(MeetingSummaryDefaults.RequestTimeoutSeconds);
}

public sealed record ModelProxyCompletionResult(string Content);
