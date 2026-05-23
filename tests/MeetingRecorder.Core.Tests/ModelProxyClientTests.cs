using MeetingRecorder.Core.Services;
using System.Net;
using System.Net.Http.Headers;
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
        Assert.Equal("sk-modelproxy", handler.Request.Headers.Authorization.Parameter);
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Backend"));
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Codex-Model"));
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
    public async Task GetModelsAsync_Reads_Default_Model_From_ModelProxy_Models_Endpoint()
    {
        using var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "object": "list",
                      "default_model": "gpt-5.4-mini",
                      "default_codex_model": "gpt-5.4-mini",
                      "data": [
                        {
                          "id": "gpt-5.4-mini",
                          "object": "model",
                          "owned_by": "modelproxy",
                          "default": true,
                          "backend": "codex",
                          "default_backend_model": "gpt-5.4-mini"
                        },
                        {
                          "id": "gpt-5.5",
                          "object": "model",
                          "owned_by": "modelproxy",
                          "default": false,
                          "backend": "codex",
                          "default_backend_model": "gpt-5.5"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        using var httpClient = new HttpClient(handler);
        var client = new ModelProxyClient(httpClient);

        var catalog = await client.GetModelsAsync();

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("http://127.0.0.1:8645/v1/models", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-modelproxy", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("gpt-5.4-mini", catalog.DefaultModel);
        Assert.Equal("gpt-5.4-mini", catalog.ResolveModel());
        Assert.Equal("gpt-5.5", catalog.ResolveModel(" gpt-5.5 "));
        Assert.Equal(2, catalog.Models.Count);
        Assert.True(catalog.Models[0].IsDefault);
        Assert.Equal("gpt-5.4-mini", catalog.Models[0].DefaultBackendModel);
    }

    [Fact]
    public async Task SummaryChatClient_Posts_No_Search_AppServer_ModelProxy_Request()
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
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            "sk-modelproxy-test",
            backend: "app-server",
            webSearchEnabled: false);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromSeconds(120));

        var result = await client.CompleteAsync(providerOptions, request);

        Assert.Equal("summary-provider-ok", result.Content);
        Assert.NotNull(handler.Request);
        Assert.Equal("app-server", Assert.Single(handler.Request!.Headers.GetValues("X-ModelProxy-Backend")));
        Assert.Equal("false", Assert.Single(handler.Request.Headers.GetValues("X-ModelProxy-Web-Search")));
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Codex-Model"));
    }

    [Fact]
    public async Task SummaryChatClient_Captures_ModelProxy_Routing_Headers_For_Web_Search_Cli_Fallback()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
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
        };
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Request-Id", "mp-test");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Requested-Backend", "codex");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Effective-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-App-Server-Web-Search-Supported", "false");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Fallback-Reason", "app_server_search_unsupported");

        using var handler = new CapturingHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            "sk-modelproxy-test",
            webSearchEnabled: true);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromSeconds(30));

        var result = await client.CompleteAsync(providerOptions, request);

        Assert.NotNull(result.ModelProxyRouting);
        Assert.Equal("mp-test", result.ModelProxyRouting!.RequestId);
        Assert.Equal("codex", result.ModelProxyRouting.RequestedBackend);
        Assert.Equal("cli", result.ModelProxyRouting.EffectiveBackend);
        Assert.Equal("cli", result.ModelProxyRouting.WebSearchBackend);
        Assert.False(result.ModelProxyRouting.AppServerWebSearchSupported);
        Assert.Equal("app_server_search_unsupported", result.ModelProxyRouting.FallbackReason);
        Assert.Equal("true", Assert.Single(handler.Request!.Headers.GetValues("X-ModelProxy-Web-Search")));
    }

    [Fact]
    public async Task SummaryChatClient_Reports_NonStreaming_CliTimeout_With_Structured_Metadata()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway",
            Content = new StringContent(
                """
                {
                  "detail": {
                    "category": "cli_timeout",
                    "type": "cli_timeout",
                    "message": "Prompt text should not leak.",
                    "request_id": "mp-timeout",
                    "backend": "cli",
                    "requested_backend": "auto",
                    "elapsed_seconds": 45.123,
                    "timeout_seconds": 45,
                    "next_step": "Retry with a shorter prompt or increase the ModelProxy CLI timeout."
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Request-Id", "mp-timeout");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Requested-Backend", "auto");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Effective-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-App-Server-Web-Search-Supported", "false");

        using var handler = new CapturingHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            "sk-modelproxy-secret",
            webSearchEnabled: true);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: private prompt marker")],
            TimeSpan.FromSeconds(60));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(providerOptions, request));

        Assert.Contains("HTTP 502", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cli_timeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Request: mp-timeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Effective backend: cli", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elapsed seconds: 45.123", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Timeout seconds: 45", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Retry with a shorter prompt", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompt text should not leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt marker", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-modelproxy-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummaryChatClient_Ignores_Sse_Comment_Lines_When_Parsing_Stream()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                : request_id=mp-test effective_backend=cli

                data: {"choices":[{"delta":{"content":"summary"},"finish_reason":null}]}

                : keepalive this is not assistant text

                data: {"choices":[{"delta":{"content":"-provider-ok"},"finish_reason":null}]}

                data: [DONE]

                """,
                Encoding.UTF8,
                "text/event-stream"),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

        using var handler = new CapturingHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromSeconds(120))
        {
            Stream = true,
        };

        var result = await client.CompleteAsync(
            SummaryChatProviderOptions.ForModelProxy("sk-modelproxy-test"),
            request);

        Assert.Equal("summary-provider-ok", result.Content);
        Assert.Contains("\"stream\":true", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("keepalive", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request_id", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SummaryChatClient_Parses_Terminal_Sse_Error_As_ModelProxy_Failure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                : request_id=mp-stream effective_backend=cli

                event: error
                data: {"error":{"type":"cli_timeout","category":"cli_timeout","message":"Prompt text should not leak.","request_id":"mp-stream","backend":"cli","requested_backend":"auto","web_search":true,"elapsed_seconds":45.123,"timeout_seconds":45,"next_step":"Retry with a shorter prompt or increase the ModelProxy CLI timeout."}}

                data: [DONE]

                """,
                Encoding.UTF8,
                "text/event-stream"),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Request-Id", "mp-stream");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Requested-Backend", "auto");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Effective-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search-Backend", "cli");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-App-Server-Web-Search-Supported", "false");

        using var handler = new CapturingHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: private prompt marker")],
            TimeSpan.FromSeconds(60))
        {
            Stream = true,
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(
                SummaryChatProviderOptions.ForModelProxy(
                    "sk-modelproxy-secret",
                    webSearchEnabled: true),
                request));

        Assert.Contains("ModelProxy streaming request failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cli_timeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Request: mp-stream", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Backend: cli", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elapsed seconds: 45.123", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Timeout seconds: 45", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Retry with a shorter prompt", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not reach", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prompt text should not leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt marker", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-modelproxy-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummaryChatClient_Forced_AppServer_WebSearch_400_Surfaces_Capability_Message_Without_Secrets()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
            Content = new StringContent(
                """
                {
                  "detail": {
                    "category": "unsupported_web_search_backend",
                    "message": "raw prompt text should not leak",
                    "request_id": "mp-test",
                    "backend": "app-server"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Request-Id", "mp-test");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Requested-Backend", "app-server");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Effective-Backend", "app-server");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search-Backend", "unsupported");
        response.Headers.TryAddWithoutValidation("X-ModelProxy-App-Server-Web-Search-Supported", "false");

        using var handler = new CapturingHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var providerOptions = SummaryChatProviderOptions.ForModelProxy(
            "sk-modelproxy-secret",
            backend: "app-server",
            webSearchEnabled: true);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: private prompt marker")],
            TimeSpan.FromSeconds(60));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(providerOptions, request));

        Assert.Contains("HTTP 400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("App-server web search is not available", exception.Message, StringComparison.Ordinal);
        Assert.Contains("retry without web search", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mp-test", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw prompt text should not leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt marker", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-modelproxy-secret", exception.Message, StringComparison.Ordinal);
        Assert.Equal("app-server", Assert.Single(handler.Request!.Headers.GetValues("X-ModelProxy-Backend")));
        Assert.Equal("true", Assert.Single(handler.Request.Headers.GetValues("X-ModelProxy-Web-Search")));
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

        var result = await service.ValidateModelProxyAsync(new MeetingRecorder.Core.Configuration.AppConfig());

        Assert.True(result.Success);
        Assert.Equal(SummaryChatProviderKind.ModelProxy, result.ProviderKind);
        Assert.NotNull(handler.Request);
        Assert.Equal("sk-modelproxy", handler.Request!.Headers.Authorization!.Parameter);
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Backend"));
        Assert.False(handler.Request.Headers.Contains("X-ModelProxy-Codex-Model"));
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

    [Fact]
    public async Task SummaryChatClient_Reports_Timeout_As_Summary_Request_Failure()
    {
        using var handler = new DelayingHandler(TimeSpan.FromSeconds(5));
        using var httpClient = new HttpClient(handler);
        var client = new SummaryChatClient(httpClient);
        var request = new SummaryChatRequest(
            "gpt-5.4-mini",
            [new SummaryChatMessage(SummaryChatRole.User, "Reply exactly: summary-provider-ok")],
            TimeSpan.FromMilliseconds(10));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.CompleteAsync(SummaryChatProviderOptions.ForModelProxy("sk-modelproxy-test"), request));

        Assert.Contains("summary request timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("summary-provider-ok", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-modelproxy-test", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed class DelayingHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayingHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
