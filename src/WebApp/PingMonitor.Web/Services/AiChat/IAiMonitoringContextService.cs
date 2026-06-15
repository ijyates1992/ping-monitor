using System.Security.Claims;

namespace PingMonitor.Web.Services.AiChat;

public interface IAiMonitoringContextService
{
    Task<AiMonitoringContextResult> TryGetNetworkHealthSummaryAsync(AiMonitoringContextRequest request, CancellationToken cancellationToken);
}

public sealed class AiMonitoringContextRequest
{
    public ClaimsPrincipal? Principal { get; init; }
    public string? UserId { get; init; }
    public string UserMessage { get; init; } = string.Empty;
}

public sealed class AiMonitoringContextResult
{
    public const string ToolName = "get_network_health_summary";
    public bool ShouldInclude { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public AiNetworkHealthSummary? Summary { get; init; }
}

public sealed class AiNetworkHealthSummary
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string DataSource { get; init; } = "current_endpoint_state";
    public int VisibleEndpointCount { get; init; }
    public AiNetworkHealthStateCounts StateCounts { get; init; } = new();
    public IReadOnlyList<AiNetworkHealthEndpoint> DownEndpoints { get; init; } = [];
    public IReadOnlyList<AiNetworkHealthEndpoint> DegradedEndpoints { get; init; } = [];
    public IReadOnlyList<AiNetworkHealthEndpoint> UnknownEndpoints { get; init; } = [];
    public IReadOnlyList<AiNetworkHealthEndpoint> SuppressedEndpoints { get; init; } = [];
    public IReadOnlyList<AiNetworkHealthAgent> StaleAgents { get; init; } = [];
    public IReadOnlyList<AiNetworkHealthStateChange> RecentStateChanges { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed class AiNetworkHealthStateCounts
{
    public int Up { get; init; }
    public int Degraded { get; init; }
    public int Down { get; init; }
    public int Suppressed { get; init; }
    public int Unknown { get; init; }
}

public sealed class AiNetworkHealthEndpoint
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? LastChangedUtc { get; init; }
}

public sealed class AiNetworkHealthAgent
{
    public string AgentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
}

public sealed class AiNetworkHealthStateChange
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PreviousState { get; init; } = string.Empty;
    public string NewState { get; init; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; init; }
    public string? ReasonCode { get; init; }
}
