using PingMonitor.Web.ViewModels.EventLogs;

namespace PingMonitor.Web.Services.EventLogs;

public interface IEventLogQueryService
{
    Task<IReadOnlyList<RecentEventViewModel>> GetRecentEventsAsync(int count, CancellationToken cancellationToken);
    Task<EventLogHistoryPageViewModel?> GetEndpointHistoryPageAsync(string endpointId, string? search, string? eventType, DateTimeOffset? dateFromUtc, DateTimeOffset? dateToUtc, int page, int pageSize, CancellationToken cancellationToken);
    Task<EventLogHistoryPageViewModel?> GetAgentHistoryPageAsync(string agentId, string? search, string? eventType, DateTimeOffset? dateFromUtc, DateTimeOffset? dateToUtc, int page, int pageSize, CancellationToken cancellationToken);
}
