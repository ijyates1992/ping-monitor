using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;

namespace PingMonitor.Web.Services.Agents;

internal sealed class AgentManagementQueryService : IAgentManagementQueryService
{
    private readonly PingMonitorDbContext _dbContext;

    public AgentManagementQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
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
                AssignmentCount = assignmentCounts.GetValueOrDefault(agent.AgentId, 0)
            })
            .ToList();
    }
}
