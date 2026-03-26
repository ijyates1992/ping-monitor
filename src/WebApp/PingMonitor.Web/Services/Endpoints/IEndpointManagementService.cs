namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointManagementService
{
    Task<CreateEndpointResult> CreateEndpointWithAssignmentAsync(CreateEndpointCommand command, CancellationToken cancellationToken);
    Task<EditEndpointModel?> GetEditModelAsync(string assignmentId, CancellationToken cancellationToken);
    Task<UpdateEndpointResult> UpdateEndpointWithAssignmentAsync(UpdateEndpointCommand command, CancellationToken cancellationToken);
    Task<RemoveEndpointResult> RemoveByAssignmentAsync(string assignmentId, CancellationToken cancellationToken);
}

public sealed class CreateEndpointCommand
{
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnEndpointIds { get; init; } = [];
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
    public bool EndpointEnabled { get; init; }
    public bool AssignmentEnabled { get; init; }
}

public sealed class UpdateEndpointCommand
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnEndpointIds { get; init; } = [];
    public bool EndpointEnabled { get; init; }
    public bool AssignmentEnabled { get; init; }
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
}

public sealed class EditEndpointModel
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public IReadOnlyList<string> DependsOnEndpointIds { get; init; } = [];
    public bool EndpointEnabled { get; init; }
    public bool AssignmentEnabled { get; init; }
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
}

public sealed class CreateEndpointResult
{
    public bool Success { get; init; }
    public string? EndpointId { get; init; }
    public string? AssignmentId { get; init; }
    public IReadOnlyList<EndpointValidationError> ValidationErrors { get; init; } = [];

    public static CreateEndpointResult Succeeded(string endpointId, string assignmentId)
    {
        return new CreateEndpointResult
        {
            Success = true,
            EndpointId = endpointId,
            AssignmentId = assignmentId
        };
    }

    public static CreateEndpointResult Failed(params EndpointValidationError[] errors)
    {
        return new CreateEndpointResult
        {
            Success = false,
            ValidationErrors = errors
        };
    }
}

public sealed class UpdateEndpointResult
{
    public bool Success { get; init; }
    public IReadOnlyList<EndpointValidationError> ValidationErrors { get; init; } = [];

    public static UpdateEndpointResult Succeeded() => new() { Success = true };

    public static UpdateEndpointResult Failed(params EndpointValidationError[] errors)
    {
        return new UpdateEndpointResult
        {
            Success = false,
            ValidationErrors = errors
        };
    }
}

public sealed class EndpointValidationError
{
    public EndpointValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }
    public string Message { get; }
}

public sealed class RemoveEndpointResult
{
    public bool Found { get; init; }
    public bool Changed { get; init; }

    public static RemoveEndpointResult NotFound() => new() { Found = false, Changed = false };

    public static RemoveEndpointResult Completed(bool changed) => new() { Found = true, Changed = changed };
}
