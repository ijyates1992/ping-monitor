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
        var validationErrors = await ValidateCreateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return CreateEndpointResult.Failed(validationErrors.ToArray());
        }

        var now = DateTimeOffset.UtcNow;
        var endpointId = Guid.NewGuid().ToString();
        var assignmentId = Guid.NewGuid().ToString();

        var endpoint = new EndpointModel
        {
            EndpointId = endpointId,
            Name = command.EndpointName.Trim(),
            Target = command.Target.Trim(),
            Enabled = command.EndpointEnabled,
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
        foreach (var dependencyEndpointId in NormalizeDependencies(command.DependsOnEndpointIds))
        {
            _dbContext.EndpointDependencies.Add(new EndpointDependency
            {
                EndpointDependencyId = Guid.NewGuid().ToString(),
                EndpointId = endpointId,
                DependsOnEndpointId = dependencyEndpointId,
                CreatedAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreateEndpointResult.Succeeded(endpointId, assignmentId);
    }

    public async Task<EditEndpointModel?> GetEditModelAsync(string assignmentId, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = NormalizeRequired(assignmentId);
        if (normalizedAssignmentId is null)
        {
            return null;
        }

        var model = await (
            from assignment in _dbContext.MonitorAssignments.AsNoTracking()
            join endpoint in _dbContext.Endpoints.AsNoTracking() on assignment.EndpointId equals endpoint.EndpointId
            where assignment.AssignmentId == normalizedAssignmentId
            select new EditEndpointModel
            {
                AssignmentId = assignment.AssignmentId,
                EndpointId = endpoint.EndpointId,
                EndpointName = endpoint.Name,
                Target = endpoint.Target,
                AgentId = assignment.AgentId,
                EndpointEnabled = endpoint.Enabled,
                AssignmentEnabled = assignment.Enabled,
                PingIntervalSeconds = assignment.PingIntervalSeconds,
                RetryIntervalSeconds = assignment.RetryIntervalSeconds,
                TimeoutMs = assignment.TimeoutMs,
                FailureThreshold = assignment.FailureThreshold,
                RecoveryThreshold = assignment.RecoveryThreshold
            }).SingleOrDefaultAsync(cancellationToken);

        if (model is null)
        {
            return null;
        }

        var dependencyIds = await _dbContext.EndpointDependencies.AsNoTracking()
            .Where(x => x.EndpointId == model.EndpointId)
            .OrderBy(x => x.DependsOnEndpointId)
            .Select(x => x.DependsOnEndpointId)
            .ToArrayAsync(cancellationToken);

        return new EditEndpointModel
        {
            AssignmentId = model.AssignmentId,
            EndpointId = model.EndpointId,
            EndpointName = model.EndpointName,
            Target = model.Target,
            AgentId = model.AgentId,
            DependsOnEndpointIds = dependencyIds,
            EndpointEnabled = model.EndpointEnabled,
            AssignmentEnabled = model.AssignmentEnabled,
            PingIntervalSeconds = model.PingIntervalSeconds,
            RetryIntervalSeconds = model.RetryIntervalSeconds,
            TimeoutMs = model.TimeoutMs,
            FailureThreshold = model.FailureThreshold,
            RecoveryThreshold = model.RecoveryThreshold
        };
    }

    public async Task<UpdateEndpointResult> UpdateEndpointWithAssignmentAsync(UpdateEndpointCommand command, CancellationToken cancellationToken)
    {
        var validationErrors = await ValidateUpdateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return UpdateEndpointResult.Failed(validationErrors.ToArray());
        }

        var assignment = await _dbContext.MonitorAssignments
            .SingleAsync(x => x.AssignmentId == command.AssignmentId.Trim(), cancellationToken);
        var endpoint = await _dbContext.Endpoints
            .SingleAsync(x => x.EndpointId == command.EndpointId.Trim(), cancellationToken);

        endpoint.Name = command.EndpointName.Trim();
        endpoint.Target = command.Target.Trim();
        endpoint.Enabled = command.EndpointEnabled;

        assignment.AgentId = command.AgentId.Trim();
        assignment.Enabled = command.AssignmentEnabled;
        assignment.PingIntervalSeconds = command.PingIntervalSeconds;
        assignment.RetryIntervalSeconds = command.RetryIntervalSeconds;
        assignment.TimeoutMs = command.TimeoutMs;
        assignment.FailureThreshold = command.FailureThreshold;
        assignment.RecoveryThreshold = command.RecoveryThreshold;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var dependencyIds = NormalizeDependencies(command.DependsOnEndpointIds);
        var existingDependencies = await _dbContext.EndpointDependencies
            .Where(x => x.EndpointId == endpoint.EndpointId)
            .ToListAsync(cancellationToken);

        var dependenciesToRemove = existingDependencies
            .Where(x => !dependencyIds.Contains(x.DependsOnEndpointId, StringComparer.Ordinal))
            .ToArray();
        if (dependenciesToRemove.Length > 0)
        {
            _dbContext.EndpointDependencies.RemoveRange(dependenciesToRemove);
        }

        foreach (var dependencyId in dependencyIds)
        {
            if (existingDependencies.All(x => !string.Equals(x.DependsOnEndpointId, dependencyId, StringComparison.Ordinal)))
            {
                _dbContext.EndpointDependencies.Add(new EndpointDependency
                {
                    EndpointDependencyId = Guid.NewGuid().ToString(),
                    EndpointId = endpoint.EndpointId,
                    DependsOnEndpointId = dependencyId,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return UpdateEndpointResult.Succeeded();
    }

    public async Task<RemoveEndpointResult> RemoveByAssignmentAsync(string assignmentId, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = NormalizeRequired(assignmentId);
        if (normalizedAssignmentId is null)
        {
            return RemoveEndpointResult.NotFound();
        }

        var assignment = await _dbContext.MonitorAssignments
            .SingleOrDefaultAsync(x => x.AssignmentId == normalizedAssignmentId, cancellationToken);

        if (assignment is null)
        {
            return RemoveEndpointResult.NotFound();
        }

        var endpoint = await _dbContext.Endpoints
            .SingleAsync(x => x.EndpointId == assignment.EndpointId, cancellationToken);

        var endpointAssignments = await _dbContext.MonitorAssignments
            .Where(x => x.EndpointId == endpoint.EndpointId)
            .ToListAsync(cancellationToken);

        var changed = false;
        if (endpoint.Enabled)
        {
            endpoint.Enabled = false;
            changed = true;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var endpointAssignment in endpointAssignments)
        {
            if (!endpointAssignment.Enabled)
            {
                continue;
            }

            endpointAssignment.Enabled = false;
            endpointAssignment.UpdatedAtUtc = now;
            changed = true;
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return RemoveEndpointResult.Completed(changed);
    }

    private async Task<List<EndpointValidationError>> ValidateCreateAsync(CreateEndpointCommand command, CancellationToken cancellationToken)
    {
        var errors = new List<EndpointValidationError>();

        ValidateCommon(
            command.EndpointName,
            command.Target,
            command.AgentId,
            command.PingIntervalSeconds,
            command.RetryIntervalSeconds,
            command.TimeoutMs,
            command.FailureThreshold,
            command.RecoveryThreshold,
            errors);

        if (errors.Count > 0)
        {
            return errors;
        }

        await ValidateAgentAndDependenciesAsync(command.AgentId, command.DependsOnEndpointIds, null, errors, cancellationToken);

        var normalizedEndpointName = command.EndpointName.Trim();
        var endpointNameExists = await _dbContext.Endpoints.AsNoTracking()
            .AnyAsync(x => x.Name == normalizedEndpointName, cancellationToken);

        if (endpointNameExists)
        {
            errors.Add(new EndpointValidationError(nameof(CreateEndpointCommand.EndpointName), "An endpoint with this name already exists."));
        }

        return errors;
    }

    private async Task<List<EndpointValidationError>> ValidateUpdateAsync(UpdateEndpointCommand command, CancellationToken cancellationToken)
    {
        var errors = new List<EndpointValidationError>();

        var normalizedAssignmentId = NormalizeRequired(command.AssignmentId);
        var normalizedEndpointId = NormalizeRequired(command.EndpointId);

        if (normalizedAssignmentId is null)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.AssignmentId), "Assignment ID is required."));
        }

        if (normalizedEndpointId is null)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.EndpointId), "Endpoint ID is required."));
        }

        ValidateCommon(
            command.EndpointName,
            command.Target,
            command.AgentId,
            command.PingIntervalSeconds,
            command.RetryIntervalSeconds,
            command.TimeoutMs,
            command.FailureThreshold,
            command.RecoveryThreshold,
            errors);

        if (errors.Count > 0)
        {
            return errors;
        }

        var assignment = await _dbContext.MonitorAssignments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AssignmentId == normalizedAssignmentId, cancellationToken);

        if (assignment is null)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.AssignmentId), "Assignment was not found."));
            return errors;
        }

        if (!string.Equals(assignment.EndpointId, normalizedEndpointId, StringComparison.Ordinal))
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.EndpointId), "Endpoint does not match assignment."));
            return errors;
        }

        var endpointExists = await _dbContext.Endpoints.AsNoTracking()
            .AnyAsync(x => x.EndpointId == normalizedEndpointId, cancellationToken);

        if (!endpointExists)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.EndpointId), "Endpoint was not found."));
            return errors;
        }

        await ValidateAgentAndDependenciesAsync(command.AgentId, command.DependsOnEndpointIds, normalizedEndpointId, errors, cancellationToken);

        var normalizedEndpointName = command.EndpointName.Trim();
        var duplicateNameExists = await _dbContext.Endpoints.AsNoTracking()
            .AnyAsync(x => x.Name == normalizedEndpointName && x.EndpointId != normalizedEndpointId, cancellationToken);

        if (duplicateNameExists)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.EndpointName), "An endpoint with this name already exists."));
        }

        var duplicateAssignmentExists = await _dbContext.MonitorAssignments.AsNoTracking()
            .AnyAsync(x => x.AssignmentId != normalizedAssignmentId && x.AgentId == command.AgentId.Trim() && x.EndpointId == normalizedEndpointId, cancellationToken);

        if (duplicateAssignmentExists)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.AgentId), "This agent is already assigned to the endpoint."));
        }

        return errors;
    }

    private async Task ValidateAgentAndDependenciesAsync(
        string agentId,
        IReadOnlyList<string> dependsOnEndpointIds,
        string? currentEndpointId,
        ICollection<EndpointValidationError> errors,
        CancellationToken cancellationToken)
    {
        var normalizedAgentId = agentId.Trim();
        var agentExists = await _dbContext.Agents.AsNoTracking()
            .AnyAsync(x => x.AgentId == normalizedAgentId && x.Enabled && !x.ApiKeyRevoked, cancellationToken);

        if (!agentExists)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.AgentId), "Selected agent is not available."));
        }

        var normalizedDependencies = NormalizeDependencies(dependsOnEndpointIds);

        var dependencyIds = normalizedDependencies.ToArray();
        if (dependencyIds.Length == 0)
        {
            return;
        }

        var existingDependencyIds = await _dbContext.Endpoints.AsNoTracking()
            .Where(x => dependencyIds.Contains(x.EndpointId))
            .Select(x => x.EndpointId)
            .ToArrayAsync(cancellationToken);

        foreach (var dependencyId in dependencyIds)
        {
            if (!existingDependencyIds.Contains(dependencyId, StringComparer.Ordinal))
            {
                errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointIds), $"Dependency endpoint '{dependencyId}' does not exist."));
            }
        }

        if (string.IsNullOrWhiteSpace(currentEndpointId))
        {
            return;
        }

        foreach (var dependencyId in dependencyIds)
        {
            if (string.Equals(dependencyId, currentEndpointId, StringComparison.Ordinal))
            {
                errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointIds), "Dependency cannot reference the endpoint itself."));
                continue;
            }

            var hasCycle = await WouldCreateCycleAsync(currentEndpointId, dependencyId, cancellationToken);
            if (hasCycle)
            {
                errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointIds), "Selected dependency creates a circular dependency."));
            }
        }
    }

    private async Task<bool> WouldCreateCycleAsync(string endpointId, string dependencyEndpointId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(dependencyEndpointId);

        while (stack.Count > 0)
        {
            var currentId = stack.Pop();
            if (string.Equals(currentId, endpointId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!visited.Add(currentId))
            {
                continue;
            }

            var nextDependencies = await _dbContext.EndpointDependencies.AsNoTracking()
                .Where(x => x.EndpointId == currentId)
                .Select(x => x.DependsOnEndpointId)
                .ToArrayAsync(cancellationToken);

            foreach (var nextDependencyId in nextDependencies)
            {
                stack.Push(nextDependencyId);
            }
        }

        return false;
    }

    private static void ValidateCommon(
        string endpointName,
        string target,
        string agentId,
        int pingIntervalSeconds,
        int retryIntervalSeconds,
        int timeoutMs,
        int failureThreshold,
        int recoveryThreshold,
        ICollection<EndpointValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.EndpointName), "Endpoint name is required."));
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.Target), "Target is required."));
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.AgentId), "Agent is required."));
        }

        ValidatePositive(nameof(UpdateEndpointCommand.PingIntervalSeconds), pingIntervalSeconds, "Ping interval must be at least 1 second.", errors);
        ValidatePositive(nameof(UpdateEndpointCommand.RetryIntervalSeconds), retryIntervalSeconds, "Retry interval must be at least 1 second.", errors);
        ValidatePositive(nameof(UpdateEndpointCommand.TimeoutMs), timeoutMs, "Timeout must be at least 1 millisecond.", errors);
        ValidatePositive(nameof(UpdateEndpointCommand.FailureThreshold), failureThreshold, "Failure threshold must be at least 1.", errors);
        ValidatePositive(nameof(UpdateEndpointCommand.RecoveryThreshold), recoveryThreshold, "Recovery threshold must be at least 1.", errors);
    }

    private static HashSet<string> NormalizeDependencies(IReadOnlyList<string> dependencyIds)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependencyId in dependencyIds)
        {
            var value = NormalizeRequired(dependencyId);
            if (value is not null)
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    private static string? NormalizeRequired(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidatePositive(string field, int value, string message, ICollection<EndpointValidationError> errors)
    {
        if (value < 1)
        {
            errors.Add(new EndpointValidationError(field, message));
        }
    }
}
