namespace PingMonitor.Web.Services.StartupGate;

public interface IStartupAdminBootstrapService
{
    Task<StartupAdminStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<(bool Succeeded, IReadOnlyList<string> Errors)> CreateInitialAdminAsync(StartupAdminBootstrapForm form, CancellationToken cancellationToken);
}
