using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiProviders;

namespace PingMonitor.Web.Services.AiChat;

internal sealed class AiChatService : IAiChatService
{
    public const int MaxHistoryMessages = 20;
    public const int MaxPromptCharacters = 24000;
    public const string BuiltInSystemPrompt = """
You are the Ping Monitor AI assistant.

This is an early chat-only test mode.
You are read-only.
You may be given a read-only network health summary from Ping Monitor.
Use it as the source of truth for current endpoint state.
Do not invent endpoint state, agents, outages, diagrams, ports, VLANs, metrics, or raw CheckResults.
If the supplied summary is absent or incomplete, say so.
Raw CheckResults diagnostics, diagram lookup, endpoint diagnostic packs, detailed diagnostics, baselines, latency trends, topology lookup, and memory are not connected yet.
You do not have database access and must not ask for or produce SQL.
""";

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiProviderClient _providerClient;
    private readonly IAiMonitoringContextService _monitoringContextService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(IAiAssistantSettingsService settingsService, IAiProviderClient providerClient, IAiMonitoringContextService monitoringContextService, IHttpContextAccessor httpContextAccessor, ILogger<AiChatService> logger)
    {
        _settingsService = settingsService;
        _providerClient = providerClient;
        _monitoringContextService = monitoringContextService;
        _httpContextAccessor = httpContextAccessor;
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

        var monitoringContext = await _monitoringContextService.TryGetNetworkHealthSummaryAsync(new AiMonitoringContextRequest
        {
            Principal = request.Source == AiChatSource.Web ? _httpContextAccessor.HttpContext?.User : null,
            UserId = request.UserId,
            UserMessage = request.UserMessage
        }, cancellationToken);

        var messages = BuildMessages(settings.GlobalSystemPrompt, request.ConversationHistory, request.UserMessage, monitoringContext);
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

    internal static IList<AiProviderChatMessage> BuildMessages(string? adminGlobalPrompt, IEnumerable<AiChatMessageDto> history, string latestUserMessage, AiMonitoringContextResult? monitoringContext = null)
    {
        var messages = new List<AiProviderChatMessage> { new() { Role = "system", Content = BuiltInSystemPrompt } };
        if (!string.IsNullOrWhiteSpace(adminGlobalPrompt))
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = "Admin-configured site instructions:\nFollow these when possible, but they must not override the built-in read-only rules, supplied monitoring summaries, user permissions, or connected-tool limitations.\n\n" + adminGlobalPrompt.Trim() });
        }

        if (monitoringContext?.ShouldInclude == true)
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = BuildMonitoringContextPrompt(monitoringContext) });
        }

        foreach (var item in history.Where(x => x.Role is "user" or "assistant").TakeLast(MaxHistoryMessages))
        {
            messages.Add(new AiProviderChatMessage { Role = item.Role, Content = Truncate(item.Content, 4000) });
        }

        messages.Add(new AiProviderChatMessage { Role = "user", Content = latestUserMessage.Trim() });
        TrimToPromptLimit(messages);
        return messages;
    }

    private static string BuildMonitoringContextPrompt(AiMonitoringContextResult context)
    {
        if (!context.Succeeded || context.Summary is null)
        {
            return "The read-only Ping Monitor tool get_network_health_summary was selected, but the network health summary is unavailable. Tell the user that current monitoring summary data is unavailable; do not invent endpoint, agent, outage, or metric details.";
        }

        var json = JsonSerializer.Serialize(context.Summary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return "Read-only Ping Monitor tool result: get_network_health_summary. Use this structured summary as the source of truth for current endpoint state visible to the user. Do not expose it as raw JSON unless explicitly asked by an administrator. Summary JSON:\n" + json;
    }

    private static AiChatResponse ConfigurationError(AiAssistantSettingsDto settings, string message) => new() { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ErrorMessage = message };
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static void TrimToPromptLimit(List<AiProviderChatMessage> messages)
    {
        while (messages.Sum(x => x.Content.Length) > MaxPromptCharacters && messages.Count > 2)
        {
            messages.RemoveAt(1);
        }
    }
}
