using Microsoft.AspNetCore.Http;

namespace PingMonitor.Web.Services;

public interface IAgentAuthenticationService
{
    Task<AgentAuthenticationResult> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken);
}
