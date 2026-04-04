using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.State;

internal sealed class AssignmentTopologyCache : IAssignmentTopologyCache
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private volatile TopologySnapshot? _snapshot;

    public AssignmentTopologyCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<AssignmentTopologyContext?> GetAssignmentContextAsync(string assignmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return null;
        }

        var normalizedAssignmentId = assignmentId.Trim();
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.Assignments.TryGetValue(normalizedAssignmentId, out var context)
            ? context
            : null;
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            _snapshot = null;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<TopologySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        if (snapshot is not null && DateTimeOffset.UtcNow - snapshot.CreatedAtUtc <= SnapshotLifetime)
        {
            return snapshot;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot is not null && DateTimeOffset.UtcNow - _snapshot.CreatedAtUtc <= SnapshotLifetime)
            {
                return _snapshot;
            }

            _snapshot = await BuildSnapshotAsync(cancellationToken);
            return _snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<TopologySnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();

        var assignments = await dbContext.MonitorAssignments.AsNoTracking()
            .Select(x => new AssignmentRow
            {
                AssignmentId = x.AssignmentId,
                AgentId = x.AgentId,
                EndpointId = x.EndpointId,
                Enabled = x.Enabled,
                FailureThreshold = x.FailureThreshold,
                RecoveryThreshold = x.RecoveryThreshold
            })
            .ToArrayAsync(cancellationToken);

        var endpoints = await dbContext.Endpoints.AsNoTracking()
            .Select(x => new EndpointRow
            {
                EndpointId = x.EndpointId,
                Name = x.Name
            })
            .ToDictionaryAsync(x => x.EndpointId, StringComparer.Ordinal, cancellationToken);

        var agents = await dbContext.Agents.AsNoTracking()
            .Select(x => new AgentRow
            {
                AgentId = x.AgentId,
                Enabled = x.Enabled,
                ApiKeyRevoked = x.ApiKeyRevoked,
                Status = x.Status
            })
            .ToDictionaryAsync(x => x.AgentId, StringComparer.Ordinal, cancellationToken);

        var dependencies = await dbContext.EndpointDependencies.AsNoTracking()
            .Select(x => new DependencyRow
            {
                EndpointId = x.EndpointId,
                DependsOnEndpointId = x.DependsOnEndpointId
            })
            .ToArrayAsync(cancellationToken);

        var assignmentByAgentAndEndpoint = assignments
            .ToDictionary(x => (x.AgentId, x.EndpointId), x => x.AssignmentId);

        var parentEndpointIdsByEndpoint = dependencies
            .GroupBy(x => x.EndpointId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DependsOnEndpointId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var childEndpointIdsByParentEndpoint = dependencies
            .GroupBy(x => x.DependsOnEndpointId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.EndpointId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var contexts = new Dictionary<string, AssignmentTopologyContext>(StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            var endpointName = endpoints.TryGetValue(assignment.EndpointId, out var endpoint)
                ? endpoint.Name
                : assignment.EndpointId;

            var agent = agents.TryGetValue(assignment.AgentId, out var agentRow)
                ? agentRow
                : null;

            var parentDependencies = new List<AssignmentParentDependency>();
            if (parentEndpointIdsByEndpoint.TryGetValue(assignment.EndpointId, out var parentEndpointIds))
            {
                foreach (var parentEndpointId in parentEndpointIds)
                {
                    if (assignmentByAgentAndEndpoint.TryGetValue((assignment.AgentId, parentEndpointId), out var parentAssignmentId))
                    {
                        parentDependencies.Add(new AssignmentParentDependency
                        {
                            ParentEndpointId = parentEndpointId,
                            ParentAssignmentId = parentAssignmentId
                        });
                    }
                }
            }

            var childAssignments = new List<string>();
            if (childEndpointIdsByParentEndpoint.TryGetValue(assignment.EndpointId, out var childEndpointIds))
            {
                foreach (var childEndpointId in childEndpointIds)
                {
                    if (assignmentByAgentAndEndpoint.TryGetValue((assignment.AgentId, childEndpointId), out var childAssignmentId))
                    {
                        childAssignments.Add(childAssignmentId);
                    }
                }
            }

            contexts[assignment.AssignmentId] = new AssignmentTopologyContext
            {
                AssignmentId = assignment.AssignmentId,
                AgentId = assignment.AgentId,
                EndpointId = assignment.EndpointId,
                EndpointName = endpointName,
                AssignmentEnabled = assignment.Enabled,
                FailureThreshold = assignment.FailureThreshold,
                RecoveryThreshold = assignment.RecoveryThreshold,
                AgentEnabled = agent?.Enabled ?? false,
                AgentApiKeyRevoked = agent?.ApiKeyRevoked ?? true,
                AgentStatus = agent?.Status ?? AgentHealthStatus.Offline,
                ParentDependencies = parentDependencies,
                ChildAssignmentIds = childAssignments
            };
        }

        return new TopologySnapshot(DateTimeOffset.UtcNow, contexts);
    }

    private sealed record TopologySnapshot(DateTimeOffset CreatedAtUtc, IReadOnlyDictionary<string, AssignmentTopologyContext> Assignments);

    private sealed class AssignmentRow
    {
        public required string AssignmentId { get; init; }
        public required string AgentId { get; init; }
        public required string EndpointId { get; init; }
        public required bool Enabled { get; init; }
        public required int FailureThreshold { get; init; }
        public required int RecoveryThreshold { get; init; }
    }

    private sealed class EndpointRow
    {
        public required string EndpointId { get; init; }
        public required string Name { get; init; }
    }

    private sealed class AgentRow
    {
        public required string AgentId { get; init; }
        public required bool Enabled { get; init; }
        public required bool ApiKeyRevoked { get; init; }
        public required AgentHealthStatus Status { get; init; }
    }

    private sealed class DependencyRow
    {
        public required string EndpointId { get; init; }
        public required string DependsOnEndpointId { get; init; }
    }
}
