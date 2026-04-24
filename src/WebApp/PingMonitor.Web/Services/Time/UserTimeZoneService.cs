using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using PingMonitor.Web.Models.Identity;

namespace PingMonitor.Web.Services.Time;

public sealed class UserTimeZoneService : IUserTimeZoneService
{
    private const string UtcTimeZoneId = "UTC";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadOnlyList<string> _selectableTimeZoneIds;

    public UserTimeZoneService(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;

        var ids = TimeZoneInfo
            .GetSystemTimeZones()
            .Select(x => x.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (!ids.Contains(UtcTimeZoneId, StringComparer.Ordinal))
        {
            ids.Insert(0, UtcTimeZoneId);
        }
        else
        {
            ids.RemoveAll(id => string.Equals(id, UtcTimeZoneId, StringComparison.Ordinal));
            ids.Insert(0, UtcTimeZoneId);
        }

        _selectableTimeZoneIds = ids;
    }

    public async Task<TimeZoneInfo> GetCurrentUserTimeZoneAsync(CancellationToken cancellationToken)
    {
        var id = await GetCurrentUserTimeZoneIdAsync(cancellationToken);
        return ResolveOrUtc(id);
    }

    public async Task<string> GetCurrentUserTimeZoneIdAsync(CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User?.Identity?.IsAuthenticated != true)
        {
            return UtcTimeZoneId;
        }

        var user = await _userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return UtcTimeZoneId;
        }

        return IsSupportedTimeZoneId(user.DisplayTimeZoneId) ? user.DisplayTimeZoneId! : UtcTimeZoneId;
    }

    public IReadOnlyList<string> GetSelectableTimeZoneIds() => _selectableTimeZoneIds;

    public bool IsSupportedTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        return _selectableTimeZoneIds.Contains(timeZoneId.Trim(), StringComparer.Ordinal);
    }

    public TimeZoneInfo ResolveOrUtc(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
