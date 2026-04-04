using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.State;

internal sealed class AssignmentCurrentStateCache : IAssignmentCurrentStateCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, CachedAssignmentState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public AssignmentCurrentStateCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<CachedAssignmentState> GetStateAsync(string assignmentId, string agentId, string endpointId, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = assignmentId.Trim();
        if (_states.TryGetValue(normalizedAssignmentId, out var cached))
        {
            return cached;
        }

        var gate = _gates.GetOrAdd(normalizedAssignmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_states.TryGetValue(normalizedAssignmentId, out cached))
            {
                return cached;
            }

            var hydrated = await HydrateSingleAsync(normalizedAssignmentId, agentId, endpointId, cancellationToken);
            _states[normalizedAssignmentId] = hydrated;
            return hydrated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, CachedAssignmentState>> GetStatesAsync(
        IReadOnlyCollection<AssignmentStateLookupRequest> requests,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CachedAssignmentState>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            var state = await GetStateAsync(request.AssignmentId, request.AgentId, request.EndpointId, cancellationToken);
            result[state.AssignmentId] = state;
        }

        return result;
    }

    public void Upsert(CachedAssignmentState state)
    {
        _states[state.AssignmentId] = state;
    }

    public void Invalidate(string assignmentId)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return;
        }

        _states.TryRemove(assignmentId.Trim(), out _);
    }

    private async Task<CachedAssignmentState> HydrateSingleAsync(
        string assignmentId,
        string agentId,
        string endpointId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PingMonitorDbContext>();

        var row = await dbContext.EndpointStates.AsNoTracking()
            .Where(x => x.AssignmentId == assignmentId)
            .Select(x => new
            {
                x.AssignmentId,
                x.AgentId,
                x.EndpointId,
                x.CurrentState,
                x.ConsecutiveFailureCount,
                x.ConsecutiveSuccessCount,
                x.LastCheckUtc,
                x.LastStateChangeUtc,
                x.SuppressedByEndpointId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new CachedAssignmentState
            {
                AssignmentId = assignmentId,
                AgentId = agentId,
                EndpointId = endpointId,
                CurrentState = EndpointStateKind.Unknown,
                ConsecutiveFailureCount = 0,
                ConsecutiveSuccessCount = 0,
                LastCheckUtc = null,
                LastStateChangeUtc = null,
                SuppressedByEndpointId = null,
                ExistsInDatabase = false
            };
        }

        return new CachedAssignmentState
        {
            AssignmentId = row.AssignmentId,
            AgentId = row.AgentId,
            EndpointId = row.EndpointId,
            CurrentState = row.CurrentState,
            ConsecutiveFailureCount = row.ConsecutiveFailureCount,
            ConsecutiveSuccessCount = row.ConsecutiveSuccessCount,
            LastCheckUtc = row.LastCheckUtc,
            LastStateChangeUtc = row.LastStateChangeUtc,
            SuppressedByEndpointId = row.SuppressedByEndpointId,
            ExistsInDatabase = true
        };
    }
}
