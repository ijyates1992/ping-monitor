using PingMonitor.Web.Contracts;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services;

public interface IAgentAuthenticationService
{
    Task<bool> ValidateAsync(string instanceId, string? authorizationHeader, CancellationToken cancellationToken);
}

public interface IAgentConfigurationService
{
    Task<AgentHelloResponse> BuildHelloResponseAsync(string instanceId, AgentHelloRequest request, CancellationToken cancellationToken);
    Task<AgentConfigResponse> GetConfigurationAsync(string instanceId, CancellationToken cancellationToken);
}

public interface IResultIngestionService
{
    Task<SubmitResultsResponse> IngestAsync(string instanceId, SubmitResultsRequest request, CancellationToken cancellationToken);
}

public interface IHeartbeatService
{
    Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(string instanceId, AgentHeartbeatRequest request, CancellationToken cancellationToken);
}

public interface IStateEvaluationService
{
    Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken);
}

internal sealed class PlaceholderAgentAuthenticationService : IAgentAuthenticationService
{
    public Task<bool> ValidateAsync(string instanceId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(instanceId));
    }
}

internal sealed class PlaceholderAgentConfigurationService : IAgentConfigurationService
{
    public Task<AgentHelloResponse> BuildHelloResponseAsync(string instanceId, AgentHelloRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AgentHelloResponse(
            AgentId: instanceId,
            ServerTimeUtc: now,
            ConfigRefreshSeconds: 300,
            HeartbeatIntervalSeconds: 60,
            ResultBatchIntervalSeconds: 10,
            MaxResultBatchSize: 500,
            ConfigVersion: "cfg_placeholder_v1");

        return Task.FromResult(response);
    }

    public Task<AgentConfigResponse> GetConfigurationAsync(string instanceId, CancellationToken cancellationToken)
    {
        var response = new AgentConfigResponse(
            ConfigVersion: "cfg_placeholder_v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assignments: new List<MonitorAssignmentDto>());

        return Task.FromResult(response);
    }
}

internal sealed class PlaceholderResultIngestionService : IResultIngestionService
{
    public Task<SubmitResultsResponse> IngestAsync(string instanceId, SubmitResultsRequest request, CancellationToken cancellationToken)
    {
        var response = new SubmitResultsResponse(
            Accepted: true,
            AcceptedCount: request.Results.Count,
            Duplicate: false,
            ServerTimeUtc: DateTimeOffset.UtcNow);

        return Task.FromResult(response);
    }
}

internal sealed class PlaceholderHeartbeatService : IHeartbeatService
{
    public Task<AgentHeartbeatResponse> ProcessHeartbeatAsync(string instanceId, AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var response = new AgentHeartbeatResponse(
            Ok: true,
            ServerTimeUtc: DateTimeOffset.UtcNow,
            ConfigChanged: false);

        return Task.FromResult(response);
    }
}

internal sealed class PlaceholderStateEvaluationService : IStateEvaluationService
{
    public Task EvaluateAssignmentStateAsync(string assignmentId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
