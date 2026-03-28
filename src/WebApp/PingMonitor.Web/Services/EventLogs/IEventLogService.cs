namespace PingMonitor.Web.Services.EventLogs;

public interface IEventLogService
{
    Task WriteAsync(EventLogWriteRequest request, CancellationToken cancellationToken);
}
