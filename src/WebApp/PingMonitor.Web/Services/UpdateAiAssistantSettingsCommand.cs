namespace PingMonitor.Web.Services;

public sealed class UpdateAiAssistantSettingsCommand
{
    public bool AssistantEnabled { get; set; }
    public bool WebChatEnabled { get; set; }
    public bool TelegramChatEnabled { get; set; }
    public bool MemoryEnabled { get; set; }
    public bool DebugLoggingEnabled { get; set; }
    public string? ProviderDisplayName { get; set; }
    public string? ProviderType { get; set; }
    public string? BaseUrl { get; set; }
    public string? ModelName { get; set; }
    public string? ApiKey { get; set; }
    public bool ClearApiKey { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public int MaxOutputTokens { get; set; }
    public double Temperature { get; set; }
    public bool ToolCallingEnabled { get; set; }
    public string? GlobalSystemPrompt { get; set; }
    public string? UpdatedByUserId { get; set; }
}
