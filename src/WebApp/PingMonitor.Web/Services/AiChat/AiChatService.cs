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

Scheduled and event-triggered AI tasks can only be created, edited, enabled, disabled, or deleted from the web UI. You do not have tools to create or change them. If the user asks you to create or change a scheduled task, explain that they must use the Scheduled AI tasks page. If the user asks you to create or change an event-triggered task, explain that they must use the Event-triggered AI tasks page.

When running as an event-triggered AI task, use the supplied trigger context as the reason for the report. Use available read-only tools to gather supporting monitoring data. Do not invent event details. Do not claim the event caused a wider outage unless tools support that. Do not perform remediation or write actions.

You have access to approved read-only Ping Monitor tools.
Use tools when you need current monitoring state, endpoint state counts, down/unknown/suppressed endpoints, endpoint metrics, agent health, saved Network Diagram documentation, or current application runtime/build metadata.
Use `get_network_health_summary` for broad network status questions.
Use `get_application_runtime_info` for questions about the Ping Monitor app version, build, schema version, database size, runtime environment, startup gate, update/build status, or deployment/runtime details.
Do not guess version, schema, database size, or build status. Use the tool when available.
Do not expose or request secrets, connection strings, credentials, API keys, protected settings, filesystem paths, or host details that the tool does not provide.
If runtime info is redacted because the user is not an admin, say that detailed runtime information is admin-only.
Use Network Diagram tools for questions about saved topology documentation, device connections, switch ports, link labels, link media, link speed, LAG/LACP, VLANs, diagram areas, or diagram notes.
Network Diagram data is saved documentation only. It is not live switch port state, not SNMP discovery, and not authoritative live topology.
When answering from diagram tools, clearly say "According to the saved diagram..." or equivalent.
Do not claim a diagram link is currently up/down unless a monitoring tool separately says so.
Do not create or modify monitoring dependencies from diagram links.
If Network Diagram lookup is disabled, say Network Diagram lookup is disabled.

For endpoint-specific questions:
1. Use `search_endpoints` to resolve the endpoint name/target.
2. If one clear match is returned, use `get_endpoint_metrics_summary`.
3. If multiple matches are returned, ask the user which endpoint they mean.
4. If no match is returned, say no visible endpoint matched.

Use `get_endpoint_metrics_summary` for questions about uptime, downtime, UNKNOWN state, SUPPRESSED state, RTT, latency, jitter, packet loss, failed checks, recent check pattern, reliability, or flapping.
Do not guess endpoint state or metrics when tools can answer.
If tools are unavailable or return no data, say so.
AI tool results may be limited or truncated by administrator-configured context limits. If a tool result says it is truncated, mention that limitation and ask the user to narrow the request if needed.
Do not invent endpoint states, outage history, metrics, CheckResults, diagrams, ports, VLANs, links, devices, diagram names, or topology.
If a diagram tool returns no match or multiple matches, say so or ask the user to clarify.
Do not display raw JSON tool results to the user.
Summarize the tool result in plain English.
When summarizing endpoint metrics:
- state currentState first
- explain DOWN, UNKNOWN, and SUPPRESSED separately
- UNKNOWN is not DOWN
- SUPPRESSED is not downtime
- use RTT and jitter only if available
- say when sample counts are low or data is incomplete
- mention the applied time window
You have access to user-specific AI memory tools if memory is enabled.
Use search_user_memories when user wording may depend on aliases, preferences, or prior context.
Only use remember_user_memory when the user explicitly asks you to remember something.
Only create a memory when the user clearly asks you to remember something, such as "remember that..." or "from now on...".
Do not store live monitoring state, current endpoint health, raw metrics, uptime, RTT, packet loss, diagram facts, passwords, API keys, or secrets as memories.
When in doubt, ask for confirmation before creating a memory.
Memories are user-specific. Never claim a memory applies to other users.
Live Ping Monitor tool data overrides memory.
Do not store current endpoint state, uptime, RTT, packet loss, CheckResults, diagram facts, passwords, API keys, secrets, or temporary incident status as memory.
If a user asks you to forget/delete a memory, use delete_user_memory when a matching memory exists.
Prompt history, persistent audit log, and non-memory write actions are not connected yet.
""";

    private readonly IAiAssistantSettingsService _settingsService;

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
        var limits = settings.ToolLimits ?? new AiToolExecutionLimits();
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
        var toolsEnabled = runtime.ToolCallingEnabled && limits.MaxToolRounds > 0;
        var toolDefinitions = toolsEnabled ? _toolRegistry.GetDefinitions().Where(x => settings.MemoryEnabled || !x.Name.Contains("user_memor", StringComparison.Ordinal)).Select(x => x.ToProviderDefinition()).ToArray() : [];
        var totalToolResultCharacters = 0;
        AiProviderChatResult result = new();

        for (var round = 0; round <= limits.MaxToolRounds; round++)
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

            if (round == limits.MaxToolRounds)
            {
                return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI assistant requested too many tool rounds. Please narrow the request and try again." };
            }

            if (result.ToolCalls.Count > limits.MaxToolCallsPerRound)
            {
                return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI assistant requested too many tools at once. Please narrow the request and try again." };
            }

            messages.Add(new AiProviderChatMessage { Role = "assistant", Content = result.ResponseText, ToolCalls = result.ToolCalls.ToList() });
            foreach (var toolCall in result.ToolCalls)
            {
                var toolResult = await _toolRegistry.ExecuteAsync(new AiToolCall { Name = toolCall.Function.Name, ArgumentsJson = toolCall.Function.Arguments, Principal = request.Principal, ApplicationUserId = request.ApplicationUserId, CurrentUserMessage = request.UserMessage, ConversationSource = request.Source.ToString(), Limits = limits }, cancellationToken);
                if (toolResult.ContentJson.Length > limits.MaxSingleToolResultCharacters)
                {
                    return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = settings.WebChatEnabled, TelegramChatEnabled = settings.TelegramChatEnabled, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "An AI assistant tool result was too large. Please narrow the request and try again." };
                }

                totalToolResultCharacters += toolResult.ContentJson.Length;
                if (totalToolResultCharacters > limits.MaxTotalToolResultCharacters)
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
