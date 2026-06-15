namespace PingMonitor.Web.Models;

public sealed class AiAssistantSettings
{
    public const int SingletonId = 1;
    public const string OpenAICompatibleProviderType = "OpenAICompatible";

    public int AiAssistantSettingsId { get; set; } = SingletonId;
    public bool AssistantEnabled { get; set; }
    public bool WebChatEnabled { get; set; }
    public bool TelegramChatEnabled { get; set; }
    public bool MemoryEnabled { get; set; }
    public bool DebugLoggingEnabled { get; set; }
    public string ProviderDisplayName { get; set; } = "Local Ollama";
    public string ProviderType { get; set; } = OpenAICompatibleProviderType;
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ModelName { get; set; } = string.Empty;
    public string? ApiKeyProtected { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 180;
    public int MaxOutputTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.2;
    public bool ToolCallingEnabled { get; set; } = true;
    public string GlobalSystemPrompt { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
}
