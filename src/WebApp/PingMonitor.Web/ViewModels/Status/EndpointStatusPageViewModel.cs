using PingMonitor.Web.Models;
using PingMonitor.Web.ViewModels.EventLogs;

namespace PingMonitor.Web.ViewModels.Status;

public sealed class EndpointStatusPageViewModel
{
    public EndpointStatusSummaryViewModel Summary { get; init; } = new();
    public int IngestPerMinute { get; init; }
    public int DropPerMinute { get; init; }
    public EndpointStatusFiltersViewModel Filters { get; init; } = new();
    public IReadOnlyList<EndpointStatusRowViewModel> Rows { get; init; } = [];
    public IReadOnlyList<RecentEventViewModel> RecentEvents { get; init; } = [];
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
    public string? GroupId { get; init; }
    public IReadOnlyList<string> AvailableAgents { get; init; } = [];
    public IReadOnlyList<StatusGroupOptionViewModel> AvailableGroups { get; init; } = [];
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
    public string IconKey { get; init; } = "generic";
    public string Target { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string AgentInstanceId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public EndpointStateKind CurrentState { get; init; } = EndpointStateKind.Unknown;
    public string StatusCssClass { get; init; } = "status-unknown";
    public DateTimeOffset? LastCheckUtc { get; init; }
    public DateTimeOffset? LastStateChangeUtc { get; init; }
    public TimeSpan? CurrentStateDuration { get; init; }
    public string CurrentStateDurationDisplay { get; init; } = "—";
    public int ConsecutiveFailureCount { get; init; }
    public int ConsecutiveSuccessCount { get; init; }
    public string CheckType { get; init; } = string.Empty;
    public bool AssignmentEnabled { get; init; }
    public bool EndpointEnabled { get; init; }
    public IReadOnlyList<string> ParentEndpointIds { get; init; } = [];
    public IReadOnlyList<string> ParentEndpointNames { get; init; } = [];
    public string? SuppressedByEndpointId { get; init; }
    public string? SuppressedByEndpointName { get; init; }
    public IReadOnlyList<string> GroupNames { get; init; } = [];
    public double? UptimePercent { get; init; }
    public double? LastRttMs { get; init; }
    public double? AverageRttMs { get; init; }
}

public sealed class StatusGroupOptionViewModel
{
    public string GroupId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
