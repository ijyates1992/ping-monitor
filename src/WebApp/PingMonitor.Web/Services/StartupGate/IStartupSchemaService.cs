namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupSchemaService
{
    Task<StartupSchemaStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<StartupSchemaStatus> ApplySchemaAsync(CancellationToken cancellationToken);
}
