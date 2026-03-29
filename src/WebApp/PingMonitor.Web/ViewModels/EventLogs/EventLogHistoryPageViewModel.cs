namespace PingMonitor.Web.ViewModels.EventLogs;

public sealed class EventLogHistoryPageViewModel
{
    public required string ScopeId { get; init; }
    public required string ScopeLabel { get; init; }
    public required string ScopeName { get; init; }
    public required string BackLink { get; init; }
    public required string EmptyMessage { get; init; }
    public EventLogHistoryFilterViewModel Filters { get; init; } = new();
    public IReadOnlyList<EventLogHistoryRowViewModel> Rows { get; init; } = [];
    public EventLogHistoryPaginationViewModel Pagination { get; init; } = new();
}

public sealed class EventLogHistoryFilterViewModel
{
    public string? Search { get; init; }
    public string? EventType { get; init; }
    public DateTimeOffset? DateFromUtc { get; init; }
    public DateTimeOffset? DateToUtc { get; init; }
    public int PageSize { get; init; }
    public IReadOnlyList<string> AvailableEventTypes { get; init; } = [];
}

public sealed class EventLogHistoryRowViewModel
{
    public string EventLogId { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string Category { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? EndpointId { get; init; }
    public string? EndpointName { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? AssignmentId { get; init; }
    public RecentEventRowKind RowKind { get; init; } = RecentEventRowKind.Default;
}

public sealed class EventLogHistoryPaginationViewModel
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed class RecentEventViewModel
{
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string Category { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? EndpointId { get; init; }
    public string? EndpointName { get; init; }
    public string? AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? AssignmentId { get; init; }
    public RecentEventRowKind RowKind { get; init; } = RecentEventRowKind.Default;
}

public enum RecentEventRowKind
{
    Default,
    EndpointDown,
    EndpointRecovery
}
