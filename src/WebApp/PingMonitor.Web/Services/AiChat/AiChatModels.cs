namespace PingMonitor.Web.Services.AiChat;

public sealed class AiChatRequest
{
    public IList<AiChatMessageDto> ConversationHistory { get; set; } = new List<AiChatMessageDto>();
    public string UserMessage { get; set; } = string.Empty;
}

public sealed class AiChatResponse
{
    public bool Succeeded { get; set; }
    public bool AssistantEnabled { get; set; }
    public bool WebChatEnabled { get; set; }
    public string? AssistantMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderName { get; set; }
    public string? ModelName { get; set; }
}

public sealed class AiChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
