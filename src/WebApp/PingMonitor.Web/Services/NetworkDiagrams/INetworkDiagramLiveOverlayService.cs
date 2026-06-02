using System.Security.Claims;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.NetworkDiagrams;

public interface INetworkDiagramLiveOverlayService
{
    Task<NetworkDiagramLiveOverlayResponse> GetOverlayAsync(string diagramId, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class NetworkDiagramLiveOverlayResponse
{
    public string DiagramId { get; init; } = string.Empty;
    public DateTimeOffset RefreshedAtUtc { get; init; }
    public IReadOnlyList<NetworkDiagramNodeLiveOverlayDto> Nodes { get; init; } = [];
}

public sealed class NetworkDiagramNodeLiveOverlayDto
{
    public string NodeId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public EndpointStateKind SummaryState { get; init; } = EndpointStateKind.Unknown;
    public string SummaryStateLabel { get; init; } = "Unknown";
    public double? UptimePercent24h { get; init; }
    public string UptimeDisplay { get; init; } = "—";
    public double? LastRttMs { get; init; }
    public double? AverageRttMs { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? LastSuccessfulCheckUtc { get; init; }
    public IReadOnlyList<NetworkDiagramAssignmentLiveOverlayDto> Assignments { get; init; } = [];
}

public sealed class NetworkDiagramAssignmentLiveOverlayDto
{
    public string AssignmentId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public EndpointStateKind State { get; init; } = EndpointStateKind.Unknown;
    public string StateLabel { get; init; } = "Unknown";
    public double? UptimePercent24h { get; init; }
    public string UptimeDisplay { get; init; } = "—";
    public double? LastRttMs { get; init; }
    public double? AverageRttMs { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? LastSuccessfulCheckUtc { get; init; }
    public string? SuppressedByEndpointId { get; init; }
    public string? SuppressedByEndpointName { get; init; }
}
