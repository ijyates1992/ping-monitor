using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.Endpoints;

namespace PingMonitor.Web.Services.Endpoints;

internal sealed class EndpointManagementQueryService : IEndpointManagementQueryService
{
    private readonly PingMonitorDbContext _dbContext;

    public EndpointManagementQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ManageEndpointsPageViewModel> GetManagePageAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            join agent in _dbContext.Agents.AsNoTracking() on assignment.AgentId equals agent.AgentId
            join state in _dbContext.EndpointStates.AsNoTracking() on assignment.AssignmentId equals state.AssignmentId into stateJoin
            from state in stateJoin.DefaultIfEmpty()
            orderby endpoint.Name, agent.InstanceId
            select new
            {
                assignment.AssignmentId,
                endpoint.EndpointId,
                EndpointName = endpoint.Name,
                endpoint.Target,
                AgentDisplay = string.IsNullOrWhiteSpace(agent.Name)
                    ? agent.InstanceId
                    : $"{agent.Name} ({agent.InstanceId})",
                endpoint.Enabled,
                AssignmentEnabled = assignment.Enabled,
                assignment.PingIntervalSeconds,
                assignment.RetryIntervalSeconds,
                assignment.TimeoutMs,
                assignment.FailureThreshold,
                assignment.RecoveryThreshold,
                CurrentState = state != null ? state.CurrentState : EndpointStateKind.Unknown
            }).ToArrayAsync(cancellationToken);

        var endpointIds = rows.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray();
        var endpointNames = await _dbContext.Endpoints.AsNoTracking()
            .ToDictionaryAsync(x => x.EndpointId, x => x.Name, cancellationToken);
        var dependencyLookup = await _dbContext.EndpointDependencies.AsNoTracking()
            .Where(x => endpointIds.Contains(x.EndpointId))
            .GroupBy(x => x.EndpointId)
            .ToDictionaryAsync(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.DependsOnEndpointId).ToArray(),
                cancellationToken);

        return new ManageEndpointsPageViewModel
        {
            Rows = rows.Select(row => new ManageEndpointRowViewModel
            {
                AssignmentId = row.AssignmentId,
                EndpointName = row.EndpointName,
                Target = row.Target,
                AgentDisplay = row.AgentDisplay,
                DependencyEndpointNames = dependencyLookup.GetValueOrDefault(row.EndpointId, Array.Empty<string>())
                    .Select(endpointId => endpointNames.GetValueOrDefault(endpointId, endpointId))
                    .OrderBy(x => x)
                    .ToArray(),
                EndpointEnabled = row.Enabled,
                AssignmentEnabled = row.AssignmentEnabled,
                PingIntervalSeconds = row.PingIntervalSeconds,
                RetryIntervalSeconds = row.RetryIntervalSeconds,
                TimeoutMs = row.TimeoutMs,
                FailureThreshold = row.FailureThreshold,
                RecoveryThreshold = row.RecoveryThreshold,
                CurrentState = row.CurrentState
            }).ToArray()
        };
    }

    public async Task<EditEndpointOptionsViewModel> GetEditOptionsAsync(string assignmentId, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = assignmentId.Trim();

        var endpointId = await _dbContext.MonitorAssignments.AsNoTracking()
            .Where(x => x.AssignmentId == normalizedAssignmentId)
            .Select(x => x.EndpointId)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return new EditEndpointOptionsViewModel();
        }

        var agents = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.Enabled && !x.ApiKeyRevoked)
            .OrderBy(x => x.InstanceId)
            .Select(x => new EndpointAgentOptionViewModel
            {
                AgentId = x.AgentId,
                DisplayName = string.IsNullOrWhiteSpace(x.Name)
                    ? x.InstanceId
                    : $"{x.Name} ({x.InstanceId})"
            })
            .ToArrayAsync(cancellationToken);

        var dependencies = await _dbContext.Endpoints.AsNoTracking()
            .Where(x => x.EndpointId != endpointId)
            .OrderBy(x => x.Name)
            .Select(x => new EndpointDependencyOptionViewModel
            {
                EndpointId = x.EndpointId,
                EndpointName = x.Name
            })
            .ToArrayAsync(cancellationToken);

        return new EditEndpointOptionsViewModel
        {
            Agents = agents,
            Dependencies = dependencies
        };
    }
}
