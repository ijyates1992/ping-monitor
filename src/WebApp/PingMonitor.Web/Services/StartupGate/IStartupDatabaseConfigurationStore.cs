namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupDatabaseConfigurationStore
{
    Task<StartupDatabaseConfiguration?> LoadAsync(CancellationToken cancellationToken);
    Task<string?> LoadPasswordAsync(CancellationToken cancellationToken);
    Task SaveAsync(StartupDatabaseConfigurationInput input, CancellationToken cancellationToken);
    string BuildConnectionString(StartupDatabaseConfiguration configuration, string password);
}
