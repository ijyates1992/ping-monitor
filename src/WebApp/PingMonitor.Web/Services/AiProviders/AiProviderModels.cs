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
}

public sealed class AiProviderChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class AiProviderChatResult
{
    public bool Succeeded { get; set; }
    public string? ResponseText { get; set; }
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
