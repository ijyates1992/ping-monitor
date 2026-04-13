using PingMonitor.Web.Services.ApplicationUpdate;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class ApplicationUpdatePageViewModel
{
    public ApplicationUpdateCheckResult Result { get; init; } = new();
    public string RepositoryDisplayName { get; init; } = string.Empty;
    public bool ChecksEnabled { get; init; }
}
