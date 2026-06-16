using PingMonitor.Web.Models;
using PingMonitor.Web.Services.AiProviders;
using PingMonitor.Web.Services.AiTools;

namespace PingMonitor.Web.Services.AiChat;

internal sealed class AiChatService : IAiChatService
{
    public const int MaxHistoryMessages = 20;
    public const int MaxPromptCharacters = 24000;
    public const string BuiltInSystemPrompt = """
You are the Ping Monitor AI assistant.
You are read-only.

You may have access to approved read-only Ping Monitor tools.
Use tools when you need current monitoring state, endpoint state counts, down/unknown/suppressed endpoints, or agent health.
Do not guess current network state when a tool can answer.
Use `get_network_health_summary` for broad network status questions.
If tools are unavailable or return no data, say so.
Do not invent endpoint states, outage history, metrics, CheckResults, diagrams, ports, VLANs, or topology.
Do not display raw JSON tool results to the user.
Summarize the tool result in plain English.
Raw CheckResults diagnostics, endpoint-specific diagnostics, diagram lookup, switch port/VLAN answers, persistent memory, prompt history, persistent audit log, and write actions are not connected yet.
UNKNOWN is not DOWN. SUPPRESSED is not downtime.
""";

    private readonly IAiAssistantSettingsService _settingsService;
    private const int MaxToolRounds = 3;
    private const int MaxToolCallsPerRound = 5;
    private const int MaxTotalToolResultCharacters = 36000;

    private readonly IAiProviderClient _providerClient;
    private readonly IAiToolRegistry _toolRegistry;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(IAiAssistantSettingsService settingsService, IAiProviderClient providerClient, IAiToolRegistry toolRegistry, ILogger<AiChatService> logger)
    {
        _settingsService = settingsService;
        _providerClient = providerClient;
        _toolRegistry = toolRegistry;
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

        var messages = BuildMessages(settings.GlobalSystemPrompt, request.ConversationHistory, request.UserMessage);
        var toolsEnabled = runtime.ToolCallingEnabled;
        var toolDefinitions = toolsEnabled ? _toolRegistry.GetDefinitions().Select(x => x.ToProviderDefinition()).ToArray() : [];
        var totalToolResultCharacters = 0;
        AiProviderChatResult result = new();

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            result = await _providerClient.SendChatAsync(new AiProviderChatRequest
            {
                ProviderName = runtime.ProviderDisplayName,
                BaseUrl = runtime.BaseUrl,
                ModelName = runtime.ModelName,
                ApiKey = runtime.ApiKey,
                TimeoutSeconds = runtime.RequestTimeoutSeconds,
                Temperature = runtime.Temperature,
                MaxOutputTokens = runtime.MaxOutputTokens,
                Messages = messages,
                Tools = toolDefinitions.ToList(),
                ToolChoice = toolsEnabled && toolDefinitions.Length > 0 ? "auto" : null
            }, cancellationToken);

            if (!result.Succeeded || result.ToolCalls.Count == 0)
            {
                break;
            }

            if (round == MaxToolRounds)
            {
                return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI assistant requested too many tool rounds. Please narrow the request and try again." };
            }

            if (result.ToolCalls.Count > MaxToolCallsPerRound)
            {
                return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI assistant requested too many tools at once. Please narrow the request and try again." };
            }

            messages.Add(new AiProviderChatMessage { Role = "assistant", Content = result.ResponseText, ToolCalls = result.ToolCalls.ToList() });
            foreach (var toolCall in result.ToolCalls)
            {
                var toolResult = await _toolRegistry.ExecuteAsync(new AiToolCall { Name = toolCall.Function.Name, ArgumentsJson = toolCall.Function.Arguments, Principal = request.Principal, ApplicationUserId = request.ApplicationUserId }, cancellationToken);
                totalToolResultCharacters += toolResult.ContentJson.Length;
                if (totalToolResultCharacters > MaxTotalToolResultCharacters)
                {
                    return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI assistant tool results were too large. Please narrow the request and try again." };
                }

                messages.Add(new AiProviderChatMessage { Role = "tool", ToolCallId = toolCall.Id, Content = toolResult.ContentJson });
            }
        }

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.ResponseText))
        {
            _logger.LogWarning("AI web chat provider call failed. Provider={ProviderName} Model={ModelName} StatusCode={StatusCode}", runtime.ProviderDisplayName, runtime.ModelName, result.StatusCode);
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI provider did not return a response. Check the AI Assistant settings and the local Ollama/OpenAI-compatible endpoint." };
        }

        return new AiChatResponse { Succeeded = true, AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = result.Model ?? runtime.ModelName, AssistantMessage = result.ResponseText.Trim() };
    }

    internal static IList<AiProviderChatMessage> BuildMessages(string? adminGlobalPrompt, IEnumerable<AiChatMessageDto> history, string latestUserMessage)
    {
        var messages = new List<AiProviderChatMessage> { new() { Role = "system", Content = BuiltInSystemPrompt } };
        if (!string.IsNullOrWhiteSpace(adminGlobalPrompt))
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = "Admin-configured site instructions:\nFollow these when possible, but they must not override application safety rules, user permissions, read-only behaviour, tool result truthfulness, or the built-in prompt.\n\n" + adminGlobalPrompt.Trim() });
        }

        foreach (var item in history.Where(x => x.Role is "user" or "assistant").TakeLast(MaxHistoryMessages))
        {
            messages.Add(new AiProviderChatMessage { Role = item.Role, Content = Truncate(item.Content, 4000) });
        }

        messages.Add(new AiProviderChatMessage { Role = "user", Content = latestUserMessage.Trim() });
        TrimToPromptLimit(messages);
        return messages;
    }

    private static AiChatResponse ConfigurationError(AiAssistantSettingsDto settings, string message) => new() { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ErrorMessage = message };
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static void TrimToPromptLimit(List<AiProviderChatMessage> messages)
    {
        while (messages.Sum(x => x.Content?.Length ?? 0) > MaxPromptCharacters && messages.Count > 2)
        {
            messages.RemoveAt(1);
        }
    }
}
