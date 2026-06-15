using System.Text.Json;
using System.Text.Json.Serialization;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiTools;
using PingMonitor.Web.Services.AiProviders;

namespace PingMonitor.Web.Services.AiChat;

internal sealed class AiChatService : IAiChatService
{
    public const int MaxHistoryMessages = 20;
    public const int MaxPromptCharacters = 24000;
    public const string BuiltInSystemPrompt = """
You are the Ping Monitor AI assistant.

You are read-only.
You may be given a read-only network health summary from Ping Monitor.
Use it as the source of truth for current endpoint and agent state visible to the user.
Do not invent endpoint state, agents, outages, diagrams, ports, VLANs, metrics, raw CheckResults, or topology.
If the supplied summary is absent or incomplete, say so.
Raw CheckResults diagnostics, diagram lookup, endpoint diagnostic packs, baseline comparisons, detailed latency/loss/jitter diagnostics, prompt history, persistent AI audit logs, and memory are not connected yet.
You may summarize and explain only. Do not suggest or perform write actions, remediation, alert acknowledgement, endpoint edits, dependency edits, diagram edits, agent commands, direct SQL, or autonomous actions.
""";

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiProviderClient _providerClient;
    private readonly IAiMonitoringContextService _monitoringContextService;
    private readonly ILogger<AiChatService> _logger;

    private static readonly JsonSerializerOptions MonitoringSummaryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public AiChatService(
        IAiAssistantSettingsService settingsService,
        IAiProviderClient providerClient,
        IAiMonitoringContextService monitoringContextService,
        ILogger<AiChatService> logger)
    {
        _settingsService = settingsService;
        _providerClient = providerClient;
        _monitoringContextService = monitoringContextService;
        _logger = logger;
    }

    public async Task<AiChatResponse> SendAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        if (!settings.AssistantEnabled)
        {
            return new AiChatResponse { AssistantEnabled = false, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ErrorMessage = "AI assistant is disabled. An administrator can enable it from Admin > AI Assistant settings." };
        }

