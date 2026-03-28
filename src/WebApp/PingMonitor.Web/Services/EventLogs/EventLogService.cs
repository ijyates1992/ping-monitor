using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.EventLogs;

internal sealed class EventLogService : IEventLogService
{
    private readonly PingMonitorDbContext _dbContext;

    public EventLogService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(EventLogWriteRequest request, CancellationToken cancellationToken)
    {
        _dbContext.EventLogs.Add(new EventLog
        {
            EventLogId = $"evt_{Guid.NewGuid():N}",
            OccurredAtUtc = request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
            EventCategory = request.Category,
            EventType = request.EventType.Trim(),
            Severity = request.Severity,
            AgentId = Normalize(request.AgentId),
            EndpointId = Normalize(request.EndpointId),
            AssignmentId = Normalize(request.AssignmentId),
            Message = request.Message.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(request.DetailsJson) ? null : request.DetailsJson.Trim()
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
