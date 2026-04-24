namespace PingMonitor.Web.Services.Time;

public sealed record DisplayTimeZoneOption(string Value, string Label);

public interface IUserTimeZoneService
{
    Task<TimeZoneInfo> GetCurrentUserTimeZoneAsync(CancellationToken cancellationToken);
    Task<string> GetCurrentUserTimeZoneIdAsync(CancellationToken cancellationToken);
    IReadOnlyList<string> GetSelectableTimeZoneIds();
    IReadOnlyList<DisplayTimeZoneOption> GetSelectableTimeZoneOptions();
    bool IsSupportedTimeZoneId(string? timeZoneId);
    TimeZoneInfo ResolveOrUtc(string? timeZoneId);
}
