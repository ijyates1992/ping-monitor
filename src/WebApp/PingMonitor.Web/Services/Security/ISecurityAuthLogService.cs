namespace PingMonitor.Web.Services.Security;

public interface ISecurityAuthLogService
{
    Task LogUserAttemptAsync(UserAuthLogWriteRequest request, CancellationToken cancellationToken);
    Task LogAgentAttemptAsync(AgentAuthLogWriteRequest request, CancellationToken cancellationToken);
}
