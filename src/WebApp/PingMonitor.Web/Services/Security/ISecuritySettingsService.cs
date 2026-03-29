namespace PingMonitor.Web.Services.Security;

public interface ISecuritySettingsService
{
    Task<SecuritySettingsDto> GetCurrentAsync(CancellationToken cancellationToken);
    Task<SecuritySettingsDto> UpdateAsync(UpdateSecuritySettingsCommand command, CancellationToken cancellationToken);
}
