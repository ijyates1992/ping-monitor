using PingMonitor.Web.Services.StartupGate;

namespace PingMonitor.Web.ViewModels.StartupGate;

public sealed class StartupGatePageViewModel
{
    public required StartupGateStatus Status { get; init; }
    public required StartupDatabaseConfigurationForm DatabaseForm { get; init; }
    public required StartupAdminBootstrapForm AdminForm { get; init; }
    public string? StatusMessage { get; init; }
    public string? ErrorMessage { get; init; }
}
