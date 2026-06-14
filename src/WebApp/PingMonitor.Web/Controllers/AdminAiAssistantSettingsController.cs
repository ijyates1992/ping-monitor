using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services;
using PingMonitor.Web.Services.Identity;
using PingMonitor.Web.ViewModels.Admin;

namespace PingMonitor.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Admin)]
[Route("admin/ai-assistant-settings")]
public sealed class AdminAiAssistantSettingsController : Controller
{
    private readonly IAiAssistantSettingsService _settingsService;

    public AdminAiAssistantSettingsController(IAiAssistantSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetCurrentAsync(cancellationToken);
        return View("Index", ToViewModel(settings, saved: false));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] AiAssistantSettingsPageViewModel model, CancellationToken cancellationToken)
    {
        ValidateSettings(model);
        if (!ModelState.IsValid)
        {
            model.ApiKey = null;
            return View("Index", model);
        }

        var updated = await _settingsService.UpdateAsync(new UpdateAiAssistantSettingsCommand
        {
            AssistantEnabled = model.AssistantEnabled,
            WebChatEnabled = model.WebChatEnabled,
            TelegramChatEnabled = model.TelegramChatEnabled,
            MemoryEnabled = model.MemoryEnabled,
            DebugLoggingEnabled = model.DebugLoggingEnabled,
            ProviderDisplayName = model.ProviderDisplayName,
            ProviderType = model.ProviderType,
            BaseUrl = model.BaseUrl,
            ModelName = model.ModelName,
            ApiKey = model.ApiKey,
            ClearApiKey = model.ClearApiKey,
            RequestTimeoutSeconds = model.RequestTimeoutSeconds,
            MaxOutputTokens = model.MaxOutputTokens,
            Temperature = model.Temperature,
            ToolCallingEnabled = model.ToolCallingEnabled,
            GlobalSystemPrompt = model.GlobalSystemPrompt,
            UpdatedByUserId = User?.Identity?.Name
        }, cancellationToken);

        return View("Index", ToViewModel(updated, saved: true));
    }

    private void ValidateSettings(AiAssistantSettingsPageViewModel model)
    {
        if (!string.Equals(model.ProviderType?.Trim(), AiAssistantSettings.OpenAICompatibleProviderType, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.ProviderType), "Provider type must be OpenAICompatible.");
        }

        if (model.AssistantEnabled && string.IsNullOrWhiteSpace(model.ModelName))
        {
            ModelState.AddModelError(nameof(model.ModelName), "Model name is required when the AI assistant is enabled.");
        }

        if (model.AssistantEnabled && string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            ModelState.AddModelError(nameof(model.BaseUrl), "Base URL is required when the AI assistant is enabled.");
        }

        if (!string.IsNullOrWhiteSpace(model.BaseUrl)
            && (!Uri.TryCreate(model.BaseUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            ModelState.AddModelError(nameof(model.BaseUrl), "Base URL must be an absolute http or https URL.");
        }
    }

    private static AiAssistantSettingsPageViewModel ToViewModel(AiAssistantSettingsDto settings, bool saved)
    {
        return new AiAssistantSettingsPageViewModel
        {
            AssistantEnabled = settings.AssistantEnabled,
            WebChatEnabled = settings.WebChatEnabled,
            TelegramChatEnabled = settings.TelegramChatEnabled,
            MemoryEnabled = settings.MemoryEnabled,
            DebugLoggingEnabled = settings.DebugLoggingEnabled,
            ProviderDisplayName = settings.ProviderDisplayName,
            ProviderType = settings.ProviderType,
            BaseUrl = settings.BaseUrl,
            ModelName = settings.ModelName,
            ApiKey = null,
            ApiKeyConfigured = settings.ApiKeyConfigured,
            RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
            MaxOutputTokens = settings.MaxOutputTokens,
            Temperature = settings.Temperature,
            ToolCallingEnabled = settings.ToolCallingEnabled,
            GlobalSystemPrompt = settings.GlobalSystemPrompt,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId,
            Saved = saved
        };
    }
}
