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
            join parent in _dbContext.Endpoints.AsNoTracking() on endpoint.DependsOnEndpointId equals parent.EndpointId into parentJoin
            from parent in parentJoin.DefaultIfEmpty()
            orderby endpoint.Name, agent.InstanceId
            select new ManageEndpointRowViewModel
            {
                AssignmentId = assignment.AssignmentId,
                EndpointName = endpoint.Name,
                Target = endpoint.Target,
                AgentDisplay = string.IsNullOrWhiteSpace(agent.Name)
                    ? agent.InstanceId
                    : $"{agent.Name} ({agent.InstanceId})",
                ParentEndpointName = parent != null ? parent.Name : null,
                EndpointEnabled = endpoint.Enabled,
                AssignmentEnabled = assignment.Enabled,
                PingIntervalSeconds = assignment.PingIntervalSeconds,
                RetryIntervalSeconds = assignment.RetryIntervalSeconds,
                TimeoutMs = assignment.TimeoutMs,
                FailureThreshold = assignment.FailureThreshold,
                RecoveryThreshold = assignment.RecoveryThreshold,
                CurrentState = state != null ? state.CurrentState : EndpointStateKind.Unknown
            }).ToArrayAsync(cancellationToken);

        return new ManageEndpointsPageViewModel
        {
            Rows = rows
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
