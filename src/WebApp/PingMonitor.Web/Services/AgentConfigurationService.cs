using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Contracts.Config;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class AgentConfigurationService : IAgentConfigurationService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly AgentApiOptions _options;

    public AgentConfigurationService(PingMonitorDbContext dbContext, IOptions<AgentApiOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<AgentConfigResponse> GetConfigurationAsync(Agent agent, CancellationToken cancellationToken)
    {
        var assignments = await _dbContext.MonitorAssignments
            .Where(x => x.AgentId == agent.AgentId)
            .Join(
                _dbContext.Endpoints,
                assignment => assignment.EndpointId,
                endpoint => endpoint.EndpointId,
                (assignment, endpoint) => new { Assignment = assignment, Endpoint = endpoint })
            .OrderBy(x => x.Endpoint.Name)
            .ToListAsync(cancellationToken);

        var responseAssignments = assignments
            .Select(x => new MonitorAssignmentDto(
                AssignmentId: x.Assignment.AssignmentId,
                EndpointId: x.Endpoint.EndpointId,
                Name: x.Endpoint.Name,
                Target: x.Endpoint.Target,
                CheckType: x.Assignment.CheckType.ToString().ToLowerInvariant(),
                Enabled: x.Assignment.Enabled && x.Endpoint.Enabled,
                PingIntervalSeconds: x.Assignment.PingIntervalSeconds,
                RetryIntervalSeconds: x.Assignment.RetryIntervalSeconds,
                TimeoutMs: x.Assignment.TimeoutMs,
                FailureThreshold: x.Assignment.FailureThreshold,
                RecoveryThreshold: x.Assignment.RecoveryThreshold,
                DependsOnEndpointId: x.Endpoint.DependsOnEndpointId,
                Tags: x.Endpoint.Tags))
            .ToArray();

        return new AgentConfigResponse(
            ConfigVersion: _options.ConfigVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assignments: responseAssignments);
    }

    public Task<string> GetCurrentConfigVersionAsync(Agent agent, CancellationToken cancellationToken)
    {
        return Task.FromResult(_options.ConfigVersion);
    }
}
