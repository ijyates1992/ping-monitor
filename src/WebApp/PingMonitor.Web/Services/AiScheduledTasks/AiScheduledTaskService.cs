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

        entity.Name = command.Name.Trim();
        entity.Prompt = command.Prompt.Trim();
        entity.Enabled = command.Enabled;
        entity.ScheduleKind = command.ScheduleKind;
        entity.RunOnceAtUtc = command.ScheduleKind == AiScheduledTaskScheduleKind.Once ? command.RunOnceAtUtc : null;
        entity.TimeOfDayLocal = command.ScheduleKind == AiScheduledTaskScheduleKind.Once ? null : command.TimeOfDayLocal;
        entity.DayOfWeek = command.ScheduleKind == AiScheduledTaskScheduleKind.Weekly ? command.DayOfWeek : null;
        entity.DayOfMonth = command.ScheduleKind == AiScheduledTaskScheduleKind.Monthly ? command.DayOfMonth : null;
        entity.TimeZoneId = command.TimeZoneId.Trim();
        entity.DeliveryTarget = command.DeliveryTarget;
        entity.UpdatedAtUtc = now;
        entity.NextRunAtUtc = command.Enabled ? CalculateNextRunUtc(command.ScheduleKind, command.RunOnceAtUtc, command.TimeOfDayLocal, command.DayOfWeek, command.DayOfMonth, command.TimeZoneId, now) : null;
        if (!command.Enabled) entity.LastStatus = AiScheduledTaskLastStatus.Disabled;
        else if (entity.LastStatus == AiScheduledTaskLastStatus.Disabled) entity.LastStatus = AiScheduledTaskLastStatus.NeverRun;

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
        if (!Enum.IsDefined(command.ScheduleKind)) return "Schedule kind is invalid.";
        if (!Enum.IsDefined(command.DeliveryTarget) || command.DeliveryTarget != AiScheduledTaskDeliveryTarget.TelegramOwner) return "Delivery target is invalid.";
        if (!IsValidTimeZone(command.TimeZoneId)) return "Time zone is invalid.";
        if (command.Enabled && !await _dbContext.TelegramAccounts.AsNoTracking().AnyAsync(x => x.UserId == command.OwnerUserId && x.Verified && x.IsActive, cancellationToken)) return "Link your Telegram account before enabling scheduled AI tasks.";
        var now = DateTimeOffset.UtcNow;
        return command.ScheduleKind switch
        {
            AiScheduledTaskScheduleKind.Once when command.RunOnceAtUtc is null || command.RunOnceAtUtc <= now => "Run-once date/time must be in the future.",
            AiScheduledTaskScheduleKind.Daily when command.TimeOfDayLocal is null => "Daily schedules require a time of day.",
            AiScheduledTaskScheduleKind.Weekly when command.DayOfWeek is null || command.TimeOfDayLocal is null => "Weekly schedules require a day of week and time of day.",
            AiScheduledTaskScheduleKind.Monthly when command.DayOfMonth is null or < 1 or > 28 || command.TimeOfDayLocal is null => "Monthly schedules require a day from 1 to 28 and a time of day.",
            _ => null
        };
    }

    public DateTimeOffset? CalculateNextRunUtc(AiScheduledTaskScheduleKind kind, DateTimeOffset? runOnceAtUtc, TimeOnly? timeOfDayLocal, DayOfWeek? dayOfWeek, int? dayOfMonth, string timeZoneId, DateTimeOffset nowUtc)
    {
        if (kind == AiScheduledTaskScheduleKind.Once) return runOnceAtUtc > nowUtc ? runOnceAtUtc : null;
        if (!IsValidTimeZone(timeZoneId) || timeOfDayLocal is null) return null;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        DateOnly date = DateOnly.FromDateTime(localNow.DateTime);
        if (kind == AiScheduledTaskScheduleKind.Weekly)
        {
            var days = ((int)dayOfWeek!.Value - (int)localNow.DayOfWeek + 7) % 7;
            date = date.AddDays(days);
        }
        if (kind == AiScheduledTaskScheduleKind.Monthly)
        {
            var day = dayOfMonth!.Value;
            date = new DateOnly(localNow.Year, localNow.Month, day);
            if (date.ToDateTime(timeOfDayLocal.Value) <= localNow.DateTime) date = new DateOnly(localNow.AddMonths(1).Year, localNow.AddMonths(1).Month, day);
        }
        var candidate = date.ToDateTime(timeOfDayLocal.Value);
        if (kind != AiScheduledTaskScheduleKind.Monthly && candidate <= localNow.DateTime) candidate = kind == AiScheduledTaskScheduleKind.Daily ? candidate.AddDays(1) : candidate.AddDays(7);
        return TimeZoneInfo.ConvertTimeToUtc(candidate, tz);
    }

    private static bool IsValidTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > AiScheduledTask.TimeZoneIdMaxLength) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()); return true; } catch { return false; }
    }

    private static AiScheduledTaskDto ToDto(AiScheduledTask x) => new(x.AiScheduledTaskId, x.OwnerUserId, x.Name, x.Prompt, x.Enabled, x.ScheduleKind, x.RunOnceAtUtc, x.TimeOfDayLocal, x.DayOfWeek, x.DayOfMonth, x.TimeZoneId, x.DeliveryTarget, x.NextRunAtUtc, x.LastRunAtUtc, x.LastStatus, x.LastError, x.LastResponsePreview, x.CreatedAtUtc, x.UpdatedAtUtc);
}
