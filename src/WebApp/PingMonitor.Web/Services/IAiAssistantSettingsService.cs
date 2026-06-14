namespace PingMonitor.Web.Services;

public interface IAiAssistantSettingsService
{
    Task<AiAssistantSettingsDto> GetCurrentAsync(CancellationToken cancellationToken);
    Task<AiAssistantSettingsDto> UpdateAsync(UpdateAiAssistantSettingsCommand command, CancellationToken cancellationToken);
}
