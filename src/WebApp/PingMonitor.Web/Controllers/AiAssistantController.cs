using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.ViewModels.AiAssistant;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("ai-assistant")]
public sealed class AiAssistantController : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxStoredMessages = 20;

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiChatService _chatService;

    public AiAssistantController(IAiAssistantSettingsService settingsService, IAiChatService chatService)
    {
        _settingsService = settingsService;
        _chatService = chatService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        var model = new AiAssistantPageViewModel
        {
            AssistantEnabled = settings.AssistantEnabled,
            WebChatEnabled = settings.WebChatEnabled
        };
        ApplyDisabledMessage(model);
        model.HistoryJson = SerializeHistory(model.Messages);
        return View("Index", model);
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromForm] AiAssistantPageViewModel model, CancellationToken cancellationToken)
    {
        model.Messages = ParseHistory(model.HistoryJson);

        if (string.IsNullOrWhiteSpace(model.Message))
        {
            await PopulateFlagsAsync(model, cancellationToken);
            model.ErrorMessage = "Enter a message before sending.";
            model.HistoryJson = SerializeHistory(model.Messages);
            return View("Index", model);
        }

        if (model.Message.Length > AiAssistantPageViewModel.MaxUserMessageLength)
        {
            await PopulateFlagsAsync(model, cancellationToken);
            model.ErrorMessage = "Message must be 4000 characters or fewer.";
            model.HistoryJson = SerializeHistory(model.Messages);
            return View("Index", model);
        }

        var userMessage = model.Message.Trim();
        var response = await _chatService.SendAsync(new AiChatRequest { ConversationHistory = model.Messages, UserMessage = userMessage, User = User }, cancellationToken);
        model.AssistantEnabled = response.AssistantEnabled;
        model.WebChatEnabled = response.WebChatEnabled;
        model.Message = string.Empty;

        if (response.Succeeded && !string.IsNullOrWhiteSpace(response.AssistantMessage))
        {
            model.Messages.Add(new AiChatMessageDto { Role = "user", Content = userMessage });
            model.Messages.Add(new AiChatMessageDto { Role = "assistant", Content = response.AssistantMessage });
            model.Messages = BoundHistory(model.Messages);
            model.StatusMessage = "AI assistant response received.";
        }
        else
        {
            model.ErrorMessage = response.ErrorMessage ?? "The AI provider did not return a response. Check the AI Assistant settings and the local Ollama/OpenAI-compatible endpoint.";
        }

        model.HistoryJson = SerializeHistory(model.Messages);
        return View("Index", model);
    }

    [HttpPost("clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        var model = new AiAssistantPageViewModel { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, StatusMessage = "Conversation cleared." };
        ApplyDisabledMessage(model);
        model.HistoryJson = SerializeHistory(model.Messages);
        return View("Index", model);
    }

    internal static IList<AiChatMessageDto> BoundHistory(IEnumerable<AiChatMessageDto> messages) => messages
        .Where(x => x.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(x.Content))
        .Select(x => new AiChatMessageDto { Role = x.Role, Content = x.Content.Length <= AiAssistantPageViewModel.MaxUserMessageLength ? x.Content : x.Content[..AiAssistantPageViewModel.MaxUserMessageLength] })
        .TakeLast(MaxStoredMessages)
        .ToList();

    private async Task PopulateFlagsAsync(AiAssistantPageViewModel model, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        model.AssistantEnabled = settings.AssistantEnabled;
        model.WebChatEnabled = settings.WebChatEnabled;
        ApplyDisabledMessage(model);
    }

    private static IList<AiChatMessageDto> ParseHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<AiChatMessageDto>();
        try { return BoundHistory(JsonSerializer.Deserialize<List<AiChatMessageDto>>(json, JsonOptions) ?? []); }
        catch (JsonException) { return new List<AiChatMessageDto>(); }
    }

    private static string SerializeHistory(IEnumerable<AiChatMessageDto> messages) => JsonSerializer.Serialize(BoundHistory(messages), JsonOptions);

    private static void ApplyDisabledMessage(AiAssistantPageViewModel model)
    {
        if (!model.AssistantEnabled || !model.WebChatEnabled)
        {
            model.ErrorMessage ??= "AI web chat is disabled. An administrator can enable it from Admin > AI Assistant settings.";
        }
    }
}
