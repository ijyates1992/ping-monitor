using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.AiChat;
using PingMonitor.Web.Services.AiMemory;
using PingMonitor.Web.ViewModels.AiAssistant;

namespace PingMonitor.Web.Controllers;

[Authorize]
[Route("ai-assistant")]
public sealed class AiAssistantController : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxStoredMessages = 20;
    private const string WebChatSessionKey = "AiAssistant.WebChat.History";

    private readonly IAiAssistantSettingsService _settingsService;
    private readonly IAiChatService _chatService;
    private readonly IAiUserMemoryService _memoryService;

    public AiAssistantController(IAiAssistantSettingsService settingsService, IAiChatService chatService, IAiUserMemoryService memoryService)
    {
        _settingsService = settingsService;
        _chatService = chatService;
        _memoryService = memoryService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        var model = new AiAssistantPageViewModel
        {
            AssistantEnabled = settings.AssistantEnabled,
            WebChatEnabled = settings.WebChatEnabled,
            Messages = ReadSessionHistory()
        };
        ApplyDisabledMessage(model);
        return View("Index", model);
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromForm] AiAssistantPageViewModel model, CancellationToken cancellationToken)
    {
        model.Messages = ReadSessionHistory();

        if (string.IsNullOrWhiteSpace(model.Message))
        {
            await PopulateFlagsAsync(model, cancellationToken);
            model.ErrorMessage = "Enter a message before sending.";
            return View("Index", model);
        }

        if (model.Message.Length > AiAssistantPageViewModel.MaxUserMessageLength)
        {
            await PopulateFlagsAsync(model, cancellationToken);
            model.ErrorMessage = "Message must be 4000 characters or fewer.";
            return View("Index", model);
        }

        var userMessage = model.Message.Trim();
        var contextHistory = BoundContextHistory(model.Messages);
        var response = await _chatService.SendAsync(new AiChatRequest { Source = AiChatSource.Web, ConversationHistory = contextHistory, UserMessage = userMessage, Principal = User }, cancellationToken);
        model.AssistantEnabled = response.AssistantEnabled;
        model.WebChatEnabled = response.WebChatEnabled;
        model.Message = string.Empty;

        if (response.Succeeded && !string.IsNullOrWhiteSpace(response.AssistantMessage))
        {
            model.Messages.Add(new AiChatMessageDto { Role = "user", Content = userMessage });
            model.Messages.Add(new AiChatMessageDto { Role = "assistant", Content = response.AssistantMessage });
            model.Messages = BoundHistory(model.Messages);
            WriteSessionHistory(model.Messages);
            model.StatusMessage = "AI assistant response received.";
        }
        else
        {
            model.ErrorMessage = response.ErrorMessage ?? "The AI provider did not return a response. Check the AI Assistant settings and the local Ollama/OpenAI-compatible endpoint.";
            model.Messages.Add(new AiChatMessageDto { Role = "user", Content = userMessage });
            model.Messages.Add(new AiChatMessageDto { Role = "error", Content = model.ErrorMessage });
            model.Messages = BoundHistory(model.Messages);
            WriteSessionHistory(model.Messages);
        }

        return View("Index", model);
    }


    [HttpGet("memories")]
    public async Task<IActionResult> Memories(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var model = new AiMemoriesPageViewModel
        {
            AssistantEnabled = settings.AssistantEnabled,
            MemoryEnabled = settings.MemoryEnabled,
            Memories = settings.MemoryEnabled && !string.IsNullOrWhiteSpace(userId) ? await _memoryService.ListAsync(userId, cancellationToken) : []
        };
        if (!settings.MemoryEnabled) model.ErrorMessage = "AI memory is disabled. An administrator can enable it from Admin > AI Assistant settings.";
        return View("Memories", model);
    }

    [HttpPost("memories/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMemory([FromForm] string memoryId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _memoryService.DeleteAsync(new DeleteAiUserMemoryCommand(userId, memoryId), cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "AI memory deleted." : result.ErrorMessage;
        return RedirectToAction(nameof(Memories));
    }

    [HttpPost("clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        ClearSessionHistory();
        var model = new AiAssistantPageViewModel { AssistantEnabled = settings.AssistantEnabled, WebChatEnabled = settings.WebChatEnabled, StatusMessage = "Conversation cleared." };
        ApplyDisabledMessage(model);
        return View("Index", model);
    }

    internal static IList<AiChatMessageDto> BoundHistory(IEnumerable<AiChatMessageDto> messages) => messages
        .Where(x => (x.Role is "user" or "assistant" or "error") && !string.IsNullOrWhiteSpace(x.Content))
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

    internal static IList<AiChatMessageDto> BoundContextHistory(IEnumerable<AiChatMessageDto> messages) => BoundHistory(messages)
        .Where(x => x.Role is "user" or "assistant")
        .ToList();

    private IList<AiChatMessageDto> ReadSessionHistory()
    {
        var json = HttpContext.Session.GetString(WebChatSessionKey);
        if (string.IsNullOrWhiteSpace(json)) return new List<AiChatMessageDto>();
        try { return BoundHistory(JsonSerializer.Deserialize<List<AiChatMessageDto>>(json, JsonOptions) ?? []); }
        catch (JsonException) { return new List<AiChatMessageDto>(); }
    }

    private void WriteSessionHistory(IEnumerable<AiChatMessageDto> messages) =>
        HttpContext.Session.SetString(WebChatSessionKey, JsonSerializer.Serialize(BoundHistory(messages), JsonOptions));

    private void ClearSessionHistory() => HttpContext.Session.Remove(WebChatSessionKey);

    private static void ApplyDisabledMessage(AiAssistantPageViewModel model)
    {
        if (!model.AssistantEnabled || !model.WebChatEnabled)
        {
            model.ErrorMessage ??= "AI web chat is disabled. An administrator can enable it from Admin > AI Assistant settings.";
        }
    }
}
