using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.BrowserNotifications;

internal sealed class BrowserNotificationQueryService : IBrowserNotificationQueryService
{
    private const int DefaultMaxItems = 25;
    private const int AbsoluteMaxItems = 100;

    private static readonly string[] EligibleEventTypes =
    [
        EventType.EndpointStateChanged,
        EventType.AgentBecameOffline,
        EventType.AgentBecameOnline
    ];

    private readonly PingMonitorDbContext _dbContext;

    public BrowserNotificationQueryService(PingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BrowserNotificationFeedDto> GetFeedAsync(string? lastEventId, int maxItems, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.NotificationSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NotificationSettingsId == NotificationSettings.SingletonId, cancellationToken);

        if (settings is null || !settings.BrowserNotificationsEnabled)
        {
            return new BrowserNotificationFeedDto
            {
                BrowserNotificationsEnabled = false
            };
        }

        var boundedMaxItems = maxItems <= 0
            ? DefaultMaxItems
            : Math.Min(maxItems, AbsoluteMaxItems);

        var rows = await GetFeedRowsAsync(lastEventId, boundedMaxItems, cancellationToken);

        var items = rows
            .Select(row => MapNotification(row, settings))
            .Where(x => x is not null)
            .Cast<BrowserNotificationEventDto>()
            .ToArray();

        return new BrowserNotificationFeedDto
        {
            BrowserNotificationsEnabled = true,
            LastEventId = items.Length == 0 ? lastEventId : items[^1].EventId,
            Items = items
        };
    }

    private async Task<IReadOnlyList<EventRow>> GetFeedRowsAsync(string? lastEventId, int boundedMaxItems, CancellationToken cancellationToken)
    {
        var query = _dbContext.EventLogs.AsNoTracking()
            .Where(x => EligibleEventTypes.Contains(x.EventType));

        if (string.IsNullOrWhiteSpace(lastEventId))
        {
            return await ProjectFeedRows(query)
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.EventLogId)
                .Take(boundedMaxItems)
                .ToArrayAsync(cancellationToken);
        }

        var marker = await _dbContext.EventLogs.AsNoTracking()
            .Where(x => x.EventLogId == lastEventId)
            .Select(x => new { x.EventLogId, x.OccurredAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        if (marker is null)
        {
            return await ProjectFeedRows(query)
                .OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.EventLogId)
                .Take(boundedMaxItems)
                .ToArrayAsync(cancellationToken);
        }

        var markerWindow = await ProjectFeedRows(query.Where(x => x.OccurredAtUtc >= marker.OccurredAtUtc))
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.EventLogId)
            .Take(AbsoluteMaxItems * 5)
            .ToArrayAsync(cancellationToken);

        var markerIndex = Array.FindIndex(markerWindow, x => string.Equals(x.EventLogId, marker.EventLogId, StringComparison.Ordinal));
        if (markerIndex >= 0)
        {
            return markerWindow
                .Skip(markerIndex + 1)
                .Take(boundedMaxItems)
                .ToArray();
        }

        return markerWindow
            .Where(x => x.OccurredAtUtc > marker.OccurredAtUtc)
            .Take(boundedMaxItems)
            .ToArray();
    }

    private static IQueryable<EventRow> ProjectFeedRows(IQueryable<EventLog> query)
    {
        return query.Select(x => new EventRow
        {
            EventLogId = x.EventLogId,
            EventType = x.EventType,
            Message = x.Message,
            Severity = x.Severity,
            OccurredAtUtc = x.OccurredAtUtc,
            EndpointId = x.EndpointId,
            AgentId = x.AgentId
        });
    }

    private static BrowserNotificationEventDto? MapNotification(EventRow row, NotificationSettings settings)
    {
        var mappedEvent = MapSupportedEventType(row);
        if (mappedEvent is null)
        {
            return null;
        }

        if (!IsEventTypeEnabled(mappedEvent.Value.EventKind, settings))
        {
            return null;
        }

        return Build(row, mappedEvent.Value.Title);
    }

    private static SupportedBrowserEventType? MapSupportedEventType(EventRow row)
    {
        if (row.EventType == EventType.EndpointStateChanged)
        {
            if (row.Message.Contains("went down.", StringComparison.Ordinal))
            {
                return new SupportedBrowserEventType(BrowserNotificationEventKind.EndpointDown, "Endpoint Down");
            }

            if (row.Message.Contains("recovered", StringComparison.Ordinal))
            {
                return new SupportedBrowserEventType(BrowserNotificationEventKind.EndpointRecovered, "Endpoint Recovered");
            }

            return null;
        }

        if (row.EventType == EventType.AgentBecameOffline)
        {
            return new SupportedBrowserEventType(BrowserNotificationEventKind.AgentOffline, "Agent Offline");
        }

        if (row.EventType == EventType.AgentBecameOnline)
        {
            return new SupportedBrowserEventType(BrowserNotificationEventKind.AgentOnline, "Agent Online");
        }

        return null;
    }

    private static bool IsEventTypeEnabled(BrowserNotificationEventKind eventKind, NotificationSettings settings)
    {
        return eventKind switch
        {
            BrowserNotificationEventKind.EndpointDown => settings.BrowserNotifyEndpointDown,
            BrowserNotificationEventKind.EndpointRecovered => settings.BrowserNotifyEndpointRecovered,
            BrowserNotificationEventKind.AgentOffline => settings.BrowserNotifyAgentOffline,
            BrowserNotificationEventKind.AgentOnline => settings.BrowserNotifyAgentOnline,
            _ => false
        };
    }

    private static BrowserNotificationEventDto Build(EventRow row, string title)
    {
        return new BrowserNotificationEventDto
        {
            EventId = row.EventLogId,
            EventType = row.EventType,
            Title = title,
            Body = row.Message,
            Severity = row.Severity.ToString().ToLowerInvariant(),
            OccurredAtUtc = row.OccurredAtUtc,
            RelatedEndpointId = row.EndpointId,
            RelatedAgentId = row.AgentId,
            Url = ResolveUrl(row)
        };
    }

    private static string? ResolveUrl(EventRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.EndpointId))
        {
            return $"/endpoints/{row.EndpointId}/history";
        }

        if (!string.IsNullOrWhiteSpace(row.AgentId))
        {
            return $"/agents/{row.AgentId}/history";
        }

        return null;
    }

    private sealed class EventRow
    {
        public string EventLogId { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public EventSeverity Severity { get; init; }
        public DateTimeOffset OccurredAtUtc { get; init; }
        public string? EndpointId { get; init; }
        public string? AgentId { get; init; }
    }

    private enum BrowserNotificationEventKind
    {
        EndpointDown,
        EndpointRecovered,
        AgentOffline,
        AgentOnline
    }

    private readonly record struct SupportedBrowserEventType(BrowserNotificationEventKind EventKind, string Title);
}
