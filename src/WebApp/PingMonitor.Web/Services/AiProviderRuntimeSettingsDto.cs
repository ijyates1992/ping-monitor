namespace PingMonitor.Web.Services;

public sealed class AiProviderRuntimeSettingsDto
{
    public string ProviderDisplayName { get; set; } = "Local Ollama";
    public string ProviderType { get; set; } = "OpenAICompatible";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ModelName { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.2;
    public bool ToolCallingEnabled { get; set; } = true;
}
