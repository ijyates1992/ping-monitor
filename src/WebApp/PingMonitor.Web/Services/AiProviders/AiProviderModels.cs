using System.Text.Json.Nodes;

namespace PingMonitor.Web.Services.AiProviders;

public sealed class AiProviderChatRequest
{
    public string ProviderName { get; set; } = "OpenAI-compatible";
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    public double Temperature { get; set; } = 0.2;
    public int MaxOutputTokens { get; set; } = 256;
    public IList<AiProviderChatMessage> Messages { get; set; } = new List<AiProviderChatMessage>();
    public IList<AiProviderToolDefinition> Tools { get; set; } = new List<AiProviderToolDefinition>();
    public string? ToolChoice { get; set; }
}

public sealed class AiProviderChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; } = string.Empty;
    public string? ToolCallId { get; set; }
    public IList<AiProviderToolCall> ToolCalls { get; set; } = new List<AiProviderToolCall>();
}

public sealed class AiProviderToolDefinition
{
    public string Type { get; set; } = "function";
    public AiProviderFunctionDefinition Function { get; set; } = new();
}

public sealed class AiProviderFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonObject Parameters { get; set; } = new();
}

public sealed class AiProviderToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public AiProviderToolCallFunction Function { get; set; } = new();
}

public sealed class AiProviderToolCallFunction
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}

public sealed class AiProviderChatResult
{
    public bool Succeeded { get; set; }
    public string? ResponseText { get; set; }
    public IList<AiProviderToolCall> ToolCalls { get; set; } = new List<AiProviderToolCall>();
    public string? Model { get; set; }
    public string ProviderName { get; set; } = "OpenAI-compatible";
    public long ElapsedMilliseconds { get; set; }
    public string? ErrorMessage { get; set; }
    public int? StatusCode { get; set; }
    public string? RawErrorBody { get; set; }
}

public interface IAiProviderClient
{
    Task<AiProviderChatResult> SendChatAsync(AiProviderChatRequest request, CancellationToken cancellationToken);
}