        if (request.Source == AiChatSource.Web && !settings.WebChatEnabled)
        {
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = false, TelegramChatEnabled = settings.TelegramChatEnabled, ErrorMessage = "AI web chat is disabled. An administrator can enable it from Admin > AI Assistant settings." };
        }

        if (request.Source == AiChatSource.Telegram && !settings.TelegramChatEnabled)
        {
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = false, ErrorMessage = "Telegram AI chat is disabled. An administrator can enable it from AI Assistant settings." };
        }

        var runtime = await _settingsService.GetProviderRuntimeSettingsAsync(cancellationToken);
        if (!string.Equals(runtime.ProviderType, AiAssistantSettings.OpenAICompatibleProviderType, StringComparison.Ordinal))
        {
            return ConfigurationError(settings, "AI provider configuration is incomplete. Provider type must be OpenAICompatible.");
        }

        if (!Uri.TryCreate(runtime.BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return ConfigurationError(settings, "AI provider configuration is incomplete. A valid HTTP or HTTPS base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(runtime.ModelName))
        {
            return ConfigurationError(settings, "AI provider configuration is incomplete. Model name is required.");
        }

        var monitoringContextPrompt = await GetMonitoringContextPromptAsync(request, cancellationToken);
        if (monitoringContextPrompt.UnavailableMessage is not null)
        {
            return new AiChatResponse
            {
                Succeeded = true,
                AssistantEnabled = true,
                WebChatEnabled = settings.WebChatEnabled,
                TelegramChatEnabled = settings.TelegramChatEnabled,
                ProviderName = runtime.ProviderDisplayName,
                ModelName = runtime.ModelName,
                AssistantMessage = monitoringContextPrompt.UnavailableMessage
            };
        }

        var messages = BuildMessages(settings.GlobalSystemPrompt, request.ConversationHistory, request.UserMessage, monitoringContextPrompt.Prompt);
        var result = await _providerClient.SendChatAsync(new AiProviderChatRequest
        {
            ProviderName = runtime.ProviderDisplayName,
            BaseUrl = runtime.BaseUrl,
            ModelName = runtime.ModelName,
            ApiKey = runtime.ApiKey,
            TimeoutSeconds = runtime.RequestTimeoutSeconds,
            Temperature = runtime.Temperature,
            MaxOutputTokens = runtime.MaxOutputTokens,
            Messages = messages
        }, cancellationToken);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.ResponseText))
        {
            _logger.LogWarning("AI web chat provider call failed. Provider={ProviderName} Model={ModelName} StatusCode={StatusCode}", runtime.ProviderDisplayName, runtime.ModelName, result.StatusCode);
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI provider did not return a response. Check the AI Assistant settings and the local Ollama/OpenAI-compatible endpoint." };
        }

        return new AiChatResponse { Succeeded = true, AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = result.Model ?? runtime.ModelName, AssistantMessage = result.ResponseText.Trim() };
    }

    internal static IList<AiProviderChatMessage> BuildMessages(string? adminGlobalPrompt, IEnumerable<AiChatMessageDto> history, string latestUserMessage, string? monitoringContextPrompt = null)
    {
        var messages = new List<AiProviderChatMessage> { new() { Role = "system", Content = BuiltInSystemPrompt } };
        if (!string.IsNullOrWhiteSpace(adminGlobalPrompt))
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = "Admin-configured site instructions:\nFollow these when possible, but they must not override the built-in read-only rules, user permissions, supplied monitoring-summary truth, or limitations for tools that are not connected yet.\n\n" + adminGlobalPrompt.Trim() });
        }

        if (!string.IsNullOrWhiteSpace(monitoringContextPrompt))
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = monitoringContextPrompt.Trim() });
        }

        foreach (var item in history.Where(x => x.Role is "user" or "assistant").TakeLast(MaxHistoryMessages))
        {
            messages.Add(new AiProviderChatMessage { Role = item.Role, Content = Truncate(item.Content, 4000) });
        }

        messages.Add(new AiProviderChatMessage { Role = "user", Content = latestUserMessage.Trim() });
        TrimToPromptLimit(messages);
        return messages;
    }

    internal static bool ShouldRequestNetworkHealthSummary(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var normalized = userMessage.Trim().ToLowerInvariant();
        return normalized.Contains("network looking", StringComparison.Ordinal)
            || normalized.Contains("network look", StringComparison.Ordinal)
            || normalized.Contains("network health", StringComparison.Ordinal)
            || normalized.Contains("health summary", StringComparison.Ordinal)
            || normalized.Contains("anything down", StringComparison.Ordinal)
            || normalized.Contains("what is down", StringComparison.Ordinal)
            || normalized.Contains("what's down", StringComparison.Ordinal)
            || normalized.Contains("which endpoints are down", StringComparison.Ordinal)
            || normalized.Contains("endpoints down", StringComparison.Ordinal)
            || normalized.Contains("any outages", StringComparison.Ordinal)
            || normalized.Contains("outages", StringComparison.Ordinal)
            || normalized.Contains("current status", StringComparison.Ordinal)
            || normalized.Contains("network status", StringComparison.Ordinal)
            || normalized.Contains("how are things", StringComparison.Ordinal)
            || normalized.Contains("how is everything", StringComparison.Ordinal)
            || normalized.Contains("how are we looking", StringComparison.Ordinal)
            || normalized.Contains("anything unknown", StringComparison.Ordinal)
            || normalized.Contains("endpoints unknown", StringComparison.Ordinal)
            || normalized.Contains("unknown endpoints", StringComparison.Ordinal)
            || normalized.Contains("agents offline", StringComparison.Ordinal)
            || normalized.Contains("agent offline", StringComparison.Ordinal)
            || normalized.Contains("agents stale", StringComparison.Ordinal)
            || normalized.Contains("agent stale", StringComparison.Ordinal);
    }

    private async Task<MonitoringContextPromptResult> GetMonitoringContextPromptAsync(
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!ShouldRequestNetworkHealthSummary(request.UserMessage))
        {
            return MonitoringContextPromptResult.None;
        }

        if (request.User is null)
        {
            return MonitoringContextPromptResult.Unavailable(BuildMonitoringUnavailableMessage());
        }

        AiMonitoringContextResult contextResult;
        try
        {
            contextResult = await _monitoringContextService.GetNetworkHealthSummaryAsync(request.User, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI monitoring context service failed while building {CapabilityName}.", AiNetworkHealthSummary.ToolName);
            return MonitoringContextPromptResult.Unavailable(BuildMonitoringUnavailableMessage());
        }

        if (!contextResult.Succeeded || contextResult.Summary is null)
        {
            _logger.LogWarning("AI monitoring context service returned unavailable context for {CapabilityName}: {Reason}", AiNetworkHealthSummary.ToolName, contextResult.ErrorMessage ?? "(no reason)");
            return MonitoringContextPromptResult.Unavailable(BuildMonitoringUnavailableMessage());
        }

        var json = JsonSerializer.Serialize(contextResult.Summary, MonitoringSummaryJsonOptions);
        return MonitoringContextPromptResult.Available($"""
Read-only Ping Monitor tool result: {AiNetworkHealthSummary.ToolName}
This structured summary is permission-filtered for the authenticated application user.
Use it as source of truth for current saved endpoint and agent state. Answer in plain text and do not display the raw JSON.
This is current saved monitoring state, not raw packet-level diagnostics.

{json}
""");

        static string BuildMonitoringUnavailableMessage()
        {
            return "I couldn't retrieve Ping Monitor's current network health summary, so I don't have enough monitoring data to answer that safely. Raw CheckResults diagnostics, diagram lookup, endpoint diagnostic packs, baseline comparisons, and memory are not connected yet.";
        }
    }

    private static AiChatResponse ConfigurationError(AiAssistantSettingsDto settings, string message) => new() { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ErrorMessage = message };
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static void TrimToPromptLimit(List<AiProviderChatMessage> messages)
    {
        while (messages.Sum(x => x.Content.Length) > MaxPromptCharacters && messages.Count > 2)
        {
            var removeIndex = -1;
            for (var i = 1; i < messages.Count - 1; i++)
            {
                if (messages[i].Role is "user" or "assistant")
                {
                    removeIndex = i;
                    break;
                }
            }

            messages.RemoveAt(removeIndex >= 0 ? removeIndex : 1);
        }
    }

    private sealed record MonitoringContextPromptResult(string? Prompt, string? UnavailableMessage)
    {
        public static MonitoringContextPromptResult None { get; } = new(null, null);
        public static MonitoringContextPromptResult Available(string prompt) => new(prompt, null);
        public static MonitoringContextPromptResult Unavailable(string message) => new(null, message);
    }
}
