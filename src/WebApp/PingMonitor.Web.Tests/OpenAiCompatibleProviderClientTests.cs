using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PingMonitor.Web.Services.AiProviders;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class OpenAiCompatibleProviderClientTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/chat/completions")]
    public async Task BuildsChatCompletionsRequest_AndSendsExpectedBody(string baseUrl, string expectedUrl)
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"model\":\"llama\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Ping Monitor AI test OK\"}}]}")
        });
        var client = CreateClient(handler);

        var result = await client.SendChatAsync(BuildRequest(baseUrl), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedUrl, handler.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret", handler.AuthorizationParameter);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("llama", json.RootElement.GetProperty("model").GetString());
        Assert.False(json.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(0.2, json.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(256, json.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("system", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", json.RootElement.GetProperty("messages")[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ParsesSuccessfulAssistantMessage()
    {
        var client = CreateClient(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\" hello \"}}]}") }));
        var result = await client.SendChatAsync(BuildRequest("http://localhost:11434/v1"), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.ResponseText);
    }

    [Fact]
    public async Task HandlesNonSuccessWithConciseError()
    {
        var client = CreateClient(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad request body") }));
        var result = await client.SendChatAsync(BuildRequest("http://localhost:11434/v1"), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("HTTP 400", result.ErrorMessage);
        Assert.Equal("bad request body", result.RawErrorBody);
    }

    [Fact]
    public async Task HandlesInvalidJson()
    {
        var client = CreateClient(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") }));
        var result = await client.SendChatAsync(BuildRequest("http://localhost:11434/v1"), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("Provider returned invalid JSON.", result.ErrorMessage);
    }

    [Fact]
    public async Task HandlesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = CreateClient(new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await client.SendChatAsync(BuildRequest("http://localhost:11434/v1"), cts.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("Provider request was cancelled.", result.ErrorMessage);
    }

    private static AiProviderChatRequest BuildRequest(string baseUrl) => new()
    {
        ProviderName = "Local Ollama",
        BaseUrl = baseUrl,
        ModelName = "llama",
        ApiKey = "secret",
        TimeoutSeconds = 60,
        Temperature = 0.2,
        MaxOutputTokens = 256,
        Messages = { new AiProviderChatMessage { Role = "system", Content = "sys" }, new AiProviderChatMessage { Role = "user", Content = "user" } }
    };

    private static OpenAiCompatibleProviderClient CreateClient(HttpMessageHandler handler) => new(new FakeHttpClientFactory(new HttpClient(handler)), NullLogger<OpenAiCompatibleProviderClient>.Instance);

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) => _responseFactory = responseFactory;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
