namespace PingMonitor.Web.Services;

public interface IAgentAuthenticationService
{
    Task<bool> ValidateAsync(string instanceId, string? authorizationHeader, CancellationToken cancellationToken);
}
