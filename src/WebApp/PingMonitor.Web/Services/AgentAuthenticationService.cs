using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services;

internal sealed class AgentAuthenticationService : IAgentAuthenticationService
{
    private const string InstanceIdHeaderName = "X-Instance-Id";
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentApiKeyHasher _apiKeyHasher;

    public AgentAuthenticationService(PingMonitorDbContext dbContext, IAgentApiKeyHasher apiKeyHasher)
    {
        _dbContext = dbContext;
        _apiKeyHasher = apiKeyHasher;
    }

    public async Task<AgentAuthenticationResult> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var instanceId = request.Headers[InstanceIdHeaderName].ToString().Trim();
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return AgentAuthenticationResult.Unauthorized($"Missing required header '{InstanceIdHeaderName}'.");
        }

        var authorizationHeader = request.Headers.Authorization.ToString();
        if (!TryReadBearerToken(authorizationHeader, out var apiKey))
        {
            return AgentAuthenticationResult.Unauthorized("Missing or invalid bearer token.");
        }

        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.InstanceId == instanceId, cancellationToken);
        if (agent is null)
        {
            return AgentAuthenticationResult.Unauthorized("The supplied agent credentials are invalid.");
        }

        if (!agent.Enabled)
        {
            return AgentAuthenticationResult.Forbidden("The agent is disabled.");
        }

        if (agent.ApiKeyRevoked)
        {
            return AgentAuthenticationResult.Forbidden("The agent API key has been revoked.");
        }

        if (string.IsNullOrWhiteSpace(agent.ApiKeyHash) || !_apiKeyHasher.Verify(agent, apiKey!))
        {
            return AgentAuthenticationResult.Unauthorized("The supplied agent credentials are invalid.");
        }

        return AgentAuthenticationResult.Success(agent);
    }

    private static bool TryReadBearerToken(string? authorizationHeader, out string? apiKey)
    {
        apiKey = null;
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorizationHeader[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        apiKey = token;
        return true;
    }
}
