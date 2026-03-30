using PingMonitor.Web.Data;
using PingMonitor.Web.Models;
using PingMonitor.Web.Services.SmtpNotifications;

namespace PingMonitor.Web.Services.EventLogs;

internal sealed class EventLogService : IEventLogService
{
    private readonly PingMonitorDbContext _dbContext;
    private readonly ISmtpNotificationSender _smtpNotificationSender;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(
        PingMonitorDbContext dbContext,
        ISmtpNotificationSender smtpNotificationSender,
        ILogger<EventLogService> logger)
    {
        _dbContext = dbContext;
        _smtpNotificationSender = smtpNotificationSender;
        _logger = logger;
    }

    public async Task WriteAsync(EventLogWriteRequest request, CancellationToken cancellationToken)
    {
        var eventLog = new EventLog
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
        };

        _dbContext.EventLogs.Add(eventLog);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var smtpResult = await _smtpNotificationSender.SendForEventAsync(eventLog, cancellationToken);
        if (smtpResult.Success)
        {
            return;
        }

        if (!smtpResult.Skipped)
        {
            _logger.LogWarning(
                "SMTP notification was not delivered for event {EventType}: {Message}",
                eventLog.EventType,
                smtpResult.Message);
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
