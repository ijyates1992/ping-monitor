namespace PingMonitor.Web.Services.ApplicationUpdater;

public interface IApplicationUpdaterOperationalSettingsService
{
    Task<ApplicationUpdaterOperationalSettingsDto> GetCurrentAsync(CancellationToken cancellationToken);

    Task<ApplicationUpdaterOperationalSettingsDto> UpdateAsync(UpdateApplicationUpdaterOperationalSettingsCommand command, CancellationToken cancellationToken);
}
