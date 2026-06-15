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
You may be given read-only Ping Monitor context including network health summaries, endpoint lookup results, endpoint diagnostics packs, bounded uptime/state interval summaries, and bounded check-result summaries or series.
Use supplied context as the source of truth for current endpoint and agent state visible to the user.
Do not invent endpoint states, uptime, CheckResults, RTT, packet loss, agents, diagrams, ports, VLANs, outage history, metrics, or topology.
If endpoint diagnostics context is missing, ambiguous, or incomplete, say so.
Diagram lookup, switch port/VLAN answers, persistent memory, prompt history, persistent AI audit logs, write actions, formal tool-calling, and unrestricted raw CheckResults export are not connected yet.
You may summarize and explain only. Do not suggest or perform write actions, remediation, alert acknowledgement, endpoint edits, dependency edits, diagram edits, agent commands, direct SQL, or autonomous actions.
""";

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiProviderClient _providerClient;
    private readonly IAiMonitoringContextService _monitoringContextService;
    private readonly IAiEndpointLookupService _endpointLookupService;
    private readonly IAiEndpointDiagnosticsService _endpointDiagnosticsService;
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
        IAiEndpointLookupService endpointLookupService,
        IAiEndpointDiagnosticsService endpointDiagnosticsService,
        ILogger<AiChatService> logger)
    {
        _settingsService = settingsService;
        _providerClient = providerClient;
        _monitoringContextService = monitoringContextService;
        _endpointLookupService = endpointLookupService;
        _endpointDiagnosticsService = endpointDiagnosticsService;
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


    internal static bool ShouldRequestEndpointDiagnostics(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return false;
        var normalized = userMessage.Trim().ToLowerInvariant();
        return normalized.Contains("what is going on with", StringComparison.Ordinal)
            || normalized.Contains("what's going on with", StringComparison.Ordinal)
            || normalized.Contains("uptime", StringComparison.Ordinal)
            || normalized.Contains("down today", StringComparison.Ordinal)
            || normalized.Contains("been down", StringComparison.Ordinal)
            || normalized.Contains("flapping", StringComparison.Ordinal)
            || normalized.Contains("recent check", StringComparison.Ordinal)
            || normalized.Contains("check pattern", StringComparison.Ordinal)
            || normalized.Contains("packet loss", StringComparison.Ordinal)
            || normalized.Contains("latency", StringComparison.Ordinal)
            || normalized.Contains("rtt", StringComparison.Ordinal)
            || normalized.Contains("reliable", StringComparison.Ordinal);
    }

    internal static string RequestedWindowFromMessage(string userMessage)
    {
        var normalized = userMessage.ToLowerInvariant();
        if (normalized.Contains("1h", StringComparison.Ordinal) || normalized.Contains("last hour", StringComparison.Ordinal)) return "1h";
        if (normalized.Contains("6h", StringComparison.Ordinal) || normalized.Contains("six hours", StringComparison.Ordinal)) return "6h";
        if (normalized.Contains("7d", StringComparison.Ordinal) || normalized.Contains("week", StringComparison.Ordinal)) return "7d";
        if (normalized.Contains("today", StringComparison.Ordinal)) return "today";
        if (normalized.Contains("24h", StringComparison.Ordinal) || normalized.Contains("24 hours", StringComparison.Ordinal)) return "24h";
        if (normalized.Contains("month", StringComparison.Ordinal) || normalized.Contains("year", StringComparison.Ordinal)) return "30d";
        return "24h";
    }

    private async Task<MonitoringContextPromptResult> GetMonitoringContextPromptAsync(
        AiChatRequest request,
        CancellationToken cancellationToken)
    {
        var wantsEndpointDiagnostics = ShouldRequestEndpointDiagnostics(request.UserMessage);
        var wantsNetworkSummary = ShouldRequestNetworkHealthSummary(request.UserMessage);
        if (!wantsEndpointDiagnostics && !wantsNetworkSummary)
        {
            return MonitoringContextPromptResult.None;
        }

        if (request.User is null)
        {
            return MonitoringContextPromptResult.Unavailable(BuildMonitoringUnavailableMessage());
        }

        if (wantsEndpointDiagnostics)
        {
            var lookup = await _endpointLookupService.SearchEndpointsAsync(request.User, request.UserMessage, cancellationToken);
            if (lookup.Ambiguous)
            {
                var choices = string.Join("\n", lookup.Matches.Select(x => $"- {x.Name} ({x.Target})"));
                return MonitoringContextPromptResult.Unavailable($"I found multiple visible endpoints that could match. Which one do you mean?\n{choices}");
            }
            if (lookup.StrongMatch is null)
            {
                return MonitoringContextPromptResult.Unavailable(lookup.Message ?? "No matching visible endpoint was found.");
            }
            var diagnostics = await _endpointDiagnosticsService.GetDiagnosticsPackAsync(request.User, lookup.StrongMatch.EndpointId, RequestedWindowFromMessage(request.UserMessage), cancellationToken);
            if (!diagnostics.Succeeded || diagnostics.Pack is null)
            {
                return MonitoringContextPromptResult.Unavailable(diagnostics.ErrorMessage ?? "I couldn't retrieve endpoint diagnostics safely.");
            }
            var diagnosticsJson = JsonSerializer.Serialize(diagnostics.Pack, MonitoringSummaryJsonOptions);
            return MonitoringContextPromptResult.Available($"""
Read-only Ping Monitor tool result: {AiEndpointDiagnosticsPack.ToolName}
This structured endpoint diagnostics pack is permission-filtered for the authenticated application user.
Use it as source of truth. Answer in plain text and do not display the raw JSON.
CheckResults context is bounded; UNKNOWN is not DOWN; SUPPRESSED is not downtime.

{diagnosticsJson}
""");
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
            return "I couldn't retrieve Ping Monitor's current network health summary, so I don't have enough monitoring data to answer that safely. Endpoint diagnostics are bounded. Diagram lookup, switch port/VLAN answers, memory, write actions, and unrestricted raw CheckResults export are not connected yet.";
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
