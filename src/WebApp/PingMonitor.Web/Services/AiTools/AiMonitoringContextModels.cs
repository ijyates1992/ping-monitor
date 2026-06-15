using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiTools;

public sealed class AiMonitoringContextResult
{
    public bool Succeeded { get; init; }
    public AiNetworkHealthSummary? Summary { get; init; }
    public string? ErrorMessage { get; init; }

    public static AiMonitoringContextResult Success(AiNetworkHealthSummary summary) => new()
    {
        Succeeded = true,
        Summary = summary
    };

    public static AiMonitoringContextResult Unavailable(string message) => new()
    {
        Succeeded = false,
        ErrorMessage = message
    };
}

public sealed class AiNetworkHealthSummary
{
    public const string ToolName = "get_network_health_summary";
    public string CapabilityName { get; init; } = ToolName;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string DataSource { get; init; } = "current_endpoint_state";
    public bool PermissionFiltered { get; init; } = true;
    public int VisibleEndpointCount { get; init; }
    public int VisibleAssignmentCount { get; init; }
    public AiNetworkStateCounts StateCounts { get; init; } = new();
    public IReadOnlyList<AiEndpointStateSummaryItem> DownEndpoints { get; init; } = [];
    public int DownEndpointOmittedCount { get; init; }
    public IReadOnlyList<AiEndpointStateSummaryItem> DegradedEndpoints { get; init; } = [];
    public int DegradedEndpointOmittedCount { get; init; }
    public IReadOnlyList<AiEndpointStateSummaryItem> UnknownEndpoints { get; init; } = [];
    public int UnknownEndpointOmittedCount { get; init; }
    public IReadOnlyList<AiEndpointStateSummaryItem> SuppressedEndpoints { get; init; } = [];
    public int SuppressedEndpointOmittedCount { get; init; }
    public IReadOnlyList<AiAgentHealthSummaryItem> StaleAgents { get; init; } = [];
    public int StaleAgentOmittedCount { get; init; }
    public IReadOnlyList<AiAgentHealthSummaryItem> OfflineAgents { get; init; } = [];
    public int OfflineAgentOmittedCount { get; init; }
    public string RecentStateChangeWindow { get; init; } = "PT1H";
    public int RecentStateChangeCount { get; init; }
    public IReadOnlyList<AiRecentStateChangeSummaryItem> RecentStateChanges { get; init; } = [];
    public int RecentStateChangeOmittedCount { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } =
    [
        "This summary uses current endpoint state and recent transitions only.",
        "Raw CheckResults diagnostics are not connected in this slice.",
        "Endpoint diagnostic packs, diagram lookup, baseline comparisons, and AI memory are not connected yet."
    ];
}

public sealed class AiNetworkStateCounts
{
    public int Up { get; init; }
    public int Degraded { get; init; }
    public int Down { get; init; }
    public int Suppressed { get; init; }
    public int Unknown { get; init; }
}

public sealed class AiEndpointStateSummaryItem
{
    public string EndpointId { get; init; } = string.Empty;
    public string AssignmentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public EndpointStateKind State { get; init; } = EndpointStateKind.Unknown;
    public DateTimeOffset? LastChangedUtc { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public string? SuppressedByEndpointId { get; init; }
    public string? SuppressedByEndpointName { get; init; }
}

public sealed class AiAgentHealthSummaryItem
{
    public string AgentId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AgentHealthStatus Status { get; init; } = AgentHealthStatus.Offline;
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public int VisibleAssignmentCount { get; init; }
}

public sealed class AiRecentStateChangeSummaryItem
{
    public string EndpointId { get; init; } = string.Empty;
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public EndpointStateKind PreviousState { get; init; } = EndpointStateKind.Unknown;
    public EndpointStateKind NewState { get; init; } = EndpointStateKind.Unknown;
    public DateTimeOffset TransitionAtUtc { get; init; }
    public string? ReasonCode { get; init; }
}

