using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using EndpointModel = PingMonitor.Web.Models.Endpoint;

namespace PingMonitor.Web.Services.Endpoints;

internal sealed class EndpointManagementService : IEndpointManagementService
{
    private readonly PingMonitorDbContext _dbContext;

    public EndpointManagementService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CreateEndpointResult> CreateEndpointWithAssignmentAsync(CreateEndpointCommand command, CancellationToken cancellationToken)
    {
        var validationErrors = await ValidateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return CreateEndpointResult.Failed(validationErrors.ToArray());
        }

        var now = DateTimeOffset.UtcNow;
        var endpointId = Guid.NewGuid().ToString();
        var assignmentId = Guid.NewGuid().ToString();
        var dependencyId = NormalizeDependency(command.DependsOnEndpointId);

        if (string.Equals(dependencyId, endpointId, StringComparison.Ordinal))
        {
            return CreateEndpointResult.Failed(
                new CreateEndpointValidationError(nameof(CreateEndpointCommand.DependsOnEndpointId), "Dependency cannot reference the new endpoint itself."));
        }

        var endpoint = new EndpointModel
        {
            EndpointId = endpointId,
            Name = command.EndpointName.Trim(),
            Target = command.Target.Trim(),
            Enabled = command.EndpointEnabled,
            DependsOnEndpointId = dependencyId,
            CreatedAtUtc = now,
            Tags = []
        };

        var assignment = new MonitorAssignment
        {
            AssignmentId = assignmentId,
            AgentId = command.AgentId.Trim(),
            EndpointId = endpointId,
            CheckType = CheckType.Icmp,
            Enabled = command.AssignmentEnabled,
            PingIntervalSeconds = command.PingIntervalSeconds,
            RetryIntervalSeconds = command.RetryIntervalSeconds,
            TimeoutMs = command.TimeoutMs,
            FailureThreshold = command.FailureThreshold,
            RecoveryThreshold = command.RecoveryThreshold,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.Endpoints.Add(endpoint);
        _dbContext.MonitorAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreateEndpointResult.Succeeded(endpointId, assignmentId);
    }

    private async Task<List<CreateEndpointValidationError>> ValidateAsync(CreateEndpointCommand command, CancellationToken cancellationToken)
    {
        var errors = new List<CreateEndpointValidationError>();

        if (string.IsNullOrWhiteSpace(command.EndpointName))
        {
            errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.EndpointName), "Endpoint name is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Target))
        {
            errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.Target), "Target is required."));
        }

        if (string.IsNullOrWhiteSpace(command.AgentId))
        {
            errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.AgentId), "Agent is required."));
        }

        ValidatePositive(nameof(CreateEndpointCommand.PingIntervalSeconds), command.PingIntervalSeconds, "Ping interval must be at least 1 second.", errors);
        ValidatePositive(nameof(CreateEndpointCommand.RetryIntervalSeconds), command.RetryIntervalSeconds, "Retry interval must be at least 1 second.", errors);
        ValidatePositive(nameof(CreateEndpointCommand.TimeoutMs), command.TimeoutMs, "Timeout must be at least 1 millisecond.", errors);
        ValidatePositive(nameof(CreateEndpointCommand.FailureThreshold), command.FailureThreshold, "Failure threshold must be at least 1.", errors);
        ValidatePositive(nameof(CreateEndpointCommand.RecoveryThreshold), command.RecoveryThreshold, "Recovery threshold must be at least 1.", errors);

        if (errors.Count > 0)
        {
            return errors;
        }

        var normalizedAgentId = command.AgentId.Trim();
        var agentExists = await _dbContext.Agents.AsNoTracking()
            .AnyAsync(x => x.AgentId == normalizedAgentId && x.Enabled && !x.ApiKeyRevoked, cancellationToken);
        if (!agentExists)
        {
            errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.AgentId), "Selected agent is not available."));
        }

        var dependencyId = NormalizeDependency(command.DependsOnEndpointId);
        if (dependencyId is not null)
        {
            var dependencyExists = await _dbContext.Endpoints.AsNoTracking()
                .AnyAsync(x => x.EndpointId == dependencyId, cancellationToken);

            if (!dependencyExists)
            {
                errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.DependsOnEndpointId), "Selected dependency endpoint does not exist."));
            }
            else
            {
                var hasCycle = await WouldCreateCycleAsync(dependencyId, cancellationToken);
                if (hasCycle)
                {
                    errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.DependsOnEndpointId), "Selected dependency creates a circular dependency."));
                }
            }
        }

        var normalizedEndpointName = command.EndpointName.Trim();
        var endpointNameExists = await _dbContext.Endpoints.AsNoTracking()
            .AnyAsync(x => x.Name == normalizedEndpointName, cancellationToken);

        if (endpointNameExists)
        {
            errors.Add(new CreateEndpointValidationError(nameof(CreateEndpointCommand.EndpointName), "An endpoint with this name already exists."));
        }

        return errors;
    }

    private async Task<bool> WouldCreateCycleAsync(string dependencyEndpointId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentId = dependencyEndpointId;

        while (!string.IsNullOrWhiteSpace(currentId))
        {
            if (!visited.Add(currentId))
            {
                return true;
            }

            currentId = await _dbContext.Endpoints.AsNoTracking()
                .Where(x => x.EndpointId == currentId)
                .Select(x => x.DependsOnEndpointId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    private static string? NormalizeDependency(string? dependencyId)
    {
        return string.IsNullOrWhiteSpace(dependencyId) ? null : dependencyId.Trim();
    }

    private static void ValidatePositive(string field, int value, string message, ICollection<CreateEndpointValidationError> errors)
    {
        if (value < 1)
        {
            errors.Add(new CreateEndpointValidationError(field, message));
        }
    }
}
