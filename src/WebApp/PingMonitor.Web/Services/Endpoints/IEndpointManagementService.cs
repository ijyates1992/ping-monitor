namespace PingMonitor.Web.Services.Endpoints;

public interface IEndpointManagementService
{
    Task<CreateEndpointResult> CreateEndpointWithAssignmentAsync(CreateEndpointCommand command, CancellationToken cancellationToken);
}

public sealed class CreateEndpointCommand
{
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string? DependsOnEndpointId { get; init; }
    public int PingIntervalSeconds { get; init; }
    public int RetryIntervalSeconds { get; init; }
    public int TimeoutMs { get; init; }
    public int FailureThreshold { get; init; }
    public int RecoveryThreshold { get; init; }
    public bool EndpointEnabled { get; init; }
    public bool AssignmentEnabled { get; init; }
}

public sealed class CreateEndpointResult
{
    public bool Success { get; init; }
    public string? EndpointId { get; init; }
    public string? AssignmentId { get; init; }
    public IReadOnlyList<CreateEndpointValidationError> ValidationErrors { get; init; } = [];

    public static CreateEndpointResult Succeeded(string endpointId, string assignmentId)
    {
        return new CreateEndpointResult
        {
            Success = true,
            EndpointId = endpointId,
            AssignmentId = assignmentId
        };
    }

    public static CreateEndpointResult Failed(params CreateEndpointValidationError[] errors)
    {
        return new CreateEndpointResult
        {
            Success = false,
            ValidationErrors = errors
        };
    }
}

public sealed class CreateEndpointValidationError
{
    public CreateEndpointValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }
    public string Message { get; }
}
