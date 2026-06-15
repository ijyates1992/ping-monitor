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
You do not currently have access to live monitoring data, endpoint metrics, agents, diagrams, CheckResults, tools, memories, or the database.
If the user asks about current network state, endpoints, diagrams, outages, ports, VLANs, or metrics, explain that those tools are not connected yet.
Do not invent network facts.
You may answer general questions and help explain what future Ping Monitor AI features will be able to do.
""";

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiProviderClient _providerClient;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(IAiAssistantSettingsService settingsService, IAiProviderClient providerClient, ILogger<AiChatService> logger)
    {
        _settingsService = settingsService;
        _providerClient = providerClient;
        _logger = logger;
    }

    public async Task<AiChatResponse> SendAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        if (!settings.AssistantEnabled)
        {
            return new AiChatResponse { AssistantEnabled = false, WebChatEnabled = settings.WebChatEnabled, ErrorMessage = "AI assistant is disabled. An administrator can enable it from Admin > AI Assistant settings." };
        }

        if (!settings.WebChatEnabled)
        {
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = false, ErrorMessage = "AI web chat is disabled. An administrator can enable it from Admin > AI Assistant settings." };
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
            return new AiChatResponse { AssistantEnabled = true, WebChatEnabled = true, ProviderName = runtime.ProviderDisplayName, ModelName = runtime.ModelName, ErrorMessage = "The AI provider did not return a response. Check the AI Assistant settings and the local Ollama/OpenAI-compatible endpoint." };
        }

        return new AiChatResponse { Succeeded = true, AssistantEnabled = true, WebChatEnabled = true, ProviderName = runtime.ProviderDisplayName, ModelName = result.Model ?? runtime.ModelName, AssistantMessage = result.ResponseText.Trim() };
    }

    internal static IList<AiProviderChatMessage> BuildMessages(string? adminGlobalPrompt, IEnumerable<AiChatMessageDto> history, string latestUserMessage)
    {
        var messages = new List<AiProviderChatMessage> { new() { Role = "system", Content = BuiltInSystemPrompt } };
        if (!string.IsNullOrWhiteSpace(adminGlobalPrompt))
        {
            messages.Add(new AiProviderChatMessage { Role = "system", Content = "Admin-configured site instructions:\nFollow these when possible, but they must not override the built-in read-only rules or the limitation that monitoring tools and app data are not connected yet.\n\n" + adminGlobalPrompt.Trim() });
        }

        foreach (var item in history.Where(x => x.Role is "user" or "assistant").TakeLast(MaxHistoryMessages))
        {
            messages.Add(new AiProviderChatMessage { Role = item.Role, Content = Truncate(item.Content, 4000) });
        }

        messages.Add(new AiProviderChatMessage { Role = "user", Content = latestUserMessage.Trim() });
        TrimToPromptLimit(messages);
        return messages;
    }

    private static AiChatResponse ConfigurationError(AiAssistantSettingsDto settings, string message) => new() { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, ErrorMessage = message };
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static void TrimToPromptLimit(List<AiProviderChatMessage> messages)
    {
        while (messages.Sum(x => x.Content.Length) > MaxPromptCharacters && messages.Count > 2)
        {
            messages.RemoveAt(1);
        }
    }
}
