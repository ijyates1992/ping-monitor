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
            DependsOnEndpointId = NormalizeDependency(command.DependsOnEndpointId),
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

    public async Task<EditEndpointModel?> GetEditModelAsync(string assignmentId, CancellationToken cancellationToken)
    {
        var normalizedAssignmentId = NormalizeRequired(assignmentId);
        if (normalizedAssignmentId is null)
        {
            return null;
        }

        return await (
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
                DependsOnEndpointId = endpoint.DependsOnEndpointId,
                EndpointEnabled = endpoint.Enabled,
                AssignmentEnabled = assignment.Enabled,
                PingIntervalSeconds = assignment.PingIntervalSeconds,
                RetryIntervalSeconds = assignment.RetryIntervalSeconds,
                TimeoutMs = assignment.TimeoutMs,
                FailureThreshold = assignment.FailureThreshold,
                RecoveryThreshold = assignment.RecoveryThreshold
            }).SingleOrDefaultAsync(cancellationToken);
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
        endpoint.DependsOnEndpointId = NormalizeDependency(command.DependsOnEndpointId);

        assignment.AgentId = command.AgentId.Trim();
        assignment.Enabled = command.AssignmentEnabled;
        assignment.PingIntervalSeconds = command.PingIntervalSeconds;
        assignment.RetryIntervalSeconds = command.RetryIntervalSeconds;
        assignment.TimeoutMs = command.TimeoutMs;
        assignment.FailureThreshold = command.FailureThreshold;
        assignment.RecoveryThreshold = command.RecoveryThreshold;
        assignment.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return UpdateEndpointResult.Succeeded();
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

        await ValidateAgentAndDependencyAsync(command.AgentId, command.DependsOnEndpointId, null, errors, cancellationToken);

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

        await ValidateAgentAndDependencyAsync(command.AgentId, command.DependsOnEndpointId, normalizedEndpointId, errors, cancellationToken);

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

    private async Task ValidateAgentAndDependencyAsync(
        string agentId,
        string? dependsOnEndpointId,
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

        var dependencyId = NormalizeDependency(dependsOnEndpointId);
        if (dependencyId is null)
        {
            return;
        }

        var dependencyExists = await _dbContext.Endpoints.AsNoTracking()
            .AnyAsync(x => x.EndpointId == dependencyId, cancellationToken);

        if (!dependencyExists)
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointId), "Selected dependency endpoint does not exist."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentEndpointId) && string.Equals(dependencyId, currentEndpointId, StringComparison.Ordinal))
        {
            errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointId), "Dependency cannot reference the endpoint itself."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentEndpointId))
        {
            var hasCycle = await WouldCreateCycleAsync(currentEndpointId, dependencyId, cancellationToken);
            if (hasCycle)
            {
                errors.Add(new EndpointValidationError(nameof(UpdateEndpointCommand.DependsOnEndpointId), "Selected dependency creates a circular dependency."));
            }
        }
    }

    private async Task<bool> WouldCreateCycleAsync(string endpointId, string dependencyEndpointId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentId = dependencyEndpointId;

        while (!string.IsNullOrWhiteSpace(currentId))
        {
            if (string.Equals(currentId, endpointId, StringComparison.Ordinal))
            {
                return true;
            }

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

    private static string? NormalizeDependency(string? dependencyId)
    {
        return string.IsNullOrWhiteSpace(dependencyId) ? null : dependencyId.Trim();
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
