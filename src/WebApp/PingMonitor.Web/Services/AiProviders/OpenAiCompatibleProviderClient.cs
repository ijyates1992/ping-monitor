using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

        var profile = DetectProviderProfile(baseUri);
        var endpointUri = BuildChatCompletionsUri(baseUri, profile);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 600)));

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? "ollama" : request.ApiKey.Trim();
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = BuildPayload(request, profile);
            httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

            _logger.LogDebug("AI provider request prepared. Provider={ProviderName} Profile={ProviderProfile} Model={ModelName} Method={Method} Host={Host} Path={Path} ParameterNames={ParameterNames} MessageCount={MessageCount} ToolCount={ToolCount} MaxOutputTokens={MaxOutputTokens} TimeoutSeconds={TimeoutSeconds}",
                request.ProviderName, profile.Name, request.ModelName, httpRequest.Method.Method, endpointUri.Host, endpointUri.AbsolutePath, string.Join(',', payload.Select(x => x.Key)), request.Messages.Count, request.Tools.Count, request.MaxOutputTokens, request.TimeoutSeconds);

            var client = _httpClientFactory.CreateClient(nameof(OpenAiCompatibleProviderClient));
            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            result.StatusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var redactedBody = RedactSecrets(body);
                var providerError = ParseProviderError(redactedBody);
                _logger.LogWarning("AI provider request failed. Provider={ProviderName} Profile={ProviderProfile} Model={ModelName} StatusCode={StatusCode} ErrorType={ErrorType} ErrorCode={ErrorCode} ErrorParam={ErrorParam} ErrorMessage={ErrorMessage}",
                    request.ProviderName, profile.Name, request.ModelName, (int)response.StatusCode, providerError.Type, providerError.Code, providerError.Param, providerError.Message);
                return Fail(result, sw, FormatProviderError(response.StatusCode, response.ReasonPhrase, profile, providerError), redactedBody);
            }

            ChatCompletionResponseDto? dto;
            try { dto = JsonSerializer.Deserialize<ChatCompletionResponseDto>(body, JsonOptions); }
            catch (JsonException) { return Fail(result, sw, "Provider returned invalid JSON."); }

            var errorMessage = RedactSecrets(dto?.Error?.Message);
            if (!string.IsNullOrWhiteSpace(errorMessage)) return Fail(result, sw, $"Provider returned an error: {errorMessage}");

            var message = dto?.Choices?.FirstOrDefault()?.Message;
            var text = message?.Content;
            var toolCalls = message?.ToolCalls?.Select(MapToolCall).Where(x => !string.IsNullOrWhiteSpace(x.Function.Name)).ToArray() ?? [];
            if (string.IsNullOrWhiteSpace(text) && toolCalls.Length == 0) return Fail(result, sw, "Provider response did not include assistant message content or tool calls.");

            sw.Stop();
            result.Succeeded = true;
            result.ResponseText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            result.ToolCalls = toolCalls;
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

    internal static Uri BuildChatCompletionsUri(Uri baseUri) => BuildChatCompletionsUri(baseUri, DetectProviderProfile(baseUri));

    internal static Uri BuildChatCompletionsUri(Uri baseUri, AiProviderProfile profile)
    {
        var trimmed = baseUri.ToString().TrimEnd('/');
        if (profile.IsOpenAiOfficial && !baseUri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/v1";
        }
        return new Uri(trimmed + "/chat/completions");
    }

    internal static AiProviderProfile DetectProviderProfile(Uri baseUri)
        => string.Equals(baseUri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
            ? AiProviderProfile.OpenAiOfficial
            : AiProviderProfile.GenericOpenAiCompatible;

    internal static JsonObject BuildPayload(AiProviderChatRequest request, AiProviderProfile profile)
    {
        var payload = new JsonObject
        {
            ["model"] = request.ModelName.Trim(),
            ["messages"] = new JsonArray(request.Messages.Select(x => MapMessage(x, profile, request.ModelName)).ToArray<JsonNode?>())
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = new JsonArray(request.Tools.Select(MapToolDefinition).ToArray<JsonNode?>());
            if (!string.IsNullOrWhiteSpace(request.ToolChoice)) payload["tool_choice"] = request.ToolChoice.Trim();
        }

        if (profile.IsOpenAiOfficial)
        {
            payload["max_completion_tokens"] = Math.Max(1, request.MaxOutputTokens);
        }
        else
        {
            payload["temperature"] = request.Temperature;
            payload["max_tokens"] = Math.Max(1, request.MaxOutputTokens);
            payload["stream"] = false;
        }

        return payload;
    }

    private static JsonObject MapMessage(AiProviderChatMessage message, AiProviderProfile profile, string modelName)
    {
        var role = message.Role;
        if (profile.IsOpenAiOfficial && IsModernOpenAiReasoningModel(modelName) && string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)) role = "developer";
        var json = new JsonObject { ["role"] = role };
        if (message.Content is not null) json["content"] = message.Content;
        if (!string.IsNullOrWhiteSpace(message.ToolCallId)) json["tool_call_id"] = message.ToolCallId;
        if (message.ToolCalls.Count > 0) json["tool_calls"] = new JsonArray(message.ToolCalls.Select(MapToolCallJson).ToArray<JsonNode?>());
        return json;
    }

    private static JsonObject MapToolDefinition(AiProviderToolDefinition tool)
    {
        var parameters = tool.Function.Parameters.DeepClone();
        if (parameters is JsonObject obj && obj.Count == 0) obj["type"] = "object";
        return new JsonObject
        {
            ["type"] = string.IsNullOrWhiteSpace(tool.Type) ? "function" : tool.Type,
            ["function"] = new JsonObject
            {
                ["name"] = tool.Function.Name,
                ["description"] = string.IsNullOrWhiteSpace(tool.Function.Description) ? tool.Function.Name : tool.Function.Description,
                ["parameters"] = parameters
            }
        };
    }

    private static JsonObject MapToolCallJson(AiProviderToolCall call) => new()
    {
        ["id"] = call.Id,
        ["type"] = string.IsNullOrWhiteSpace(call.Type) ? "function" : call.Type,
        ["function"] = new JsonObject { ["name"] = call.Function.Name, ["arguments"] = call.Function.Arguments }
    };

    private static bool IsModernOpenAiReasoningModel(string modelName)
        => modelName.StartsWith("o", StringComparison.OrdinalIgnoreCase) || modelName.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

    private static AiProviderChatResult Fail(AiProviderChatResult result, Stopwatch sw, string message, string? rawErrorBody = null)
    {
        sw.Stop();
        result.Succeeded = false;
        result.ErrorMessage = RedactSecrets(message);
        result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        result.RawErrorBody = string.IsNullOrWhiteSpace(rawErrorBody) ? null : Truncate(RedactSecrets(rawErrorBody.Trim()) ?? string.Empty, MaxErrorBodyLength);
        return result;
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string? RedactSecrets(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = Regex.Replace(value, @"Bearer\s+[A-Za-z0-9_\.\-]+", "Bearer [redacted]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"sk-[A-Za-z0-9_\-]+", "[redacted-api-key]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, "(?i)(api[_ -]?key|authorization|token|secret)(['\"\\s:=]+)([^'\"\\s,}]+)", "$1$2[redacted]");
        return redacted;
    }

    private static ProviderError ParseProviderError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new ProviderError(null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.TryGetProperty("error", out var e) ? e : doc.RootElement;
            return new ProviderError(GetString(error, "message"), GetString(error, "type"), GetString(error, "code"), GetString(error, "param"));
        }
        catch (JsonException)
        {
            return new ProviderError(Truncate(body.Trim(), 500), null, null, null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null ? property.ToString() : null;

    private static string FormatProviderError(HttpStatusCode statusCode, string? reasonPhrase, AiProviderProfile profile, ProviderError error)
    {
        var status = (int)statusCode;
        var source = profile.IsOpenAiOfficial ? "OpenAI" : "Provider";
        var details = string.Join(" ", new[] { error.Type, error.Code is null ? null : $"code={error.Code}", error.Param is null ? null : $"param={error.Param}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var message = string.IsNullOrWhiteSpace(error.Message) ? $"{source} returned HTTP {status} ({reasonPhrase})." : $"{source} returned {status}{(string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details)}: {error.Message}";
        return RedactSecrets(message) ?? message;
    }

    private static ChatCompletionToolCallDto MapToolCall(AiProviderToolCall call) => new() { Id = call.Id, Type = call.Type, Function = new ChatCompletionToolCallFunctionDto { Name = call.Function.Name, Arguments = call.Function.Arguments } };
    private static AiProviderToolCall MapToolCall(ChatCompletionToolCallDto call) => new() { Id = call.Id ?? string.Empty, Type = call.Type ?? "function", Function = new AiProviderToolCallFunction { Name = call.Function?.Name ?? string.Empty, Arguments = call.Function?.Arguments ?? "{}" } };

    internal sealed record AiProviderProfile(string Name, bool IsOpenAiOfficial)
    {
        public static AiProviderProfile OpenAiOfficial { get; } = new("OpenAIOfficial", true);
        public static AiProviderProfile GenericOpenAiCompatible { get; } = new("GenericOpenAICompatible", false);
    }

    private sealed record ProviderError(string? Message, string? Type, string? Code, string? Param);
    private sealed class ChatCompletionMessageDto { [JsonPropertyName("role")] public string Role { get; set; } = string.Empty; [JsonPropertyName("content")] public string? Content { get; set; } = string.Empty; [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; } [JsonPropertyName("tool_calls")] public ChatCompletionToolCallDto[]? ToolCalls { get; set; } }
    private sealed class ChatCompletionToolCallDto { [JsonPropertyName("id")] public string? Id { get; set; } [JsonPropertyName("type")] public string? Type { get; set; } [JsonPropertyName("function")] public ChatCompletionToolCallFunctionDto? Function { get; set; } }
    private sealed class ChatCompletionToolCallFunctionDto { [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("arguments")] public string? Arguments { get; set; } }
    private sealed class ChatCompletionResponseDto { [JsonPropertyName("model")] public string? Model { get; set; } [JsonPropertyName("choices")] public ChatCompletionChoiceDto[]? Choices { get; set; } [JsonPropertyName("error")] public ChatCompletionErrorDto? Error { get; set; } }
    private sealed class ChatCompletionChoiceDto { [JsonPropertyName("message")] public ChatCompletionMessageDto? Message { get; set; } }
    private sealed class ChatCompletionErrorDto { [JsonPropertyName("message")] public string? Message { get; set; } }
}
