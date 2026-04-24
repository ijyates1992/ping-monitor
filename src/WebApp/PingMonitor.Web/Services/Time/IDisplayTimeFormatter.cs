namespace PingMonitor.Web.Services.Time;

public interface IDisplayTimeFormatter
{
    Task<string> FormatForCurrentUserAsync(DateTimeOffset? utcValue, string nullDisplay = "n/a", CancellationToken cancellationToken = default);
    string FormatForTimeZone(DateTimeOffset utcValue, TimeZoneInfo timeZone);
    string FormatUtc(DateTimeOffset? utcValue, string nullDisplay = "n/a");
}
