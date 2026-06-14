using System.ComponentModel.DataAnnotations;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class AiAssistantSettingsPageViewModel
{
    [Display(Name = "AI assistant enabled")]
    public bool AssistantEnabled { get; set; }
    [Display(Name = "In-app web chat enabled")]
    public bool WebChatEnabled { get; set; }
    [Display(Name = "Telegram AI chat enabled")]
    public bool TelegramChatEnabled { get; set; }
    [Display(Name = "AI memory enabled")]
    public bool MemoryEnabled { get; set; }
    [Display(Name = "AI debug logging enabled")]
    public bool DebugLoggingEnabled { get; set; }

    [Display(Name = "Provider display name")]
    [StringLength(128)]
    public string ProviderDisplayName { get; set; } = "Local Ollama";
    [Display(Name = "Provider type")]
    [StringLength(64)]
    public string ProviderType { get; set; } = "OpenAICompatible";
    [Display(Name = "Base URL")]
    [StringLength(2048)]
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    [Display(Name = "Model name")]
    [StringLength(255)]
    public string ModelName { get; set; } = string.Empty;
    [Display(Name = "API key secret")]
    [StringLength(4096)]
    public string? ApiKey { get; set; }
    [Display(Name = "Clear saved API key")]
    public bool ClearApiKey { get; set; }
    public bool ApiKeyConfigured { get; set; }

    [Display(Name = "Request timeout seconds")]
    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 60;
    [Display(Name = "Max output tokens")]
    [Range(64, 32768)]
    public int MaxOutputTokens { get; set; } = 2048;
    [Display(Name = "Temperature")]
    [Range(0, 2)]
    public double Temperature { get; set; } = 0.2;
    [Display(Name = "Tool calling enabled")]
    public bool ToolCallingEnabled { get; set; } = true;

    [Display(Name = "Global assistant prompt / custom instructions")]
    [StringLength(20000)]
    public string GlobalSystemPrompt { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
    public bool Saved { get; set; }
}
