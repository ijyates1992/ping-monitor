using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using EndpointModel = PingMonitor.Web.Models.Endpoint;
using PingMonitor.Web.Options;

namespace PingMonitor.Web.Services;

internal sealed class DevelopmentAgentSeeder
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly DevelopmentSeedAgentOptions _options;
    private readonly IAgentApiKeyHasher _apiKeyHasher;
    private readonly ILogger<DevelopmentAgentSeeder> _logger;

    public DevelopmentAgentSeeder(
        PingMonitorDbContext dbContext,
        IOptions<DevelopmentSeedAgentOptions> options,
        IAgentApiKeyHasher apiKeyHasher,
        ILogger<DevelopmentAgentSeeder> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _apiKeyHasher = apiKeyHasher;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Development agent seeding is enabled, but no API key was supplied. Set DevelopmentSeedAgent__ApiKey before calling /api/v1/agent/hello.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var agent = await _dbContext.Agents.SingleOrDefaultAsync(x => x.InstanceId == _options.InstanceId);
        if (agent is null)
        {
            agent = new Agent
            {
                AgentId = Guid.NewGuid().ToString(),
                InstanceId = _options.InstanceId,
                CreatedAtUtc = now,
                Status = AgentHealthStatus.Offline
            };

            _dbContext.Agents.Add(agent);
        }

        agent.Name = _options.Name;
        agent.Site = _options.Site;
        agent.Enabled = true;
        agent.ApiKeyRevoked = false;
        agent.ApiKeyCreatedAtUtc = now;
        agent.ApiKeyHash = _apiKeyHasher.Hash(agent, _options.ApiKey);

        await EnsureDevelopmentAssignmentsAsync(agent, now, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Seeded development agent {InstanceId} with local assignments for config and results testing.", _options.InstanceId);
    }

    private async Task EnsureDevelopmentAssignmentsAsync(Agent agent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var gatewayEndpoint = await EnsureEndpointAsync(
            endpointId: "endpoint-dev-gateway",
            name: "Dev Gateway",
            target: "192.0.2.1",
            enabled: true,
            tags: ["dev", "gateway"],
            notes: "Development-only seeded endpoint for local agent API testing.",
            now,
            cancellationToken);

        var printerEndpoint = await EnsureEndpointAsync(
            endpointId: "endpoint-dev-printer",
            name: "Dev Printer",
            target: "192.0.2.55",
            enabled: true,
            tags: ["dev", "printer"],
            notes: "Development-only seeded endpoint for local agent API testing.",
            now,
            cancellationToken);

        await EnsureAssignmentAsync(agent.AgentId, gatewayEndpoint.EndpointId, "assignment-dev-gateway", now, cancellationToken);
        await EnsureAssignmentAsync(agent.AgentId, printerEndpoint.EndpointId, "assignment-dev-printer", now, cancellationToken);
        await EnsureDependenciesAsync(printerEndpoint.EndpointId, [gatewayEndpoint.EndpointId], now, cancellationToken);
        await EnsureStateAsync(agent.AgentId, gatewayEndpoint.EndpointId, "assignment-dev-gateway", cancellationToken);
        await EnsureStateAsync(agent.AgentId, printerEndpoint.EndpointId, "assignment-dev-printer", cancellationToken);
    }

    private async Task<EndpointModel> EnsureEndpointAsync(
        string endpointId,
        string name,
        string target,
        bool enabled,
        List<string> tags,
        string notes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var endpoint = await _dbContext.Endpoints.SingleOrDefaultAsync(x => x.EndpointId == endpointId, cancellationToken);
        if (endpoint is null)
        {
            endpoint = new EndpointModel
            {
                EndpointId = endpointId,
                CreatedAtUtc = now
            };

            _dbContext.Endpoints.Add(endpoint);
        }

        endpoint.Name = name;
        endpoint.Target = target;
        endpoint.Enabled = enabled;
        endpoint.Tags = tags;
        endpoint.Notes = notes;
        return endpoint;
    }

    private async Task EnsureDependenciesAsync(string endpointId, IReadOnlyList<string> dependsOnEndpointIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.EndpointDependencies
            .Where(x => x.EndpointId == endpointId)
            .ToListAsync(cancellationToken);

        foreach (var link in existing)
        {
            if (!dependsOnEndpointIds.Contains(link.DependsOnEndpointId, StringComparer.Ordinal))
            {
                _dbContext.EndpointDependencies.Remove(link);
            }
        }

        foreach (var dependsOnEndpointId in dependsOnEndpointIds)
        {
            if (existing.All(x => !string.Equals(x.DependsOnEndpointId, dependsOnEndpointId, StringComparison.Ordinal)))
            {
                _dbContext.EndpointDependencies.Add(new EndpointDependency
                {
                    EndpointDependencyId = Guid.NewGuid().ToString(),
                    EndpointId = endpointId,
                    DependsOnEndpointId = dependsOnEndpointId,
                    CreatedAtUtc = now
                });
            }
        }
    }

    private async Task EnsureAssignmentAsync(string agentId, string endpointId, string assignmentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.MonitorAssignments
            .SingleOrDefaultAsync(x => x.AgentId == agentId && x.EndpointId == endpointId, cancellationToken);

        if (assignment is null)
        {
            assignment = new MonitorAssignment
            {
                AssignmentId = assignmentId,
                AgentId = agentId,
                EndpointId = endpointId,
                CreatedAtUtc = now
            };

            _dbContext.MonitorAssignments.Add(assignment);
        }

        assignment.CheckType = CheckType.Icmp;
        assignment.Enabled = true;
        assignment.PingIntervalSeconds = endpointId == "endpoint-dev-gateway" ? 30 : 60;
        assignment.RetryIntervalSeconds = endpointId == "endpoint-dev-gateway" ? 5 : 10;
        assignment.TimeoutMs = 1000;
        assignment.FailureThreshold = endpointId == "endpoint-dev-gateway" ? 3 : 2;
        assignment.RecoveryThreshold = 2;
        assignment.UpdatedAtUtc = now;
    }

    private async Task EnsureStateAsync(string agentId, string endpointId, string assignmentId, CancellationToken cancellationToken)
    {
        var state = await _dbContext.EndpointStates.SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, cancellationToken);
        if (state is not null)
        {
            state.AgentId = agentId;
            state.EndpointId = endpointId;
            return;
        }

        _dbContext.EndpointStates.Add(new EndpointState
        {
            AssignmentId = assignmentId,
            AgentId = agentId,
            EndpointId = endpointId,
            CurrentState = EndpointStateKind.Unknown
        });
    }
}
