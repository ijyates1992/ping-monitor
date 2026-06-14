using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PingMonitor.Web.Services.AiProviders;

internal sealed class OpenAiCompatibleProviderClient : IAiProviderClient
{
    private const int MaxErrorBodyLength = 4096;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiCompatibleProviderClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiCompatibleProviderClient(IHttpClientFactory httpClientFactory, ILogger<OpenAiCompatibleProviderClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AiProviderChatResult> SendChatAsync(AiProviderChatRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new AiProviderChatResult { ProviderName = request.ProviderName, Model = request.ModelName };

        if (!Uri.TryCreate(request.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return Fail(result, sw, "Provider base URL must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelName)) return Fail(result, sw, "Provider model name is required.");
        if (request.Messages.Count == 0) return Fail(result, sw, "At least one message is required.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 300)));

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(baseUri));
            var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? "ollama" : request.ApiKey.Trim();
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = JsonContent.Create(new ChatCompletionRequestDto
            {
                Model = request.ModelName.Trim(),
                Messages = request.Messages.Select(x => new ChatCompletionMessageDto { Role = x.Role, Content = x.Content }).ToArray(),
                Temperature = request.Temperature,
                MaxTokens = request.MaxOutputTokens,
                Stream = false
            }, options: JsonOptions);

            var client = _httpClientFactory.CreateClient(nameof(OpenAiCompatibleProviderClient));
            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            result.StatusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Fail(result, sw, $"Provider returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", body);
            }

            ChatCompletionResponseDto? dto;
            try { dto = JsonSerializer.Deserialize<ChatCompletionResponseDto>(body, JsonOptions); }
            catch (JsonException) { return Fail(result, sw, "Provider returned invalid JSON."); }

            var errorMessage = dto?.Error?.Message;
            if (!string.IsNullOrWhiteSpace(errorMessage)) return Fail(result, sw, $"Provider returned an error: {errorMessage}");

            var text = dto?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text)) return Fail(result, sw, "Provider response did not include assistant message content.");

            sw.Stop();
            result.Succeeded = true;
            result.ResponseText = text.Trim();
            result.Model = string.IsNullOrWhiteSpace(dto?.Model) ? request.ModelName : dto!.Model;
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(result, sw, "Provider request was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Fail(result, sw, "Provider request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI provider connection failed for {ProviderName}. StatusCode={StatusCode}", request.ProviderName, ex.StatusCode);
            result.StatusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
            return Fail(result, sw, "Provider connection failed. Verify the base URL, network path, and provider process.");
        }
        catch (JsonException)
        {
            return Fail(result, sw, "Provider returned invalid JSON.");
        }
    }

    internal static Uri BuildChatCompletionsUri(Uri baseUri) => new(baseUri.ToString().TrimEnd('/') + "/chat/completions");

    private static AiProviderChatResult Fail(AiProviderChatResult result, Stopwatch sw, string message, string? rawErrorBody = null)
    {
        sw.Stop();
        result.Succeeded = false;
        result.ErrorMessage = message;
        result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        result.RawErrorBody = string.IsNullOrWhiteSpace(rawErrorBody) ? null : Truncate(rawErrorBody.Trim(), MaxErrorBodyLength);
        return result;
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed class ChatCompletionRequestDto
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public ChatCompletionMessageDto[] Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }
    private sealed class ChatCompletionMessageDto { [JsonPropertyName("role")] public string Role { get; set; } = string.Empty; [JsonPropertyName("content")] public string Content { get; set; } = string.Empty; }
    private sealed class ChatCompletionResponseDto { [JsonPropertyName("model")] public string? Model { get; set; } [JsonPropertyName("choices")] public ChatCompletionChoiceDto[]? Choices { get; set; } [JsonPropertyName("error")] public ChatCompletionErrorDto? Error { get; set; } }
    private sealed class ChatCompletionChoiceDto { [JsonPropertyName("message")] public ChatCompletionMessageDto? Message { get; set; } }
    private sealed class ChatCompletionErrorDto { [JsonPropertyName("message")] public string? Message { get; set; } }
}
