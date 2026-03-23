using PingMonitor.Web.Models;

namespace PingMonitor.Web.ViewModels.Status;

public sealed class EndpointStatusPageViewModel
{
    public EndpointStatusSummaryViewModel Summary { get; init; } = new();
    public EndpointStatusFiltersViewModel Filters { get; init; } = new();
    public IReadOnlyList<EndpointStatusRowViewModel> Rows { get; init; } = [];
}

public sealed class EndpointStatusSummaryViewModel
{
    public int TotalAssignments { get; init; }
    public int UnknownCount { get; init; }
    public int UpCount { get; init; }
    public int DegradedCount { get; init; }
    public int DownCount { get; init; }
    public int SuppressedCount { get; init; }
}

public sealed class EndpointStatusFiltersViewModel
{
    public string? State { get; init; }
    public string? Agent { get; init; }
    public string? Search { get; init; }
    public IReadOnlyList<string> AvailableAgents { get; init; } = [];
    public IReadOnlyList<EndpointStateKind> AvailableStates { get; init; } =
    [
        EndpointStateKind.Unknown,
        EndpointStateKind.Up,
        EndpointStateKind.Degraded,
        EndpointStateKind.Down,
        EndpointStateKind.Suppressed
    ];
}

public sealed class EndpointStatusRowViewModel
{
    public string AssignmentId { get; init; } = string.Empty;
    public string EndpointId { get; init; } = string.Empty;
    public string EndpointName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentInstanceId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public EndpointStateKind CurrentState { get; init; } = EndpointStateKind.Unknown;
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? LastStateChangeUtc { get; init; }
    public int ConsecutiveFailureCount { get; init; }
    public int ConsecutiveSuccessCount { get; init; }
    public string CheckType { get; init; } = string.Empty;
    public bool AssignmentEnabled { get; init; }
    public bool EndpointEnabled { get; init; }
    public string? ParentEndpointId { get; init; }
    public string? ParentEndpointName { get; init; }
    public string? SuppressedByEndpointId { get; init; }
    public string? SuppressedByEndpointName { get; init; }
}
