using PingMonitor.Web.Services.Security;

namespace PingMonitor.Web.ViewModels.Admin;

public sealed class AdminSecurityPageViewModel
{
    public bool IncludeSuccessfulUserAttempts { get; init; }
    public bool IncludeSuccessfulAgentAttempts { get; init; }
    public IReadOnlyList<SecurityAuthLogListItem> UserAttempts { get; init; } = [];
    public IReadOnlyList<SecurityAuthLogListItem> AgentAttempts { get; init; } = [];
}
