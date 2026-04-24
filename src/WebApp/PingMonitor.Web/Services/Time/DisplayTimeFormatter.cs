namespace PingMonitor.Web.Services.Time;

public sealed class DisplayTimeFormatter : IDisplayTimeFormatter
{
    private readonly IUserTimeZoneService _userTimeZoneService;

    public DisplayTimeFormatter(IUserTimeZoneService userTimeZoneService)
    {
        _userTimeZoneService = userTimeZoneService;
    }

    public async Task<string> FormatForCurrentUserAsync(DateTimeOffset? utcValue, string nullDisplay = "n/a", CancellationToken cancellationToken = default)
    {
        if (!utcValue.HasValue)
        {
            return nullDisplay;
        }

        var timeZone = await _userTimeZoneService.GetCurrentUserTimeZoneAsync(cancellationToken);
        return FormatForTimeZone(utcValue.Value, timeZone);
    }

    public string FormatForTimeZone(DateTimeOffset utcValue, TimeZoneInfo timeZone)
    {
        var utcNormalized = utcValue.ToUniversalTime();
        var localTime = TimeZoneInfo.ConvertTime(utcNormalized, timeZone);
        return $"{localTime:yyyy-MM-dd HH:mm:ss zzz} {timeZone.Id}";
    }

    public string FormatUtc(DateTimeOffset? utcValue, string nullDisplay = "n/a")
    {
        if (!utcValue.HasValue)
        {
            return nullDisplay;
        }

        return $"{utcValue.Value.UtcDateTime:yyyy-MM-dd HH:mm:ss} +00:00 UTC";
    }
}
