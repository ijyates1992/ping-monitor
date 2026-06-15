using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiTools;

public sealed class AiEndpointLookupResult
{
    public bool Succeeded { get; init; }
    public bool Ambiguous { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<AiEndpointLookupItem> Matches { get; init; } = [];
    public AiEndpointLookupItem? StrongMatch => !Ambiguous && Matches.Count == 1 ? Matches[0] : null;
}

public sealed class AiEndpointLookupItem
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class AiEndpointDiagnosticsResult
{
    public bool Succeeded { get; init; }
    public AiEndpointDiagnosticsPack? Pack { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AiEndpointDiagnosticsPack
{
    public const string ToolName = "get_endpoint_diagnostics_pack";
    public string CapabilityName { get; init; } = ToolName;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string DataSource { get; init; } = "endpoint_diagnostics_pack";
    public bool PermissionFiltered { get; init; } = true;
    public AiEndpointLookupItem Endpoint { get; init; } = new();
    public AiEndpointAssignmentInfo? Assignment { get; init; }
    public AiEndpointCurrentStateInfo CurrentState { get; init; } = new();
    public AiTimeWindowInfo Window { get; init; } = new();
    public AiEndpointUptimeSummary Uptime { get; init; } = new();
    public AiEndpointCheckSummary Checks { get; init; } = new();
    public AiEndpointRttSummary Rtt { get; init; } = new();
    public IReadOnlyList<AiEndpointTransitionItem> RecentTransitions { get; init; } = [];
    public IReadOnlyList<AiEndpointCheckSeriesPoint> RecentSampleTail { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } =
    [
        "This diagnostics pack is bounded and may use bucketed summaries.",
        "Full raw CheckResults export is not exposed to the AI assistant.",
        "Diagram lookup, switch port/VLAN answers, AI memory, and write actions are not connected yet."
    ];
}

public sealed class AiEndpointAssignmentInfo
{
    public string AssignmentId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public AgentHealthStatus AgentOnlineState { get; init; }
}

public sealed class AiEndpointCurrentStateInfo
{
    public EndpointStateKind State { get; init; } = EndpointStateKind.Unknown;
    public DateTimeOffset? LastChangedUtc { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public string? UnknownReason { get; init; }
}

public sealed class AiTimeWindowInfo
{
    public string RequestedWindow { get; init; } = "24h";
    public string AppliedWindow { get; init; } = "24h";
    public bool Clamped { get; init; }
    public DateTimeOffset FromUtc { get; init; }
    public DateTimeOffset ToUtc { get; init; }
}

public sealed class AiEndpointUptimeSummary
{
    public long UptimeSeconds { get; init; }
    public long DowntimeSeconds { get; init; }
    public long UnknownSeconds { get; init; }
    public long SuppressedSeconds { get; init; }
    public double? UptimePercent { get; init; }
    public int DownTransitions { get; init; }
    public int RecoveryTransitions { get; init; }
}

public sealed class AiEndpointCheckSummary
{
    public int ReceivedSamples { get; init; }
    public int SuccessfulSamples { get; init; }
    public int FailedSamples { get; init; }
    public int? ExpectedSamples { get; init; }
    public int? MissingSamplesEstimate { get; init; }
    public double? PacketLossPercent { get; init; }
}

public sealed class AiEndpointRttSummary
{
    public bool Available { get; init; }
    public double? MinMs { get; init; }
    public double? AvgMs { get; init; }
    public double? MedianMs { get; init; }
    public double? P95Ms { get; init; }
    public double? MaxMs { get; init; }
    public string? Reason { get; init; }
}

public sealed class AiEndpointTransitionItem
{
    public EndpointStateKind PreviousState { get; init; }
    public EndpointStateKind NewState { get; init; }
    public DateTimeOffset TransitionAtUtc { get; init; }
    public string? ReasonCode { get; init; }
}

public sealed class AiEndpointCheckSeriesPoint
{
    public DateTimeOffset CheckedAtUtc { get; init; }
    public bool Success { get; init; }
    public double? RttMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
