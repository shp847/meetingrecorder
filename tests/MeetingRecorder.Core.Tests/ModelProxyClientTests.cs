using MeetingRecorder.Core.Services;
using System.Net;
using System.Text;

namespace MeetingRecorder.Core.Tests;

public sealed class ModelProxyClientTests
{
    [Fact]
    public async Task CompleteSyntheticPromptAsync_Posts_Text_Only_ModelProxy_Request_With_Web_Search_Disabled()
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "chatcmpl-test",
                      "object": "chat.completion",
                      "choices": [
                        {
                          "message": {
                            "role": "assistant",
                            "content": "meeting-recorder-ok"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        using var httpClient = new HttpClient(handler);
        var client = new ModelProxyClient(httpClient);

        var result = await client.CompleteSyntheticPromptAsync("Reply exactly: meeting-recorder-ok");

        Assert.Equal("meeting-recorder-ok", result.Content);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://127.0.0.1:8645/v1/chat/completions", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-modelproxy-meeting-recorder", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("codex", Assert.Single(handler.Request.Headers.GetValues("X-ModelProxy-Backend")));
        Assert.Equal("gpt-5.4-mini", Assert.Single(handler.Request.Headers.GetValues("X-ModelProxy-Codex-Model")));
        Assert.Equal("false", Assert.Single(handler.Request.Headers.GetValues("X-ModelProxy-Web-Search")));
        Assert.Contains("\"model\":\"gpt-5.4-mini\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"content\":\"Reply exactly: meeting-recorder-ok\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteSyntheticPromptAsync_Rejects_Blank_Prompts_Without_Network_Call()
    {
        using var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new ModelProxyClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.CompleteSyntheticPromptAsync(" "));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task CompleteSyntheticPromptAsync_Reports_Http_Failures_Without_Response_Body()
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized",
                Content = new StringContent("sensitive upstream body"),
            });
        using var httpClient = new HttpClient(handler);
        var client = new ModelProxyClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteSyntheticPromptAsync("Reply exactly: meeting-recorder-ok"));

        Assert.Contains("HTTP 401 Unauthorized", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive upstream body", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummaryChatClient_Posts_OpenAi_Request_Without_ModelProxy_Headers()
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "summary-provider-ok"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var providerOptions = SummaryChatProviderOptions.ForOpenAi("sk-openai-test");
        var request = new SummaryChatRequest(
            "gpt-5-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromSeconds(120));

        var result = await client.CompleteAsync(providerOptions, request);

        Assert.Equal("summary-provider-ok", result.Content);
        Assert.Equal(SummaryChatProviderKind.OpenAi, result.ProviderKind);
        Assert.Equal("OpenAI", result.ProviderName);
        Assert.Equal("gpt-5-mini", result.Model);
        Assert.NotNull(handler.Request);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-openai-test", handler.Request.Headers.Authorization.Parameter);
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Backend"));
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Codex-Model"));
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Web-Search"));
        Assert.Contains("\"model\":\"gpt-5-mini\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attendee", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meeting", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SummaryProviderValidationService_Uses_Synthetic_Content_Only()
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "summary-provider-ok"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        using var httpClient = new HttpClient(handler);
        var service = new SummaryProviderValidationService(new SummaryChatClient(httpClient));

        var result = await service.ValidateModelProxyAsync(
            new MeetingRecorder.Core.Configuration.AppConfig(),
            "sk-modelproxy-test");

        Assert.True(result.Success);
        Assert.Equal(SummaryChatProviderKind.ModelProxy, result.ProviderKind);
        Assert.NotNull(handler.Request);
        Assert.Contains("summary-provider-ok", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attendee", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meeting", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SummaryChatClient_Reports_Sanitized_Http_Failures(HttpStatusCode statusCode)
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(statusCode)
            {
                ReasonPhrase = "Failure",
                Content = new StringContent(
                    """
                    {
                      "detail": {
                        "category": "backend_busy",
                        "message": "safe next step only",
                        "request_id": "mp-test"
                      },
                      "raw": "secret body should not leak"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromSeconds(120));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(SummaryChatProviderOptions.ForModelProxy("sk-modelproxy-test"), request));

        Assert.Contains($"HTTP {(int)statusCode}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("backend_busy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mp-test", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret body should not leak", exception.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler, IDisposable
    {
        private readonly HttpResponseMessage _response;

        public CapturingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }

        void IDisposable.Dispose()
        {
            _response.Dispose();
        }
    }
}
