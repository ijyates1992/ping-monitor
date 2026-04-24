using PingMonitor.Web.Services.Time;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class DisplayTimeFormatterTests
{
    [Fact]
    public void FormatForTimeZone_UsesDstOffset_ForEuropeLondonSummer()
    {
        var formatter = new DisplayTimeFormatter(new StubUserTimeZoneService(TimeZoneInfo.Utc));
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        var formatted = formatter.FormatForTimeZone(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), london);

        Assert.Contains("2026-07-01 13:00:00", formatted, StringComparison.Ordinal);
        Assert.Contains("+01:00 Europe/London", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatForCurrentUserAsync_UsesUtc_WhenUserPreferenceUtc()
    {
        var formatter = new DisplayTimeFormatter(new StubUserTimeZoneService(TimeZoneInfo.Utc));

        var formatted = await formatter.FormatForCurrentUserAsync(new DateTimeOffset(2026, 4, 24, 13, 42, 10, TimeSpan.Zero));

        Assert.Equal("2026-04-24 13:42:10 +00:00 UTC", formatted);
    }

    [Fact]
    public async Task FormatForCurrentUserAsync_FallsBackToUtc_WhenServiceReturnsUtcForInvalidId()
    {
        var formatter = new DisplayTimeFormatter(new StubUserTimeZoneService(TimeZoneInfo.Utc));

        var formatted = await formatter.FormatForCurrentUserAsync(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-12-25 00:00:00 +00:00 UTC", formatted);
    }

    private sealed class StubUserTimeZoneService : IUserTimeZoneService
    {
        private readonly TimeZoneInfo _zone;

        public StubUserTimeZoneService(TimeZoneInfo zone)
        {
            _zone = zone;
        }

        public Task<TimeZoneInfo> GetCurrentUserTimeZoneAsync(CancellationToken cancellationToken) => Task.FromResult(_zone);
        public Task<string> GetCurrentUserTimeZoneIdAsync(CancellationToken cancellationToken) => Task.FromResult(_zone.Id);
        public IReadOnlyList<string> GetSelectableTimeZoneIds() => [_zone.Id];
        public bool IsSupportedTimeZoneId(string? timeZoneId) => string.Equals(timeZoneId, _zone.Id, StringComparison.Ordinal);
        public TimeZoneInfo ResolveOrUtc(string? timeZoneId) => _zone;
    }
}
