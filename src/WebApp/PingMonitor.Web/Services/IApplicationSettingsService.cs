namespace PingMonitor.Web.Services;

public interface IApplicationSettingsService
{
    Task<ApplicationSettingsDto> GetCurrentAsync(CancellationToken cancellationToken);

    Task<ApplicationSettingsDto> UpdateAsync(UpdateApplicationSettingsCommand command, CancellationToken cancellationToken);
}
