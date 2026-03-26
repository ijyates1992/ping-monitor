using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Services.Metrics;

namespace PingMonitor.Web.Services.Agents;

internal sealed class AgentManagementQueryService : IAgentManagementQueryService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly IAgentMetricsService _agentMetricsService;

    public AgentManagementQueryService(PingMonitorDbContext dbContext, IAgentMetricsService agentMetricsService)
    {
        _dbContext = dbContext;
        _agentMetricsService = agentMetricsService;
    }

    public async Task<IReadOnlyList<AgentManagementRow>> ListAsync(CancellationToken cancellationToken)
    {
        var assignmentCounts = await _dbContext.MonitorAssignments
            .AsNoTracking()
            .GroupBy(assignment => assignment.AgentId)
            .Select(group => new
            {
                AgentId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, cancellationToken);

        var agents = await _dbContext.Agents
            .AsNoTracking()
            .OrderBy(agent => agent.Name ?? agent.InstanceId)
            .ThenBy(agent => agent.InstanceId)
            .Select(agent => new
            {
                agent.AgentId,
                agent.Name,
                agent.InstanceId,
                agent.Enabled,
                agent.ApiKeyRevoked,
                agent.LastSeenUtc,
                agent.LastHeartbeatUtc,
                agent.AgentVersion,
                agent.MachineName,
                agent.Platform,
                agent.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var uptimeByAgentId = await _agentMetricsService.GetAgentMetricSummariesAsync(
            agents.Select(x => x.AgentId).ToArray(),
            cancellationToken);

        return agents.Select(agent => new AgentManagementRow
            {
                AgentId = agent.AgentId,
                Name = string.IsNullOrWhiteSpace(agent.Name) ? "(unnamed agent)" : agent.Name!,
                InstanceId = agent.InstanceId,
                Enabled = agent.Enabled,
                ApiKeyRevoked = agent.ApiKeyRevoked,
                LastSeenUtc = agent.LastSeenUtc,
                LastHeartbeatUtc = agent.LastHeartbeatUtc,
                AgentVersion = agent.AgentVersion ?? "Unknown",
                MachineName = agent.MachineName ?? "Unknown",
                Platform = agent.Platform ?? "Unknown",
                CreatedAtUtc = agent.CreatedAtUtc,
                AssignmentCount = assignmentCounts.GetValueOrDefault(agent.AgentId, 0),
                UptimePercent = uptimeByAgentId.GetValueOrDefault(agent.AgentId)?.UptimePercent
            })
            .ToList();
    }

    public async Task<RemoveAgentDetails?> GetRemoveDetailsAsync(string agentId, CancellationToken cancellationToken)
    {
        var normalizedAgentId = agentId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAgentId))
        {
            return null;
        }

        var details = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.AgentId == normalizedAgentId)
            .Select(x => new
            {
                x.AgentId,
                x.Name,
                x.InstanceId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (details is null)
        {
            return null;
        }

        var assignmentCount = await _dbContext.MonitorAssignments.AsNoTracking()
            .CountAsync(x => x.AgentId == normalizedAgentId, cancellationToken);

        return new RemoveAgentDetails
        {
            AgentId = details.AgentId,
            Name = string.IsNullOrWhiteSpace(details.Name) ? "(unnamed agent)" : details.Name,
            InstanceId = details.InstanceId,
            AssignmentCount = assignmentCount
        };
    }
}
