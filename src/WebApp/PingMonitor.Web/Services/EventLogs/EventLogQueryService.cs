using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.ViewModels.EventLogs;

namespace PingMonitor.Web.Services.EventLogs;

internal sealed class EventLogQueryService : IEventLogQueryService
{
    private const int MaxPageSize = 100;
    private readonly PingMonitorDbContext _dbContext;

    public EventLogQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RecentEventViewModel>> GetRecentEventsAsync(int count, CancellationToken cancellationToken)
    {
        var safeCount = Math.Clamp(count, 1, 100);
        return await BuildProjection(_dbContext.EventLogs.AsNoTracking())
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.EventLogId)
            .Take(safeCount)
            .Select(x => new RecentEventViewModel
            {
                OccurredAtUtc = x.OccurredAtUtc,
                Category = x.Category,
                EventType = x.EventType,
                Severity = x.Severity,
                Message = x.Message,
                VisualSeverity = x.VisualSeverity,
                SeverityCssClass = x.SeverityCssClass,
                EndpointId = x.EndpointId,
                EndpointName = x.EndpointName,
                AgentId = x.AgentId,
                AgentName = x.AgentName,
                AssignmentId = x.AssignmentId,
                RowKind = x.RowKind
            })
            .ToArrayAsync(cancellationToken);
    }

    public Task<EventLogHistoryPageViewModel?> GetEndpointHistoryPageAsync(string endpointId, string? search, string? eventType, DateTimeOffset? dateFromUtc, DateTimeOffset? dateToUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        return GetHistoryPageAsync(
            scopeType: "Endpoint",
            scopeId: endpointId,
            baseQuery: _dbContext.EventLogs.AsNoTracking().Where(x => x.EndpointId == endpointId),
            scopeNameQuery: _dbContext.Endpoints.AsNoTracking().Where(x => x.EndpointId == endpointId).Select(x => x.Name),
            backLink: "/endpoints",
            emptyMessage: "No endpoint events match the selected filters.",
            search,
            eventType,
            dateFromUtc,
            dateToUtc,
            page,
            pageSize,
            cancellationToken);
    }

    public Task<EventLogHistoryPageViewModel?> GetAgentHistoryPageAsync(string agentId, string? search, string? eventType, DateTimeOffset? dateFromUtc, DateTimeOffset? dateToUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        return GetHistoryPageAsync(
            scopeType: "Agent",
            scopeId: agentId,
            baseQuery: _dbContext.EventLogs.AsNoTracking().Where(x => x.AgentId == agentId),
            scopeNameQuery: _dbContext.Agents.AsNoTracking().Where(x => x.AgentId == agentId).Select(x => x.Name ?? x.InstanceId),
            backLink: "/agents",
            emptyMessage: "No agent events match the selected filters.",
            search,
            eventType,
            dateFromUtc,
            dateToUtc,
            page,
            pageSize,
            cancellationToken);
    }

    private async Task<EventLogHistoryPageViewModel?> GetHistoryPageAsync(
        string scopeType,
        string scopeId,
        IQueryable<Models.EventLog> baseQuery,
        IQueryable<string> scopeNameQuery,
        string backLink,
        string emptyMessage,
        string? search,
        string? eventType,
        DateTimeOffset? dateFromUtc,
        DateTimeOffset? dateToUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scopeName = await scopeNameQuery.SingleOrDefaultAsync(cancellationToken);
        if (scopeName is null)
        {
            return null;
        }

        var normalizedSearch = Normalize(search);
        var normalizedEventType = Normalize(eventType);
        var safePageSize = Math.Clamp(pageSize, 10, MaxPageSize);
        var safePage = Math.Max(1, page);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            baseQuery = baseQuery.Where(x =>
                EF.Functions.Like(x.Message, $"%{normalizedSearch}%") ||
                EF.Functions.Like(x.EventType, $"%{normalizedSearch}%") ||
                (x.DetailsJson != null && EF.Functions.Like(x.DetailsJson, $"%{normalizedSearch}%")));
        }

        if (!string.IsNullOrWhiteSpace(normalizedEventType))
        {
            baseQuery = baseQuery.Where(x => x.EventType == normalizedEventType);
        }

        if (dateFromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.OccurredAtUtc >= dateFromUtc.Value);
        }

        if (dateToUtc.HasValue)
        {
            baseQuery = baseQuery.Where(x => x.OccurredAtUtc <= dateToUtc.Value);
        }

        var projected = BuildProjection(baseQuery);
        var totalCount = await projected.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        safePage = Math.Min(safePage, totalPages);

        var rows = await projected
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.EventLogId)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(x => new EventLogHistoryRowViewModel
            {
                EventLogId = x.EventLogId,
                OccurredAtUtc = x.OccurredAtUtc,
                Category = x.Category,
                EventType = x.EventType,
                Severity = x.Severity,
                Message = x.Message,
                VisualSeverity = x.VisualSeverity,
                SeverityCssClass = x.SeverityCssClass,
                EndpointId = x.EndpointId,
                EndpointName = x.EndpointName,
                AgentId = x.AgentId,
                AgentName = x.AgentName,
                AssignmentId = x.AssignmentId,
                RowKind = x.RowKind
            })
            .ToArrayAsync(cancellationToken);

        var availableEventTypes = await _dbContext.EventLogs.AsNoTracking()
            .Where(x => (scopeType == "Endpoint" && x.EndpointId == scopeId) || (scopeType == "Agent" && x.AgentId == scopeId))
            .Select(x => x.EventType)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);

        return new EventLogHistoryPageViewModel
        {
            ScopeId = scopeId,
            ScopeLabel = scopeType,
            ScopeName = scopeName,
            BackLink = backLink,
            EmptyMessage = emptyMessage,
            Filters = new EventLogHistoryFilterViewModel
            {
                Search = normalizedSearch,
                EventType = normalizedEventType,
                DateFromUtc = dateFromUtc,
                DateToUtc = dateToUtc,
                PageSize = safePageSize,
                AvailableEventTypes = availableEventTypes
            },
            Rows = rows,
            Pagination = new EventLogHistoryPaginationViewModel
            {
                Page = safePage,
                PageSize = safePageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            }
        };
    }

    private IQueryable<EventProjection> BuildProjection(IQueryable<Models.EventLog> query)
    {
        return from eventLog in query
            join endpoint in _dbContext.Endpoints.AsNoTracking() on eventLog.EndpointId equals endpoint.EndpointId into endpointJoin
            from endpoint in endpointJoin.DefaultIfEmpty()
            join agent in _dbContext.Agents.AsNoTracking() on eventLog.AgentId equals agent.AgentId into agentJoin
            from agent in agentJoin.DefaultIfEmpty()
            select new EventProjection
            {
                EventLogId = eventLog.EventLogId,
                OccurredAtUtc = eventLog.OccurredAtUtc,
                Category = eventLog.EventCategory.ToString(),
                EventType = eventLog.EventType,
                Severity = eventLog.Severity.ToString(),
                VisualSeverity = eventLog.Severity == Models.EventSeverity.Warning
                    ? Models.LogVisualSeverity.Warning
                    : eventLog.Severity == Models.EventSeverity.Error
                        ? Models.LogVisualSeverity.Error
                        : Models.LogVisualSeverity.Info,
                SeverityCssClass = eventLog.Severity == Models.EventSeverity.Warning
                    ? "log-warning"
                    : eventLog.Severity == Models.EventSeverity.Error
                        ? "log-error"
                        : "log-info",
                Message = eventLog.Message,
                AgentId = eventLog.AgentId,
                AgentName = agent != null ? (agent.Name ?? agent.InstanceId) : null,
                EndpointId = eventLog.EndpointId,
                EndpointName = endpoint != null ? endpoint.Name : null,
                AssignmentId = eventLog.AssignmentId,
                RowKind = RecentEventRowKind.Default
            };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class EventProjection
    {
        public string EventLogId { get; init; } = string.Empty;
        public DateTimeOffset OccurredAtUtc { get; init; }
        public string Category { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string Severity { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public Models.LogVisualSeverity VisualSeverity { get; init; } = Models.LogVisualSeverity.Info;
        public string SeverityCssClass { get; init; } = "log-info";
        public string? AgentId { get; init; }
        public string? AgentName { get; init; }
        public string? EndpointId { get; init; }
        public string? EndpointName { get; init; }
        public string? AssignmentId { get; init; }
        public RecentEventRowKind RowKind { get; init; }
    }
}
