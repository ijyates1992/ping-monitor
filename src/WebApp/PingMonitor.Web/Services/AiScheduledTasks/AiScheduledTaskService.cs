using Microsoft.EntityFrameworkCore;
using PingMonitor.Web.Data;
using PingMonitor.Web.Models;

namespace PingMonitor.Web.Services.AiScheduledTasks;

internal sealed class AiScheduledTaskService : IAiScheduledTaskService
{
    private readonly PingMonitorDbContext _dbContext;
    public AiScheduledTaskService(PingMonitorDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<AiScheduledTaskDto>> ListForUserAsync(string ownerUserId, CancellationToken cancellationToken) =>
        await _dbContext.AiScheduledTasks.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId).OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync(cancellationToken);

    public async Task<AiScheduledTaskDto?> GetForUserAsync(string ownerUserId, string taskId, CancellationToken cancellationToken) =>
        await _dbContext.AiScheduledTasks.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId && x.AiScheduledTaskId == taskId).Select(x => ToDto(x)).FirstOrDefaultAsync(cancellationToken);

    public async Task<AiScheduledTaskMutationResult> SaveAsync(SaveAiScheduledTaskCommand command, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(command, cancellationToken);
        if (validation is not null) return new(false, validation, null);

        var now = DateTimeOffset.UtcNow;
        AiScheduledTask? entity = null;
        if (!string.IsNullOrWhiteSpace(command.AiScheduledTaskId))
        {
            entity = await _dbContext.AiScheduledTasks.FirstOrDefaultAsync(x => x.OwnerUserId == command.OwnerUserId && x.AiScheduledTaskId == command.AiScheduledTaskId, cancellationToken);
            if (entity is null) return new(false, "Scheduled AI task was not found for the current user.", null);
        }
        else
        {
            entity = new AiScheduledTask { AiScheduledTaskId = Guid.NewGuid().ToString("N"), OwnerUserId = command.OwnerUserId, CreatedAtUtc = now };
            _dbContext.AiScheduledTasks.Add(entity);
        }

        var firstRunUtc = ConvertLocalFirstRunToUtc(command.FirstRunDate!.Value, command.FirstRunTime!.Value, command.TimeZoneId.Trim()).UtcValue!.Value;

        entity.Name = command.Name.Trim();
        entity.Prompt = command.Prompt.Trim();
        entity.Enabled = command.Enabled;
        entity.FirstRunAtUtc = firstRunUtc;
        entity.RepeatEnabled = command.RepeatEnabled;
        entity.RepeatEvery = command.RepeatEnabled ? command.RepeatEvery : null;
        entity.RepeatUnit = command.RepeatEnabled ? command.RepeatUnit : null;
        entity.MissedRunPolicy = command.MissedRunPolicy;
        entity.TimeZoneId = command.TimeZoneId.Trim();
        entity.DeliveryTarget = command.DeliveryTarget;
        entity.ScheduleKind = command.RepeatEnabled ? command.RepeatUnit switch { AiScheduledTaskRepeatUnit.Days => AiScheduledTaskScheduleKind.Daily, AiScheduledTaskRepeatUnit.Weeks => AiScheduledTaskScheduleKind.Weekly, AiScheduledTaskRepeatUnit.Months => AiScheduledTaskScheduleKind.Monthly, _ => AiScheduledTaskScheduleKind.Daily } : AiScheduledTaskScheduleKind.Once;
        entity.RunOnceAtUtc = command.RepeatEnabled ? null : entity.FirstRunAtUtc;
        entity.TimeOfDayLocal = null; entity.DayOfWeek = null; entity.DayOfMonth = null;
        entity.UpdatedAtUtc = now;
        entity.NextRunAtUtc = command.Enabled ? CalculateNextRunUtc(entity.FirstRunAtUtc.Value, entity.RepeatEnabled, entity.RepeatEvery, entity.RepeatUnit, entity.TimeZoneId, now) : null;
        if (!command.Enabled) entity.LastStatus = AiScheduledTaskLastStatus.Disabled;
        else if (entity.LastStatus is AiScheduledTaskLastStatus.Disabled or AiScheduledTaskLastStatus.Completed) entity.LastStatus = AiScheduledTaskLastStatus.NeverRun;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null, ToDto(entity));
    }

    public async Task<AiScheduledTaskMutationResult> DeleteAsync(string ownerUserId, string taskId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.AiScheduledTasks.FirstOrDefaultAsync(x => x.OwnerUserId == ownerUserId && x.AiScheduledTaskId == taskId, cancellationToken);
        if (entity is null) return new(false, "Scheduled AI task was not found for the current user.", null);
        _dbContext.AiScheduledTasks.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null, ToDto(entity));
    }

    private async Task<string?> ValidateAsync(SaveAiScheduledTaskCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId)) return "A signed-in user is required.";
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > AiScheduledTask.NameMaxLength) return $"Name is required and must be {AiScheduledTask.NameMaxLength} characters or fewer.";
        if (string.IsNullOrWhiteSpace(command.Prompt) || command.Prompt.Trim().Length > AiScheduledTask.PromptMaxLength) return $"Prompt is required and must be {AiScheduledTask.PromptMaxLength} characters or fewer.";
        if (!Enum.IsDefined(command.DeliveryTarget) || command.DeliveryTarget != AiScheduledTaskDeliveryTarget.TelegramOwner) return "Delivery target is invalid.";
        if (!IsValidTimeZone(command.TimeZoneId)) return "Select a valid time zone.";
        if (!Enum.IsDefined(command.MissedRunPolicy)) return "Missed run behaviour is invalid.";
        if (command.FirstRunDate is null) return "First run date is required.";
        if (command.FirstRunTime is null) return "First run time is required.";
        if (command.FirstRunTime.Value.Ticks % TimeSpan.TicksPerMinute != 0) return "First run time must use HH:mm minute precision.";
        var firstRunConversion = ConvertLocalFirstRunToUtc(command.FirstRunDate.Value, command.FirstRunTime.Value, command.TimeZoneId.Trim());
        if (!firstRunConversion.Succeeded) return firstRunConversion.ErrorMessage;
        if (command.RepeatEnabled)
        {
            if (command.RepeatEvery is null or < 1) return "Repeat every must be at least 1.";
            if (command.RepeatUnit is null || !Enum.IsDefined(command.RepeatUnit.Value)) return "Repeat unit is invalid.";
        }
        if (command.Enabled && !await _dbContext.TelegramAccounts.AsNoTracking().AnyAsync(x => x.UserId == command.OwnerUserId && x.Verified && x.IsActive, cancellationToken)) return "Link your Telegram account before enabling scheduled AI tasks.";
        if (firstRunConversion.UtcValue!.Value <= DateTimeOffset.UtcNow && command.MissedRunPolicy != AiScheduledTaskMissedRunPolicy.RetryOnce) return "First run must be in the future unless missed runs are set to run once as soon as possible.";
        return null;
    }

    public DateTimeOffset? CalculateNextRunUtc(DateTimeOffset firstRunAtUtc, bool repeatEnabled, int? repeatEvery, AiScheduledTaskRepeatUnit? repeatUnit, string timeZoneId, DateTimeOffset nowUtc)
    {
        if (firstRunAtUtc > nowUtc) return firstRunAtUtc;
        if (!repeatEnabled) return null;
        if (repeatEvery is null or < 1 || repeatUnit is null || !IsValidTimeZone(timeZoneId)) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        var firstLocal = TimeZoneInfo.ConvertTime(firstRunAtUtc, tz).DateTime;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime;
        var candidate = firstLocal;
        while (candidate <= nowLocal) candidate = AddInterval(candidate, repeatEvery.Value, repeatUnit.Value);
        return SafeLocalToUtc(candidate, tz);
    }

    public AiScheduledTaskDueDecision EvaluateDue(DateTimeOffset firstRunAtUtc, bool repeatEnabled, int? repeatEvery, AiScheduledTaskRepeatUnit? repeatUnit, AiScheduledTaskMissedRunPolicy missedRunPolicy, string timeZoneId, DateTimeOffset nowUtc)
    {
        if (firstRunAtUtc > nowUtc) return new(false, firstRunAtUtc, false, null);
        var nextFuture = CalculateNextRunUtc(firstRunAtUtc, repeatEnabled, repeatEvery, repeatUnit, timeZoneId, nowUtc);
        if (missedRunPolicy == AiScheduledTaskMissedRunPolicy.RetryOnce) return new(true, nextFuture, false, null);
        return repeatEnabled ? new(false, nextFuture, false, null) : new(false, null, true, AiScheduledTaskLastStatus.Completed);
    }

    internal static DateTime AddInterval(DateTime value, int every, AiScheduledTaskRepeatUnit unit) => unit switch
    {
        AiScheduledTaskRepeatUnit.Hours => value.AddHours(every),
        AiScheduledTaskRepeatUnit.Days => value.AddDays(every),
        AiScheduledTaskRepeatUnit.Weeks => value.AddDays(7 * every),
        AiScheduledTaskRepeatUnit.Months => value.AddMonths(every),
        _ => value
    };

    public static AiScheduledTaskLocalTimeConversion ConvertLocalFirstRunToUtc(DateOnly date, TimeOnly time, string timeZoneId)
    {
        if (!IsValidTimeZone(timeZoneId)) return new(false, null, "Select a valid time zone.");
        if (time.Ticks % TimeSpan.TicksPerMinute != 0) return new(false, null, "First run time must use HH:mm minute precision.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local)) return new(false, null, "First run time does not exist in the selected time zone due to daylight saving time. Choose a valid time.");
        return new(true, LocalToUtc(local, timeZone), null);
    }

    private static DateTimeOffset SafeLocalToUtc(DateTime local, TimeZoneInfo tz)
    {
        while (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return LocalToUtc(local, tz);
    }

    private static DateTimeOffset LocalToUtc(DateTime local, TimeZoneInfo tz)
    {
        if (tz.IsAmbiguousTime(local)) return new DateTimeOffset(local, tz.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static bool IsValidTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > AiScheduledTask.TimeZoneIdMaxLength) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()); return true; } catch { return false; }
    }

    private static AiScheduledTaskDto ToDto(AiScheduledTask x) => new(x.AiScheduledTaskId, x.OwnerUserId, x.Name, x.Prompt, x.Enabled, x.FirstRunAtUtc ?? x.RunOnceAtUtc ?? x.NextRunAtUtc, x.RepeatEnabled || x.ScheduleKind != AiScheduledTaskScheduleKind.Once, x.RepeatEvery ?? (x.ScheduleKind == AiScheduledTaskScheduleKind.Once ? null : 1), x.RepeatUnit ?? x.ScheduleKind switch { AiScheduledTaskScheduleKind.Daily => AiScheduledTaskRepeatUnit.Days, AiScheduledTaskScheduleKind.Weekly => AiScheduledTaskRepeatUnit.Weeks, AiScheduledTaskScheduleKind.Monthly => AiScheduledTaskRepeatUnit.Months, _ => null }, x.MissedRunPolicy, x.TimeZoneId, x.DeliveryTarget, x.NextRunAtUtc, x.LastRunAtUtc, x.LastStatus, x.LastError, x.LastResponsePreview, x.CreatedAtUtc, x.UpdatedAtUtc);
}
