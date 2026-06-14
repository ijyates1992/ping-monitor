using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using System.Security.Cryptography;
using System.Text;

namespace PingMonitor.Web.Services;

internal sealed class AiAssistantSettingsService : IAiAssistantSettingsService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ILogger<AiAssistantSettingsService> _logger;

    public AiAssistantSettingsService(PingMonitorDbContext dbContext, ILogger<AiAssistantSettingsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AiAssistantSettingsDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);
        return ToDto(settings);
    }

    public async Task<AiAssistantSettingsDto> UpdateAsync(UpdateAiAssistantSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateEntityAsync(cancellationToken);

        settings.AssistantEnabled = command.AssistantEnabled;
        settings.WebChatEnabled = command.WebChatEnabled;
        settings.TelegramChatEnabled = command.TelegramChatEnabled;
        settings.MemoryEnabled = command.MemoryEnabled;
        settings.DebugLoggingEnabled = command.DebugLoggingEnabled;
        settings.ProviderDisplayName = NormalizeString(command.ProviderDisplayName) ?? "Local Ollama";
        settings.ProviderType = NormalizeProviderType(command.ProviderType);
        settings.BaseUrl = NormalizeString(command.BaseUrl) ?? string.Empty;
        settings.ModelName = NormalizeString(command.ModelName) ?? string.Empty;
        settings.RequestTimeoutSeconds = command.RequestTimeoutSeconds;
        settings.MaxOutputTokens = command.MaxOutputTokens;
        settings.Temperature = command.Temperature;
        settings.ToolCallingEnabled = command.ToolCallingEnabled;
        settings.GlobalSystemPrompt = command.GlobalSystemPrompt?.Trim() ?? string.Empty;

        if (command.ClearApiKey)
        {
            settings.ApiKeyProtected = null;
        }
        else if (!string.IsNullOrWhiteSpace(command.ApiKey))
        {
            settings.ApiKeyProtected = ProtectSecret(command.ApiKey.Trim());
        }

        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        settings.UpdatedByUserId = NormalizeString(command.UpdatedByUserId);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("AI assistant settings updated by {UpdatedByUserId}. AssistantEnabled={AssistantEnabled} WebChatEnabled={WebChatEnabled} TelegramChatEnabled={TelegramChatEnabled} MemoryEnabled={MemoryEnabled} DebugLoggingEnabled={DebugLoggingEnabled} ProviderType={ProviderType}",
            settings.UpdatedByUserId ?? "(unknown)",
            settings.AssistantEnabled,
            settings.WebChatEnabled,
            settings.TelegramChatEnabled,
            settings.MemoryEnabled,
            settings.DebugLoggingEnabled,
            settings.ProviderType);

        return ToDto(settings);
    }

    private async Task<AiAssistantSettings> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AiAssistantSettings
            .SingleOrDefaultAsync(x => x.AiAssistantSettingsId == AiAssistantSettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new AiAssistantSettings
        {
            AiAssistantSettingsId = AiAssistantSettings.SingletonId,
            AssistantEnabled = false,
            WebChatEnabled = false,
            TelegramChatEnabled = false,
            MemoryEnabled = false,
            DebugLoggingEnabled = false,
            ProviderDisplayName = "Local Ollama",
            ProviderType = AiAssistantSettings.OpenAICompatibleProviderType,
            BaseUrl = "http://localhost:11434/v1",
            ModelName = string.Empty,
            RequestTimeoutSeconds = 60,
            MaxOutputTokens = 2048,
            Temperature = 0.2,
            ToolCallingEnabled = true,
            GlobalSystemPrompt = string.Empty,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.AiAssistantSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private string ProtectSecret(string secret)
    {
        var payload = Encoding.UTF8.GetBytes(secret);
        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Protect(payload, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }
        else
        {
            _logger.LogWarning("AI assistant API key fallback storage is in use because DPAPI is only available on Windows.");
        }

        return Convert.ToBase64String(payload);
    }

    private static string? NormalizeString(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeProviderType(string? value)
    {
        var normalized = NormalizeString(value);
        return string.Equals(normalized, AiAssistantSettings.OpenAICompatibleProviderType, StringComparison.Ordinal)
            ? AiAssistantSettings.OpenAICompatibleProviderType
            : AiAssistantSettings.OpenAICompatibleProviderType;
    }

    private static AiAssistantSettingsDto ToDto(AiAssistantSettings settings)
    {
        return new AiAssistantSettingsDto
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
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.ApiKeyProtected),
            RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
            MaxOutputTokens = settings.MaxOutputTokens,
            Temperature = settings.Temperature,
            ToolCallingEnabled = settings.ToolCallingEnabled,
            GlobalSystemPrompt = settings.GlobalSystemPrompt,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            UpdatedByUserId = settings.UpdatedByUserId
        };
    }
}
