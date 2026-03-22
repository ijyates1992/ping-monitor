namespace PingMonitor.Web.Services;

internal sealed class PlaceholderAgentAuthenticationService : IAgentAuthenticationService
{
    public Task<bool> ValidateAsync(string instanceId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(instanceId));
    }
}
