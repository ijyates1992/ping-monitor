namespace PingMonitor.Web.Services.Time;

public interface IUserTimeZoneService
{
    Task<TimeZoneInfo> GetCurrentUserTimeZoneAsync(CancellationToken cancellationToken);
    Task<string> GetCurrentUserTimeZoneIdAsync(CancellationToken cancellationToken);
    IReadOnlyList<string> GetSelectableTimeZoneIds();
    bool IsSupportedTimeZoneId(string? timeZoneId);
    TimeZoneInfo ResolveOrUtc(string? timeZoneId);
}
