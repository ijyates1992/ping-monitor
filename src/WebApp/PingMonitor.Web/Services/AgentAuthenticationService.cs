using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.Security;

namespace PingMonitor.Web.Services;

internal sealed class AgentAuthenticationService : IAgentAuthenticationService
{
    private const string InstanceIdHeaderName = "X-Instance-Id";
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentApiKeyHasher _apiKeyHasher;
    private readonly ISecurityAuthLogService _securityAuthLogService;
    private readonly ISecurityEnforcementService _securityEnforcementService;

    public AgentAuthenticationService(
        PingMonitorDbContext dbContext,
        IAgentApiKeyHasher apiKeyHasher,
        ISecurityAuthLogService securityAuthLogService,
        ISecurityEnforcementService securityEnforcementService)
    {
        _dbContext = dbContext;
        _apiKeyHasher = apiKeyHasher;
        _securityAuthLogService = securityAuthLogService;
        _securityEnforcementService = securityEnforcementService;
    }

    public async Task<AgentAuthenticationResult> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Enforcement order:
        // 1) resolve source IP
        // 2) check active agent-auth IP block
        // 3) if blocked, reject and log once
        // 4) continue normal auth checks
        // 5) after failed auth, evaluate automatic blocking thresholds
        var sourceIpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var ipBlockStatus = await _securityEnforcementService.GetIpBlockStatusAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
        if (ipBlockStatus.IsBlocked)
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: "blocked_agent", agentId: null, failureReason: ipBlockStatus.FailureReason, cancellationToken);
            return AgentAuthenticationResult.Forbidden("Authentication attempt denied.");
        }

        var instanceId = request.Headers[InstanceIdHeaderName].ToString().Trim();
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: "unknown_instance", agentId: null, failureReason: "missing_instance_header", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Unauthorized($"Missing required header '{InstanceIdHeaderName}'.");
        }

        var authorizationHeader = request.Headers.Authorization.ToString();
        if (!TryReadBearerToken(authorizationHeader, out var apiKey))
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: instanceId, agentId: null, failureReason: "missing_or_invalid_bearer_token", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Unauthorized("Missing or invalid bearer token.");
        }

        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.InstanceId == instanceId, cancellationToken);
        if (agent is null)
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: instanceId, agentId: null, failureReason: "unknown_instance", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Unauthorized("The supplied agent credentials are invalid.");
        }

        if (!agent.Enabled)
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: instanceId, agentId: agent.AgentId, failureReason: "disabled_agent", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Forbidden("The agent is disabled.");
        }

        if (agent.ApiKeyRevoked)
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: instanceId, agentId: agent.AgentId, failureReason: "revoked_key", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Forbidden("The agent API key has been revoked.");
        }

        if (string.IsNullOrWhiteSpace(agent.ApiKeyHash) || !_apiKeyHasher.Verify(agent, apiKey!))
        {
            await LogAttemptAsync(request, success: false, subjectIdentifier: instanceId, agentId: agent.AgentId, failureReason: "invalid_key", cancellationToken);
            await _securityEnforcementService.EvaluateFailedAttemptAsync(SecurityAuthType.Agent, sourceIpAddress, cancellationToken);
            return AgentAuthenticationResult.Unauthorized("The supplied agent credentials are invalid.");
        }

        await LogAttemptAsync(request, success: true, subjectIdentifier: instanceId, agentId: agent.AgentId, failureReason: null, cancellationToken);
        return AgentAuthenticationResult.Success(agent);
    }

    private Task LogAttemptAsync(
        HttpRequest request,
        bool success,
        string subjectIdentifier,
        string? agentId,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        return _securityAuthLogService.LogAgentAttemptAsync(
            new AgentAuthLogWriteRequest
            {
                SubjectIdentifier = subjectIdentifier,
                AgentId = agentId,
                SourceIpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                Success = success,
                FailureReason = failureReason,
                RequestPath = request.Path.Value
            },
            cancellationToken);
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
