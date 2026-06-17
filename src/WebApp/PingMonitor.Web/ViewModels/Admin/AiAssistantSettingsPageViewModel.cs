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
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 180;
    [Display(Name = "Max output tokens")]
    [Range(64, 32768)]
    public int MaxOutputTokens { get; set; } = 2048;
    [Display(Name = "Temperature")]
    [Range(0, 2)]
    public double Temperature { get; set; } = 0.2;
    [Display(Name = "Tool calling enabled")]
    public bool ToolCallingEnabled { get; set; } = true;


    [Display(Name = "Maximum tool rounds per request")]
    [Range(0, 10)] public int MaxToolRounds { get; set; } = 3;
    [Display(Name = "Maximum tool calls per round")]
    [Range(0, 20)] public int MaxToolCallsPerRound { get; set; } = 5;
    [Display(Name = "Maximum total context returned by tools (characters)")]
    [Range(1000, 200000)] public int MaxTotalToolResultCharacters { get; set; } = 24000;
    [Display(Name = "Maximum single tool result (characters)")]
    [Range(1000, 100000)] public int MaxSingleToolResultCharacters { get; set; } = 12000;
    [Display(Name = "Maximum endpoint search results (items)")]
    [Range(1, 100)] public int MaxEndpointSearchResults { get; set; } = 10;
    [Display(Name = "Maximum recent check samples per endpoint (items)")]
    [Range(1, 5000)] public int MaxEndpointMetricsSampleTailPoints { get; set; } = 120;
    [Display(Name = "Maximum endpoint transitions returned (items)")]
    [Range(1, 500)] public int MaxEndpointTransitionItems { get; set; } = 20;
    [Display(Name = "Maximum endpoint failure clusters returned (items)")]
    [Range(1, 500)] public int MaxEndpointFailureClusters { get; set; } = 10;
    [Display(Name = "Default endpoint metrics window")] public string DefaultEndpointMetricsWindow { get; set; } = "24h";
    [Display(Name = "Maximum endpoint metrics window")] public string MaximumEndpointMetricsWindow { get; set; } = "7d";
    [Display(Name = "Maximum diagram list results (items)")]
    [Range(1, 500)] public int MaxDiagramListResults { get; set; } = 50;
    [Display(Name = "Maximum diagram node search results (items)")]
    [Range(1, 200)] public int MaxDiagramNodeSearchResults { get; set; } = 10;
    [Display(Name = "Maximum diagram links returned (items)")]
    [Range(1, 1000)] public int MaxDiagramConnectionResults { get; set; } = 50;
    [Display(Name = "Maximum full diagram nodes returned (items)")]
    [Range(1, 1000)] public int MaxFullDiagramNodesReturned { get; set; } = 100;
    [Display(Name = "Maximum full diagram links returned (items)")]
    [Range(1, 1500)] public int MaxFullDiagramLinksReturned { get; set; } = 150;
    [Display(Name = "Maximum diagram tool result (characters)")]
    [Range(1000, 100000)] public int MaxDiagramToolResultCharacters { get; set; } = 30000;
    [Display(Name = "Maximum note/metadata characters per diagram item")]
    [Range(50, 5000)] public int MaxDiagramItemMetadataCharacters { get; set; } = 500;
    [Display(Name = "Maximum memory search results (items)")]
    [Range(1, 100)] public int MaxMemorySearchResults { get; set; } = 10;
    [Display(Name = "Maximum memory content returned (characters)")]
    [Range(100, 10000)] public int MaxMemoryContentCharacters { get; set; } = 1000;
    [Display(Name = "Maximum runtime largest tables returned (items)")]
    [Range(1, 100)] public int MaxRuntimeLargestTablesReturned { get; set; } = 10;

    [Display(Name = "Global assistant prompt / custom instructions")]
    [StringLength(20000)]
    public string GlobalSystemPrompt { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }
    public bool Saved { get; set; }
    public AiProviderTestConnectionViewModel? TestResult { get; set; }
}

public sealed class AiProviderTestConnectionViewModel
{
    public bool Succeeded { get; set; }
    public string? ResponseText { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
}
